﻿using BepInEx.Configuration;
using Jotunn;
using Jotunn.Managers;
using Jotunn.Utils;

namespace OdinOnDemand.Utils
{
    public static class OODConfig // OdinOnDemandConfig
    {
        public enum YouTubeAPI
        {
            YouTubeExplode,
            NodeJs
        }

        public static ConfigEntry<string> NodeUrl { get; set; } // NodeJS Server URL
        public static ConfigEntry<string> YtAuthCode { get; private set; } // NodeJS Auth Code
        //public static ConfigEntry<string> assetsToLoad { get; set; }
        public static ConfigEntry<bool> IsYtEnabled { get; private set; } 
        public static ConfigEntry<bool> DebugEnabled { get; private set; }
        public static ConfigEntry<bool> VideoBacklight { get; private set; } 
        public static ConfigEntry<bool> ScreenDisableOutOfRange { get; private set; }
        public static ConfigEntry<bool> RemoteControlOwnerOnly { get; private set; } 
        public static ConfigEntry<bool> ShowAudioCenterSphere { get; private set; } 
        public static ConfigEntry<float> MasterVolumeScreen { get; private set; } 
        public static ConfigEntry<float> MasterVolumeMusicplayer { get; private set; }
        public static ConfigEntry<float> DefaultDistance { get; private set; }
        public static ConfigEntry<float> MaxListeningDistance { get; private set; }
        public static ConfigEntry<float> RemoteControlDistance { get; private set; }
        public static ConfigEntry<float> DefaultAudioSourceVolume { get; private set; }
        public static ConfigEntry<int> YouTubeExplodeTimeout { get; private set; } 
        public static ConfigEntry<int> SoundCloudExplodeTimeout { get; private set; }
        private static ConfigEntryBase ChangedSetting { get; set; }

        public static ConfigEntry<YouTubeAPI> YoutubeAPI { get; private set; }
        private static bool IsConfigChanged { get; set; }

        public static void Bind(ConfigFile config)
        {
            config.SaveOnConfigSet = true;
            IsConfigChanged = false;

            YoutubeAPI = config.Bind("OdinOnDemand", "YouTube API Type", YouTubeAPI.YouTubeExplode,
                new ConfigDescription(
                    "Whether to use built-in library or self-hosted NodeJS yt-dlp server to grab YouTube URLs. " +
                    "You probably do not need to touch this setting, but see GitHub for setup if interested. Restart required.",
                    null,
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));

            if (YoutubeAPI.Value == YouTubeAPI.NodeJs)
            {
                NodeUrl = config.Bind("OdinOnDemand", "NodeJS Server", "http://localhost:3000/yt/",
                    new ConfigDescription("yt-dlp NodeJS Server URL. Must format like http://ip:port/yt/", null,
                        new ConfigurationManagerAttributes { IsAdminOnly = true }));
                YtAuthCode = config.Bind("OdinOnDemand", "NodeJS Auth Code", "CHANGEME=",
                    new ConfigDescription(
                        "The auth code for the NodeJS yt-dlp server. This must match the one in server.js.", null,
                        new ConfigurationManagerAttributes { IsAdminOnly = true }));
            }

            MasterVolumeScreen = config.Bind("OdinOnDemand", "Screen Master Volume", 1f,
                new ConfigDescription("Master volume of all Screens. Clientside setting.",
                    new AcceptableValueRange<float>(-15f, 15f)));

            MasterVolumeMusicplayer = config.Bind("OdinOnDemand", "Musicplayer Master Volume", 1f,
                new ConfigDescription("Master volume of all Boomboxes and Gramophones. Clientside setting.",
                    new AcceptableValueRange<float>(-15f, 15f)));

            DefaultDistance = config.Bind("OdinOnDemand", "Audio Default Distance", 75f,
                new ConfigDescription("Default listening distance for all mediaplayers.", null,
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));

            MaxListeningDistance = config.Bind("OdinOnDemand", "Maximum Listening Distance", 250f,
                new ConfigDescription(
                    "The maximum a viking can set a mediaplayer's listening distance to. Admins ignore this.", null,
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));

            RemoteControlDistance = config.Bind("OdinOnDemand", "Remote Control Distance", 25f,
                new ConfigDescription("How far away you can control mediaplayers with the remote control.", null,
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));

            DefaultAudioSourceVolume = config.Bind("OdinOnDemand", "Audio Default Volume", 1f,
                new ConfigDescription("The default volume when a mediaplayer is first loaded into the world.",
                    new AcceptableValueRange<float>(0f, 1f)));

            IsYtEnabled = config.Bind("OdinOnDemand", "Enable Youtube", true,
                new ConfigDescription("Enable Youtube Functionality. Enabled by default.", null,
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));

            RemoteControlOwnerOnly = config.Bind("OdinOnDemand", "Remote Control Owner Only", false,
                new ConfigDescription(
                    "If enabled, only the creator of the mediaplayer may control it with a remote control.", null,
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));
            
            ShowAudioCenterSphere = config.Bind("OdinOnDemand", "Show Audio Center Sphere", true,
                new ConfigDescription(
                    "When this setting is enabled and you link a speaker to a mediaplayer, it will display a small red sphere representing the center of the audio for the mediaplayer.", null));

            VideoBacklight = config.Bind("OdinOnDemand", "Video Screen Backlight", true,
                new ConfigDescription(
                    "This will make the screen unaffected by world light and look better. On by default. Only applied on newly placed screens."));

            DebugEnabled = config.Bind("OdinOnDemand", "Debug Logging", false,
                new ConfigDescription("Enable Debug Logging"));


            ScreenDisableOutOfRange = config.Bind("OdinOnDemand", "Screens Stop Rendering Out of Range", false,
                new ConfigDescription(
                    "When a player is outside the audio range of a screen, the screen will stop rendering. " +
                    "(Experimental, requires expensive distance calculations. With lots of screens this may add up.) ",
                    null,
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));

            YouTubeExplodeTimeout = config.Bind("OdinOnDemand", "YoutubeExplode Timeout", 4000,
                new ConfigDescription("Custom timeout for YoutubeExplode tasks", null,
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));

            SoundCloudExplodeTimeout = config.Bind("OdinOnDemand", "SoundCloudExplode Timeout", 6000,
                new ConfigDescription("Custom timeout for SoundCloudExplode tasks", null,
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));
            /*
            assetsToLoad = config.Bind("OdinOnDemand", "Assets", "",
                new ConfigDescription("seperated by commas", null,
                new ConfigurationManagerAttributes { IsAdminOnly = true }));
            */
            config.SettingChanged += Config_SettingChanged;
        }

        public static void ReadAndWriteConfigValues(ConfigFile config)
        {
            config.Save();
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