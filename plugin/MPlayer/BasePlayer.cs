using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OdinOnDemand.Components;
using OdinOnDemand.Dynamic;
using OdinOnDemand.Interfaces;
using OdinOnDemand.Utils;
using OdinOnDemand.Utils.Config;
using OdinOnDemand.Utils.Net;
using OdinOnDemand.Utils.Net.Explode;
using OdinOnDemand.Utils.UI;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.Video;
using Logger = Jotunn.Logger;

namespace OdinOnDemand.MPlayer
{
    public abstract class BasePlayer : MonoBehaviour, IPlayer
    {
        

        // Media Player Core Components
        public VideoPlayer mScreen { get; set; }
        public AudioSource mAudio { get; set; }
        public Piece mPiece { get; internal set; }
        public Animator Animator { get; set; }
        protected GameObject ScreenPlaneObj { get; set; }
        protected internal GameObject ScreenUICanvasObj { get; set; }
        protected GameObject RadioPanelObj { get; set; }
        protected Coroutine DynamicStationCoroutine { get; set; }
        protected internal GameObject LoadingCircleObj { get; set; }
        protected Material TargetTexMat { get; set; }

        // Media Player Information
        public string mName { get; set; }
        public string MediaPlayerID { get; set; }
        public string UnparsedURL { get; set; }
        public Uri DownloadURL { get; set; }
        private YoutubeDecoder youtubeDecoder;
        private int playbackGeneration;
        private double pendingPlaybackTime;
        private bool hasPendingPlaybackTime;
        private bool youtubeBackendActive;
        private bool youtubeLoading;
        private bool legacyYoutubePlayback;

// Playlist Management
        public int PlaylistPosition { get; set; }
        public string PlaylistString { get; set; }
        public List<VideoInfo> CurrentPlaylist { get; set; }
        public List<VideoInfo> PreShufflePlaylist { get; set; }
        public string PlaylistURL { get; set; }

// Player Interaction and UI
        public PlayerSettings PlayerSettings { get; set; }
        public UIController UIController { get; set; }
        protected static AudioFader AudioFaderComp { get; set; }
        protected ParticleSystem WaveParticleSystem { get; set; }

// Networking and Data Handling
        public URLGrab URLGrab { get; set; }
        public DLSharp Ytdl { get; set; }
        public RpcHandler RPC { get; set; }
        public ZNetView ZNetView { get; set; }

        
        // Speaker Management
        internal HashSet<SpeakerComponent> mSpeakers = new HashSet<SpeakerComponent>();
        private Transform centerAudioSphere;
        protected SphereCollider triggerCollider;


        public void Awake() {
            ComponentLists.MediaComponentLists[GetType()].Add(this);
            UIController = new UIController(this);
            PlayerSettings = new PlayerSettings();
            //Network
            ZNetView = GetComponent<ZNetView>();
            // Controllers handlers and utils
            UIController.Initialize();
            RPC = OdinOnDemandPlugin.RPCHandlers;
            URLGrab = new URLGrab();
            ZNetView = gameObject.GetComponentInParent<ZNetView>();
            mScreen = gameObject.GetComponentInChildren<VideoPlayer>();
            //Screen events
            mScreen.prepareCompleted += ScreenPrepareCompleted;
            mScreen.loopPointReached += EndReached;
            mScreen.errorReceived += ScreenErrorReceived;
            SetupAudio();
            // Repeating tasks
            InvokeRepeating(nameof(UpdateLoadingIndicator), 0.5f, 0.5f);
            InvokeRepeating(nameof(UpdateChecks), 1f, 1f);
            InvokeRepeating(nameof(SyncTime), OODConfig.SyncTime.Value + 30f, OODConfig.SyncTime.Value);
            
            Ytdl = gameObject.AddComponent<DLSharp>();
            StartCoroutine(Ytdl.Setup());
            
            var zdo = ZNetView.GetZDO();
            if (ZNetScene.instance) //If we're freshly placed set some default data and flip bool
            {
                if (zdo != null)
                {
                    if (!zdo.GetString("MediaPlayerID").Equals(""))
                    {
                        RequestOwnership(zdo);
                        var id = GenerateUniqueID();
                        zdo.Set("MediaPlayerID", id); // Generate unique ID for this media player
                        SendUpdateZDO_RPC();
                    }
                    
                    MediaPlayerID = zdo.GetString("MediaPlayerID"); 
                }
            }
            
            // Audio fader
            if (OODConfig.AudioFadeType.Value != OODConfig.FadeType.None)
            {
                if (AudioFader.Instance == null && GameObject.Find("OODAudioFader") == null)
                {
                    var audioFader = new GameObject("OODAudioFader");
                    AudioFaderComp = audioFader.AddComponent<AudioFader>();
                    DontDestroyOnLoad(audioFader);
                }
            }
        }

        public void OnDestroy()
        {
            playbackGeneration++;
            DestroyYoutubeBackend();
            if (mScreen != null)
            {
                mScreen.prepareCompleted -= ScreenPrepareCompleted;
                mScreen.loopPointReached -= EndReached;
                mScreen.errorReceived -= ScreenErrorReceived;
            }
            ComponentLists.RemoveComponent(GetType(), this);
        }

        private void EndReached(VideoPlayer source)
        {
            if (PlayerSettings.PlayerLinkType == PlayerSettings.LinkType.Youtube && !legacyYoutubePlayback) return;
            HandlePlaybackEnded();
        }

        private void YoutubeEnded()
        {
            if (!youtubeBackendActive) return;
            HandlePlaybackEnded();
        }

        private void HandlePlaybackEnded()
        {
            if (PlayerSettings.IsPlayingPlaylist)
            {
                if (PlaylistPosition < CurrentPlaylist.Count - 1)
                {
                    SetLooping(false);
                    PlaylistPosition++;
                    SetURL(CurrentPlaylist[PlaylistPosition].Url);
                    return;
                }

                if (PlayerSettings.IsLooping)
                {
                    SetLooping(false);
                    PlaylistPosition = 0;
                    SetURL(CurrentPlaylist[PlaylistPosition].Url);
                    return;
                }
            }

            if (IsPlaybackLooping())
            {
                PlayerSettings.IsPlaying = true;
                PlayerSettings.IsPaused = false;
                return;
            }

            if (PlayerSettings.PlayerType == CinemaPackage.MediaPlayers.Radio &&
                Animator && !IsPlaybackLooping() && !PlayerSettings.IsPlayingPlaylist)
            {
                Animator.SetBool(PlayerSettings.Playing, false);
            }

            PlayerSettings.IsPlaying = IsPlaybackPlaying();
            PlayerSettings.IsPaused = false;
        }

        private void ScreenPrepareCompleted(VideoPlayer source)
        {
            if (PlayerSettings.PlayerLinkType == PlayerSettings.LinkType.Youtube && !legacyYoutubePlayback) return;
            CompletePreparation();
        }

        private void YoutubePrepared()
        {
            if (!youtubeBackendActive || youtubeDecoder == null) return;
            CompletePreparation();
        }

