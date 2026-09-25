using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OdinOnDemand.Components;
using OdinOnDemand.Dynamic;
using OdinOnDemand.Patches;
using OdinOnDemand.Utils;
using OdinOnDemand.Utils.Config;
using OdinOnDemand.Utils.Net;
using UnityEngine;
using UnityEngine.Rendering;
using Paths = BepInEx.Paths;

namespace OdinOnDemand
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    internal class OdinOnDemandPlugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.valmedia.odinondemand";
        public const string PluginName = "OdinOnDemand";
        public const string PluginVersion = "1.3.0";

        private static readonly CustomLocalization Localization = LocalizationManager.Instance.GetLocalization();
        public static readonly RpcHandler RPCHandlers = new RpcHandler();
        public static Material MainScreenMat;
        public static Dictionary<string, Sprite> UISprites;
        private AssetBundle _valMediaAssets;
        
        private static Harmony _harmony;
        private static string _pieceRecipeFile;
        private static string _itemRecipeFile;
        private static readonly string OdinConfigFolder = Paths.ConfigPath + "/OdinOnDemand/";
        public static ConfigFile OdinConfig { get; private set; }
        
        public StationManager StationManager { get; private set; }

        /// <summary>A dedicated server: no graphics device, and no audio to play either.</summary>
        internal static bool IsHeadless => SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;

        private void Awake()
        {
            //setup config
            OdinConfig = Config;
            MigrateLegacyConfig();
            OODConfig.Bind(OdinConfig);
            _pieceRecipeFile = OdinConfigFolder + "/recipes.json";
            _itemRecipeFile = OdinConfigFolder + "/recipes_item.json";
            OODConfig.SyncManager();
            
            //create rpc handler
            RPCHandlers.Create();
            
            var stationMan = new GameObject("OODStationMan");
            StationManager = stationMan.AddComponent<StationManager>();
            DontDestroyOnLoad(stationMan);
            
            //init config
            AddLocalizations();
            
            //load assets and configure pieces and items
            LoadAssets();
            PrefabManager.OnVanillaPrefabsAvailable += AddCartVariant;
            
            //Key Config
            ConfigFile keyConfig = new ConfigFile(Path.Combine(OdinConfigFolder, "com.ood.valmedia.keyconfig.cfg"), true);
            SynchronizationManager.Instance.RegisterCustomConfig(keyConfig);
            KeyConfig.SetupKeyConfig(keyConfig);
            
            //setup harmony patches
            _harmony = new Harmony("Harmony.ValMedia.OOD");
            _harmony.PatchAll();
            
            Jotunn.Logger.LogDebug("** OdinOnDemand Initialized **");
        }

        /// <summary>
        ///     Settings used to live in OdinOnDemand/config.cfg, a file config managers never list
        ///     and Jotunn never synced, so server values such as the Haldor toggle were ignored.
        /// </summary>
        private void MigrateLegacyConfig()
        {
            var legacyFile = Path.Combine(OdinConfigFolder, "config.cfg");
            if (!File.Exists(legacyFile) || File.Exists(Config.ConfigFilePath)) return;
            try
            {
                File.Move(legacyFile, Config.ConfigFilePath);
                Config.Reload();
                Jotunn.Logger.LogInfo("Moved settings from " + legacyFile + " to " + Config.ConfigFilePath);
            }
            catch (Exception e)
            {
                Jotunn.Logger.LogWarning("Could not move legacy config " + legacyFile + ": " + e.Message);
            }
        }

        private static void AddCartVariant()
        {
            var pieceConfig = new PieceConfig();
            pieceConfig.Name = "Bard's Wagon";
            pieceConfig.Description = "A mobile media player on wheels, controlled by a remote.";
            pieceConfig.PieceTable = "Hammer";
            pieceConfig.Category = "OOD";
            pieceConfig.AddRequirement(new RequirementConfig("Wood", 26));
            pieceConfig.AddRequirement(new RequirementConfig("Bronze", 2));
            pieceConfig.AddRequirement(new RequirementConfig("BronzeNails", 14));
            
            
            var assets = AssetUtils.LoadAssetBundleFromResources("videoplayers", typeof(OdinOnDemandPlugin).Assembly);
            var attach = assets.LoadAsset<GameObject>("assets/cartplayer_attach.prefab");
            pieceConfig.Icon = assets.LoadAsset<Sprite>("assets/MOD ICONS/cartplayericon.png");
            
            
            var cartPiece = new CustomPiece("cartplayer", "Cart", pieceConfig);
            if (PieceManager.Instance.AddPiece(cartPiece))
            {
                PieceCategoryPatch.Apply(cartPiece.Piece);
                var cart = PrefabManager.Instance.GetPrefab("cartplayer");
                Instantiate(attach, cart.transform, true);
                cart.transform.Find("cartplayer_attach(Clone)").gameObject.AddComponent<CartPlayerComponent>();
            }
           
            assets.Unload(false);
            PrefabManager.OnVanillaPrefabsAvailable -= AddCartVariant;
        }

        private void LoadAssets()
        {
            // Load asset bundle from the filesystem, setup sprite textures
            _valMediaAssets = AssetUtils.LoadAssetBundleFromResources("videoplayers", typeof(OdinOnDemandPlugin).Assembly);
            Jotunn.Logger.LogDebug("Loading OdinOnDemand Assets");
            // read / setup piece recipes
            AddRecipes();

            //Master screen material
            MainScreenMat = _valMediaAssets.LoadAsset<Material>("assets/MOD MATS/screenmaterial.mat");
            // ui assets setup
            UISprites = new Dictionary<string, Sprite>();
            foreach (var asset in _valMediaAssets.GetAllAssetNames())
            {
                if (asset.Contains("modui"))
                {
                    //Jotunn.Logger.LogInfo(asset);
                    var sprite = _valMediaAssets.LoadAsset<Sprite>(asset);
                    UISprites.Add(sprite.name, sprite);
                }
            }
            //unload assetbundle after we have our assets
            _valMediaAssets.Unload(false);
        }
        
        private void AddRecipes()
        {
            var pieceRecipesStringFromFile = LoadRecipeJson(_pieceRecipeFile, "default.json");
            var itemRecipesStringFromFile = LoadRecipeJson(_itemRecipeFile, "default_items.json");
            
            //check if old recipe file, if so update to new recipes
            var oldRecipeBool = !pieceRecipesStringFromFile.Contains("receiver") || !pieceRecipesStringFromFile.Contains("theater") ||
                                !pieceRecipesStringFromFile.Contains("speaker"); // TODO recipe versioning
            if (oldRecipeBool && OODConfig.AutoUpdateRecipes.Value) 
            {
                Jotunn.Logger.LogDebug("Old recipe file detected. Updating to new recipes.");
                List<PieceConfig> oldRecipes = PieceConfig.ListFromJson(pieceRecipesStringFromFile);
                var newRecipes = FileFromManifest("OdinOnDemand.Assets.default.json");
                UpdateRecipeFile(newRecipes, oldRecipes, _pieceRecipeFile);
            }
            else
            {
                if(!OODConfig.AutoUpdateRecipes.Value) Jotunn.Logger.LogWarning("Auto recipe update disabled. Skipping recipe update.");
            }
            
            LoadPieceConfigList(PieceConfig.ListFromJson(pieceRecipesStringFromFile));
            LoadItemConfigList(ItemConfig.ListFromJson(itemRecipesStringFromFile));
            
        }

        private void UpdateRecipeFile(string newRecipes, List<PieceConfig> oldRecipes, string file)
        {
            if (newRecipes != "")
            {
                var newRecipeList = PieceConfig.ListFromJson(newRecipes);
                MergeRecipes(oldRecipes, newRecipeList);
                
                try
                {
                    var dtoList = PieceConfigDTO.ToDTOList(oldRecipes);
                    string json = JsonConvert.SerializeObject(dtoList, Formatting.Indented);
                    File.WriteAllText(file, json);
                }
                catch (Exception e)
                {
                    Jotunn.Logger.LogError("Failed to write new recipes to file. Check log for details.");
                    Jotunn.Logger.LogWarning(e);
                }
            }
            else
            {
                Jotunn.Logger.LogError("FATAL Failed to load default recipes.");
            }
        }


        private static void MergeRecipes(List<PieceConfig> mergeRecipe, List<PieceConfig> recipesToMerge)
        {
            foreach (var newRecipe in recipesToMerge)
            {
                if (!mergeRecipe.Any(r => r.Name == newRecipe.Name))
                {
                    mergeRecipe.Add(newRecipe);
                }
            }
        }

        /// <summary>
        ///     Returns the recipe JSON in <paramref name="file" />. A missing or invalid file is
        ///     replaced with the embedded default, and the default is used even when it cannot be
        ///     written: a fresh install has no OdinOnDemand config folder yet, and failing here
        ///     stopped the plugin from loading.
        /// </summary>
        private static string LoadRecipeJson(string file, string defaultResource)
        {
            string json = null;
            try
            {
                if (File.Exists(file)) json = File.ReadAllText(file);
            }
            catch (Exception e)
            {
                Jotunn.Logger.LogWarning("Could not read " + file + ": " + e.Message);
            }

            if (json != null && IsValidJson(json)) return json;
            if (json != null)
                Jotunn.Logger.LogWarning(
                    "JSON in " + file + " is invalid. Setting to default recipes. " +
                    "If you wish to edit recipes please use a JSON validator or delete your recipe file and restart the game for a new default file.");

            var defaults = FileFromManifest("OdinOnDemand.Assets." + defaultResource);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file, defaults);
                Jotunn.Logger.LogDebug("Wrote default recipes to " + file);
            }
            catch (Exception e)
            {
                Jotunn.Logger.LogWarning("Could not write default recipes to " + file + ", using them unsaved: " + e.Message);
            }
            return defaults;
        }

        private static string FileFromManifest(string file)
        {
            var manifestFile = "";
            using var stream =
                typeof(OdinOnDemandPlugin).Assembly.GetManifestResourceStream(file);
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                manifestFile = reader.ReadToEnd();
            }
            return manifestFile;
        }


        private void LoadPieceConfigList(List<PieceConfig> pieceConfigs)  //Loads recipes from the json file after it's parsed to a list
        {
            pieceConfigs.ForEach(c =>
            {
                var properName = LocalizationManager.Instance.TryTranslate(c.Name).ToLower().Replace(" ", "").Replace("'", "");
                var tex = _valMediaAssets.LoadAsset<Texture2D>("assets/MOD ICONS/" + properName + "icon.png");
                var mySprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), Vector2.zero);
                c.Icon = mySprite; //TODO: procedural icon generation, or embedded icons in assetbundle
                var customPiece = new CustomPiece(_valMediaAssets, properName, false, c);
                if (PieceManager.Instance.AddPiece(customPiece)) PieceCategoryPatch.Apply(customPiece.Piece);
            });
        }
        
        private void LoadItemConfigList(List<ItemConfig> itemConfigs)  //Loads recipes from the json file after it's parsed to a list
        {
            itemConfigs.ForEach(c =>
            {
                var properName = LocalizationManager.Instance.TryTranslate(c.Name).ToLower().Replace(" ", "").Replace("'", "");
                Logger.LogDebug("Adding item: " + properName);
                ItemManager.Instance.AddItem(new CustomItem(_valMediaAssets, properName, false, c));
            });
        }
        
        private void AddLocalizations() //TODO move to file based localization
        {
            Localization.AddTranslation("English", new Dictionary<string, string>
            {
                { "tag_odinondemand", "OdinOnDemand" },
                { "piece_flatscreen", "Flatscreen" },
                { "piece_tabletv", "Table TV" },
                { "piece_boombox", "Boombox" },
                { "piece_oldtv", "Old TV" },
                { "piece_laptop", "Laptop" },
                { "piece_monitor", "Monitor" },
                { "piece_gramophone", "Gramophone" },
                { "piece_theaterscreen", "Theater Screen" },
                { "piece_receiver", "Receiver" },
                { "piece_studiospeaker", "Studio Speaker" },
                { "piece_radio", "Radio" },
                { "piece_standingspeaker", "Standing Speaker" },
                { "item_remote", "Remote Control" },
                { "item_skaldsgirdle", "Skald's Girdle" },
                { "remote_usehint", "Use Screen" },
                { "remote_linkhint", "Link/Unlink" },
                { "remote_showlinkshint", "Show Speaker Links" },
                { "skaldsgirdle_hint", "Consult Skald"},
                { "remote_changelinkmodehint", "Change Link Mode"},
                { "item_remote_description", "Allows you to use media-players from a distance." },
                { "item_skaldsgirdle_description", "Allows you to commune with Skald while traveling."}
            });
        }
        
        // Returns true if strInput is valid JSON otherwise returns false. Uses Newtonsoft.Json, stolen from stackoverflow
        private static bool IsValidJson(string strInput)
        {
            if (string.IsNullOrWhiteSpace(strInput)) return false;
            strInput = strInput.Trim();
            if ((strInput.StartsWith("{") && strInput.EndsWith("}")) || //For object
                (strInput.StartsWith("[") && strInput.EndsWith("]"))) //For array
                try
                {
                    var obj = JToken.Parse(strInput);
                    return true;
                }
                catch (JsonReaderException jex)
                {
                    //Exception in parsing json
                    Jotunn.Logger.LogWarning(jex.Message);
                    return false;
                }
                catch (Exception ex) //some other exception
                {
                    Jotunn.Logger.LogWarning(ex.ToString());
                    return false;
                }

            return false;
        }
    }
}