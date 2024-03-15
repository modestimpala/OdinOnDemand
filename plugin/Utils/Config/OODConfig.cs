using BepInEx.Configuration;
using Jotunn.Managers;
using Jotunn.Utils;
using Logger = Jotunn.Logger;

namespace OdinOnDemand.Utils.Config
{
    public static class OODConfig // OdinOnDemandConfig
    {
        public enum YouTubeAPI
        {
            YouTubeExplode,
            NodeJs
        }
        
        public enum FadeType
        {
            Fade,
            None
        }

        public static ConfigEntry<string> NodeUrl { get; set; } // NodeJS Server URL
        public static ConfigEntry<string> YtAuthCode { get; private set; } // NodeJS Auth Code
        //public static ConfigEntry<string> assetsToLoad { get; set; }
        public static ConfigEntry<bool> IsYtEnabled { get; private set; } 
        public static ConfigEntry<bool> AutoUpdateRecipes { get; private set; }
        public static ConfigEntry<bool> DebugEnabled { get; private set; }
        public static ConfigEntry<bool> VideoBacklight { get; private set; } 
        public static ConfigEntry<bool> MobilePlayerVisuals { get; private set; } 
        public static ConfigEntry<bool> ScreenDisableOutOfRange { get; private set; }
        public static ConfigEntry<bool> RemoteControlOwnerOnly { get; private set; } 
        public static ConfigEntry<float> MasterVolumeScreen { get; private set; } 
        public static ConfigEntry<float> MasterVolumeMusicplayer { get; private set; }
        public static ConfigEntry<float> MasterVolumeTransport { get; private set; }
        public static ConfigEntry<float> DefaultDistance { get; private set; }
        public static ConfigEntry<float> MaxListeningDistance { get; private set; }
        public static ConfigEntry<float> MobileListeningDistance { get; private set; }
        public static ConfigEntry<float> SyncTime { get; private set; }
        public static ConfigEntry<bool> SkaldsGirdleEnabled { get; private set; }
        public static ConfigEntry<int> SkaldsGirdleCost { get; private set; }
        public static ConfigEntry<float> RemoteControlDistance { get; private set; }
        public static ConfigEntry<float> DefaultAudioSourceVolume { get; private set; }
        private static ConfigEntryBase ChangedSetting { get; set; }
        public static ConfigEntry<YouTubeAPI> YoutubeAPI { get; private set; }
        public static ConfigEntry<bool> VipMode { get; private set; }
        public static ConfigEntry<string> VipList { get; private set; }
        public static ConfigEntry<string> VipMessage { get; private set; }
        
        public static ConfigEntry<FadeType> AudioFadeType { get; private set; }
        public static ConfigEntry<float> LowestVolumeDB { get; private set; }
        private static bool IsConfigChanged { get; set; }