        private void CompletePreparation()
        {
            youtubeLoading = false;
            if (PlayerSettings.PlayerType == CinemaPackage.MediaPlayers.CinemaScreen && ScreenPlaneObj)
                ScreenPlaneObj.SetActive(true);

            if (UIController.LoadingIndicatorObj) UIController.LoadingIndicatorObj.SetActive(false);
            if (ScreenUICanvasObj && LoadingCircleObj)
            {
                ScreenUICanvasObj.SetActive(false);
                LoadingCircleObj.SetActive(false);
            }

            ApplyPendingPlaybackTime();
            StartCoroutine(DelayedExecution(0.5f, SendRequestTimeSync_RPC));

            PlayerSettings.IsPlaying = true;
            if (PlayerSettings.IsPaused)
            {
                PauseCurrentBackend();
                if (Animator) Animator.SetBool(PlayerSettings.Playing, false);
                if (WaveParticleSystem) WaveParticleSystem.Stop();
                return;
            }

            PlayCurrentBackend();
            if (Animator) Animator.SetBool(PlayerSettings.Playing, true);
            if (WaveParticleSystem && OODConfig.MobilePlayerVisuals.Value) WaveParticleSystem.Play();
        }

        private void ScreenErrorReceived(VideoPlayer source, string message)
        {
            if (PlayerSettings.PlayerLinkType == PlayerSettings.LinkType.Youtube && !legacyYoutubePlayback) return;
            HandlePlaybackError(message);
        }

        private void YoutubeError(string message)
        {
            if (!youtubeBackendActive) return;
            HandlePlaybackError(message);
            DestroyYoutubeBackend();
        }

        private void HandlePlaybackError(string message)
        {
            youtubeLoading = false;
            Logger.LogError("Media playback failed: " + message);
            PlayerSettings.IsPlaying = false;
            PlayerSettings.IsPaused = false;
            if (Animator) Animator.SetBool(PlayerSettings.Playing, false);
            if (WaveParticleSystem) WaveParticleSystem.Stop();
            if (ScreenUICanvasObj && LoadingCircleObj)
            {
                ScreenUICanvasObj.SetActive(false);
                LoadingCircleObj.SetActive(false);
            }
            if (UIController.LoadingIndicatorObj)
            {
                UIController.SetLoadingIndicatorText("Failed to load media");
                StartCoroutine(ResetLoadingIndicatorAfterDelay(playbackGeneration));
            }
        }

        public double PlaybackTime
        {
            get
            {
                if (youtubeBackendActive && youtubeDecoder != null)
                    return youtubeDecoder.Time;
                if (PlayerSettings.PlayerLinkType == PlayerSettings.LinkType.Youtube && !legacyYoutubePlayback)
                    return hasPendingPlaybackTime ? pendingPlaybackTime : 0d;
                if (IsVideoLink())
                    return mScreen != null ? mScreen.time : 0d;
                if (mAudio != null && mAudio.clip != null)
                    return mAudio.time;
                return hasPendingPlaybackTime ? pendingPlaybackTime : 0d;
            }
        }

        public bool IsVideoPlaying => IsVideoLink() && IsPlaybackPlaying();

        public void SetLooping(bool looping)
        {
            if (youtubeBackendActive && youtubeDecoder != null)
            {
                youtubeDecoder.IsLooping = looping && !PlayerSettings.IsPlayingPlaylist;
                return;
            }
            if (PlayerSettings.PlayerLinkType == PlayerSettings.LinkType.Youtube && !legacyYoutubePlayback) return;

            if (mScreen != null) mScreen.isLooping = looping && !PlayerSettings.IsPlayingPlaylist;
            if (mAudio != null) mAudio.loop = looping;
        }

        private bool IsVideoLink()
        {
            return PlayerSettings.PlayerLinkType == PlayerSettings.LinkType.Youtube ||
                   PlayerSettings.PlayerLinkType == PlayerSettings.LinkType.RelativeVideo ||
                   PlayerSettings.PlayerLinkType == PlayerSettings.LinkType.Video;
        }

        private bool IsPlaybackPlaying()
        {
            if (youtubeBackendActive && youtubeDecoder != null)
                return youtubeDecoder.IsPlaying;
            if (PlayerSettings.PlayerLinkType == PlayerSettings.LinkType.Youtube && !legacyYoutubePlayback)
                return false;
            if (IsVideoLink())
                return mScreen != null && mScreen.isPlaying;
            return mAudio != null && mAudio.isPlaying;
        }

        private bool IsPlaybackPrepared()
        {
            if (youtubeBackendActive && youtubeDecoder != null)
                return youtubeDecoder.IsPrepared;
            if (PlayerSettings.PlayerLinkType == PlayerSettings.LinkType.Youtube && !legacyYoutubePlayback)
                return false;
            if (IsVideoLink())
                return mScreen != null && mScreen.isPrepared;
            return mAudio != null && mAudio.clip != null;
        }

        private bool IsPlaybackLooping()
        {
            if (youtubeBackendActive && youtubeDecoder != null)
                return youtubeDecoder.IsLooping;
            if (PlayerSettings.PlayerLinkType == PlayerSettings.LinkType.Youtube && !legacyYoutubePlayback)
                return PlayerSettings.IsLooping;
            if (IsVideoLink())
                return mScreen != null && mScreen.isLooping;
            return mAudio != null && mAudio.loop;
        }

        private void PlayCurrentBackend()
        {
            if (youtubeBackendActive && youtubeDecoder != null)
            {
                if (youtubeDecoder.IsPrepared) youtubeDecoder.Play();
                return;
            }
            if (PlayerSettings.PlayerLinkType == PlayerSettings.LinkType.Youtube && !legacyYoutubePlayback)
                return;

            if (IsVideoLink())
            {
                if (mScreen != null) mScreen.Play();
                return;
            }

            if (mAudio == null || mAudio.clip == null) return;
            mAudio.UnPause();
            if (!mAudio.isPlaying) mAudio.Play();
        }

        private void PauseCurrentBackend()
        {
            if (youtubeBackendActive && youtubeDecoder != null)
            {
                if (youtubeDecoder.IsPrepared) youtubeDecoder.Pause();
                return;
            }
            if (PlayerSettings.PlayerLinkType == PlayerSettings.LinkType.Youtube && !legacyYoutubePlayback)
                return;

            if (IsVideoLink())
            {
                if (mScreen != null) mScreen.Pause();
                return;
            }

            if (mAudio != null) mAudio.Pause();
        }

        private void ApplyPendingPlaybackTime()
        {
            if (!hasPendingPlaybackTime) return;
            if (youtubeBackendActive && youtubeDecoder != null)
            {
                if (!youtubeDecoder.IsPrepared) return;
                youtubeDecoder.Time = pendingPlaybackTime;
            }
            else if (PlayerSettings.PlayerLinkType == PlayerSettings.LinkType.Youtube && !legacyYoutubePlayback)
            {
                return;
            }
            else if (IsVideoLink())
            {
                if (mScreen == null || !mScreen.isPrepared) return;
                mScreen.time = pendingPlaybackTime;
            }
            else if (mAudio != null && mAudio.clip != null)
            {
                mAudio.time = Mathf.Clamp((float)pendingPlaybackTime, 0f, mAudio.clip.length);
            }
            else
            {
                return;
            }

            hasPendingPlaybackTime = false;
        }

