using OdinOnDemand.Utils;
using UnityEngine;

namespace OdinOnDemand.Utils
{
    public class PlayerSettings
    {
        public static readonly int Playing = Animator.StringToHash("Playing");
        public static readonly int MainTex = Shader.PropertyToID("_MainTex");
        public static readonly int EmissiveColorMap = Shader.PropertyToID("_EmissiveColorMap");

        private static volatile bool _trackingForward;

        internal LinkType playerLinkType;

        public PlayerSettings() { } // This class just stores player settings for the MediaPlayer

        public bool IsFreshlyPlaced { set; get; } = true;

        public bool TrackingForward { set => _trackingForward = value; get => _trackingForward; }

        public bool IsPaused { set; get; }

        public int LoadingCount { set; get; }

        public bool IsGuiActive { get; set; }

        public bool AdminOnly { set; get; }

        public bool AutoPlay { set; get; }

        public bool IsLocked { set; get; } = true;

        public bool IsLooping { set; get; }

        public bool IsPlayingPlaylist { set; get; }

        public bool IsSettingsGuiActive { set; get; }

        public bool IsShuffling { set; get; }

        public float MuteVol { set; get; } = 0.5f;

        public CinemaPackage.MediaPlayers PlayerType { set; get; }

        public float Volume { set; get; } = 0.5f;

        public bool IsLinkedToParent { set; get; } = false;

        public enum LinkType
        {
            Youtube,
            Soundcloud,
            Video,
            Audio,
            RelativeAudio,
            RelativeVideo,
        }
    }
}