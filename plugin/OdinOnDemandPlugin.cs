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
        public const string PluginVersion = "1.0.0";

        private static readonly CustomLocalization Localization = LocalizationManager.Instance.GetLocalization();
        public static readonly RpcHandler RPCHandlers = new RpcHandler();
        public static Material MainScreenMat;
        public static Dictionary<string, Sprite> UISprites;
        private AssetBundle _valMediaAssets;
        
        private static Harmony _harmony;
        private static string _pieceRecipeFile;
        private static string _itemRecipeFile;
        private static readonly string OdinConfigFolder = Paths.ConfigPath + "/OdinOnDemand/";
        public static ConfigFile OdinConfig { get; private set; } = new ConfigFile(OdinConfigFolder + "config.cfg", true);
        
        public StationManager StationManager { get; private set; }

        private void Awake()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) => {
                string assemblyName = new AssemblyName(args.Name).Name + ".dll";
                var resource = Assembly.GetExecutingAssembly().GetManifestResourceNames().FirstOrDefault(r => r.EndsWith(assemblyName));

                if (resource != null)
                {
                    using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
                    {
                        byte[] assemblyData = new byte[stream.Length];
                        stream.Read(assemblyData, 0, assemblyData.Length);
                        return Assembly.Load(assemblyData);
                    }
                }

                return null;
            };

            //setup config
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
            
            
            if (PieceManager.Instance.AddPiece(new CustomPiece("cartplayer", "Cart", pieceConfig)))
            {
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
            //Piece recipes
            if (!File.Exists(_pieceRecipeFile))
            {
                WriteDefaultPieceConfig("default.json");
                Jotunn.Logger.LogDebug(
                    "Did not find recipe json, loading and writing default recipes to: " + _pieceRecipeFile);
            }
            //Item recipes
            if(!File.Exists(_itemRecipeFile))
            {
                WriteDefaultItemConfig("default_items.json");
                Jotunn.Logger.LogDebug(
                    "Did not find item recipe json, loading and writing default recipes to: " + _itemRecipeFile);
            }
            
            
            var pieceRecipesStringFromFile = File.ReadAllText(_pieceRecipeFile);
            if (!IsValidJson(pieceRecipesStringFromFile))
            {
                WriteDefaultPieceConfig("default.json");
                Jotunn.Logger.LogWarning(
                    "JSON in com.ood.valmedia.recipes.json is invalid. Setting to default recipes. " +
                    "If you wish to edit recipes please use a JSON validator or delete your recipe file and restart the game for a new default file.");
            }
            var itemRecipesStringFromFile = File.ReadAllText(_itemRecipeFile);
            if(!IsValidJson(itemRecipesStringFromFile))
            {
                WriteDefaultItemConfig("default_items.json");
                Jotunn.Logger.LogWarning(
                    "JSON in com.ood.valmedia.recipes_item.json is invalid. Setting to default recipes. " +
                    "If you wish to edit recipes please use a JSON validator or delete your recipe file and restart the game for a new default file.");
            }
            
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

        private void WriteDefaultPieceConfig(string fileName)
        {
            try
            {
                var file = _pieceRecipeFile;
                var defaultRecip = FileFromManifest("OdinOnDemand.Assets." + fileName);

                var writer = File.CreateText(file);
                writer.Write(defaultRecip);
                writer.Close();
                writer.Dispose();
                
            } catch (Exception ex)
            {
                Jotunn.Logger.LogError("Exception when handling default recipe file. Check log for details.");
                Jotunn.Logger.LogWarning(ex);
            }
        }
        
        private void WriteDefaultItemConfig(string fileName)
        {
            try
            {
                var file = _itemRecipeFile;
                var defaultRecip = FileFromManifest("OdinOnDemand.Assets." + fileName);
                
                var writer = File.CreateText(file);
                writer.Write(defaultRecip);
                writer.Close();
                writer.Dispose();
                
            } catch (Exception ex)
            {
                Jotunn.Logger.LogError("Exception when handling default recipe file. Check log for details.");
                Jotunn.Logger.LogWarning(ex);
            }
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
                PieceManager.Instance.AddPiece(new CustomPiece(_valMediaAssets, properName, false, c));
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