        private int BeginSourceSwitch(double initialTime)
        {
            playbackGeneration++;
            DestroyYoutubeBackend();
            legacyYoutubePlayback = OODConfig.UseLegacyYoutubePlayback.Value;
            if (DynamicStationCoroutine != null)
            {
                StopCoroutine(DynamicStationCoroutine);
                DynamicStationCoroutine = null;
            }

            if (mScreen != null)
            {
                mScreen.Stop();
                mScreen.url = "";
                mScreen.isLooping = false;
                mScreen.audioOutputMode = VideoAudioOutputMode.AudioSource;
                if (mAudio != null) mScreen.SetTargetAudioSource(0, mAudio);
            }

            if (mAudio != null)
            {
                mAudio.Stop();
                mAudio.clip = null;
                mAudio.loop = false;
            }

            pendingPlaybackTime = Math.Max(0d, initialTime);
            hasPendingPlaybackTime = true;
            return playbackGeneration;
        }

        private void DestroyYoutubeBackend()
        {
            youtubeLoading = false;
            youtubeBackendActive = false;
            if (youtubeDecoder == null) return;

            youtubeDecoder.Prepared -= YoutubePrepared;
            youtubeDecoder.Ended -= YoutubeEnded;
            youtubeDecoder.Error -= YoutubeError;
            youtubeDecoder.Stop();
            youtubeDecoder.enabled = false;
            Destroy(youtubeDecoder);
            youtubeDecoder = null;
        }

        private void PrepareYoutubeBackend(string videoUrl, string audioUrl, int generation)
        {
            if (generation != playbackGeneration || PlayerSettings.PlayerLinkType != PlayerSettings.LinkType.Youtube)
                return;

            DestroyYoutubeBackend();
            youtubeLoading = true;
            if (mScreen != null)
            {
                mScreen.Stop();
                mScreen.url = "";
            }
            if (mAudio != null)
            {
                mAudio.Stop();
                mAudio.clip = null;
                mAudio.loop = false;
            }

            if (legacyYoutubePlayback)
            {
                mScreen.source = VideoSource.Url;
                mScreen.url = videoUrl;
                SetLooping(PlayerSettings.IsLooping);
                BeginLoadingPrepare();
                return;
            }

            youtubeDecoder = gameObject.AddComponent<YoutubeDecoder>();
            youtubeBackendActive = true;
            youtubeDecoder.Prepared += YoutubePrepared;
            youtubeDecoder.Ended += YoutubeEnded;
            youtubeDecoder.Error += YoutubeError;
            youtubeDecoder.IsLooping = PlayerSettings.IsLooping && !PlayerSettings.IsPlayingPlaylist;

            try
            {
                youtubeDecoder.Prepare(
                    videoUrl,
                    audioUrl,
                    mAudio,
                    mScreen != null ? mScreen.targetTexture : null);
            }
            catch (Exception exception)
            {
                HandlePlaybackError(exception.Message);
                DestroyYoutubeBackend();
            }
        }

        private void UpdateChecks() //1second checks, screen render distance, master volume updates and playlist gui updates
        {
            //Master volume updates from config
            mAudio.outputAudioMixerGroup.audioMixer.GetFloat("MasterVolume", out var masterVolumeCheck);
            if (PlayerSettings.PlayerType == CinemaPackage.MediaPlayers.CinemaScreen)
            {
                if (masterVolumeCheck != OODConfig.MasterVolumeScreen.Value)
                    mAudio.outputAudioMixerGroup.audioMixer.SetFloat("MasterVolume",
                        OODConfig.MasterVolumeScreen.Value);
            }
            else if(PlayerSettings.PlayerType == CinemaPackage.MediaPlayers.BeltPlayer || PlayerSettings.PlayerType == CinemaPackage.MediaPlayers.CartPlayer)
            {
                if (masterVolumeCheck != OODConfig.MasterVolumeTransport.Value)
                    mAudio.outputAudioMixerGroup.audioMixer.SetFloat("MasterVolume",
                        OODConfig.MasterVolumeTransport.Value);
            }
            else 
            {
                if (masterVolumeCheck != OODConfig. MasterVolumeMusicplayer.Value)
                    mAudio.outputAudioMixerGroup.audioMixer.SetFloat("MasterVolume",
                        OODConfig.MasterVolumeMusicplayer.Value);
            }

            //Playlist GUI checks and updates
            if (UIController.URLPanelObj)
            {
                if (PlayerSettings.IsPlayingPlaylist)
                {
                    UIController.UpdatePlaylistUI();
                    UIController.PlaylistTrackText.text = PlaylistString;
                }
                else
                {
                    UIController.UpdatePlaylistUI();
                }
            }

            if (!IsPlaybackPlaying() && !IsPlaybackLooping() && (!IsVideoLink() || !IsPlaybackPrepared()))
            {
                if (Animator != null) Animator.SetBool(PlayerSettings.Playing, false);
            }

            //trigger collider checks
            if (!triggerCollider) return;
            if (triggerCollider.radius != mAudio.maxDistance) triggerCollider.radius = mAudio.maxDistance;
        }
        

        public void SetURL(string url)
        {
            System.Text.Encoding encoding = System.Text.Encoding.UTF8;
            byte[] bytes = encoding.GetBytes(url);
            url = encoding.GetString(bytes);
            UnparsedURL = url; //Save the unparsed url for later use
            if (UnparsedURL == "")
            {
                Stop(true);
                return;
            }
            // Just send RPC. It will be sent back to us and we'll handle it there.
            if (((IPlayer)this).mScreen|| mAudio)
            {
                if (url.Contains("youtube.com/watch?v=") || url.Contains("youtube.com/shorts/") ||
                    url.Contains("youtu.be") || url.Contains("youtube.com/playlist"))
                {
                    UIController.UpdatePlaylistInfo();
                    if (url.Contains("?list=") || url.Contains("&list="))
                    {
                        SetPlaylist(UnparsedURL);
                        return;
                    }
                }
                UpdatePlayerTime(0f);
                PlayerSettings.IsPaused = false;
                PlayerSettings.IsPlaying = true;
                RPC.SendData(0, CinemaPackage.RPCDataType.SetVideoUrl, PlayerSettings.PlayerType, MediaPlayerID, gameObject.transform.position, 0f, UnparsedURL, CinemaPackage.PlayerStatus.Playing);
                SaveZDO();
            }
        }

        private void SetPlaylist(string url) // Set playlist from url
        {
            StartCoroutine(URLGrab.GetYouTubePlaylistCoroutine(url, (videoInfos) =>
            {
                if (videoInfos != null)
                {
                    // Process the list of videoInfos
                    CurrentPlaylist = videoInfos;
                    PlayerSettings.IsPlayingPlaylist = true;
                    PlaylistPosition = 0;
                    if (OODConfig.DebugEnabled.Value) // Debug playlist info
                    {
                        Logger.LogDebug("Playlist info");
                        Logger.LogDebug("Count: " + CurrentPlaylist.Count);
                        Logger.LogDebug(CurrentPlaylist.ToString());
                        Logger.LogDebug("Playing first url of " + CurrentPlaylist.ElementAt(PlaylistPosition).Url);
                    }
                    PlaylistURL = url;
                    SetURL(CurrentPlaylist.ElementAt(PlaylistPosition).Url); // Play first url, when it finishes it will play the next one with the OnVideoEnd event
                    if (UIController.URLPanelObj)
                    {
                        UIController.UpdatePlaylistUI();
                        UIController.ToggleShuffleObj.GetComponentInChildren<Text>().text = "N";
                        UIController.ToggleShuffleObj.SetActive(true);
                        UIController.ToggleShuffleTextObj.SetActive(true);
                    }
                }
                else
                {
                    // Handle error or null case
                    Logger.LogError("Failed to load playlist");
                }
            }));
        }
        
