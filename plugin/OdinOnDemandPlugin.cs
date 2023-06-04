using System;
using System.Collections.Generic;
using System.IO;
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
        public const string PluginVersion = "0.9.87";

        private static readonly CustomLocalization Localization = LocalizationManager.Instance.GetLocalization();
        public static readonly RpcHandler RPCHandlers = new RpcHandler();
        public static Material MainScreenMat;
        public static Dictionary<string, Sprite> UISprites;
        private AssetBundle _valMediaAssets;

        private void Awake()
        {
            //setup config
            OODConfig.Bind(Config);
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
            // Load recipes from JSON file
            var file = Paths.ConfigPath + "/com.ood.valmedia.recipes.json";
            if (!File.Exists(file))
            {
                LoadDefaultRecipes(true);
                Jotunn.Logger.LogDebug(
                    "Did not find default json, loading and writing default recipes to com.ood.valmedia.recipes.json");
                return;
            }
            var recipesFromFile = File.ReadAllText(file);
            //Check to make sure it has all the new recipes. there is a better way to do this? Versioning? maybe don't force update?
            var oldRecipeBool = !recipesFromFile.Contains("receiver") || !recipesFromFile.Contains("theater") ||
                                !recipesFromFile.Contains("speaker");
            if (!IsValidJson(recipesFromFile) || oldRecipeBool) 
            {
                LoadDefaultRecipes();
                Jotunn.Logger.LogWarning(
                    "JSON in com.ood.valmedia.recipes.json is invalid or outdated. Setting to default recipes. Try deleting your recipe file.");
                return;
            }
            LoadRecipes(PieceConfig.ListFromJson(recipesFromFile));

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
                var writer = File.CreateText(file);
                var defaultRecip = RecipFromManifest();
                if (defaultRecip != "")
                {
                    LoadRecipes(PieceConfig.ListFromJson(defaultRecip));
                }
                else
                {
                    Jotunn.Logger.LogError("FATAL Failed to load default recipes.");
                }
            } catch (Exception ex)
            {
                Jotunn.Logger.LogDebug(ex);
            }
        }

        private static string RecipFromManifest()
        {
            string defaultRecip = "";
            using (Stream stream =
                   typeof(OdinOnDemandPlugin).Assembly.GetManifestResourceStream("OdinOnDemand.Assets.default.json"))
            {
                if (stream != null)
                    using (var reader = new StreamReader(stream))
                    {
                        defaultRecip = reader.ReadToEnd();
                    }
            }

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