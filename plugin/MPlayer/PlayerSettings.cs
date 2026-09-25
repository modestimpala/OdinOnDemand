using System.Collections.Generic;
using OdinOnDemand.Dynamic;
using OdinOnDemand.Utils.Net;
using UnityEngine;

namespace OdinOnDemand.MPlayer
{
    public class PlayerSettings
    {
        public static readonly int Playing = Animator.StringToHash("Playing");
        public static readonly int MainTex = Shader.PropertyToID("_MainTex");
        public static readonly int EmissiveColorMap = Shader.PropertyToID("_EmissiveColorMap");
        
        internal LinkType PlayerLinkType;

        public PlayerMode CurrentMode = PlayerMode.URL;
        
        public List<AudioClip> CurrentDynamicList = new List<AudioClip>();

        public bool IsPaused { set; get; }
        
        public bool IsPlaying { set; get; }

        public int LoadingCount { set; get; }

        public bool IsGuiActive { get; set; }

        public bool AdminOnly { set; get; }

        public bool IsLocked { set; get; } = true;

        public bool IsLooping { set; get; }

        public bool IsPlayingPlaylist { set; get; }

        public bool IsSettingsGuiActive { set; get; }

        public bool IsShuffling { set; get; }

        /// <summary>Volume restored by unmuting when no earlier level is known.</summary>
        public const float FallbackUnmuteVolume = 0.5f;

        /// <summary>The volume to return to on unmute.</summary>
        public float MuteVol { set; get; } = FallbackUnmuteVolume;

        public float VerticalDistanceDropoff { set; get; } = 0f;
        
        public float DropoffPower { set; get; } = 1.5f;
        public float Volume { set; get; } = 0.5f;

        public bool IsLinkedToParent { set; get; } = false;

        /// <summary>Where linked speakers play from. Shared through the ZDO.</summary>
        public SpeakerMode SpeakerOutput { set; get; } = SpeakerMode.Center;

        public CinemaPackage.MediaPlayers PlayerType { set; get; }
        
        public DynamicStation DynamicStation { get; set; }
        
        public Sprite Thumbnail { get; set; }

        /// <summary>Title of the playing media, shown in the URL panel. Null when unknown.</summary>
        public string MediaTitle { get; set; }

        public enum LinkType
        {
            Youtube,
            Soundcloud,
            Video,
            Audio,
            RelativeAudio,
            RelativeVideo,
            NetworkStream,
            LiveChannel,
        }

        public enum PlayerMode
        {
            URL = 0,
            Dynamic = 1
        }
    }
}