        public void Play(bool isRPC = false)
        {
            if (PlayerSettings.CurrentMode == PlayerSettings.PlayerMode.Dynamic && !isRPC)
            {
                if (PlayerSettings.DynamicStation != null)
                {
                    RPC.SendData(0, CinemaPackage.RPCDataType.RequestStation, PlayerSettings.PlayerType,
                        MediaPlayerID, gameObject.transform.position, 0, PlayerSettings.DynamicStation.Title);
                }
                return;
            }

            if (!isRPC)
            {
                RPC.SendData(0, CinemaPackage.RPCDataType.Play, PlayerSettings.PlayerType, MediaPlayerID,
                    gameObject.transform.position, GetTime());
                PlayerSettings.IsPlaying = true;
                PlayerSettings.IsPaused = false;
                SaveZDO();
                return;
            }

            if (PlayerSettings.PlayerType == CinemaPackage.MediaPlayers.CinemaScreen && ScreenPlaneObj)
                ScreenPlaneObj.SetActive(true);

            PlayCurrentBackend();
            PlayerSettings.IsPlaying = true;
            PlayerSettings.IsPaused = false;

            if (Animator) Animator.SetBool(PlayerSettings.Playing, true);
            if (WaveParticleSystem && OODConfig.MobilePlayerVisuals.Value &&
                (IsPlaybackPlaying() || IsPlaybackPrepared()))
            {
                WaveParticleSystem.Play();
            }
        }

        public void PlayStation(string stationName)
        {
            var station = StationManager.Instance.GetStation(stationName);
            PlayerSettings.DynamicStation = station;
            PlayerSettings.IsPlaying = true;
            PlayerSettings.IsPaused = false;
            PlayerSettings.CurrentMode = PlayerSettings.PlayerMode.Dynamic;
            UnparsedURL = PlayerSettings.DynamicStation.Title;
            RPC.SendData(0,CinemaPackage.RPCDataType.RequestStation, PlayerSettings.PlayerType, MediaPlayerID, gameObject.transform.position, 0, stationName);
            SaveZDO();
        }
        
        public void RPC_PlayStation(string dataURL, string trackTitle, float time)
        {
            var station = StationManager.Instance.GetStation(dataURL);
            var track = station?.Tracks.FirstOrDefault(x => x.Title == trackTitle);
            if (track == null) return;
            station.CurrentTrackIndex = station.Tracks.IndexOf(track);
            station.Tracks[station.CurrentTrackIndex].CurrentTime = time;
            PlayerSettings.CurrentMode = PlayerSettings.PlayerMode.Dynamic;
            PlayerSettings.DynamicStation = station;
            PlayerSettings.IsPlaying = true;
            PlayerSettings.IsPaused = false;
            InitiateDynamicStationPlayback();
            StartCoroutine(DelayedExecution(2f, SendRequestTimeSync_RPC));
        }

        private void InitiateDynamicStationPlayback()
        {
            var clip = PlayerSettings.DynamicStation.Tracks[PlayerSettings.DynamicStation.CurrentTrackIndex];
            BeginSourceSwitch(clip.CurrentTime);
            PlayerSettings.PlayerLinkType = PlayerSettings.LinkType.Audio;
            mAudio.loop = PlayerSettings.IsLooping;
            mAudio.clip = clip.AudioClip;
            ApplyPendingPlaybackTime();
            foreach (var component in ComponentLists.MediaComponentLists)
            {
                foreach (BasePlayer player in component.Value)
                {
                    if (player == this || !player || player.PlayerSettings.DynamicStation == null) continue;
                    if (player.PlayerSettings.CurrentMode == PlayerSettings.PlayerMode.Dynamic &&
                        player.PlayerSettings.DynamicStation.Title == PlayerSettings.DynamicStation.Title &&
                        player.PlayerSettings.IsPlaying && !player.PlayerSettings.IsPaused)
                    {
                        player.BeginSourceSwitch(clip.CurrentTime);
                        player.PlayerSettings.PlayerLinkType = PlayerSettings.LinkType.Audio;
                        player.mAudio.loop = player.PlayerSettings.IsLooping;
                        player.mAudio.clip = clip.AudioClip;
                        player.ApplyPendingPlaybackTime();
                        player.mAudio.Play();
                        player.DynamicStationCoroutine = player.StartCoroutine(
                            player.AudioEndEvent(clip.AudioClip.length - player.mAudio.time, player.PlayNextDynamicStationTrack));
                    }
                }
            }
            mAudio.Play();
            PlayerSettings.IsPlaying = true;
            PlayerSettings.IsPaused = false;
            PlayerSettings.IsPlayingPlaylist = false;
            PlayerSettings.PlayerLinkType = PlayerSettings.LinkType.Audio;
            PlayerSettings.CurrentMode = PlayerSettings.PlayerMode.Dynamic;
            UnparsedURL = PlayerSettings.DynamicStation.Title;
            float remainingTime = clip.AudioClip.length - mAudio.time;
            DynamicStationCoroutine = StartCoroutine(AudioEndEvent(remainingTime, PlayNextDynamicStationTrack));
            if (WaveParticleSystem) WaveParticleSystem.Play();
            if (Animator) Animator.SetBool(PlayerSettings.Playing, true);
            UpdateRadioPanel();
        }

        private void PlayNextDynamicStationTrack()
        {
            if (PlayerSettings.DynamicStation == null) return;
            mAudio.Stop();
            mAudio.clip = null;
            StartCoroutine(DelayedExecution(0.35f, () =>
                {
                    if (PlayerSettings.CurrentMode != PlayerSettings.PlayerMode.Dynamic ||
                        PlayerSettings.DynamicStation == null)
                        return;
                    RPC.SendData(0, CinemaPackage.RPCDataType.RequestStation, PlayerSettings.PlayerType, MediaPlayerID,
                        gameObject.transform.position, 0, PlayerSettings.DynamicStation.Title);
                    StartCoroutine(DelayedExecution(2f, SendRequestTimeSync_RPC));
                }
            ));
        }

        private IEnumerator AudioEndEvent(float time, Action method)
        {
            yield return new WaitForSeconds(time);
            method?.Invoke();
        }
        
        public void UpdateRadioPanel()
        {
            if (RadioPanelObj && UIController.RadioPanelThumbnail && IsPlaybackPlaying())
            {
                if(ScreenUICanvasObj) ScreenUICanvasObj.SetActive(true);
                if(ScreenPlaneObj) ScreenPlaneObj.SetActive(true);
                ClearRenderTexture(mScreen.targetTexture);
                if (PlayerSettings.DynamicStation != null && PlayerSettings.CurrentMode == PlayerSettings.PlayerMode.Dynamic)
                {
                    RadioPanelObj.SetActive(true);
                    // TODO title ??
                    //var title = RadioPanelObj.transform.Find("Title").GetComponent<Text>();
                    //title.text = PlayerSettings.DynamicStation.Title;

                    UIController.RadioPanelThumbnail.sprite = PlayerSettings.DynamicStation.Thumbnail != null ? PlayerSettings.DynamicStation.Thumbnail : null;
                }
                else if (mAudio.isPlaying && !IsVideoLink())
                {
                    RadioPanelObj.SetActive(true);
                    UIController.RadioPanelThumbnail.sprite = PlayerSettings.Thumbnail != null ? PlayerSettings.Thumbnail : null;
                }
                else if (IsVideoLink())
                {
                    RadioPanelObj.SetActive(false);
                }
            }
        }

