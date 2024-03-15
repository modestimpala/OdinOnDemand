using System;
using System.Collections;
using System.Collections.Generic;
using OdinOnDemand.MPlayer;
using OdinOnDemand.Utils;
using OdinOnDemand.Utils.Net;
using OdinOnDemand.Utils.Net.Explode;
using UnityEngine;
using UnityEngine.Video;

namespace OdinOnDemand.Interfaces
{
    public interface IPlayer
    {
        AudioSource mAudio { get; }
        VideoPlayer mScreen { get; }
        PlayerSettings PlayerSettings { get; }
        string UnparsedURL { get; set; }
        public int PlaylistPosition {  set; get; } 
        public string PlaylistString { set; get; } 
        public List<VideoInfo> CurrentPlaylist {  set; get; }
        public List<VideoInfo> PreShufflePlaylist { set; get; }
        public ZNetView ZNetView { get; }
        public string PlaylistURL { set; get; }
        
        GameObject gameObject { get; }
        
        public void SetURL(string url);
        
        public void Play(bool isRPC = false);
        
        public void Pause(bool isRPC = false);
        
        public void Stop(bool isRPC = false);

        public void SaveZDO(bool saveTime = false);
        
        public void UpdateZDO();
        
        public Coroutine StartPlayerCoroutine(IEnumerator routine);
        
        public void StopPlayerCoroutine(Coroutine routine);
    }
}