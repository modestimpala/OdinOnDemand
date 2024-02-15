using System;
using OdinOnDemand.Components;
using UnityEngine;
using UnityEngine.Audio;
using Logger = Jotunn.Logger;

namespace OdinOnDemand.Utils
{
    public class AudioFader : MonoBehaviour
    {
        public static AudioFader Instance { get; set; }
        private static MusicMan _musicMan;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
            }
        }

        public AudioFader()
        {
            _musicMan = MusicMan.m_instance;
        }

        public void Update()
        {
            if (OODConfig.AudioFadeType.Value == OODConfig.FadeType.Fade)
            {
                FadeGameMusic();
            }
        }

        public void FadeGameMusic()
        {
            var (distance, closestMediaPlayer) = GetDistanceFromMediaplayers();
            if (closestMediaPlayer == null)
            {
                Logger.LogInfo("No media players found");
                _musicMan.m_musicVolume = PlayerPrefs.GetFloat("MusicVolume", 1f);
                return;
            }
            var maxDistance = closestMediaPlayer.mAudio.maxDistance / 1.35f;
            if(distance > maxDistance)
            {
                _musicMan.m_musicVolume = PlayerPrefs.GetFloat("MusicVolume", 1f);
                return;
            }
            float normalizedDistance = Mathf.Clamp01(distance / maxDistance);
            float currentVolumeDb = 20 * Mathf.Log10(_musicMan.m_musicSource.volume);
            float volumeDb = Mathf.Lerp(OODConfig.LowestVolumeDB.Value, currentVolumeDb, normalizedDistance);
            _musicMan.m_musicSource.volume = Mathf.Pow(10.0f, volumeDb / 20.0f);
            Logger.LogInfo("volume: " + _musicMan.m_musicSource.volume);
        }
        
        private static (float, MediaPlayerComponent) GetDistanceFromMediaplayers()
        {
            var mediaPlayers = RpcHandler.mediaPlayerList;
            if (mediaPlayers.Count == 0)
            {
                Logger.LogInfo("No media players in RpcHandler.mediaPlayerList");
            }
            if (!Player.m_localPlayer)
            {
                Logger.LogInfo("Player.m_localPlayer is null");
                return (float.MaxValue, null);
            }
            var playerPos = Player.m_localPlayer.transform.position;
            var distance = float.MaxValue;
            MediaPlayerComponent closestMediaPlayer = null;
            foreach (var mediaPlayer in mediaPlayers)
            {
                if (!mediaPlayer.mAudio.isPlaying && mediaPlayer.mAudio.time == 0f && !mediaPlayer.mAudio.loop)
                {
                    Logger.LogInfo("MediaPlayer " + mediaPlayer.name + " is not playing, at start of audio clip, or not looping");
                    continue;
                }
                if(mediaPlayer.PlayerSettings.IsPaused)
                {
                    Logger.LogInfo("MediaPlayer " + mediaPlayer.name + " is paused");
                    continue;
                }
                var mediaPlayerPos = mediaPlayer.transform.position;
                var newDistance = Vector3.Distance(playerPos, mediaPlayerPos);
                if (newDistance < distance)
                {
                    distance = newDistance;
                    closestMediaPlayer = mediaPlayer;
                }
            }
            if (closestMediaPlayer == null)
            {
                Logger.LogInfo("No suitable media player found");
            }
            return (distance, closestMediaPlayer);
        }
    }
}