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

        public float MuteVol { set; get; } = 0.5f;

        public float VerticalDistanceDropoff { set; get; } = 0f;
        
        public float DropoffPower { set; get; } = 1.5f;
        public float Volume { set; get; } = 0.5f;

        public bool IsLinkedToParent { set; get; } = false;

        public CinemaPackage.MediaPlayers PlayerType { set; get; }
        
        public DynamicStation DynamicStation { get; set; }
        
        public Sprite Thumbnail { get; set; }

        public enum LinkType
        {
            Youtube,
            Soundcloud,
            Video,
            Audio,
            RelativeAudio,
            RelativeVideo,
        }

        public enum PlayerMode
        {
            URL = 0,
            Dynamic = 1
        }
    }
}