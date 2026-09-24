using OdinOnDemand.MPlayer;
using OdinOnDemand.Utils.Config;
using OdinOnDemand.Utils.Net;
using UnityEngine;


namespace OdinOnDemand.Utils
{
    /// <summary>
    ///     Ducks the game's music near an active media player. MusicMan rewrites its source volume
    ///     every frame from the track volume and the player's music setting, so the fade scales
    ///     that result in LateUpdate and never touches MusicMan's own fields.
    /// </summary>
    public class AudioFader : MonoBehaviour
    {
        public static AudioFader Instance { get; set; }

        private AudioSource musicSource;
        private float unfadedVolume;
        private float appliedVolume = -1f;

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

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void LateUpdate()
        {
            var musicMan = MusicMan.instance;
            var source = musicMan ? musicMan.m_musicSource : null;
            if (source != musicSource)
            {
                musicSource = source;
                appliedVolume = -1f;
            }
            if (!source) return;

            // MusicMan skips its volume write on some frames (nothing queued, between tracks).
            // Scaling our own output again would keep lowering the music on each of those frames.
            var volume = source.volume;
            if (!Mathf.Approximately(volume, appliedVolume)) unfadedVolume = volume;

            var gain = OODConfig.AudioFadeType.Value == OODConfig.FadeType.Fade ? FadeGain() : 1f;
            appliedVolume = unfadedVolume * gain;
            source.volume = appliedVolume;
        }

        /// <summary>Linear gain between the configured floor at the player and 1 at the fade edge.</summary>
        private static float FadeGain()
        {
            var (distance, closestMediaPlayer) = GetDistanceFromMediaplayers();
            if (!closestMediaPlayer) return 1f;
            var maxDistance = closestMediaPlayer.mAudio.maxDistance / 1.35f;
            if (maxDistance <= 0f || distance > maxDistance) return 1f;

            var normalizedDistance = Mathf.Clamp01(distance / maxDistance);
            var gainDb = Mathf.Lerp(OODConfig.LowestVolumeDB.Value, 0f, normalizedDistance);
            return Mathf.Pow(10.0f, gainDb / 20.0f);
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
                    if(!component || !component.mAudio) continue;
                    if (!component.mAudio.isPlaying && (!component.mAudio.clip || component.mAudio.time == 0f) && !component.mAudio.loop)
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