        public void Pause(bool isRPC = false)
        {
            if (!isRPC)
            {
                RPC.SendData(0, CinemaPackage.RPCDataType.Pause, PlayerSettings.PlayerType, MediaPlayerID,
                    gameObject.transform.position, GetTime());
                PlayerSettings.IsPaused = true;
                SaveZDO();
                return;
            }

            PlayerSettings.IsPaused = true;
            PauseCurrentBackend();
            if (Animator) Animator.SetBool(PlayerSettings.Playing, false);
            if (WaveParticleSystem) WaveParticleSystem.Stop();
        }

        private void SetupAudio()
        {
            centerAudioSphere = transform.Find("audio/centerSphere");
            //Grab our audio source and set up values from config
            mAudio = gameObject.GetComponentInChildren<AudioSource>();
            mAudio.maxDistance = OODConfig.DefaultDistance.Value;
            PlayerSettings.Volume = OODConfig.DefaultAudioSourceVolume.Value;

            mAudio.spatialBlend = 1;
            mAudio.spatialize = true;
            mAudio.spatializePostEffects = true;
            mAudio.volume = PlayerSettings.Volume;
            if (mScreen != null)
            {
                mScreen.audioOutputMode = UnityEngine.Video.VideoAudioOutputMode.AudioSource;
                mScreen.SetTargetAudioSource(0, mAudio);
            }
        }

        protected void SetupRadioPanel()
        {
            if(RadioPanelObj) UIController.RadioPanelThumbnail = RadioPanelObj.transform.Find("thumbnail").GetComponent<Image>();
           if(ScreenUICanvasObj)
           {
               var waveformPanel = ScreenUICanvasObj.transform.Find("mainCanvas/radioPanel/waveformPanel");
               if (waveformPanel)
               {
                   var waveform = waveformPanel.gameObject.AddComponent<AudioWaveformVisualizer>();
                   waveform.Setup(mAudio);
               }
           }
            if (UIController.RadioPanelThumbnail)
            {
                if (PlayerSettings.DynamicStation != null && PlayerSettings.CurrentMode == PlayerSettings.PlayerMode.Dynamic)
                {
                    UIController.RadioPanelThumbnail.sprite = PlayerSettings.DynamicStation.Thumbnail;
                }

                if (PlayerSettings.Thumbnail != null)
                {
                    UIController.RadioPanelThumbnail.sprite = PlayerSettings.Thumbnail;
                }
                else
                {
                    UIController.RadioPanelThumbnail.sprite = null;
                }
            }
        }

        protected void UpdateLoadingIndicator()
        {
            if ((URLGrab.LoadingBool || youtubeLoading) && !String.IsNullOrEmpty(UnparsedURL))
            {
                if (ScreenUICanvasObj && LoadingCircleObj && RadioPanelObj)
                {
                    ScreenUICanvasObj.SetActive(true);
                    RadioPanelObj.SetActive(false);
                    LoadingCircleObj.SetActive(true);
                }

                UIController?.SetLoadingIndicatorActive(true);
                var loadingMessageIndex = PlayerSettings.LoadingCount % 4;
                if (UIController != null && UIController.LoadingIndicatorObj != null)
                    UIController.LoadingIndicatorObj.GetComponent<Text>().text =
                        UIController.LoadingMessages[loadingMessageIndex];
                PlayerSettings.LoadingCount++;
            }
            else if (UIController.LoadingIndicatorObj && UIController.LoadingIndicatorObj.activeSelf)
            {
                UIController.SetLoadingIndicatorActive(false);
            }
        }


        public  void Stop(bool isRPC = false) //Stop the video (if playing)
        {
            //If not RPC, send stop RPC command
            if (!isRPC)
            {
                RPC.SendData(0, CinemaPackage.RPCDataType.Stop, PlayerSettings.PlayerType, MediaPlayerID,
                    gameObject.transform.position, GetTime());
                return;
            }

            PlayerSettings.DynamicStation = null;
            BeginSourceSwitch(0d);
            hasPendingPlaybackTime = false;
            if (PlayerSettings.PlayerType == CinemaPackage.MediaPlayers.CinemaScreen)
            {
                ScreenPlaneObj.SetActive(false);
                if (ScreenUICanvasObj && LoadingCircleObj)
                {
                    ScreenUICanvasObj.SetActive(false);
                    LoadingCircleObj.SetActive(false);
                }
            }

            if (Animator) Animator.SetBool(PlayerSettings.Playing, false);
            if (WaveParticleSystem) WaveParticleSystem.Stop();
            
            PlayerSettings.IsPlayingPlaylist = false;
            PlayerSettings.IsPlaying = false;
            PlayerSettings.IsPaused = false;
            CurrentPlaylist = null;

            URLGrab.Reset();
            ClearRenderTexture(mScreen.targetTexture);
            UnparsedURL = null;
            DownloadURL = null;
            PlaylistURL = null;
            PlaylistString = null;
            PlaylistPosition = 0;
            UIController.SetInputFieldText("");
            UIController.UpdatePlaylistUI();
            SaveZDO();
            SendUpdateZDO_RPC();
        }

        protected IEnumerator AudioWebRequest(Uri url, int generation)
        {
            var dh = new DownloadHandlerAudioClip(url, AudioType.MPEG)
            {
                compressed = false
            };
            using var wr = new UnityWebRequest(url, "GET", dh, null);
            yield return wr.SendWebRequest();

            if (generation != playbackGeneration) yield break;
            if (wr.result == UnityWebRequest.Result.ProtocolError ||
                wr.result == UnityWebRequest.Result.ConnectionError)
            {
                Logger.LogError(wr.error);
                yield break;
            }

            mAudio.clip = dh.audioClip;
            ApplyPendingPlaybackTime();
            if (!PlayerSettings.IsPaused) mAudio.Play();
            StartCoroutine(DelayedExecution(0.5f, SendRequestTimeSync_RPC));
            PlayerSettings.IsPlaying = true;
            if (Animator) Animator.SetBool(PlayerSettings.Playing, !PlayerSettings.IsPaused);
            if (WaveParticleSystem && OODConfig.MobilePlayerVisuals.Value && !PlayerSettings.IsPaused)
                WaveParticleSystem.Play();
            if (ScreenUICanvasObj) ScreenUICanvasObj.SetActive(true);
            UpdateRadioPanel();
            UIController.ResetLoadingIndicator();
        }

        private IEnumerator CreateThumbnailFromURL(Uri url, int generation)
        {
            var dh = new DownloadHandlerTexture(true);
            using var wr = new UnityWebRequest(url, "GET", dh, null);
            yield return wr.SendWebRequest();

            if (generation != playbackGeneration) yield break;
            if (wr.result == UnityWebRequest.Result.ProtocolError ||
                wr.result == UnityWebRequest.Result.ConnectionError)
            {
                Logger.LogError($"Error downloading image: {wr.error}");
                yield break;
            }

            Texture2D texture = DownloadHandlerTexture.GetContent(wr);
            Rect rect = new Rect(0, 0, texture.width, texture.height);
            Vector2 pivot = new Vector2(0.5f, 0.5f);
            PlayerSettings.Thumbnail = Sprite.Create(texture, rect, pivot);
        }

        
        public void PlaySoundcloud(string sentUrl, bool isRPC)
        {
            int generation = playbackGeneration;
            var url = URLGrab.CleanUrl(sentUrl);
            UIController.SetLoadingIndicatorText("Processing");
            UIController.SetLoadingIndicatorActive(true);
            if (sentUrl != null)
            {
                StartCoroutine(URLGrab.GetSoundcloudExplodeCoroutine(url, (resultUrl, artworkUri) =>
                {
                    if (generation != playbackGeneration) return;
                    if (resultUrl != null)
                    {
                        if (artworkUri != null)
                            StartCoroutine(CreateThumbnailFromURL(artworkUri, generation));
                        else
                            PlayerSettings.Thumbnail = null;
                        StartCoroutine(AudioWebRequest(resultUrl, generation));
                    }
                    else
                    {
                        UIController.SetLoadingIndicatorText("Null, check logs");
                        Logger.LogWarning("Failed to load Soundcloud");
                        StartCoroutine(ResetLoadingIndicatorAfterDelay(generation));
                    }
                }));
            }
            else
            {
                Logger.LogInfo("Soundcloud Null, check for exceptions");
                StartCoroutine(UIController.UnavailableIndicator("Soundcloud Null"));
            }
        }
        
