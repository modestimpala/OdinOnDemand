using OdinOnDemand.Components;
using OdinOnDemand.MPlayer;
using OdinOnDemand.Utils.Config;
using OdinOnDemand.Utils.Net;
using UnityEngine;


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
        }
        
        private static (float, BasePlayer) GetDistanceFromMediaplayers()
        {
            if (!Player.m_localPlayer)
            {
                return (float.MaxValue, null);
            }

            var playerPos = Player.m_localPlayer.transform.position;
            float distance = float.MaxValue;
            BasePlayer closestMediaPlayer = null;

            foreach (var kvp in ComponentLists.MediaComponentLists)
            {
                foreach (BasePlayer component in kvp.Value)
                {
                    if(!component) continue;
                    if (!component.mAudio.isPlaying && component.mAudio.time == 0f && !component.mAudio.loop)
                    {
                        continue;
                    }

                    if (component.PlayerSettings.IsPaused || !component.PlayerSettings.IsPlaying)
                    {
                        continue;
                    }

                    var mediaPlayerPos = component.transform.position;
                    var newDistance = Vector3.Distance(playerPos, mediaPlayerPos);
                    if (newDistance < distance)
                    {
                        distance = newDistance;
                        closestMediaPlayer = component;
                    }
                }
            }
            return (distance, closestMediaPlayer);
        }
    }
}