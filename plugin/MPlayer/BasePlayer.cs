using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Threading;
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
        /// <summary>
        ///     RPC address of this player: its ZDO id, which every client agrees on. The girdle
        ///     shares its wearer's ZDO, so each wearer's girdle gets a distinct id.
        /// </summary>
        public string MediaPlayerID
        {
            get
            {
                var zdo = ZNetView ? ZNetView.GetZDO() : null;
                return zdo != null ? zdo.m_uid.ToString() : "";
            }
        }
        public string UnparsedURL { get; set; }
        public Uri DownloadURL { get; set; }
        private YoutubeDecoder youtubeDecoder;
        private int playbackGeneration;
        private double pendingPlaybackTime;
        private bool hasPendingPlaybackTime;
        private bool youtubeBackendActive;
        private bool youtubeLoading;
        private CancellationTokenSource networkCancellation;
        private float indicatorHoldUntil;
        private Coroutine videoOutputWatch;

        /// <summary>How long after preparing to keep watching for a late video format.</summary>
        private const float VideoOutputWatchSeconds = 3f;

        /// <summary>
        ///     Drift a network stream is left alone for. Seeking one discards its buffer and
        ///     waits out the stream buffer again (1.5 s by default), so correcting less than
        ///     this made playback stutter on every time sync and fall further behind.
        /// </summary>
        private const double NetworkSeekTolerance = 1.0d;

        /// <summary>Fresh extractions tried after a video download fails, per window below.</summary>
        private const int MaxStreamRecoveries = 2;
        private const float StreamRecoveryWindowSeconds = 120f;
        private int streamRecoveries;
        private float streamRecoveryWindowStart = float.NegativeInfinity;

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
        // The saved links are the truth. A linked speaker that is unloaded, or not loaded yet,
        // stays linked and keeps its place in the audio center on every client.
        private List<SpeakerLink> speakerLinks = new List<SpeakerLink>();
        internal int SpeakerCount => speakerLinks.Count;
        private Transform centerAudioSphere;
        protected SphereCollider triggerCollider;
        private AudioTap audioTap;
        private readonly List<SpeakerEmitter> speakerEmitters = new List<SpeakerEmitter>();
        private float baseSpatialBlend = 1f;

        /// <summary>
        ///     Whether the player's own position is one of its speakers: counted in the center
        ///     and given its own emitter in each-speaker mode.
        /// </summary>
        protected virtual bool EmitsFromSelf => true;

        internal IReadOnlyList<SpeakerLink> SpeakerLinks => speakerLinks;

        /// <summary>The volume listeners hear, after mute, the slider and drop-off.</summary>
        internal float OutputVolume { get; private set; }

        /// <summary>
        ///     Unity may apply a source's volume before its filters, so the tap would copy audio
        ///     already scaled by it. In each-speaker mode the source stays at full volume and
        ///     the emitters apply the volume, once.
        /// </summary>
        protected void SetOutputVolume(float volume)
        {
            OutputVolume = volume;
            if (!mAudio) return;
            mAudio.volume = audioTap && audioTap.Active ? 1f : volume;
        }

        /// <summary>Where the sound plays from in center mode.</summary>
        internal Vector3 AudioPosition => mAudio ? mAudio.transform.position : transform.position;

        /// <summary>
        ///     True on a dedicated server. The server instantiates the pieces around its reference
        ///     point and every connected player's girdle, but nobody sees or hears them there: it
        ///     keeps their saved state and plays nothing.
        /// </summary>
        protected static bool Headless => OdinOnDemandPlugin.IsHeadless;


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
            if (Headless)
            {
                SetupHeadless();
                return;
            }
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

        /// <summary>
        ///     Keeps the audio source for its saved range and silences both outputs. Playback on
        ///     the server used to run yt-dlp, Streamlink and VLC for nobody, and the radio panel's
        ///     waveform sampled the disabled audio system every frame.
        /// </summary>
        private void SetupHeadless()
        {
            mAudio = gameObject.GetComponentInChildren<AudioSource>();
            if (mAudio)
            {
                mAudio.playOnAwake = false;
                mAudio.Stop();
                mAudio.enabled = false;
            }
            if (mScreen)
            {
                mScreen.playOnAwake = false;
                mScreen.Stop();
                mScreen.enabled = false;
            }
        }

        public void OnDestroy()
        {
            playbackGeneration++;
            CancelNetworkPreparation();
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
            if (UsesVlcBackend()) return;
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
            if (UsesVlcBackend()) return;
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
            // Audio-only VLC sources (radio, audio_only live) show the radio panel and waveform
            // instead of a blank screen.
            UpdateRadioPanel();
            if (videoOutputWatch != null) StopCoroutine(videoOutputWatch);
            videoOutputWatch = StartCoroutine(WatchForVideoOutput(playbackGeneration));
        }

        /// <summary>
        ///     A live source can report Playing before LibVLC negotiates its video format, so the
        ///     radio panel shows for anything that still looks audio-only and steps aside as soon
        ///     as video actually arrives.
        /// </summary>
        private IEnumerator WatchForVideoOutput(int generation)
        {
            var deadline = Time.time + VideoOutputWatchSeconds;
            while (Time.time < deadline)
            {
                yield return new WaitForSeconds(0.25f);
                if (generation != playbackGeneration) yield break;
                if (!HasVideoContent()) continue;
                if (RadioPanelObj) RadioPanelObj.SetActive(false);
                yield break;
            }
        }

        private void ScreenErrorReceived(VideoPlayer source, string message)
        {
            if (UsesVlcBackend()) return;
            HandlePlaybackError(message);
        }

        private void YoutubeError(string message)
        {
            if (!youtubeBackendActive) return;
            if (youtubeDecoder != null && youtubeDecoder.InputFailed && TryRecoverStream(message))
                return;
            HandlePlaybackError(message);
            DestroyYoutubeBackend();
        }

        /// <summary>
        ///     Stream URLs can be refused or expire while the page URL stays good; pressing Set
        ///     again, a fresh extraction, was the only way back. This does that automatically and
        ///     resumes where playback was, at most a couple of times so a video that keeps
        ///     failing still ends in an error instead of retrying forever.
        /// </summary>
        private bool TryRecoverStream(string message)
        {
            var url = UnparsedURL;
            if (string.IsNullOrEmpty(url)) return false;
            if (Time.time - streamRecoveryWindowStart > StreamRecoveryWindowSeconds)
            {
                streamRecoveryWindowStart = Time.time;
                streamRecoveries = 0;
            }
            if (streamRecoveries >= MaxStreamRecoveries) return false;
            streamRecoveries++;

            var resumeAt = (float)youtubeDecoder.FailedAtSeconds;
            Logger.LogWarning($"{message} Fetching a new stream URL and resuming at {resumeAt:0.0}s " +
                              $"(attempt {streamRecoveries}/{MaxStreamRecoveries}).");
            DestroyYoutubeBackend();
            RPC_SetURL(url, PlayerSettings.IsPaused, resumeAt);
            return true;
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
                HoldLoadingIndicator();
                StartCoroutine(ResetLoadingIndicatorAfterDelay(playbackGeneration));
            }
        }

        /// <summary>
        ///     Keeps a message on the loading indicator. The twice-a-second loading tick hides the
        ///     indicator as soon as nothing is loading, which is what cut error text short.
        /// </summary>
        internal void HoldLoadingIndicator()
        {
            indicatorHoldUntil = Time.time + Utils.UI.UIController.ErrorMessageSeconds;
            UIController.SetLoadingIndicatorActive(true);
        }

        public double PlaybackTime
        {
            get
            {
                if (youtubeBackendActive && youtubeDecoder != null)
                    return youtubeDecoder.Time;
                if (UsesVlcBackend())
                    return hasPendingPlaybackTime ? pendingPlaybackTime : 0d;
                if (IsVideoLink())
                    return mScreen != null ? mScreen.time : 0d;
                if (mAudio != null && mAudio.clip != null)
                    return mAudio.time;
                return hasPendingPlaybackTime ? pendingPlaybackTime : 0d;
            }
        }

        /// <summary>Total length of the current media in seconds; 0 when live or unknown.</summary>
        public double PlaybackDuration
        {
            get
            {
                if (youtubeBackendActive && youtubeDecoder != null)
                    return youtubeDecoder.Length;
                if (UsesVlcBackend())
                    return 0d;
                if (IsVideoLink())
                    return mScreen != null ? mScreen.length : 0d;
                return mAudio != null && mAudio.clip != null ? mAudio.clip.length : 0d;
            }
        }

        /// <summary>"Kick: xqc" - the service plus the channel segment of a live URL.</summary>
        private static string LiveChannelTitle(string service, Uri uri)
        {
            var channel = uri.AbsolutePath.Trim('/');
            var slash = channel.IndexOf('/');
            if (slash > 0) channel = channel.Substring(0, slash);
            return channel.Length == 0 ? service : service + ": " + channel;
        }

        public bool IsVideoPlaying => IsVideoLink() && IsPlaybackPlaying();

        public void SetLooping(bool looping)
        {
            if (youtubeBackendActive && youtubeDecoder != null)
            {
                youtubeDecoder.IsLooping = looping && !PlayerSettings.IsPlayingPlaylist;
                return;
            }
            if (UsesVlcBackend()) return;

            if (mScreen != null) mScreen.isLooping = looping && !PlayerSettings.IsPlayingPlaylist;
            if (mAudio != null) mAudio.loop = looping;
        }

        private bool UsesVlcBackend()
        {
            return PlayerSettings.PlayerLinkType == PlayerSettings.LinkType.Youtube ||
                   PlayerSettings.PlayerLinkType == PlayerSettings.LinkType.Soundcloud ||
                   PlayerSettings.PlayerLinkType == PlayerSettings.LinkType.NetworkStream ||
                   PlayerSettings.PlayerLinkType == PlayerSettings.LinkType.LiveChannel;
        }

        private bool IsVideoLink()
        {
            return UsesVlcBackend() ||
                   PlayerSettings.PlayerLinkType == PlayerSettings.LinkType.RelativeVideo ||
                   PlayerSettings.PlayerLinkType == PlayerSettings.LinkType.Video;
        }

        /// <summary>
        ///     True when the playing source really carries video. A VLC link type only means video
        ///     is possible: internet radio and audio-only live streams arrive over the same path.
        /// </summary>
        private bool HasVideoContent()
        {
            if (youtubeBackendActive && youtubeDecoder != null) return youtubeDecoder.HasVideoTrack;
            return IsVideoLink();
        }

        private bool IsPlaybackPlaying()
        {
            if (youtubeBackendActive && youtubeDecoder != null)
                return youtubeDecoder.IsPlaying;
            if (UsesVlcBackend())
                return false;
            if (IsVideoLink())
                return mScreen != null && mScreen.isPlaying;
            return mAudio != null && mAudio.isPlaying;
        }

        private bool IsPlaybackPrepared()
        {
            if (youtubeBackendActive && youtubeDecoder != null)
                return youtubeDecoder.IsPrepared;
            if (UsesVlcBackend())
                return false;
            if (IsVideoLink())
                return mScreen != null && mScreen.isPrepared;
            return mAudio != null && mAudio.clip != null;
        }

        private bool IsPlaybackLooping()
        {
            if (youtubeBackendActive && youtubeDecoder != null)
                return youtubeDecoder.IsLooping;
            if (UsesVlcBackend())
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
            if (UsesVlcBackend())
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
            if (UsesVlcBackend())
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
            else if (UsesVlcBackend())
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
            CancelNetworkPreparation();
            DestroyYoutubeBackend();
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

        private void PrepareVlcBackend(string videoUrl, string audioUrl, int generation,
            IDictionary<string, string> headers = null, bool useChunkedInput = true, bool isLive = false)
        {
            if (generation != playbackGeneration || !UsesVlcBackend())
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
                    mScreen != null ? mScreen.targetTexture : null,
                    headers,
                    useChunkedInput,
                    isLive);
            }
            catch (Exception exception)
            {
                HandlePlaybackError(exception.Message);
                DestroyYoutubeBackend();
            }
        }

        private void CancelNetworkPreparation()
        {
            networkCancellation?.Cancel();
            networkCancellation = null;
        }

        private IEnumerator PlayLiveChannel(string url, string service, int generation)
        {
            youtubeLoading = true;
            UIController.SetLoadingIndicatorText("Resolving " + service);
            if (UIController.LoadingIndicatorObj) UIController.LoadingIndicatorObj.SetActive(true);
            using (var cancellation = new CancellationTokenSource())
            {
                networkCancellation = cancellation;
                // Thread pool: the runtime scan and process launch run before the first await.
                var maxHeight = OODConfig.MaxVideoHeight.Value;
                var resolution = Task.Run(() => StreamlinkRuntime.ResolveAsync(url, maxHeight,
                    cancellation.Token));
                yield return new WaitUntil(() => resolution.IsCompleted);
                if (ReferenceEquals(networkCancellation, cancellation)) networkCancellation = null;
                // Observe exceptions even when a newer source has superseded this request.
                var error = resolution.Exception?.GetBaseException();
                if (generation != playbackGeneration || resolution.IsCanceled) yield break;
                if (error != null)
                {
                    HandlePlaybackError(error.Message);
                    if (UIController.LoadingIndicatorObj)
                        UIController.SetLoadingIndicatorText(error.Message);
                    yield break;
                }
                PrepareVlcBackend(resolution.Result, null, generation, useChunkedInput: false, isLive: true);
            }
        }

        private IEnumerator PrepareNetworkStream(string url, int generation)
        {
            youtubeLoading = true;
            using (var cancellation = new CancellationTokenSource())
            {
                networkCancellation = cancellation;
                cancellation.CancelAfter(TimeSpan.FromSeconds(10));
                // Probe off-thread: an extensionless mount can be radio, HLS, a file or a website.
                var probe = Task.Run(() => ProbeNetworkStream(url, cancellation.Token));
                yield return new WaitUntil(() => probe.IsCompleted);
                if (ReferenceEquals(networkCancellation, cancellation)) networkCancellation = null;
                var error = probe.Exception?.GetBaseException();
                if (generation != playbackGeneration) yield break;
                if (error != null || probe.IsCanceled)
                {
                    HandlePlaybackError(error?.Message ?? "Network stream classification timed out.");
                    yield break;
                }
                if (probe.Result == NetworkStreamKind.Website)
                {
                    PlayerSettings.PlayerLinkType = PlayerSettings.LinkType.Youtube;
                    PlayYoutube(url, generation);
                    yield break;
                }
                PrepareVlcBackend(url, null, generation, useChunkedInput: false,
                    isLive: probe.Result == NetworkStreamKind.Live);
            }
        }

        private enum NetworkStreamKind { Media, Live, Website }

        private static NetworkStreamKind ProbeNetworkStream(string url, CancellationToken cancellationToken)
        {
            const int maxPlaylistBytes = 256 * 1024;
            var uri = new Uri(url);
            for (int depth = 0; depth < 4; depth++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var request = (HttpWebRequest)WebRequest.Create(uri);
                request.Timeout = 10000;
                request.ReadWriteTimeout = 10000;
                request.MaximumAutomaticRedirections = 4;
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                using (cancellationToken.Register(request.Abort))
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    string contentType = response.ContentType ?? "";
                    if (contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) ||
                        contentType.StartsWith("application/xhtml+xml", StringComparison.OrdinalIgnoreCase))
                        return NetworkStreamKind.Website;
                    if (response.Headers["icy-metaint"] != null || response.Headers["icy-name"] != null ||
                        response.Headers["icy-br"] != null)
                        return NetworkStreamKind.Live;

                    using (var stream = response.GetResponseStream())
                    {
                        // Read only the signature for ordinary media, never buffer an open-ended feed.
                        var prefix = new byte[10];
                        int count = 0;
                        while (count < prefix.Length)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            int read = stream.Read(prefix, count, prefix.Length - count);
                            if (read == 0) break;
                            count += read;
                        }
                        string signature = System.Text.Encoding.UTF8.GetString(prefix, 0, count).TrimStart('\uFEFF');
                        if (!signature.StartsWith("#EXTM3U", StringComparison.Ordinal))
                            return NetworkStreamKind.Media;

                        string playlist;
                        using (var buffer = new MemoryStream())
                        {
                            buffer.Write(prefix, 0, count);
                            var chunk = new byte[4096];
                            int read;
                            while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                if (buffer.Length + read > maxPlaylistBytes)
                                    throw new InvalidDataException("HLS playlist exceeds the classification limit.");
                                buffer.Write(chunk, 0, read);
                            }
                            playlist = System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
                        }

                        bool mediaPlaylist = false;
                        bool finite = false;
                        bool variantNext = false;
                        string variant = null;
                        string rendition = null;
                        using (var lines = new StringReader(playlist))
                        {
                            string line;
                            while ((line = lines.ReadLine()) != null)
                            {
                                line = line.Trim();
                                if (line == "#EXT-X-ENDLIST" || line == "#EXT-X-PLAYLIST-TYPE:VOD")
                                    finite = true;
                                if (line.StartsWith("#EXT-X-TARGETDURATION:", StringComparison.Ordinal))
                                    mediaPlaylist = true;
                                if (line.StartsWith("#EXT-X-STREAM-INF:", StringComparison.Ordinal))
                                    variantNext = true;
                                else if (variantNext && line.Length > 0 && line[0] != '#')
                                {
                                    if (variant == null) variant = line;
                                    variantNext = false;
                                }
                                else if (rendition == null && line.StartsWith("#EXT-X-MEDIA:", StringComparison.Ordinal))
                                {
                                    var match = System.Text.RegularExpressions.Regex.Match(line, "[:,]URI=\"([^\"]+)\"");
                                    if (match.Success) rendition = match.Groups[1].Value;
                                }
                            }
                        }
                        if (mediaPlaylist)
                            return finite ? NetworkStreamKind.Media : NetworkStreamKind.Live;
                        string child = variant ?? rendition;
                        if (child == null || !Uri.TryCreate(response.ResponseUri, child, out uri) ||
                            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                            throw new InvalidDataException("HLS playlist has no supported media variant.");
                    }
                }
            }
            throw new InvalidDataException("HLS playlist nesting exceeds the classification limit.");
        }

        private void UpdateChecks() //1second checks, screen render distance, master volume updates and playlist gui updates
        {
            //Master volume updates from config
            var masterVolume = OODConfig.MasterVolumeFor(PlayerSettings.PlayerType).Value;
            var mixer = mAudio.outputAudioMixerGroup.audioMixer;
            if (!mixer.GetFloat("MasterVolume", out var masterVolumeCheck) || masterVolumeCheck != masterVolume)
                mixer.SetFloat("MasterVolume", masterVolume);

            //Playlist track or media info rows in the URL panel
            UIController.UpdateMediaInfo();

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
            url = StreamlinkRuntime.NormalizeChannelUrl(encoding.GetString(bytes));
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
            if (Headless) return;
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
            PlayerSettings.MediaTitle = PlayerSettings.DynamicStation.Title;
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
            if (!RadioPanelObj || !UIController.RadioPanelThumbnail) return;
            // Prepared, not playing: LibVLC reports a live source as playing only once its first
            // buffer lands, so gating on the play state dropped the panel on the first load.
            if (!IsPlaybackPlaying() && !IsPlaybackPrepared()) return;

            var station = PlayerSettings.DynamicStation != null &&
                          PlayerSettings.CurrentMode == PlayerSettings.PlayerMode.Dynamic;
            if (!station && HasVideoContent())
            {
                RadioPanelObj.SetActive(false);
                return;
            }

            if (ScreenUICanvasObj) ScreenUICanvasObj.SetActive(true);
            if (ScreenPlaneObj) ScreenPlaneObj.SetActive(true);
            // Wipe the last video frame only when the panel actually takes over the screen.
            if (mScreen != null) ClearRenderTexture(mScreen.targetTexture);
            RadioPanelObj.SetActive(true);
            UIController.RadioPanelThumbnail.sprite =
                station ? PlayerSettings.DynamicStation.Thumbnail : PlayerSettings.Thumbnail;
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
            SetOutputVolume(PlayerSettings.Volume);
            baseSpatialBlend = mAudio.spatialBlend;
            audioTap = mAudio.gameObject.AddComponent<AudioTap>();
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
                   waveform.Setup(mAudio, () => speakerEmitters.Count > 0 && speakerEmitters[0]
                       ? speakerEmitters[0].Source
                       : null);
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
            else if (Time.time >= indicatorHoldUntil &&
                     UIController.LoadingIndicatorObj && UIController.LoadingIndicatorObj.activeSelf)
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
            // Compressed clips decode as they play. Uncompressed ones are decoded in full on the
            // main thread when the clip is created, a visible hitch for anything long.
            var dh = new DownloadHandlerAudioClip(url, AudioTypeFor(url))
            {
                compressed = true
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

        internal static AudioType AudioTypeFor(Uri url)
        {
            switch (Path.GetExtension(url.AbsolutePath).ToLowerInvariant())
            {
                case ".mp3": return AudioType.MPEG;
                case ".ogg": return AudioType.OGGVORBIS;
                case ".wav": return AudioType.WAV;
                case ".aif":
                case ".aiff": return AudioType.AIFF;
                default: return AudioType.UNKNOWN;
            }
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
                StartCoroutine(URLGrab.GetSoundcloudExplodeCoroutine(url, (resultUrl, artworkUri, title) =>
                {
                    if (generation != playbackGeneration) return;
                    if (resultUrl != null)
                    {
                        if (artworkUri != null)
                            StartCoroutine(CreateThumbnailFromURL(artworkUri, generation));
                        else
                            PlayerSettings.Thumbnail = null;
                        if (!string.IsNullOrEmpty(title)) PlayerSettings.MediaTitle = title;
                        // Stream through VLC. Unity's clip download fetched the whole track and
                        // then decoded all of it on the main thread, freezing the game on long mixes.
                        PrepareVlcBackend(resultUrl.AbsoluteUri, null, generation, useChunkedInput: false);
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
                youtubeLoading = false;
                PlayerSettings.IsPlaying = false;
                StartCoroutine(UIController.UnavailableIndicator("YouTube disabled"));
                return;
            }
            youtubeLoading = true;

            if (OODConfig.YoutubeAPI.Value == OODConfig.YouTubeAPI.YouTubeExplode)
                StartYoutubeProcessing(url, generation);
            else
                StartCoroutine(YoutubeNodeQuery(url, generation));
        }
        public void RPC_SetURL(string url, bool isPaused = false, float time = 0f)
        {
            if (url == null || Headless) return;
            System.Text.Encoding encoding = System.Text.Encoding.UTF8;
            byte[] bytes = encoding.GetBytes(url);
            url = StreamlinkRuntime.NormalizeChannelUrl(encoding.GetString(bytes));
            if (string.IsNullOrEmpty(url))
            {
                Stop(true);
                return;
            }

            UnparsedURL = url;
            PlayerSettings.CurrentMode = PlayerSettings.PlayerMode.URL;
            PlayerSettings.DynamicStation = null;
            PlayerSettings.MediaTitle = null;
            PlayerSettings.IsPaused = isPaused;
            PlayerSettings.IsPlaying = true;
            URLGrab.Reset();
            int generation = BeginSourceSwitch(time);
            ClearRenderTexture(mScreen.targetTexture);

            if (Uri.TryCreate(url, UriKind.Absolute, out var networkUri) &&
                (networkUri.Scheme == Uri.UriSchemeHttp || networkUri.Scheme == Uri.UriSchemeHttps))
            {
                string host = networkUri.Host;
                if (host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) ||
                    host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase) ||
                    host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase) ||
                    host.Equals("youtube-nocookie.com", StringComparison.OrdinalIgnoreCase) ||
                    host.EndsWith(".youtube-nocookie.com", StringComparison.OrdinalIgnoreCase))
                {
                    PlayerSettings.PlayerLinkType = PlayerSettings.LinkType.Youtube;
                    PlayYoutube(url, generation);
                    return;
                }
                var liveService = StreamlinkRuntime.ServiceName(networkUri);
                if (liveService != null)
                {
                    PlayerSettings.PlayerLinkType = PlayerSettings.LinkType.LiveChannel;
                    PlayerSettings.MediaTitle = LiveChannelTitle(liveService, networkUri);
                    StartCoroutine(PlayLiveChannel(url, liveService, generation));
                    return;
                }
                if (!host.Equals("soundcloud.com", StringComparison.OrdinalIgnoreCase) &&
                    !host.EndsWith(".soundcloud.com", StringComparison.OrdinalIgnoreCase))
                {
                    PlayerSettings.PlayerLinkType = PlayerSettings.LinkType.NetworkStream;
                    DownloadURL = networkUri;
                    StartCoroutine(PrepareNetworkStream(url, generation));
                    return;
                }
            }

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
                        if (streams.Title != null) PlayerSettings.MediaTitle = streams.Title;
                        PrepareVlcBackend(streams.VideoUrl, streams.AudioUrl, generation, streams.Headers);
                        return;
                    }

                    HandlePlaybackError("Failed to get YouTube streams");
                },
                3,
                120));
        }
        
        private IEnumerator ResetLoadingIndicatorAfterDelay(int generation)
        {
            yield return new WaitForSeconds(Utils.UI.UIController.ErrorMessageSeconds);
            indicatorHoldUntil = 0f;
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
                !Uri.TryCreate(lines[0], UriKind.Absolute, out _))
            {
                HandlePlaybackError("Node YouTube extraction returned an invalid video URL");
                yield break;
            }

            string audioUrl = null;
            if (lines.Length > 1)
            {
                if (!Uri.TryCreate(lines[1], UriKind.Absolute, out _))
                {
                    HandlePlaybackError("Node YouTube extraction returned an invalid audio URL");
                    yield break;
                }
                audioUrl = lines[1];
            }

            // Pass the extractor's strings through untouched: Uri canonicalization rewrites
            // percent-escapes in signed stream URLs and gets them rejected.
            PrepareVlcBackend(lines[0], audioUrl, generation);
        }
        
        public void UpdatePlayerTime(float time)
        {
            pendingPlaybackTime = Math.Max(0d, time);
            hasPendingPlaybackTime = true;

            if (youtubeBackendActive)
            {
                if (youtubeDecoder != null && youtubeDecoder.IsPrepared)
                {
                    if (youtubeDecoder.IsPaused ||
                        Math.Abs(youtubeDecoder.Time - pendingPlaybackTime) > NetworkSeekTolerance)
                        youtubeDecoder.Time = pendingPlaybackTime;
                    hasPendingPlaybackTime = false;
                }
                return;
            }

            if (UsesVlcBackend())
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

        /// <summary>
        ///     False for a player that lives on another object's ZDO (the girdle uses its wearer's).
        ///     That ZDO is never handed over, and only its owner writes to it.
        /// </summary>
        protected virtual bool OwnsZdo => true;

        public virtual void SaveZDO(bool saveTime = true)
        {
            var zdo = ZNetView.GetZDO();
            if (zdo == null || mAudio == null) return;
            if (!OwnsZdo && !zdo.IsOwner()) return;
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
            zdo.Set("speakers", SpeakerHelper.CompressSpeakerLinks(speakerLinks));
            zdo.Set("speakerCount", speakerLinks.Count);
            zdo.Set("speakerMode", (int)PlayerSettings.SpeakerOutput);
        }
        
        public void SaveTimeZDO()
        {
            // The server plays nothing, so its playback time is always zero.
            if (Headless) return;
            var zdo = ZNetView.GetZDO();
            if (zdo == null || (!OwnsZdo && !zdo.IsOwner())) return;
            zdo.Set("time", (float)PlaybackTime);
        }

        /// <summary>Keeps the playback position when this client, as owner, unloads the player.</summary>
        protected void SaveTimeOnUnload()
        {
            var zdo = ZNetView ? ZNetView.GetZDO() : null;
            if (zdo != null && zdo.IsOwner() && PlayerSettings.IsPlaying) SaveTimeZDO();
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
            LoadSpeakerLinks(zdo);
            LoadLocalVolume();
            PlayerSettings.IsPlaying = zdo.GetBool("isPlaying");
            PlayerSettings.IsPaused = zdo.GetBool("isPaused");
            PlayerSettings.CurrentMode = (PlayerSettings.PlayerMode)zdo.GetInt("currentMode");
            if (Headless) return;
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

            LoadSpeakerLinks(zdo);
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
            if(zdo == null || !OwnsZdo) return;
            if (zdo.IsOwner())
                return;
            zdo.SetOwner(ZDOMan.GetSessionID());
        }

        public void SetOwnership(long peer)
        {
            var zdo = ZNetView.GetZDO();
            if (zdo == null || !OwnsZdo) return;
            if (!zdo.IsOwner())
                return;
           
            BroadcastTime();
            zdo.SetOwner(peer);
        }
        
        public void RequestOwnership(ZDO zdo)
        {
            if (zdo == null || !OwnsZdo) return;
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
            if (FindSpeakerLink(sp) >= 0) return false;
            speakerLinks.Add(new SpeakerLink(sp.mGUID, sp.transform.position));
            OnSpeakerLinksChanged();
            return true;
        }
        
        public void RemoveSpeaker(SpeakerComponent sp)
        {
            // Client changes reach the server's copy of the links only through the ZDO.
            var zdo = ZNetView ? ZNetView.GetZDO() : null;
            if (Headless && zdo != null) LoadSpeakerLinks(zdo);
            var index = FindSpeakerLink(sp);
            if (index < 0) return;
            speakerLinks.RemoveAt(index);
            OnSpeakerLinksChanged();
        }

        internal bool IsLinkedTo(SpeakerComponent sp)
        {
            return FindSpeakerLink(sp) >= 0;
        }

        // Links from older versions may carry a guid the speaker no longer reports, so the
        // saved position is the fallback match.
        private int FindSpeakerLink(SpeakerComponent sp)
        {
            var guid = sp.mGUID;
            var index = speakerLinks.FindIndex(link => link.Guid == guid);
            if (index >= 0) return index;
            var position = sp.transform.position;
            return speakerLinks.FindIndex(link => (link.Position - position).sqrMagnitude < 0.01f);
        }

        internal void SetSpeakerOutput(SpeakerMode mode)
        {
            PlayerSettings.SpeakerOutput = mode;
            SaveZDO();
            UpdateSpeakerOutput();
            SendUpdateZDO_RPC();
        }

        private void OnSpeakerLinksChanged()
        {
            if (Headless)
            {
                SaveSpeakerLinks();
                SendUpdateZDO_RPC();
                return;
            }
            SaveZDO();
            UpdateSpeakerOutput();
            UIController.UpdateSpeakerCount();
            StartCoroutine(ShowCenterSphere());
            SendUpdateZDO_RPC();
        }

        /// <summary>
        ///     Writes only the links. The server never receives the clients' other changes, so
        ///     saving its whole copy of the state would roll them back.
        /// </summary>
        private void SaveSpeakerLinks()
        {
            var zdo = ZNetView ? ZNetView.GetZDO() : null;
            if (zdo == null || !OwnsZdo) return;
            ClaimOwnership(zdo);
            zdo.Set("speakers", SpeakerHelper.CompressSpeakerLinks(speakerLinks));
            zdo.Set("speakerCount", speakerLinks.Count);
        }

        private void LoadSpeakerLinks(ZDO zdo)
        {
            speakerLinks = SpeakerHelper.DecompressSpeakerLinks(zdo.GetByteArray("speakers"));
            PlayerSettings.SpeakerOutput = (SpeakerMode)zdo.GetInt("speakerMode");
            UpdateSpeakerOutput();
        }

        /// <summary>
        ///     Places the sound for the current links and mode. Each-speaker mode turns the
        ///     player's own source 2D and silent, so it keeps feeding the tap wherever the
        ///     listener is, and the emitters carry all of the audible sound.
        /// </summary>
        private void UpdateSpeakerOutput()
        {
            if (!mAudio) return;
            var eachSpeaker = audioTap && speakerLinks.Count > 0 &&
                              PlayerSettings.SpeakerOutput == SpeakerMode.EachSpeaker;
            if (!eachSpeaker)
            {
                SetEmitterPositions(null);
                mAudio.spatialBlend = baseSpatialBlend;
                if (audioTap && !audioTap.PassThrough)
                {
                    audioTap.Active = false;
                    SetOutputVolume(OutputVolume);
                    if (isActiveAndEnabled) StartCoroutine(UnmuteAfterBlendApplies());
                    else audioTap.PassThrough = true;
                }
                mAudio.transform.position = speakerLinks.Count == 0
                    ? transform.position
                    : SpeakerHelper.CalculateAudioCenter(speakerLinks,
                        EmitsFromSelf ? transform.position : (Vector3?)null);
                return;
            }

            var positions = speakerLinks.Select(link => link.Position).ToList();
            if (EmitsFromSelf) positions.Add(transform.position);
            mAudio.transform.position = transform.position;
            SetEmitterPositions(positions);
            audioTap.PassThrough = false;
            audioTap.Active = true;
            mAudio.spatialBlend = 0f;
            SetOutputVolume(OutputVolume);
        }

        /// <summary>
        ///     The tap's flags reach the audio thread at once, the restored 3D blend only when
        ///     Unity next updates its sources. Unmuting first would play the source in 2D, at
        ///     full volume for every listener, for that moment.
        /// </summary>
        private IEnumerator UnmuteAfterBlendApplies()
        {
            yield return null;
            yield return null;
            if (audioTap && !audioTap.Active) audioTap.PassThrough = true;
        }

        /// <summary>Reuses emitters so a link change does not restart the ones that stay.</summary>
        private void SetEmitterPositions(List<Vector3> positions)
        {
            int count = positions?.Count ?? 0;
            for (int i = speakerEmitters.Count - 1; i >= count; i--)
            {
                if (speakerEmitters[i]) Destroy(speakerEmitters[i].gameObject);
                speakerEmitters.RemoveAt(i);
            }
            for (int i = 0; i < count; i++)
            {
                if (i == speakerEmitters.Count)
                    speakerEmitters.Add(SpeakerEmitter.Create(this, audioTap, baseSpatialBlend));
                speakerEmitters[i].transform.position = positions[i];
            }
        }

        public void UnlinkAllSpeakers()
        {
            speakerLinks.Clear();
            OnSpeakerLinksChanged();
        }

        /// <summary>Sets this client's volume for the player and remembers it for the next load.</summary>
        public void SetVolume(float volume)
        {
            PlayerSettings.Volume = volume;
            SetOutputVolume(volume);
            var key = LocalVolumeKey;
            if (key == null) return;
            PlayerPrefs.SetFloat(key, volume);
            PlayerPrefs.SetFloat(key + ".unmuted", PlayerSettings.MuteVol);
        }

        /// <summary>Restores the volume this client last set for the player, if any.</summary>
        protected void LoadLocalVolume()
        {
            var key = LocalVolumeKey;
            if (key == null || !PlayerPrefs.HasKey(key)) return;
            PlayerSettings.Volume = PlayerPrefs.GetFloat(key, PlayerSettings.Volume);
            PlayerSettings.MuteVol = PlayerPrefs.GetFloat(key + ".unmuted", PlayerSettings.MuteVol);
            SetOutputVolume(PlayerSettings.Volume);
            UIController?.UpdateVolumeControls();
        }

        // Volume is a per-client preference, so it is kept in local prefs rather than the ZDO.
        // Placed pieces are keyed by position, which survives server restarts; the girdle is
        // one per character and the wagon moves, so it keeps its session id.
        private string LocalVolumeKey
        {
            get
            {
                const string prefix = "OdinOnDemand.Volume.";
                switch (PlayerSettings.PlayerType)
                {
                    case CinemaPackage.MediaPlayers.BeltPlayer:
                        return prefix + "girdle";
                    case CinemaPackage.MediaPlayers.CartPlayer:
                        var id = MediaPlayerID;
                        return id.Length == 0 ? null : prefix + id;
                    default:
                        var p = transform.position;
                        return prefix + string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "{0:F1},{1:F1},{2:F1}", p.x, p.y, p.z);
                }
            }
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