        public static void Bind(ConfigFile config)
        {
            config.SaveOnConfigSet = true;
            IsConfigChanged = false;

            YoutubeAPI = config.Bind("YouTube", "YouTube API Type", YouTubeAPI.YouTubeExplode,
                new ConfigDescription(
                    "Whether to use built-in library or self-hosted NodeJS yt-dlp server to grab YouTube URLs. " +
                    "You probably do not need to touch this setting, but see GitHub for setup if interested. Restart required. Extra options will appear after Node is selected.",
                    null,
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));
            if (YoutubeAPI.Value == YouTubeAPI.NodeJs)
            {
                NodeUrl = config.Bind("YouTube", "NodeJS Server", "http://localhost:3000/yt/",
                    new ConfigDescription("yt-dlp NodeJS Server URL. Must format like http://ip:port/yt/", null,
                        new ConfigurationManagerAttributes { IsAdminOnly = true }));
                YtAuthCode = config.Bind("YouTube", "NodeJS Auth Code", "CHANGEME=",
                    new ConfigDescription(
                        "The auth code for the NodeJS yt-dlp server. This must match the one in server.js.", null,
                        new ConfigurationManagerAttributes { IsAdminOnly = true }));
            }

            MasterVolumeScreen = config.Bind("Mixer Volumes", "Screen Master Volume", 1f,
                new ConfigDescription("Master volume of all Screens. Clientside setting.",
                    new AcceptableValueRange<float>(-15f, 15f)));

            MasterVolumeMusicplayer = config.Bind("Mixer Volumes", "Radio Master Volume", 1f,
                new ConfigDescription("Master volume of all Radios. Clientside setting.",
                    new AcceptableValueRange<float>(-15f, 15f)));
            
            MasterVolumeTransport = config.Bind("Mixer Volumes", "Mobile Player Master Volume", 1f,
                new ConfigDescription("Master volume of all players roaming using Skald's Girdle or Bard's Wagon. Clientside setting.",
                    new AcceptableValueRange<float>(-15f, 15f)));

            DefaultDistance = config.Bind("Server", "Audio Default Distance", 75f,
                new ConfigDescription("Default listening distance for all mediaplayers.", null,
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));

            MaxListeningDistance = config.Bind("Server", "Maximum Listening Distance", 250f,
                new ConfigDescription(
                    "The maximum a viking can set a mediaplayer's listening distance to. Admins ignore this.", null,
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));
            
            MobileListeningDistance = config.Bind("Server", "Mobile Player Listening Distance", 30f,
                new ConfigDescription(
                    "The listening distance of Mobile Players. Admins can still change their distance in the cog-wheel menu.", null,
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));
            
            SyncTime = config.Bind("Server", "Sync Time", 60f,
                new ConfigDescription("The time in seconds between each send net-sync of the mediaplayer's current time. Lower values are more taxing on the server.",
                    new AcceptableValueRange<float>(0.001f, float.MaxValue),
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));
            
            SkaldsGirdleEnabled = config.Bind("Server", "Skalds Girdle Sold at Haldor", true,
                new ConfigDescription(
                    "Whether or not Skald's Girdle is sold at the trader.", null,
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));
            
            SkaldsGirdleCost = config.Bind("Server", "Skalds Girdle Cost", 1000, new ConfigDescription(
                "The cost of Skald's Girdle at the trader.", null,
                new ConfigurationManagerAttributes { IsAdminOnly = true }));

            RemoteControlDistance = config.Bind("Remote Control", "Remote Control Distance", 25f,
                new ConfigDescription("How far away you can control mediaplayers with the remote control.", null,
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));

            DefaultAudioSourceVolume = config.Bind("Client Side", "Audio Default Volume", 1f,
                new ConfigDescription("The default volume a mediaplayer's volume slider is at when first loaded into the world.",
                    new AcceptableValueRange<float>(0f, 1f)));

            IsYtEnabled = config.Bind("YouTube", "Enable Youtube", true,
                new ConfigDescription("Enable Youtube Functionality. Enabled by default.", null,
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));
            
            AutoUpdateRecipes = config.Bind("Recipes", "Auto-Update Recipe File", true,
                new ConfigDescription("If enabled, plugin will attempt to ammend new recipes to old recipe files. An old recipe file is one that is detected lacking certain piece names. " +
                                      "If you would like to completely remove certain recipes via JSON, disable this."));

            RemoteControlOwnerOnly = config.Bind("Remote Control", "Remote Control Owner Only", false,
                new ConfigDescription(
                    "If enabled, only the creator of the mediaplayer may control it with a remote control.", null,
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));
            
            VideoBacklight = config.Bind("Client Side", "Video Screen Backlight", true,
                new ConfigDescription(
                    "This will make the screen unaffected by world light and look better. On by default. Only applied on newly placed screens."));

            MobilePlayerVisuals = config.Bind("Client Side", "Mobile Player Visuals", true,
                new ConfigDescription(
                    "When enabled, cart players and skalds girdle will have a visual representation of audio playing as sound waves. "));
            
            DebugEnabled = config.Bind("Client Side", "Debug Logging", false,
                new ConfigDescription("Enable Extra Debug Logging"));

            ScreenDisableOutOfRange = config.Bind("Client Side", "Screens Stop Rendering Out of Range", false,
                new ConfigDescription(
                    "When a player is outside the audio range of a screen, the screen will stop rendering. ",
                    null,
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));
            
            VipMode = config.Bind("VIP Mode", "VIP Mode", false,
                new ConfigDescription("When enabled, Only VIPs can place, remove, and interact with OdinOnDemand Pieces (admins excluded) WARNING: BETA. Use at own discretion. Please report any issues on Github/Nexus.", null,
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));
            
            VipList = config.Bind("VIP Mode", "VIP List", ",",
                new ConfigDescription("SteamID List Seperated by commas", null,
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));
            VipMessage = config.Bind("VIP Mode", "VIP Message", "You are not allowed to perform this action.",
                new ConfigDescription("The message displayed when a non-VIP attempts to access a VIP-only piece.", null,
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));
            
            AudioFadeType = config.Bind("Client Side", "Music Fade Type", FadeType.Fade,
                new ConfigDescription("How the in-game music fades in and out. Fade: Gradually changes volume based off distance from nearest active mediaplayer. None: No change.",
                    null));
            LowestVolumeDB = config.Bind("Client Side", "Lowest Fade Volume DB", -60f,
                new ConfigDescription("The lowest volume the music will fade to in DB. -65-80 is completely silent, 0 is neutral volume.",
                    new AcceptableValueRange<float>(-80f, 0f)));
            
            /*
            assetsToLoad = config.Bind("OdinOnDemand", "Assets", "",
                new ConfigDescription("seperated by commas", null,
                new ConfigurationManagerAttributes { IsAdminOnly = true }));
            */
            config.SettingChanged += Config_SettingChanged;
        }

        public static void ReadAndWriteConfigValues(ConfigFile config)
        {
            config?.Save();
        }

        private static void Config_SettingChanged(object sender, SettingChangedEventArgs e)
        {
            ChangedSetting = e.ChangedSetting;
            IsConfigChanged = true;
        }

        public static void SyncManager()
        {
            SynchronizationManager.OnConfigurationSynchronized +=
                delegate(object obj, ConfigurationSynchronizationEventArgs attr)
                {
                    var initialSynchronization = attr.InitialSynchronization;
                    if (initialSynchronization)
                    {
                        if (DebugEnabled.Value) Logger.LogDebug("Initial Config sync event received");
                    }
                    else
                    {
                        if (DebugEnabled.Value) Logger.LogDebug("Config sync event received");
                    }
                };
            SynchronizationManager.OnAdminStatusChanged += delegate
            {
                if (DebugEnabled.Value)
                    Logger.LogDebug("Admin status sync event received: " +
                                    (SynchronizationManager.Instance.PlayerIsAdmin
                                        ? "You're admin now"
                                        : "Downvoted, client"));
            };
        }
    }
}