        public void PlayYoutube(string url)
        {
            PlayYoutube(url, playbackGeneration);
        }

        private void PlayYoutube(string url, int generation)
        {
            if (URLGrab.LoadingBool || generation != playbackGeneration) return;
            if (!OODConfig.IsYtEnabled.Value)
            {
                PlayerSettings.IsPlaying = false;
                StartCoroutine(UIController.UnavailableIndicator("YouTube disabled"));
                return;
            }
            youtubeLoading = true;

            if (legacyYoutubePlayback || OODConfig.YoutubeAPI.Value == OODConfig.YouTubeAPI.YouTubeExplode)
                StartYoutubeProcessing(url, generation);
            else
                StartCoroutine(YoutubeNodeQuery(url, generation));
        }
        public void RPC_SetURL(string url, bool isPaused = false, float time = 0f)
        {
            if (url == null) return;
            System.Text.Encoding encoding = System.Text.Encoding.UTF8;
            byte[] bytes = encoding.GetBytes(url);
            url = encoding.GetString(bytes);
            if (string.IsNullOrEmpty(url))
            {
                Stop(true);
                return;
            }

            UnparsedURL = url;
            PlayerSettings.CurrentMode = PlayerSettings.PlayerMode.URL;
            PlayerSettings.DynamicStation = null;
            PlayerSettings.IsPaused = isPaused;
            PlayerSettings.IsPlaying = true;
            URLGrab.Reset();
            int generation = BeginSourceSwitch(time);
            ClearRenderTexture(mScreen.targetTexture);

            if (URLGrab.IsAudioFile(url))
            {
                var relativeURL = URLGrab.GetRelativeURL(url);
                if (relativeURL != "")
                {
                    url = relativeURL;
                    PlayerSettings.PlayerLinkType = PlayerSettings.LinkType.RelativeAudio;
                }
                else
                {
                    PlayerSettings.PlayerLinkType = PlayerSettings.LinkType.Audio;
                }

                SetLooping(PlayerSettings.IsLooping);
                DownloadURL = URLGrab.CleanUrl(url);
                StartCoroutine(AudioWebRequest(DownloadURL, generation));
                return;
            }

            if (url.Contains("soundcloud.com/"))
            {
                PlayerSettings.PlayerLinkType = PlayerSettings.LinkType.Soundcloud;
                SetLooping(PlayerSettings.IsLooping);
                PlaySoundcloud(url, true);
                return;
            }

            var relativeVideoUrl = URLGrab.GetRelativeURL(url);
            if (relativeVideoUrl != "")
            {
                PlayerSettings.PlayerLinkType = PlayerSettings.LinkType.RelativeVideo;
                SetLooping(PlayerSettings.IsLooping);
                mScreen.source = VideoSource.Url;
                mScreen.url = relativeVideoUrl;
                if (OODConfig.DebugEnabled.Value) Logger.LogDebug("Playing: " + relativeVideoUrl);
                BeginLoadingPrepare();
                return;
            }

            if ((url.StartsWith("http://") || url.StartsWith("https://")) &&
                OODConfig.IsYtEnabled.Value && !Path.HasExtension(url))
            {
                PlayerSettings.PlayerLinkType = PlayerSettings.LinkType.Youtube;
                PlayYoutube(url, generation);
                return;
            }

            PlayerSettings.PlayerLinkType = PlayerSettings.LinkType.Video;
            SetLooping(PlayerSettings.IsLooping);
            mScreen.source = VideoSource.Url;
            mScreen.url = url;
            BeginLoadingPrepare();
            if (OODConfig.DebugEnabled.Value) Logger.LogDebug("Playing: " + url);
        }

        private void BeginLoadingPrepare()
        {
            mScreen.Prepare();
            ClearRenderTexture(mScreen.targetTexture);
            if (ScreenUICanvasObj && LoadingCircleObj)
            {
                ScreenUICanvasObj.SetActive(true);
                LoadingCircleObj.SetActive(true);
            }
        }
        
        public void StartYoutubeProcessing(string url)
        {
            StartYoutubeProcessing(url, playbackGeneration);
        }

        private void StartYoutubeProcessing(string url, int generation)
        {
            if (generation != playbackGeneration) return;
            youtubeLoading = true;
            if (UIController.LoadingIndicatorObj)
            {
                UIController.SetLoadingIndicatorText("Processing");
                UIController.LoadingIndicatorObj.SetActive(true);
            }
            if (ScreenUICanvasObj && LoadingCircleObj)
            {
                ScreenUICanvasObj.SetActive(true);
                LoadingCircleObj.SetActive(true);
            }

            StartCoroutine(Ytdl.GetStreamsWithRetry(
                url,
                streams =>
                {
                    if (generation != playbackGeneration) return;
                    if (streams != null && !string.IsNullOrEmpty(streams.VideoUrl))
                    {
                        PrepareYoutubeBackend(streams.VideoUrl, streams.AudioUrl, generation);
                        return;
                    }

                    HandlePlaybackError("Failed to get YouTube streams");
                },
                3,
                120,
                legacyYoutubePlayback));
        }
        
        private IEnumerator ResetLoadingIndicatorAfterDelay(int generation)
        {
            yield return new WaitForSeconds(1.75f);
            if (generation != playbackGeneration) yield break;
            UIController.ResetLoadingIndicator();
        }

        private IEnumerator YoutubeNodeQuery(string youtubeUrl, int generation)
        {
            if (string.IsNullOrEmpty(youtubeUrl)) yield break;
            youtubeLoading = true;

            var url = Uri.EscapeDataString(youtubeUrl);
            using var www = UnityWebRequest.Get(OODConfig.NodeUrl.Value + url + "/" + OODConfig.YtAuthCode.Value);
            www.timeout = 30;
            UIController.SetLoadingIndicatorText("Processing");
            if (UIController.LoadingIndicatorObj) UIController.LoadingIndicatorObj.SetActive(true);
            if (ScreenUICanvasObj && LoadingCircleObj)
            {
                ScreenUICanvasObj.SetActive(true);
                LoadingCircleObj.SetActive(true);
            }

            yield return www.SendWebRequest();
            if (generation != playbackGeneration) yield break;

            if (www.result != UnityWebRequest.Result.Success)
            {
                HandlePlaybackError("Node YouTube extraction failed: " + www.error);
                yield break;
            }

            if (www.downloadHandler.text.Contains("AUTH DENIED"))
            {
                HandlePlaybackError("Node YouTube extraction authentication denied");
                yield break;
            }

            var lines = www.downloadHandler.text
                .Replace("\\n", "\n")
                .Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim().Trim('"'))
                .Where(line => !string.IsNullOrEmpty(line))
                .ToArray();

