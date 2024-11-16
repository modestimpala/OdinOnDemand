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
        public Uri YoutubeSoundDirectUri { get; set; }
        public Uri YoutubeVideoDirectUri { get; set; }

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
        private string YoutubeURLNode { get; set; } 
        
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
            ComponentLists.RemoveComponent(GetType(), this);
        }

        private void EndReached(VideoPlayer source)
        {
            if (PlayerSettings.IsPlayingPlaylist) //Playlist next video logic
            {
                if (PlaylistPosition < CurrentPlaylist.Count() - 1)
                {
                    PlaylistPosition++;
                    SetURL(CurrentPlaylist.ElementAt(PlaylistPosition).Url);
                }
                else if (PlayerSettings.IsLooping)
                {
                    PlaylistPosition = 0;
                    SetURL(CurrentPlaylist.ElementAt(PlaylistPosition).Url);
                }
            }

            if (PlayerSettings.PlayerType ==
                CinemaPackage.MediaPlayers.Radio) //If we're a radio and not looping stop playing animation
                if (Animator && (!mScreen.isLooping || !mAudio.loop) && !PlayerSettings.IsPlayingPlaylist)
                    Animator.SetBool(PlayerSettings.Playing, false);

            var isPlaying = mScreen.isPlaying;
            PlayerSettings.IsPlaying = isPlaying;
        }

        private void ScreenPrepareCompleted(VideoPlayer source)
        {
            //Set our screen plane to active
            if (PlayerSettings.PlayerType == CinemaPackage.MediaPlayers.CinemaScreen)
            {
                if(ScreenPlaneObj) ScreenPlaneObj.SetActive(true);
            }
            
            if(WaveParticleSystem && OODConfig.MobilePlayerVisuals.Value) WaveParticleSystem.Play();
            
            //Hide the loading indicators
            if (UIController.LoadingIndicatorObj) UIController.LoadingIndicatorObj.SetActive(false);
            if (ScreenUICanvasObj && LoadingCircleObj)
            {
                ScreenUICanvasObj.SetActive(false);
                LoadingCircleObj.SetActive(false);
            }
            
            var zdotime = GetTimeZDO();
            UpdatePlayerTime(zdotime);
            
            StartCoroutine(DelayedExecution(0.5f, SendRequestTimeSync_RPC));
            //Play the video
            if(!PlayerSettings.IsPaused)
            {
                if (Animator) Animator.SetBool(PlayerSettings.Playing, true);
                if(WaveParticleSystem && OODConfig.MobilePlayerVisuals.Value) WaveParticleSystem.Play();
                PlayerSettings.IsPlaying = true;
                PlayerSettings.IsPaused = false;
                source.Play();
                mAudio.Play();
            }
            else
            {
                PlayerSettings.IsPlaying = true;
                PlayerSettings.IsPaused = true;
                source.Pause();
                mAudio.Pause();
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

            if (!mAudio.isPlaying && mAudio.time == 0f && !mAudio.loop)
            {
                //If the audio is not playing, and the time is 0, and it's not looping, then we're not playing anything
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
            mAudio.clip = null;
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
                mScreen.time = 0;
                mAudio.time = 0;
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
        
        public  void Play(bool isRPC = false) //Play the video (if paused)
        {
            if(PlayerSettings.CurrentMode == PlayerSettings.PlayerMode.Dynamic)
            {
                if (PlayerSettings.DynamicStation != null)
                {
                    RPC.SendData(0, CinemaPackage.RPCDataType.RequestStation, PlayerSettings.PlayerType, MediaPlayerID, gameObject.transform.position, 0, PlayerSettings.DynamicStation.Title);
                }
                return;
            }
            
            //If not RPC, send play RPC command
            if (!isRPC)
            {
                RPC.SendData(0,CinemaPackage.RPCDataType.Play, PlayerSettings.PlayerType, MediaPlayerID,
                    gameObject.transform.position, GetTime());
                PlayerSettings.IsPlaying = true;
                PlayerSettings.IsPaused = false;
                SaveZDO();
                return;
            }

            //Enable screen plane object
            if (PlayerSettings.PlayerType == CinemaPackage.MediaPlayers.CinemaScreen)
            {
                ScreenPlaneObj.SetActive(true);
            }

            // If link type is youtube or relative video, play just the screen
            if (PlayerSettings.PlayerLinkType == PlayerSettings.LinkType.Youtube ||
                PlayerSettings.PlayerLinkType == PlayerSettings.LinkType.RelativeVideo ||
                PlayerSettings.PlayerLinkType == PlayerSettings.LinkType.Video)
            {
                mScreen.Play();
                PlayerSettings.IsPlaying = mScreen.isPlaying;
                PlayerSettings.IsPaused = mScreen.isPaused;

                if (Animator) Animator.SetBool(PlayerSettings.Playing, true);
                if (WaveParticleSystem && OODConfig.MobilePlayerVisuals.Value)
                {
                    if (mScreen.isPlaying || mScreen.isPrepared || mAudio.isPlaying)   
                        WaveParticleSystem.Play();
                }
                //m_audio.Play();
            }
            else if (PlayerSettings.PlayerLinkType == PlayerSettings.LinkType.Audio)
            {
                
                if (Animator) Animator.SetBool(PlayerSettings.Playing, true);
                if (WaveParticleSystem && OODConfig.MobilePlayerVisuals.Value) WaveParticleSystem.Play();
                
                if (PlayerSettings.IsPaused && mAudio.clip != null)
                {
                    mAudio.UnPause();
                }
                else
                {
                    mAudio.Play();
                }

                var isPlaying = mAudio.isPlaying;
                PlayerSettings.IsPaused = !isPlaying;
                PlayerSettings.IsPlaying = isPlaying;
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
            PlayerSettings.PlayerLinkType = PlayerSettings.LinkType.Audio;
            var clip = PlayerSettings.DynamicStation.Tracks[PlayerSettings.DynamicStation.CurrentTrackIndex];
            mAudio.clip = clip.AudioClip;
            mAudio.time = clip.CurrentTime;
            foreach (var component in ComponentLists.MediaComponentLists)
            {
                foreach (BasePlayer player in component.Value)
                {
                    if (player == this || !player || player.PlayerSettings.DynamicStation == null) continue;
                    if(player.PlayerSettings.CurrentMode == PlayerSettings.PlayerMode.Dynamic &&
                       player.PlayerSettings.DynamicStation.Title == PlayerSettings.DynamicStation.Title &&
                       player.PlayerSettings.IsPlaying && !player.PlayerSettings.IsPaused)   
                    {
                        player.mAudio.clip = clip.AudioClip;
                        player.mAudio.time = clip.CurrentTime; // sync nearby players
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
            // Calculate the remaining time for the clip to finish and schedule the next track
            float remainingTime = clip.AudioClip.length - mAudio.time;
            if(DynamicStationCoroutine != null) StopCoroutine(DynamicStationCoroutine);
            DynamicStationCoroutine = StartCoroutine(AudioEndEvent(remainingTime, PlayNextDynamicStationTrack));
            if(WaveParticleSystem) WaveParticleSystem.Play();
            if(Animator) Animator.SetBool(PlayerSettings.Playing, true);
            UpdateRadioPanel();
        }

        private void PlayNextDynamicStationTrack()
        {
            if (PlayerSettings.DynamicStation == null) return;
            mAudio.Stop();
            mAudio.clip = null;
            mAudio.time = 0;
            StartCoroutine(DelayedExecution(0.35f, () =>
                {
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
            if (RadioPanelObj && UIController.RadioPanelThumbnail && mAudio.isPlaying)
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
                else if(mAudio.isPlaying && PlayerSettings.PlayerLinkType == PlayerSettings.LinkType.Audio)
                {
                    RadioPanelObj.SetActive(true);
                    UIController.RadioPanelThumbnail.sprite = PlayerSettings.Thumbnail != null ? PlayerSettings.Thumbnail : null;
                }
                else if (PlayerSettings.PlayerLinkType != PlayerSettings.LinkType.Audio)
                {
                    RadioPanelObj.SetActive(false);
                }
            }
        }

        public void Pause(bool isRPC = false) //Pause the video (if playing)
        {
            //If not RPC, send pause RPC command
            if (!isRPC)
            {
                RPC.SendData(0,CinemaPackage.RPCDataType.Pause, PlayerSettings.PlayerType, MediaPlayerID,
                    gameObject.transform.position, GetTime());
                PlayerSettings.IsPaused = true;
                SaveZDO();
                return;
            }
            //If we're a radio, stop the animation
            if (Animator) Animator.SetBool(PlayerSettings.Playing, false);
            if (WaveParticleSystem) WaveParticleSystem.Stop();
            
            //Pause the video and audio, set bools
            mScreen.Pause();
            mAudio.Pause();
            PlayerSettings.IsPaused = true;
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

        protected void UpdateLoadingIndicator() //update loading indicator, called every half second
        {
            if (URLGrab.LoadingBool  && !String.IsNullOrEmpty(UnparsedURL)) //if urlgrab is loading and not failed, update loading indicators 
            {
                if (ScreenUICanvasObj && LoadingCircleObj && RadioPanelObj)
                {
                    ScreenUICanvasObj.SetActive(true); //Show the loading circle on screen
                    RadioPanelObj.SetActive(false);
                    LoadingCircleObj.SetActive(true);
                }

                UIController?.SetLoadingIndicatorActive(true); //Show the loading indicator in the GUI

                var loadingMessageIndex = PlayerSettings.LoadingCount % 4; // Cycle through the loading messages
                if (UIController != null && UIController.LoadingIndicatorObj != null)
                    UIController.LoadingIndicatorObj.GetComponent<Text>().text =
                        UIController.LoadingMessages[loadingMessageIndex];
                PlayerSettings.LoadingCount++;
            }
            else if (!URLGrab.LoadingBool && UIController.LoadingIndicatorObj &&
                     UIController.LoadingIndicatorObj.activeSelf)
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

            if(DynamicStationCoroutine != null) StopCoroutine(DynamicStationCoroutine);
            PlayerSettings.DynamicStation = null;
            
            //Stop the video and audio, hide indicators
            mScreen.Stop();
            mAudio.Stop();
            mScreen.url = "";
            mAudio.clip = null;
            mScreen.time = 0;
            mAudio.time = 0;
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
            YoutubeURLNode = null;
            YoutubeSoundDirectUri = null;
            YoutubeVideoDirectUri = null;
            PlaylistURL = null;
            PlaylistString = null;
            PlaylistPosition = 0;
            UIController.SetInputFieldText("");
            UIController.UpdatePlaylistUI();
            SaveZDO();
            SendUpdateZDO_RPC();
        }

        protected IEnumerator AudioWebRequest(Uri url) // Pipes downloadhandler to audio clip and plays it
        {
            var dh = new DownloadHandlerAudioClip(url, AudioType.MPEG)
            {
                compressed = false // This needs to be false now or Unity crashes due to memory access violation, I think this causes the audio to decompress to PCM format instead of keeping in memory 
            };
            //Jotunn.Logger.LogDebug("testing coroutine");
            using var wr = new UnityWebRequest(url, "GET", dh, null);
            yield return wr.SendWebRequest();

            if (wr.result == UnityWebRequest.Result.ProtocolError ||
                wr.result == UnityWebRequest.Result.ConnectionError)
            {
                Logger.LogError(wr.error);
            }
            else
            {
                mAudio.clip = dh.audioClip;
                mScreen.url = "";
                mScreen.Stop();
                mAudio.Play();
                StartCoroutine(DelayedExecution(0.5f, SendRequestTimeSync_RPC));
                PlayerSettings.PlayerLinkType = PlayerSettings.LinkType.Audio;
                PlayerSettings.IsPlaying = true;
                PlayerSettings.IsPaused = false;
                if (Animator) Animator.SetBool(PlayerSettings.Playing, true);
                if (WaveParticleSystem && OODConfig.MobilePlayerVisuals.Value) WaveParticleSystem.Play();
                if(ScreenUICanvasObj) ScreenUICanvasObj.SetActive(true);
                UpdateRadioPanel();
                UIController.ResetLoadingIndicator();
            }
        }

        private IEnumerator CreateThumbnailFromURL(Uri url)
        {
            var dh = new DownloadHandlerTexture(true); // true for non-readable texture
            using var wr = new UnityWebRequest(url, "GET", dh, null);
            yield return wr.SendWebRequest();

            if (wr.result == UnityWebRequest.Result.ProtocolError || wr.result == UnityWebRequest.Result.ConnectionError)
            {
                Logger.LogError($"Error downloading image: {wr.error}");
            }
            else
            {
                // Create a sprite from the downloaded texture
                Texture2D texture = DownloadHandlerTexture.GetContent(wr);
                Rect rect = new Rect(0, 0, texture.width, texture.height);
                Vector2 pivot = new Vector2(0.5f, 0.5f); // Center pivot
                Sprite sprite = Sprite.Create(texture, rect, pivot);
                
                // Assign the sprite to PlayerSettings.Thumbnail
                PlayerSettings.Thumbnail = sprite;

                // Additional UI updates can be performed here if needed
            }
        }

        
        public void PlaySoundcloud(string sentUrl, bool isRPC) // Soundcloud urlgrab and play
        {
            var url = URLGrab.CleanUrl(sentUrl);
            UIController.SetLoadingIndicatorText("Processing");
            UIController.SetLoadingIndicatorActive(true);
            if (sentUrl != null)
            {
                StartCoroutine(URLGrab.GetSoundcloudExplodeCoroutine(url, (resultUrl, artworkUri) =>
                {
                    if (resultUrl != null)
                    {
                        if (artworkUri != null)
                        {
                            StartCoroutine(CreateThumbnailFromURL(artworkUri));
                        }
                        else
                        {
                            PlayerSettings.Thumbnail = null;
                        }
                        StartCoroutine(AudioWebRequest(resultUrl));
                        
                    }
                    else
                    {
                        UIController.SetLoadingIndicatorText("Null, check logs");
                        Logger.LogWarning("Failed to load Soundcloud");
                        StartCoroutine(ResetLoadingIndicatorAfterDelay());
                    }
                }));
            }
            else
            {
                var message = "Soundcloud Null"; 
                Logger.LogInfo("Soundcloud Null, check for exceptions");

                StartCoroutine(UIController.UnavailableIndicator(message));
            }
        }
        
        public void PlayYoutube(string url) // Youtube urlgrab and play
        {
            if (URLGrab.LoadingBool) return;
            if (OODConfig.IsYtEnabled.Value)
            {
                if (PlayerSettings.IsLooping)
                {
                    mScreen.isLooping = true;
                    mAudio.loop = true;
                }
                
                if (OODConfig.YoutubeAPI.Value == OODConfig.YouTubeAPI.YouTubeExplode)
                {
                    StartYoutubeProcessing(url);
                }
                else
                {
                    YoutubeURLNode = url;
                    StartCoroutine(YoutubeNodeQuery()); // Youtube-dl nodejs //TODO update node code
                }
            }
            else
            {
                StartCoroutine(UIController.UnavailableIndicator("YouTube disabled")); 
            }
        }
        public void RPC_SetURL(string url, bool isPaused = false, float time = 0f) // RPC SetURL
        {
            if (url == null) return;
            System.Text.Encoding encoding = System.Text.Encoding.UTF8;
            byte[] bytes = encoding.GetBytes(url);
            url = encoding.GetString(bytes);
            PlayerSettings.IsPaused = isPaused;
            ClearRenderTexture(mScreen.targetTexture);
            //check if url is audio file
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

                DownloadURL = URLGrab.CleanUrl(url);
                StartCoroutine(AudioWebRequest(DownloadURL));
                return;
            }

            // check if url is soundcloud
            if (url.Contains("soundcloud.com/"))
            {
                PlayerSettings.PlayerLinkType = PlayerSettings.LinkType.Soundcloud;
                PlaySoundcloud(url, true);
                return;
            }
            
            if (url.Contains("\\") || url.Contains(".") || url.Contains("/"))
            {
                //Relative paths for local files
                var relativeURL = URLGrab.GetRelativeURL(url);

                if (relativeURL != "")
                {
                    mScreen.url = relativeURL;
                    PlayerSettings.PlayerLinkType = PlayerSettings.LinkType.RelativeVideo;
                    if (OODConfig.DebugEnabled.Value) Logger.LogDebug("Playing: " + relativeURL);
                    BeginLoadingPrepare();
                }
                if ((url.StartsWith("http://") || url.StartsWith("https://")) && 
                    !Path.HasExtension(url) && 
                    OODConfig.IsYtEnabled.Value)
                {
                    if (PlayerSettings.IsLooping && (!mScreen.isLooping || !mAudio.loop))
                    {
                        mScreen.isLooping = true;
                        mAudio.loop = true;
                    }
    
                    PlayerSettings.PlayerLinkType = PlayerSettings.LinkType.Youtube;
                    PlayYoutube(url);
                }
                else
                {
                    PlayerSettings.PlayerLinkType = PlayerSettings.LinkType.Video;
                    mScreen.url = url;
                    BeginLoadingPrepare();
                    if (OODConfig.DebugEnabled.Value) Logger.LogDebug("Playing: " + url);
                }
            }
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
            if (UIController.LoadingIndicatorObj)
            {
                UIController.SetLoadingIndicatorText("Processing");
                UIController.LoadingIndicatorObj.SetActive(true);
            }



            StartCoroutine(Ytdl.GetVideoUrlWithRetry(
                url: url,
                onComplete: (resultUrl) =>
                {
                    if (!string.IsNullOrEmpty(resultUrl))
                    {
                        Jotunn.Logger.LogDebug("Result URL: " + resultUrl);
                        mScreen.url = resultUrl;
                        BeginLoadingPrepare();  
                    }
                    else
                    {
                        Jotunn.Logger.LogError("Failed to get video URL");
                        UIController.SetLoadingIndicatorText("Failed to load video");
                        Logger.LogWarning("Failed to load video");
                        // Optionally, reset the loading indicator after some time.
                        StartCoroutine(ResetLoadingIndicatorAfterDelay());
                    }
                },
                maxRetries: 3,
                timeoutSeconds: 120
            ));
        }
        
        private IEnumerator ResetLoadingIndicatorAfterDelay()
        {
            yield return new WaitForSeconds(1.75f);
            UIController.ResetLoadingIndicator();
        }

        private IEnumerator YoutubeNodeQuery(bool isRPC = false)
        {
            if (YoutubeURLNode == null)
            {
                Logger.LogDebug("nodejs: error in yt query, youtube url null. are you sure you want use nodejs?");
                yield break;
            }

            //clean url
            var url = Uri.EscapeDataString(YoutubeURLNode);

            var nodeUrl = OODConfig.NodeUrl.Value;
            var authCode = OODConfig.YtAuthCode.Value;
            //begin query
            var www = UnityWebRequest.Get(nodeUrl + url + "/" + authCode);
            //Jotunn.Logger.LogDebug("url at: " + nodeUrl + url + "/" + authCode);
            www.timeout = 30;
            UIController.SetLoadingIndicatorText("Processing");
            UIController.LoadingIndicatorObj.SetActive(true);


            yield return www.SendWebRequest();
            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.Log(www.error);
                UIController.LoadingIndicatorObj.GetComponent<Text>().text = www.error;
                yield return new WaitForSeconds(2);
                UIController.LoadingIndicatorObj.SetActive(false);
            }
            else if (www.downloadHandler.text.Contains("AUTH DENIED"))
            {
                UIController.SetLoadingIndicatorText("Invalid Auth");
                yield return new WaitForSeconds(2);
                UIController.LoadingIndicatorObj.SetActive(false);
            }
            else
            {
                /*
                * this feels really messy but yt-dlp returns either seperate audio/video files or one single merged file depending on codec availability
                * maybe just remove node-js completely, but i like having the backup if needed.
                * the unfortunate thing about hacky api's like youtubeexplode is they break eventually, youtue-dlp has always been consistent in my experience 
                * nodejs server is set up to hopefully only return the merged file, but just in case it returns the split files we need to deal with that too
                */
                if (www.downloadHandler.text.Contains("\\"))
                {
                    // split files, seperate into different strings
                    var lines = www.downloadHandler.text.Split(
                        new[] { "\r\n", "\r", "\n", "\\n" },
                        StringSplitOptions.None
                    );

                    for (var i = 0; i < lines.Length; i++) //clean quotations from node return
                        lines[i] = lines[i].Replace("\"", "");

                    // clean uris

                    if (Uri.TryCreate(lines[0], UriKind.Absolute, out var cleanVideoUri))
                    {
                        //Jotunn.Logger.LogDebug("Clean URI: " + cleanVideoUri.AbsoluteUri);
                        YoutubeVideoDirectUri = cleanVideoUri;
                    }
                    else
                    {
                         Logger.LogError("Invalid URI: " + lines[0]);
                        UIController.SetLoadingIndicatorText("Invalid URI");
                        yield return new WaitForSeconds(2);
                        UIController.LoadingIndicatorObj.SetActive(false);
                    }


                    if (Uri.TryCreate(lines[1], UriKind.Absolute, out var cleanSoundUri))
                    {
                        //Jotunn.Logger.LogDebug("Clean URI: " + cleanSoundUri.AbsoluteUri);
                        YoutubeSoundDirectUri = cleanSoundUri;
                    }
                    else
                    {
                         Logger.LogError("Invalid URI: " + lines[1]);
                        UIController.SetLoadingIndicatorText("Invalid URI");
                        yield return new WaitForSeconds(2);
                        UIController.LoadingIndicatorObj.SetActive(false);
                    }

                    // make audio clip and play video
                    StartCoroutine(CreateYoutubeAudioAndPlay());
                }
                else
                {
                    //single file, clean url
                    var cleanUrl = www.downloadHandler.text.Replace("\"", "");
                    if (Uri.TryCreate(cleanUrl, UriKind.Absolute, out var cleanVideoUri))
                    {
                        //Jotunn.Logger.LogDebug("Clean URI: " + cleanVideoUri.AbsoluteUri);
                        YoutubeVideoDirectUri = cleanVideoUri;
                    }
                    else
                    {
                       Logger.LogError("Invalid URI: " + cleanUrl);
                        UIController.SetLoadingIndicatorText("Invalid URI");
                        yield return new WaitForSeconds(2);
                        UIController.LoadingIndicatorObj.SetActive(false);
                    }
                    
                    // play
                    mScreen.url = YoutubeVideoDirectUri.AbsoluteUri;
                    mScreen.Prepare();
                    //m_screen.transform.Find("Plane").gameObject.SetActive(true);
                    //m_screen.Play();
                    if (ScreenUICanvasObj && LoadingCircleObj)
                    {
                        ScreenUICanvasObj.SetActive(true);
                        LoadingCircleObj.SetActive(true);
                    }

                    UIController.LoadingIndicatorObj.SetActive(false);
                }
            }
        }
        
        private IEnumerator CreateYoutubeAudioAndPlay() // node js server returns split audio/video files, this function makes an audio clip from the audio file and plays the video
        {
            if (YoutubeSoundDirectUri == null)
            {
                Logger.LogDebug("sound url is null, waiting");
                yield return new WaitForSeconds(1);
            }

            if (YoutubeSoundDirectUri == null)
            {
                Logger.LogDebug("sound url is null still, exiting");
                yield break;
            }

            var dh = new DownloadHandlerAudioClip(YoutubeSoundDirectUri, AudioType.UNKNOWN)
            {
                compressed = true // This
            };
            using var wr = new UnityWebRequest(YoutubeSoundDirectUri, "GET", dh, null);
            yield return wr.SendWebRequest();

            if (wr.result == UnityWebRequest.Result.ProtocolError ||
                wr.result == UnityWebRequest.Result.ConnectionError)
            {
                Logger.LogWarning(wr.error);
                UIController.LoadingIndicatorObj.SetActive(false);
            }
            else
            {
                mAudio.clip = dh.audioClip;
                
                if (YoutubeVideoDirectUri != null)
                {
                    mScreen.url = YoutubeVideoDirectUri.AbsoluteUri;
                    mScreen.Prepare();
                    if (ScreenUICanvasObj && LoadingCircleObj)
                    {
                        ScreenUICanvasObj.SetActive(true);
                        LoadingCircleObj.SetActive(true);
                    }

                    UIController.LoadingIndicatorObj.SetActive(false);
                }
                else
                {
                    Logger.LogDebug("nodejs: yt url is null. are you sure want to use nodejs?");
                    UIController.LoadingIndicatorObj.SetActive(false);
                }
            }
        }
        
        public  void UpdatePlayerTime(float time)
        {
            // Check to avoid constant seeking
            if (Math.Abs(mScreen.time - time) > 0.05) // Threshold can be adjusted
            {
                mScreen.time = time;
            }
            if (Math.Abs(mAudio.time - time) > 0.05) // Threshold can be adjusted
            {
                mAudio.time = time;
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
            {
                if (mScreen.isPlaying) zdo.Set("time", (float) mScreen.time);
                if (mAudio.isPlaying) zdo.Set("time", mAudio.time);
            }
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
            mAudio.loop = PlayerSettings.IsLooping;
            mScreen.isLooping = PlayerSettings.IsLooping;
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
                            MediaPlayerID, gameObject.transform.position, 0f, PlayerSettings.DynamicStation.Title);
                    }));
                    StartCoroutine(DelayedExecution(2f, SendRequestTimeSync_RPC));
                }
            }
            else
            {
                StartCoroutine(DelayedExecution(3f, () => { RPC_SetURL(UnparsedURL, PlayerSettings.IsPaused); }));
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
            PlayerSettings.IsLooping = zdo.GetBool("isLooping");
            mAudio.loop = PlayerSettings.IsLooping;
            mScreen.isLooping = PlayerSettings.IsLooping;
            PlayerSettings.CurrentMode = (PlayerSettings.PlayerMode)zdo.GetInt("currentMode");
            if (zdo.GetString("url") != UnparsedURL)
            {
                if (String.IsNullOrEmpty(UnparsedURL))
                {
                    if (PlayerSettings.CurrentMode == PlayerSettings.PlayerMode.Dynamic)
                    {
                        StartCoroutine(DelayedExecution(1f, () =>
                        {
                            PlayerSettings.DynamicStation = StationManager.Instance.GetStation(UnparsedURL);
                            if (PlayerSettings.DynamicStation?.Title == null) return;
                            RPC.SendData(0, CinemaPackage.RPCDataType.RequestStation, PlayerSettings.PlayerType,
                                MediaPlayerID, gameObject.transform.position, 0f, PlayerSettings.DynamicStation.Title);
                        }));
                        StartCoroutine(DelayedExecution(2f, SendRequestTimeSync_RPC));
                    }
                    else
                    {
                        RPC_SetURL(UnparsedURL);
                    }
                }
                else
                {
                    Stop(true);
                }
            }

            if (zdo.GetInt("speakerCount") != mSpeakers.Count)
            {
                mSpeakers = SpeakerHelper.DecompressSpeakerList(zdo.GetByteArray("speakers"));
                UpdateSpeakerCenter();
            }
            mSpeakers = SpeakerHelper.DecompressSpeakerList(zdo.GetByteArray("speakers"));
            UpdateSpeakerCenter();
            if(zdo.GetBool("isPaused") != PlayerSettings.IsPaused)
            {
                Pause(true);
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
            if(mScreen.isPlaying || mAudio.isPlaying)
            {
                BroadcastTime();
            }
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
            if (PlayerSettings.PlayerLinkType == PlayerSettings.LinkType.Youtube ||
                PlayerSettings.PlayerLinkType == PlayerSettings.LinkType.RelativeVideo ||
                PlayerSettings.PlayerLinkType == PlayerSettings.LinkType.Video)
            {
                return (float)mScreen.time;
            }
            if (PlayerSettings.PlayerLinkType == PlayerSettings.LinkType.Audio)
            {
                return mAudio.time;
            }
            return 0f;
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