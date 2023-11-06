using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
using OdinOnDemand.Utils;
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
        public const string PluginGUID = "com.ood.valmedia";
        public const string PluginName = "OdinOnDemand";
        public const string PluginVersion = "0.9.92";

        private static readonly CustomLocalization Localization = LocalizationManager.Instance.GetLocalization();
        public static readonly RpcHandler RPCHandlers = new RpcHandler();
        public static Material MainScreenMat;
        public static Dictionary<string, Sprite> UISprites;
        private AssetBundle _valMediaAssets;
        
        private static Harmony _harmony;
        private static string _configFile;

        private void Awake()
        {
            //setup config
            OODConfig.Bind(Config);
            _configFile = Paths.ConfigPath + "/com.ood.valmedia.recipes.json";
            OODConfig.SyncManager();
            
            //Key Config
            ConfigFile keyConfig = new ConfigFile(Path.Combine(Paths.ConfigPath, "com.ood.valmedia.keyconfig.cfg"), true);
            SynchronizationManager.Instance.RegisterCustomConfig(keyConfig);
            KeyConfig.SetupKeyConfig(keyConfig);
            
            //create rpc handler
            RPCHandlers.Create();
            
            //init config
            AddLocalizations();
            
            //load assets and configure pieces and items
            LoadAssets();
            
            //setup harmony patches
            _harmony = new Harmony("Harmony.ValMedia.OOD");
            _harmony.PatchAll();
            
            Jotunn.Logger.LogDebug("** OdinOnDemand Initialised **");
        }

        private void LoadAssets()
        {
            // Load asset bundle from the filesystem, setup sprite textures
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Vulkan)
            {
                _valMediaAssets = AssetUtils.LoadAssetBundleFromResources("videoplayers", typeof(OdinOnDemandPlugin).Assembly);
                Jotunn.Logger.LogDebug("Loading OdinOnDemand Assets");
            }
            else
            {
                _valMediaAssets =
                    AssetUtils.LoadAssetBundleFromResources("videoplayersvulkan", typeof(OdinOnDemandPlugin).Assembly);
                Jotunn.Logger.LogDebug("Loading Vulkan OdinOnDemand Assets");
            }
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
            AddRemoteRecipe(); //Remote recipe

            //Piece recipes
            if (!File.Exists(_configFile))
            {
                LoadDefaultRecipes(true);
                Jotunn.Logger.LogDebug(
                    "Did not find recipe json, loading and writing default recipes to: " + Paths.ConfigPath + "/com.ood.valmedia.recipes.json");
                return;
            }
            
            var recipeStringFromFile = File.ReadAllText(_configFile);
            
            if (!IsValidJson(recipeStringFromFile))
            {
                LoadDefaultRecipes();
                Jotunn.Logger.LogWarning(
                    "JSON in com.ood.valmedia.recipes.json is invalid. Setting to default recipes. " +
                    "If you wish to edit recipes please use a JSON validator or delete your recipe file and restart the game for a new default file.");
                return;
            }
            
            //check if old recipe file, if so update to new recipes
            var oldRecipeBool = !recipeStringFromFile.Contains("receiver") || !recipeStringFromFile.Contains("theater") ||
                                !recipeStringFromFile.Contains("speaker");
            if (oldRecipeBool && OODConfig.AutoUpdateRecipes.Value) 
            {
                Jotunn.Logger.LogDebug("Old recipe file detected. Updating to new recipes.");
                List<PieceConfig> oldRecipes = PieceConfig.ListFromJson(recipeStringFromFile);
                var newRecipes = RecipFromManifest();
                
                UpdateRecipeFile(newRecipes, oldRecipes, _configFile);
            }
            else
            {
                if(!OODConfig.AutoUpdateRecipes.Value) Jotunn.Logger.LogWarning("Auto recipe update disabled. Skipping recipe update.");
                LoadRecipes(PieceConfig.ListFromJson(recipeStringFromFile));
            }
        }

        private void UpdateRecipeFile(string newRecipes, List<PieceConfig> oldRecipes, string file)
        {
            if (newRecipes != "")
            {
                var newRecipeList = PieceConfig.ListFromJson(newRecipes);
                MergeRecipes(oldRecipes, newRecipeList);

                LoadRecipes(oldRecipes);
                try
                {
                    var dtoList = Utils.PieceConfigDTO.ToDTOList(oldRecipes);
                    string json = JsonConvert.SerializeObject(dtoList, Formatting.Indented);
                    File.WriteAllText(file, json);
                }
                catch (Exception e)
                {
                    Jotunn.Logger.LogError("Failed to write new recipes to file. Check log for details.");
                    Jotunn.Logger.LogDebug(e);
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

        private void AddRemoteRecipe()
        {
            // Remote config
            //TODO: Item json recipes
            var remoteConfig = new ItemConfig
            {
                Amount = 1
            };

            remoteConfig.AddRequirement(new RequirementConfig("Bronze", 1));
            var tex = _valMediaAssets.LoadAsset<Texture2D>("assets/MOD ICONS/remoteicon.png");
            var mySprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), Vector2.zero);
            remoteConfig.Icons.AddItem(mySprite);

            var remoteItem = new CustomItem(_valMediaAssets, "remote", false, remoteConfig);
            ItemManager.Instance.AddItem(remoteItem);
            var preloadAsset = PrefabManager.Instance.GetPrefab("remote");
            preloadAsset.transform.Find("attach").gameObject.AddComponent<RemoteControlItem>();
        }

        private void LoadDefaultRecipes(bool writeToFile = false)
        {
            try
            {
                var file = Paths.ConfigPath + "/com.ood.valmedia.recipes.json";
                var defaultRecip = RecipFromManifest();
                if (defaultRecip != "")
                {
                    LoadRecipes(PieceConfig.ListFromJson(defaultRecip));
                }
                else
                {
                    Jotunn.Logger.LogError("FATAL Failed to load default recipes.");
                }

                if (writeToFile)
                {
                    var writer = File.CreateText(file);
                    writer.Write(defaultRecip);
                    writer.Close();
                    writer.Dispose();
                }
            } catch (Exception ex)
            {
                Jotunn.Logger.LogError("Exception when handling default recipe file. Check log for details.");
                Jotunn.Logger.LogDebug(ex);
            }
        }

        private static string RecipFromManifest()
        {
            var defaultRecip = "";
            using var stream =
                typeof(OdinOnDemandPlugin).Assembly.GetManifestResourceStream("OdinOnDemand.Assets.default.json");
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                defaultRecip = reader.ReadToEnd();
            }
            stream?.Dispose();
            return defaultRecip;
        }


        private void LoadRecipes(List<PieceConfig> pieceConfigs)  //Loads recipes from the json file after it's parsed to a list
        {
            pieceConfigs.ForEach(c =>
            {
                var properName = LocalizationManager.Instance.TryTranslate(c.Name).ToLower().Replace(" ", "");
                var tex = _valMediaAssets.LoadAsset<Texture2D>("assets/MOD ICONS/" + properName + "icon.png");
                var mySprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), Vector2.zero);
                c.Icon = mySprite; //TODO: procedural icon generation
                //Jotunn.Logger.LogInfo("Adding recipe for " + properName);
                PieceManager.Instance.AddPiece(new CustomPiece(_valMediaAssets, properName, false, c));
                var preloadAsset = PrefabManager.Instance.GetPrefab(properName);
                
                if(properName.Contains("speaker"))
                {
                    preloadAsset.AddComponent<SpeakerComponent>();
                    
                } else if (properName.Contains("receiver"))
                {
                    preloadAsset.AddComponent<ReceiverComponent>();
                }
                else
                {
                    preloadAsset.AddComponent<MediaPlayerComponent>();
                }
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
                { "remote_usehint", "Use Screen" },
                { "remote_linkhint", "Link/Unlink" },
                { "remote_changelinkmodehint", "Change Link Mode"},
                { "item_remote_description", "Allows you to use media-players from a distance." }
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
                    Jotunn.Logger.LogDebug(jex.Message);
                    return false;
                }
                catch (Exception ex) //some other exception
                {
                    Jotunn.Logger.LogDebug(ex.ToString());
                    return false;
                }

            return false;
        }
    }
}