            if (lines.Length == 0 ||
                !Uri.TryCreate(lines[0], UriKind.Absolute, out var videoUri))
            {
                HandlePlaybackError("Node YouTube extraction returned an invalid video URL");
                yield break;
            }

            string audioUrl = null;
            if (lines.Length > 1)
            {
                if (!Uri.TryCreate(lines[1], UriKind.Absolute, out var audioUri))
                {
                    HandlePlaybackError("Node YouTube extraction returned an invalid audio URL");
                    yield break;
                }
                audioUrl = audioUri.AbsoluteUri;
            }

            PrepareYoutubeBackend(videoUri.AbsoluteUri, audioUrl, generation);
        }
        
        public void UpdatePlayerTime(float time)
        {
            pendingPlaybackTime = Math.Max(0d, time);
            hasPendingPlaybackTime = true;

            if (youtubeBackendActive)
            {
                if (youtubeDecoder != null && youtubeDecoder.IsPrepared)
                {
                    if (youtubeDecoder.IsPaused || Math.Abs(youtubeDecoder.Time - pendingPlaybackTime) > 0.05d)
                        youtubeDecoder.Time = pendingPlaybackTime;
                    hasPendingPlaybackTime = false;
                }
                return;
            }

            if (PlayerSettings.PlayerLinkType == PlayerSettings.LinkType.Youtube && !legacyYoutubePlayback)
                return;

            if (IsVideoLink())
            {
                if (mScreen != null && mScreen.isPrepared &&
                    Math.Abs(mScreen.time - pendingPlaybackTime) > 0.05d)
                {
                    mScreen.time = pendingPlaybackTime;
                    hasPendingPlaybackTime = false;
                }
                return;
            }

            if (mAudio != null && mAudio.clip != null)
            {
                mAudio.time = Mathf.Clamp((float)pendingPlaybackTime, 0f, mAudio.clip.length);
                hasPendingPlaybackTime = false;
            }
        }
        
        public void SetLock(bool locked) //Set player lock state
        {
            PlayerSettings.IsLocked = locked;
            if (UIController.URLPanelObj)
            {
                if (PlayerSettings.IsLocked)
                {
                    UIController.LockedIconObj.SetActive(true);
                    UIController.UnlockedIconObj.SetActive(false);
                }
                else
                {
                    UIController.LockedIconObj.SetActive(false);
                    UIController.UnlockedIconObj.SetActive(true);
                }
            }
        }
        public void SetDynamicStation(DynamicStation station)
        {
            PlayerSettings.DynamicStation = station;
        }

        public virtual void SaveZDO(bool saveTime = true)
        {
            var zdo = ZNetView.GetZDO();
            if (zdo == null || mAudio == null) return;
            RequestOwnership(zdo);
            zdo.Set("distance", mAudio.maxDistance);
            zdo.Set("adminOnly", PlayerSettings.AdminOnly);
            zdo.Set("isLooping", PlayerSettings.IsLooping);
            zdo.Set("isLocked", PlayerSettings.IsLocked);
            zdo.Set("isPlaying", PlayerSettings.IsPlaying);
            zdo.Set("isPaused", PlayerSettings.IsPaused);
            zdo.Set("currentMode", (int)PlayerSettings.CurrentMode);
            zdo.Set("url", UnparsedURL ?? "");
            if (saveTime) SaveTimeZDO();
            zdo.Set("speakers", SpeakerHelper.CompressSpeakerList(mSpeakers));
            zdo.Set("speakerCount", mSpeakers.Count);
        }
        
        public void SaveTimeZDO()
        {
            var zdo = ZNetView.GetZDO();
            if (zdo != null)
                zdo.Set("time", (float)PlaybackTime);
        }

        public virtual void LoadZDO()
        {
            var zdo = ZNetView.GetZDO();
            if (zdo == null) return;
            PlayerSettings.AdminOnly = zdo.GetBool("adminOnly");
            var zdoFloat = zdo.GetFloat("distance");
            if (zdoFloat != 0f) mAudio.maxDistance = zdoFloat;
            PlayerSettings.IsLocked = zdo.GetBool("isLocked");
            UnparsedURL = zdo.GetString("url");
            //Logger.LogInfo("loaded url from zdo: " + UnparsedURL);
            PlayerSettings.IsLooping = zdo.GetBool("isLooping");
            SetLooping(PlayerSettings.IsLooping);
            mSpeakers = SpeakerHelper.DecompressSpeakerList(zdo.GetByteArray("speakers"));
            UpdateSpeakerCenter();
            PlayerSettings.IsPlaying = zdo.GetBool("isPlaying");
            PlayerSettings.IsPaused = zdo.GetBool("isPaused");
            PlayerSettings.CurrentMode = (PlayerSettings.PlayerMode)zdo.GetInt("currentMode");
            if(PlayerSettings.CurrentMode == PlayerSettings.PlayerMode.Dynamic)
            {
                PlayerSettings.DynamicStation = StationManager.Instance.GetStation(UnparsedURL);
                if(PlayerSettings.DynamicStation != null)
                {
                    StartCoroutine(DelayedExecution(1f, () =>
                    {
                        RPC.SendData(0, CinemaPackage.RPCDataType.RequestStation, PlayerSettings.PlayerType,
                            MediaPlayerID, gameObject.transform.position, GetTimeZDO(), PlayerSettings.DynamicStation.Title);
                    }));
                    StartCoroutine(DelayedExecution(2f, SendRequestTimeSync_RPC));
                }
            }
            else if (!string.IsNullOrEmpty(UnparsedURL))
            {
                string loadedUrl = UnparsedURL;
                bool loadedPaused = PlayerSettings.IsPaused;
                float loadedTime = GetTimeZDO();
                StartCoroutine(DelayedExecution(3f, () =>
                {
                    if (UnparsedURL == loadedUrl)
                        RPC_SetURL(loadedUrl, loadedPaused, loadedTime);
                }));
            }
        }

        public virtual void UpdateZDO()
        {
            var zdo = ZNetView.GetZDO();
            if (zdo == null) return;

            PlayerSettings.AdminOnly = zdo.GetBool("adminOnly");
            var zdoFloat = zdo.GetFloat("distance");
            if (zdoFloat != 0f) mAudio.maxDistance = zdoFloat;
            PlayerSettings.IsLocked = zdo.GetBool("isLocked");

            bool wasPaused = PlayerSettings.IsPaused;
            bool zdoPaused = zdo.GetBool("isPaused");
            bool zdoPlaying = zdo.GetBool("isPlaying");
            float zdoTime = zdo.GetFloat("time");
            string zdoUrl = zdo.GetString("url");
            PlayerSettings.IsLooping = zdo.GetBool("isLooping");
            SetLooping(PlayerSettings.IsLooping);
            PlayerSettings.CurrentMode = (PlayerSettings.PlayerMode)zdo.GetInt("currentMode");

            if (zdoUrl != (UnparsedURL ?? ""))
            {
                if (string.IsNullOrEmpty(zdoUrl))
                {
                    Stop(true);
                }
                else if (PlayerSettings.CurrentMode == PlayerSettings.PlayerMode.Dynamic)
                {
                    UnparsedURL = zdoUrl;
                    PlayerSettings.IsPaused = zdoPaused;
                    PlayerSettings.IsPlaying = zdoPlaying;
                    StartCoroutine(DelayedExecution(1f, () =>
                    {
                        if (UnparsedURL != zdoUrl) return;
                        PlayerSettings.DynamicStation = StationManager.Instance.GetStation(zdoUrl);
                        if (PlayerSettings.DynamicStation?.Title == null) return;
                        RPC.SendData(0, CinemaPackage.RPCDataType.RequestStation, PlayerSettings.PlayerType,
                            MediaPlayerID, gameObject.transform.position, zdoTime,
                            PlayerSettings.DynamicStation.Title);
                    }));
                    StartCoroutine(DelayedExecution(2f, SendRequestTimeSync_RPC));
                }
                else
                {
                    RPC_SetURL(zdoUrl, zdoPaused, zdoTime);
                }
            }
            else
            {
                PlayerSettings.IsPlaying = zdoPlaying;
                if (zdoPaused != wasPaused)
                {
                    PlayerSettings.IsPaused = zdoPaused;
                    if (zdoPaused)
                    {
                        PauseCurrentBackend();
                        if (Animator) Animator.SetBool(PlayerSettings.Playing, false);
                        if (WaveParticleSystem) WaveParticleSystem.Stop();
                    }
                    else
                    {
                        PlayCurrentBackend();
                        if (Animator) Animator.SetBool(PlayerSettings.Playing, true);
                        if (WaveParticleSystem && OODConfig.MobilePlayerVisuals.Value)
                            WaveParticleSystem.Play();
                    }
                }
            }

            if (zdo.GetInt("speakerCount") != mSpeakers.Count)
            {
                mSpeakers = SpeakerHelper.DecompressSpeakerList(zdo.GetByteArray("speakers"));
                UpdateSpeakerCenter();
            }
        }
        
        public async void RPC_UpdateZDO() //Update ZDO with a delay
        {
            await Task.Delay(350);
            UpdateZDO();
        }
        
        private IEnumerator DelayedExecution(float delay, Action action)
        {
            yield return new WaitForSeconds(delay);
            action();
        }


        internal void SendUpdateZDO_RPC()
        {
            RPC.SendData(0, CinemaPackage.RPCDataType.UpdateZDO, PlayerSettings.PlayerType, MediaPlayerID,
                gameObject.transform.position, GetTime());
        }
        
        
        private void SyncTime()
        {
            if (IsPlaybackPlaying())
                BroadcastTime();
        }

        internal void SendRequestTimeSync_RPC()
        {
            var zdo = ZNetView.GetZDO();
            if (zdo != null)
            {
                if (zdo.IsOwner() && PlayerSettings.CurrentMode != PlayerSettings.PlayerMode.Dynamic) return;
                RPC.SendData(0, CinemaPackage.RPCDataType.RequestTime, PlayerSettings.PlayerType,
                    MediaPlayerID,
                    gameObject.transform.position, 0, UnparsedURL);
            }
        }
        
        public void BroadcastTime()
        {
            var zdo = ZNetView.GetZDO();
            if (zdo != null)
            {
                if(!zdo.IsOwner()) return;
                SaveTimeZDO();
                RPC.SendData(0, CinemaPackage.RPCDataType.SyncTime, PlayerSettings.PlayerType, MediaPlayerID,
                    gameObject.transform.position, GetTime());
            }
        }

        private string GenerateUniqueID()
        {
            return System.IO.Path.GetRandomFileName().Replace(".", "") + "-" + DateTime.Now.Ticks +  "-" + Player.m_localPlayer.GetZDOID().UserID;
        }
        
        public Coroutine StartPlayerCoroutine(IEnumerator routine)
        {
            return StartCoroutine(routine);
        }

        public void StopPlayerCoroutine(Coroutine routine)
        {
            StopCoroutine(routine);
        }
        
        public void ClaimOwnership(ZDO zdo)
        {
            if(zdo == null) return;
            if (zdo.IsOwner())
                return;
            zdo.SetOwner(ZDOMan.GetSessionID());
        }

        public void SetOwnership(long peer)
        {
            var zdo = ZNetView.GetZDO();
            if (zdo == null) return;
            if (!zdo.IsOwner())
                return;
           
            BroadcastTime();
            zdo.SetOwner(peer);
        }
        
        public void RequestOwnership(ZDO zdo)
        {
            if (zdo == null) return;
            if (zdo.IsOwner())
                return;
            var player = Player.s_players.FirstOrDefault(p => p != null && p.m_nview != null && p.m_nview.IsValid() && p.GetZDOID().UserID == zdo.GetOwner());
            if (player == null)
            {
                ClaimOwnership(zdo);
                return;
            }
            
            RPC.SendData(0, CinemaPackage.RPCDataType.RequestOwnership, PlayerSettings.PlayerType,
                MediaPlayerID, gameObject.transform.position);
        }

        public bool AddSpeaker(SpeakerComponent sp)
        {
            if (mSpeakers.Add(sp))
            {
                SaveZDO();
                UpdateSpeakerCenter();
                StartCoroutine(ShowCenterSphere());
                SendUpdateZDO_RPC();
                return true;
            }

            return false;
        }
        
        public void RemoveSpeaker(SpeakerComponent sp)
        {
            if (mSpeakers.Remove(sp))
            {
                SaveZDO();
                UpdateSpeakerCenter();
                StartCoroutine(ShowCenterSphere());
                SendUpdateZDO_RPC();
                return;
            }
            return;
        }
        
        private void UpdateSpeakerCenter()
        {
            if (mSpeakers.Count == 0)
            {
                mAudio.transform.position = transform.position;
                return;
            }
            var center = SpeakerHelper.CalculateAudioCenter(mSpeakers.ToList());
            mAudio.transform.position = center;
        }

        public void UnlinkAllSpeakers()
        {
            mSpeakers.Clear();
            UIController.UpdateSpeakerCount();
            UpdateSpeakerCenter();
            StartCoroutine(ShowCenterSphere());
            SaveZDO();
            SendUpdateZDO_RPC();
        }
        
        private IEnumerator ShowCenterSphere()
        {
            if (!centerAudioSphere) yield break;
            centerAudioSphere.gameObject.SetActive(true);
            yield return new WaitForSeconds(1f);
            centerAudioSphere.gameObject.SetActive(false);
        }

        private float GetTime()
        {
            return (float)PlaybackTime;
        }

        private float GetTimeZDO()
        {
            var zdo = ZNetView.GetZDO();
            if (zdo != null)
            {
                return zdo.GetFloat("time");
            }
            return 0f;
        }
        
        void ClearRenderTexture(RenderTexture renderTexture)
        {
            if (renderTexture == null) return;
            // Create a 1x1 black texture
            Texture2D blackTexture = new Texture2D(1, 1);
            blackTexture.SetPixel(0, 0, Color.black);
            blackTexture.Apply();
        
            // Store the current active RenderTexture
            RenderTexture currentActiveRT = RenderTexture.active;
        
            // Set the provided RenderTexture as the active one
            RenderTexture.active = renderTexture;
        
            // Copy the black texture onto the active RenderTexture
            Graphics.Blit(blackTexture, renderTexture);
        
            // Restore the previous active RenderTexture
            RenderTexture.active = currentActiveRT;
        
            // Clean up
            UnityEngine.Object.Destroy(blackTexture);
        }
    }
}