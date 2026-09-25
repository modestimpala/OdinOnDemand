using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LibVLCSharp.Shared;
using OdinOnDemand.Utils.Config;
using UnityEngine;
using Logger = Jotunn.Logger;

namespace OdinOnDemand.MPlayer
{
    /// <summary>
    /// Decodes network video and audio streams through one LibVLC clock.
    /// Native callbacks only touch managed/native buffers; Unity objects are updated by Update.
    /// </summary>
    public sealed class YoutubeDecoder : MonoBehaviour
    {
        private const uint AudioSampleRate = 48000;
        private const uint AudioChannels = 2;
        private const int AudioBufferSeconds = 4;
        private const float PrepareTimeoutSeconds = 120f;
        private const double AuthoritativeClockTimeoutSeconds = 1.5d;
        private const float AudioStatsIntervalSeconds = 5f;

        private static readonly object CoreLock = new object();
        private static bool coreInitialized;
        private static LibVLC sharedLibVlc;
        private static int sharedLibVlcUsers;

        private readonly ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();

        private LibVLC libVlc;
        private PlaybackSession session;
        private PrepareRequest pendingPrepare;
        private Task shutdownTask = Task.CompletedTask;
        private bool transitionInProgress;
        private volatile bool destroyed;
        private bool requestedPlay;
        private bool isPrepared;
        private bool isPlaying;
        private bool isPaused;
        private bool isLooping;
        private float prepareStartedAt;
        private double playbackClockSeconds;
        private long playbackClockAnchor;
        private bool playbackClockRunning;
        private long lastAuthoritativeClockTick;
        private long lastSampledNativeTime = -1;
        private double? pendingSeekSeconds;

        private AudioSource audioOutput;
        private AudioClip streamingClip;
        private bool ownsAudioLoopSetting;
        private bool savedAudioLoopSetting;
        private Texture2D cpuTexture;
        private RenderTexture targetTexture;

        /// <summary>The last failure was the video download itself; a new extraction may fix it.</summary>
        public bool InputFailed { get; private set; }

        /// <summary>Playback position when the last failure hit, to resume from.</summary>
        public double FailedAtSeconds { get; private set; }

        public event Action Prepared;
        public event Action Ended;
        public event Action<string> Error;

        public bool IsPrepared { get { return isPrepared; } }
        public bool IsPlaying { get { return isPlaying; } }
        public bool IsPaused { get { return isPaused; } }

        public bool IsLooping
        {
            get { return isLooping; }
            set { isLooping = value; }
        }

        public double Time
        {
            get
            {
                if (session == null)
                {
                    return 0d;
                }

                return pendingSeekSeconds ?? ReadPlaybackClock();
            }
            set
            {
                PlaybackSession current = session;
                if (current == null || !isPrepared || !current.Seekable)
                {
                    return;
                }

                double seconds = Math.Max(0d, value);
                double length = Length;
                if (length > 0d)
                {
                    seconds = Math.Min(seconds, length);
                }
                if (!requestedPlay || transitionInProgress)
                {
                    // VLC 3 can advance its clock while processing a seek in the paused state.
                    // Keep the requested position here and seek after the ordered resume command.
                    pendingSeekSeconds = seconds;
                    SetPlaybackClock(seconds, false);
                    return;
                }
                SeekCurrent(current, seconds);
            }
        }

        private void SeekCurrent(PlaybackSession current, double seconds)
        {
            if (!current.Seekable)
                return;
            if (current.Audio.DiagnosticsEnabled)
                current.DiagnosticSeeks++;
            bool resume = requestedPlay && current.NativePlaying && !current.IsBuffering;
            current.Audio.SetActive(resume);
            StopUnityAudio();
            long milliseconds = (long)(seconds * 1000d);
            current.Player.Time = milliseconds;
            lastSampledNativeTime = milliseconds;
            SetAuthoritativePlaybackClock(seconds, resume);
            if (resume)
                StartUnityAudio();
        }

        public double Length
        {
            get
            {
                PlaybackSession current = session;
                if (current == null || current.IsLive)
                {
                    return 0d;
                }

                if (!transitionInProgress)
                {
                    long milliseconds = current.Player.Length;
                    if (milliseconds > 0)
                        current.LengthSeconds = milliseconds / 1000d;
                }
                return current.LengthSeconds;
            }
        }

        /// <summary>
        ///     True once LibVLC has negotiated a video format, which only happens for media that
        ///     carries video. Audio-only sources - internet radio, audio_only live streams - stay
        ///     false, so the player can show its radio panel and waveform instead of a blank
        ///     screen. Deliberately a cached flag: libvlc_video_get_track_count takes the input
        ///     thread's lock and can stall the caller while a live input is still starting.
        /// </summary>
        public bool HasVideoTrack
        {
            get
            {
                PlaybackSession current = session;
                return current != null && current.VideoFormatSeen;
            }
        }

        /// <summary>
        /// Begins opening the streams. Seekable, pausable media prepares by playing then pausing.
        /// Live media instead keeps decoding with Unity output gated until Play.
        /// </summary>
        public void Prepare(string videoUrl, string audioUrl, AudioSource output, RenderTexture renderTarget,
            IDictionary<string, string> headers = null, bool useChunkedInput = true, bool isLive = false)
        {
            if (destroyed)
            {
                return;
            }

            Uri videoUri;
            Uri audioUri = null;
            if (output == null || !TryGetNetworkUri(videoUrl, out videoUri) ||
                (!string.IsNullOrEmpty(audioUrl) && !TryGetNetworkUri(audioUrl, out audioUri)))
            {
                StopInternal(true);
                RaiseError("VLC decoder received an invalid stream or audio output.");
                return;
            }

            pendingPrepare = new PrepareRequest(videoUri, audioUri, output, renderTarget, headers, useChunkedInput, isLive);
            pendingSeekSeconds = null;
            requestedPlay = false;
            isPrepared = false;
            isPlaying = false;
            isPaused = false;
            SetPlaybackClock(0d, false);

            ResetUnityAudio(false);
            DestroyCpuTexture();
            targetTexture = null;

            PlaybackSession previous = session;
            if (previous != null)
            {
                session = null;
                BeginShutdown(previous);
            }
            else if (!transitionInProgress)
            {
                StartPendingPrepare();
            }
        }

        public void Play()
        {
            PlaybackSession current = session;
            if (current == null || !isPrepared || destroyed)
            {
                return;
            }
            requestedPlay = true;
            if (transitionInProgress)
                return;
            if (current.HasEnded)
            {
                if (current.Seekable)
                    pendingSeekSeconds = pendingSeekSeconds ?? 0d;
                BeginReplay(current);
                return;
            }
            current.Audio.SetActive(true);
            current.VideoOutputEnabled = true;

            if (isPaused && current.Player.State == VLCState.Paused)
            {
                current.Player.SetPause(false);
            }
            else if (!current.Player.IsPlaying)
            {
                if (!current.Player.Play())
                {
                    FailCurrent(current, "LibVLC could not start the media stream.");
                    return;
                }
            }
            else if (!current.IsBuffering)
            {
                isPlaying = true;
                isPaused = false;
                SetAuthoritativePlaybackClock(
                    Math.Max(playbackClockSeconds, current.Player.Time / 1000d),
                    true);
                StartUnityAudio();
            }
            if (pendingSeekSeconds.HasValue)
            {
                double seconds = pendingSeekSeconds.Value;
                pendingSeekSeconds = null;
                SeekCurrent(current, seconds);
            }
        }

        public void Pause()
        {
            PlaybackSession current = session;
            if (current == null || !isPrepared || destroyed)
            {
                return;
            }

            requestedPlay = false;
            isPlaying = false;
            isPaused = true;
            FreezePlaybackClock();
            current.Audio.SetActive(false);
            current.VideoOutputEnabled = false;
            StopUnityAudio();
            if (!transitionInProgress)
            {
                current.OutputGatedPause = current.OutputGatedPause ||
                    !current.Seekable || !current.Player.CanPause;
                if (!current.OutputGatedPause)
                    current.Player.SetPause(true);
            }
        }

        /// <summary>
        /// Stops Unity output synchronously, then joins LibVLC's worker threads off the Unity thread.
        /// </summary>
        public void Stop()
        {
            StopInternal(true);
        }

        private void Update()
        {
            Action action;
            while (mainThreadActions.TryDequeue(out action))
            {
                if (destroyed)
                {
                    return;
                }

                action();
            }

            PlaybackSession current = session;
            if (current != null)
                UpdateAudioDiagnostics(current);
            if (current == null || transitionInProgress)
            {
                return;
            }

            if (!isPrepared && UnityEngine.Time.realtimeSinceStartup - prepareStartedAt > PrepareTimeoutSeconds)
            {
                FailCurrent(current, "LibVLC timed out while preparing the media stream.");
                return;
            }

            // LibVLC stalls on a failed callback read instead of raising an error.
            ChunkedHttpMediaInput input = current.Input;
            if (input != null && input.Failed)
            {
                InputFailed = true;
                FailCurrent(current, input.Rejected
                    ? "The stream server refused the video URL."
                    : "The video stopped downloading.");
                return;
            }

            current.DispatchTimingEvents();
            long sampledNativeTime = current.Player.Time;
            if (sampledNativeTime >= 0 && sampledNativeTime != lastSampledNativeTime)
            {
                HandleNativeTime(current, sampledNativeTime);
            }


            // Live inputs may not expose a native timeline; buffering/end still stop this clock.
            if (playbackClockRunning && !current.OutputGatedPause &&
                (System.Diagnostics.Stopwatch.GetTimestamp() - lastAuthoritativeClockTick) /
                (double)System.Diagnostics.Stopwatch.Frequency > AuthoritativeClockTimeoutSeconds)
            {
                FreezePlaybackClock();
            }

            UploadLatestFrame(current);
        }

        private void UpdateAudioDiagnostics(PlaybackSession current)
        {
            bool enabled = OODConfig.DecoderAudioStats.Value;
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (enabled != current.Audio.DiagnosticsEnabled)
            {
                current.Audio.SetDiagnosticsEnabled(enabled);
                current.DiagnosticStartedAt = now;
                current.NextDiagnosticLogAt = now + AudioStatsIntervalSeconds;
                current.DiagnosticSeeks = 0;
                if (enabled)
                {
                    int length, count;
                    AudioSettings.GetDSPBufferSize(out length, out count);
                    Logger.LogInfo(string.Format(CultureInfo.InvariantCulture,
                        "[VLC audio #{0}] enabled; cumulative frame counters, PCM={1}Hz/{2}ch, " +
                        "Unity={3}Hz DSP={4}x{5}; submitted means handed to Unity, not audible output.",
                        current.DiagnosticId, AudioSampleRate, AudioChannels,
                        AudioSettings.outputSampleRate, length, count));
                }
            }

            if (enabled && now >= current.NextDiagnosticLogAt)
            {
                LogAudioDiagnostics(current, "interval");
                current.NextDiagnosticLogAt = now + AudioStatsIntervalSeconds;
            }
        }

        private void LogAudioDiagnostics(PlaybackSession current, string reason)
        {
            if (!OODConfig.DecoderAudioStats.Value || !current.Audio.DiagnosticsEnabled)
                return;

            PcmRingBuffer.Diagnostics stats = current.Audio.GetDiagnostics();
            AudioSource output = current.Audio.Output;
            Logger.LogInfo(string.Format(CultureInfo.InvariantCulture,
                "[VLC audio #{0}] {1} elapsed={2:F1}s preparing={3} nativePlaying={4} buffering={5} " +
                "active={6} sourcePlaying={7} virtual={8} pitch={9:F3} volume={10:F3} " +
                "queueMs={11:F1} prefetchMs={12:F1} callbacksVlc/Unity={13}/{14} " +
                "framesReceived/Written/Submitted/Requested={15}/{16}/{17}/{18} " +
                "underruns={19} underrunFrames={20} scheduledSilenceFrames={21} " +
                "inactiveWrite/ReadFrames={22}/{23} late/expired/clearedFrames={24}/{25}/{26} " +
                "ptsResets={27} maxPtsSkewMs={28:F3} readerResets={29} " +
                "activeCalls/redundant={30}/{31} flushes={32} positionCallbacks={33} fullWaits={34} " +
                "maxVlc/UnityGapMs={35:F1}/{36:F1} seeks={37}",
                current.DiagnosticId, reason, UnityEngine.Time.realtimeSinceStartup - current.DiagnosticStartedAt,
                current.Preparing, current.NativePlaying, current.IsBuffering,
                stats.Active, output != null && output.isPlaying, output != null && output.isVirtual,
                output != null ? output.pitch : 0f, output != null ? output.volume : 0f,
                stats.QueuedFrames * 1000d / AudioSampleRate, stats.PrefetchFrames * 1000d / AudioSampleRate,
                stats.WriteCallbacks, stats.ReadCallbacks,
                stats.ReceivedFrames, stats.WrittenFrames, stats.SubmittedFrames, stats.RequestedFrames,
                stats.Underruns, stats.UnderrunFrames, stats.ScheduledSilenceFrames,
                stats.InactiveWriteFrames, stats.InactiveReadFrames,
                stats.LateFrames, stats.ExpiredFrames, stats.ClearedFrames,
                stats.PtsResets, stats.MaxPtsSkewMicroseconds / 1000d, stats.ReaderResets,
                stats.ActiveCalls, stats.RedundantActiveCalls, stats.Flushes, stats.PositionCallbacks, stats.FullWaits,
                stats.MaxWriteGapMicroseconds / 1000d, stats.MaxReadGapMicroseconds / 1000d,
                current.DiagnosticSeeks));
        }

        private void OnDestroy()
        {
            if (session != null)
                LogAudioDiagnostics(session, "destroy");
            destroyed = true;
            pendingPrepare = null;
            requestedPlay = false;
            isPrepared = false;
            isPlaying = false;
            isPaused = false;
            SetPlaybackClock(0d, false);

            ResetUnityAudio(true);
            DestroyCpuTexture();
            targetTexture = null;

            PlaybackSession current = session;
            session = null;
            Task finalShutdown = shutdownTask;
            if (current != null)
            {
                current.Audio.SetActive(false);
                finalShutdown = finalShutdown.ContinueWith(
                    ignored => current.Shutdown(),
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default);
            }

            ReleaseLibVlc(finalShutdown);
        }


        private void StartPendingPrepare()
        {
            PrepareRequest request = pendingPrepare;
            pendingPrepare = null;
            if (request == null || destroyed)
            {
                return;
            }

            MediaPlayer player = null;
            Media media = null;
            ChunkedHttpMediaInput videoInput = null;
            PlaybackSession createdSession = null;
            try
            {
                EnsureLibVlc();
                if (NativeClock.Now() <= 0)
                {
                    throw new InvalidOperationException("LibVLC monotonic clock is unavailable.");
                }

                player = new MediaPlayer(libVlc);
                player.NetworkCaching = (uint)OODConfig.StreamBufferMs.Value;

                int dspBufferLength;
                int dspBufferCount;
                AudioSettings.GetDSPBufferSize(out dspBufferLength, out dspBufferCount);
                int unityOutputRate = Math.Max(1, AudioSettings.outputSampleRate);
                long unityOutputLeadMicroseconds = (long)Math.Ceiling(
                    dspBufferLength * (double)dspBufferCount * 1000000d / unityOutputRate);

                PcmRingBuffer audio = new PcmRingBuffer(
                    (int)AudioSampleRate,
                    (int)AudioChannels,
                    (int)AudioSampleRate * AudioBufferSeconds,
                    unityOutputLeadMicroseconds,
                    request.Output,
                    delegate
                    {
                        EnqueueMainThread(delegate
                        {
                            PlaybackSession failed = createdSession;
                            if (failed != null)
                            {
                                FailCurrent(failed, "LibVLC's media clock became unavailable.");
                            }
                        });
                    });
                createdSession = new PlaybackSession(this, player, audio, request.TargetTexture != null, request.IsLive);
                createdSession.VideoOutputEnabled = request.UseChunkedInput;
                createdSession.ConfigureCallbacks();

                if (request.UseChunkedInput)
                {
                    // YouTube throttles open-ended streams; bounded requests keep decoding ahead.
                    videoInput = new ChunkedHttpMediaInput(request.VideoUri.AbsoluteUri, request.Headers);
                    media = new Media(libVlc, videoInput, ":demux=avformat");
                }
                else
                {
                    // Native access handles live HTTP, playlists and adaptive HLS segments.
                    media = new Media(libVlc, request.VideoUri);
                }
                if (request.AudioUri != null &&
                    !media.AddSlave(MediaSlaveType.Audio, 4, request.AudioUri))
                {
                    throw new InvalidOperationException("LibVLC rejected the audio slave.");
                }

                player.Media = media;
                // The media owns the input callbacks, so both stay alive for the session.
                createdSession.Media = media;
                createdSession.Input = videoInput;
                media = null;
                videoInput = null;

                ConfigureUnityAudio(request.Output, audio);
                lastSampledNativeTime = -1;
                targetTexture = request.TargetTexture;
                session = createdSession;
                requestedPlay = false;
                isPrepared = false;
                isPlaying = false;
                isPaused = false;
                SetPlaybackClock(0d, false);
                prepareStartedAt = UnityEngine.Time.realtimeSinceStartup;
                createdSession.AttachEvents();
                UpdateAudioDiagnostics(createdSession);
                audio.SetActive(request.UseChunkedInput);

                if (!player.Play())
                {
                    throw new InvalidOperationException("LibVLC refused playback.");
                }
            }
            catch
            {
                if (media != null)
                {
                    media.Dispose();
                }

                if (videoInput != null)
                {
                    videoInput.Dispose();
                }

                if (createdSession != null)
                {
                    if (ReferenceEquals(session, createdSession))
                    {
                        session = null;
                    }
                    BeginShutdown(createdSession);
                }
                else if (player != null)
                {
                    player.Dispose();
                }

                isPrepared = false;
                isPlaying = false;
                isPaused = false;
                ResetUnityAudio(true);
                targetTexture = null;
                RaiseError("VLC decoder could not initialize playback.");
            }
        }

        /// <summary>
        ///     Acquires the process-wide LibVLC instance. Creating one costs roughly 25 ms of module
        ///     loading on the Unity thread, so players share a single instance instead of stalling
        ///     the game every time a screen starts a video.
        /// </summary>
        private void EnsureLibVlc()
        {
            if (libVlc != null)
            {
                return;
            }

            lock (CoreLock)
            {
                if (!coreInitialized)
                {
                    string assemblyDirectory = Path.GetDirectoryName(typeof(YoutubeDecoder).Assembly.Location);
                    if (string.IsNullOrEmpty(assemblyDirectory))
                    {
                        throw new InvalidOperationException("Decoder assembly directory is unavailable.");
                    }

                    string nativeDirectory = Path.Combine(assemblyDirectory, "libvlc", "win-x64");
                    if (!Directory.Exists(nativeDirectory))
                    {
                        throw new DirectoryNotFoundException(nativeDirectory);
                    }

                    Core.Initialize(nativeDirectory);
                    coreInitialized = true;
                }

                if (sharedLibVlc == null)
                {
                    // Debug logging is deliberately disabled: native messages can include signed
                    // stream URLs.
                    // Speex preserves fractional frames; the default fallback truncates each block.
                    sharedLibVlc = new LibVLC(
                        "--no-video-title-show", "--no-sub-autodetect-file",
                        "--audio-resampler=speex");
                }

                sharedLibVlcUsers++;
                libVlc = sharedLibVlc;
            }
        }

        /// <summary>
        ///     Releases this decoder's claim on the shared instance, disposing it off the Unity
        ///     thread once the last player is gone.
        /// </summary>
        private void ReleaseLibVlc(Task after)
        {
            if (libVlc == null) return;
            libVlc = null;

            lock (CoreLock)
            {
                if (--sharedLibVlcUsers > 0) return;

                LibVLC nativeLibrary = sharedLibVlc;
                sharedLibVlc = null;
                if (nativeLibrary == null) return;

                after.ContinueWith(
                    ignored => nativeLibrary.Dispose(),
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default);
            }
        }

        private void ConfigureUnityAudio(AudioSource output, PcmRingBuffer audio)
        {
            if (ownsAudioLoopSetting && audioOutput != output)
            {
                ResetUnityAudio(true);
            }

            audioOutput = output;
            if (!ownsAudioLoopSetting)
            {
                savedAudioLoopSetting = output.loop;
                ownsAudioLoopSetting = true;
            }

            output.Stop();
            AudioClip clip = AudioClip.Create(
                "OdinOnDemand VLC PCM",
                (int)AudioSampleRate * AudioBufferSeconds,
                (int)AudioChannels,
                (int)AudioSampleRate,
                true,
                audio.Read,
                audio.SetReadPosition);
            clip.hideFlags = HideFlags.HideAndDontSave;
            streamingClip = clip;
            output.clip = clip;

            // Match the PCM queue's window so its read cursor can account for Unity's prefetch.
            // Decoder looping remains controlled independently by IsLooping.
            output.loop = true;
        }

        private void ResetUnityAudio(bool restoreLoopSetting)
        {
            AudioSource output = audioOutput;
            AudioClip clip = streamingClip;
            streamingClip = null;

            if (output != null)
            {
                output.Stop();
                if (output.clip == clip)
                {
                    output.clip = null;
                }
                if (restoreLoopSetting && ownsAudioLoopSetting)
                {
                    output.loop = savedAudioLoopSetting;
                }
            }

            if (clip != null)
            {
                Destroy(clip);
            }

            if (restoreLoopSetting)
            {
                ownsAudioLoopSetting = false;
                audioOutput = null;
            }
        }

        private void StartUnityAudio()
        {
            if (audioOutput != null && streamingClip != null && audioOutput.clip == streamingClip &&
                !audioOutput.isPlaying)
            {
                audioOutput.Play();
            }
        }

        private void StopUnityAudio()
        {
            if (audioOutput != null)
            {
                audioOutput.Stop();
            }
        }

        private void UploadLatestFrame(PlaybackSession current)
        {
            if (!current.VideoOutputEnabled)
                return;
            VideoFrameBuffer frameBuffer;
            int slot;
            byte[] pixels;
            if (!current.TryAcquireVideoFrame(out frameBuffer, out slot, out pixels))
            {
                return;
            }

            try
            {
                if (targetTexture == null)
                {
                    return;
                }

                if (cpuTexture == null || cpuTexture.width != frameBuffer.Width ||
                    cpuTexture.height != frameBuffer.Height)
                {
                    DestroyCpuTexture();
                    cpuTexture = new Texture2D(
                        frameBuffer.Width,
                        frameBuffer.Height,
                        TextureFormat.BGRA32,
                        false);
                    cpuTexture.hideFlags = HideFlags.HideAndDontSave;
                    cpuTexture.wrapMode = TextureWrapMode.Clamp;
                    cpuTexture.filterMode = FilterMode.Bilinear;
                }

                cpuTexture.LoadRawTextureData(pixels);
                cpuTexture.Apply(false, false);

                // LibVLC's first scanline is the top of the image; Unity texture UV zero is bottom-left.
                Graphics.Blit(
                    cpuTexture,
                    targetTexture,
                    new Vector2(1f, -1f),
                    new Vector2(0f, 1f));
            }
            catch
            {
                FailCurrent(current, "Unity could not upload a decoded video frame.");
            }
            finally
            {
                frameBuffer.Release(slot);
            }
        }

        private void DestroyCpuTexture()
        {
            if (cpuTexture != null)
            {
                Destroy(cpuTexture);
                cpuTexture = null;
            }
        }

        private void HandlePlaying(PlaybackSession source)
        {
            if (!ReferenceEquals(session, source) || transitionInProgress ||
                !source.NativePlaying || source.HasEnded)
            {
                return;
            }

            // Adaptive live inputs can advertise seek/pause despite having no finite timeline.
            // Their client-local clock is not a position that RPC synchronization may seek to.
            source.Seekable = !source.IsLive && source.Player.IsSeekable &&
                (source.Input != null || source.Player.Length > 0);
            if (source.Preparing)
            {
                source.OutputGatedPause = !source.Seekable || !source.Player.CanPause;
                if (source.OutputGatedPause)
                {
                    source.VideoOutputEnabled = false;
                    CompletePrepare(source);
                    return;
                }
                if (!source.PreparePauseRequested)
                {
                    source.PreparePauseRequested = true;
                    source.Player.SetPause(true);
                }
                return;
            }

            if (requestedPlay && !source.IsBuffering)
            {
                long nativeTime = source.Player.Time;
                SetAuthoritativePlaybackClock(
                    Math.Max(playbackClockSeconds, nativeTime / 1000d),
                    true);
                isPlaying = true;
                isPaused = false;
                StartUnityAudio();
            }
        }

        private void HandlePaused(PlaybackSession source)
        {
            if (!ReferenceEquals(session, source) || transitionInProgress)
            {
                return;
            }

            if (source.OutputGatedPause && requestedPlay)
            {
                // A late native pause must not strand an output-gated resume.
                source.Player.SetPause(false);
                return;
            }
            if (source.Preparing && source.PreparePauseRequested)
            {
                source.VideoOutputEnabled = true;
                CompletePrepare(source);
                return;
            }

            if (!requestedPlay)
            {
                isPlaying = false;
                isPaused = true;
            }
        }

        private void CompletePrepare(PlaybackSession source)
        {
            long nativeTime = source.Player.Time;
            source.Preparing = false;
            source.Audio.SetActive(false);
            SetAuthoritativePlaybackClock(
                nativeTime >= 0 ? nativeTime / 1000d : playbackClockSeconds,
                false);
            StopUnityAudio();
            requestedPlay = false;
            isPrepared = true;
            isPlaying = false;
            isPaused = true;
            InvokePrepared();
        }

        private void HandleNativeTime(PlaybackSession source, long milliseconds)
        {
            if (!ReferenceEquals(session, source) || milliseconds < 0 || (isPrepared && !requestedPlay))
            {
                return;
            }

            bool running = requestedPlay && isPrepared && source.NativePlaying &&
                           !source.IsBuffering;
            SetAuthoritativePlaybackClock(milliseconds / 1000d, running);
            lastSampledNativeTime = milliseconds;
            if (running)
            {
                isPlaying = true;
                isPaused = false;
            }
        }

        private void HandleNativePosition(PlaybackSession source, float position)
        {
            if (!ReferenceEquals(session, source) || position < 0f || position > 1f)
            {
                return;
            }

            double duration = Length;
            if (duration > 0d)
            {
                HandleNativeTime(source, (long)(duration * position * 1000d));
            }
        }

        private void HandleBuffering(PlaybackSession source, float cache)
        {
            if (!ReferenceEquals(session, source) || source.Preparing || transitionInProgress)
            {
                return;
            }

            if (cache < 100f)
            {
                FreezePlaybackClock();
                isPlaying = false;
                source.Audio.SetActive(false);
                StopUnityAudio();
            }
            else if (requestedPlay && source.NativePlaying)
            {
                long nativeTime = source.Player.Time;
                SetAuthoritativePlaybackClock(
                    Math.Max(playbackClockSeconds, nativeTime / 1000d),
                    true);
                source.Audio.SetActive(true);
                isPlaying = true;
                isPaused = false;
                StartUnityAudio();
            }
        }

        private void HandleEndReached(PlaybackSession source)
        {
            if (!ReferenceEquals(session, source))
            {
                return;
            }
            if (source.Preparing)
            {
                FailCurrent(source, "The stream ended before LibVLC could prepare playback.");
                return;
            }

            requestedPlay = false;
            isPlaying = false;
            source.HasEnded = true;
            isPaused = false;
            FreezePlaybackClock();
            double duration = Length;
            if (duration > 0d)
            {
                SetAuthoritativePlaybackClock(duration, false);
            }
            source.Audio.SetActive(false);
            if (source.OutputGatedPause)
                source.VideoOutputEnabled = false;
            LogAudioDiagnostics(source, "end");
            StopUnityAudio();

            InvokeEnded();

            // Ended subscribers may replace the session to advance a playlist. Only loop if
            // this exact media remains selected after those subscribers return.
            if (isLooping && ReferenceEquals(session, source) && !destroyed)
            {
                Play();
            }
        }

        private void FailCurrent(PlaybackSession source, string message)
        {
            if (!ReferenceEquals(session, source))
            {
                return;
            }
            FailedAtSeconds = pendingSeekSeconds ?? ReadPlaybackClock();
            lastSampledNativeTime = -1;

            session = null;
            pendingPrepare = null;
            pendingSeekSeconds = null;
            requestedPlay = false;
            isPrepared = false;
            isPlaying = false;
            isPaused = false;
            SetPlaybackClock(0d, false);
            source.Audio.SetActive(false);
            ResetUnityAudio(true);
            DestroyCpuTexture();
            targetTexture = null;
            BeginShutdown(source);
            RaiseError(message);
        }

        private void StopInternal(bool restoreLoopSetting)
        {
            pendingPrepare = null;
            pendingSeekSeconds = null;
            requestedPlay = false;
            isPrepared = false;
            isPlaying = false;
            isPaused = false;
            SetPlaybackClock(0d, false);
            lastSampledNativeTime = -1;

            PlaybackSession current = session;
            session = null;
            if (current != null)
            {
                current.Audio.SetActive(false);
                BeginShutdown(current);
            }

            ResetUnityAudio(restoreLoopSetting);
            DestroyCpuTexture();
            targetTexture = null;
        }

        private double ReadPlaybackClock()
        {
            if (!playbackClockRunning)
            {
                return playbackClockSeconds;
            }

            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (playbackClockAnchor <= 0)
            {
                return playbackClockSeconds;
            }

            double value = playbackClockSeconds +
                           (now - playbackClockAnchor) /
                           (double)System.Diagnostics.Stopwatch.Frequency;
            double duration = Length;
            return duration > 0d ? Math.Min(value, duration) : value;
        }

        private void SetPlaybackClock(double seconds, bool running)
        {
            playbackClockSeconds = Math.Max(0d, seconds);
            playbackClockAnchor = System.Diagnostics.Stopwatch.GetTimestamp();
            playbackClockRunning = running;
        }

        private void SetAuthoritativePlaybackClock(double seconds, bool running)
        {
            SetPlaybackClock(seconds, running);
            lastAuthoritativeClockTick = playbackClockAnchor;
        }

        private void FreezePlaybackClock()
        {
            if (playbackClockRunning)
            {
                playbackClockSeconds = ReadPlaybackClock();
                playbackClockAnchor = System.Diagnostics.Stopwatch.GetTimestamp();
                playbackClockRunning = false;
            }
        }

        private void BeginReplay(PlaybackSession source)
        {
            source.VideoOutputEnabled = false;
            // VLC 3 keeps an ended input until Stop joins it. Serialize this with disposal
            // so a source change or destruction cannot release the player during that join.
            transitionInProgress = true;
            isPlaying = false;
            source.Audio.SetActive(false);
            StopUnityAudio();
            SetPlaybackClock(pendingSeekSeconds ?? 0d, false);
            Task stopped = shutdownTask.ContinueWith(
                ignored => source.Player.Stop(),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);
            shutdownTask = stopped;
            stopped.ContinueWith(completed =>
            {
                Exception failure = completed.Exception;
                EnqueueMainThread(delegate
                {
                    if (!ReferenceEquals(shutdownTask, stopped) || !ReferenceEquals(session, source))
                        return;
                    transitionInProgress = false;
                    if (failure != null)
                    {
                        FailCurrent(source, "LibVLC could not stop the ended stream for replay.");
                        return;
                    }
                    source.ResetAfterStop();
                    lastSampledNativeTime = -1;
                    if (requestedPlay)
                        Play();
                    else
                        isPaused = true;
                });
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }

        private void BeginShutdown(PlaybackSession oldSession)
        {
            oldSession.Audio.SetActive(false);
            oldSession.VideoOutputEnabled = false;
            LogAudioDiagnostics(oldSession, "shutdown");
            transitionInProgress = true;
            shutdownTask = shutdownTask.ContinueWith(
                ignored => oldSession.Shutdown(),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);
            shutdownTask.ContinueWith(
                ignored => EnqueueMainThread(ShutdownFinished),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);
        }

        private void ShutdownFinished()
        {
            transitionInProgress = false;
            if (pendingPrepare != null)
            {
                StartPendingPrepare();
            }
        }

        private void EnqueueMainThread(Action action)
        {
            if (!destroyed)
            {
                mainThreadActions.Enqueue(action);
            }
        }

        private void RaiseError(string message)
        {
            Action<string> handler = Error;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(message);
            }
            catch (Exception exception)
            {
                Debug.LogError("[OdinOnDemand] VLC decoder Error subscriber failed: " +
                               exception.GetType().Name);
            }
        }

        private void InvokePrepared()
        {
            Action handler = Prepared;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler();
            }
            catch (Exception exception)
            {
                Debug.LogError("[OdinOnDemand] VLC decoder Prepared subscriber failed: " +
                               exception.GetType().Name);
            }
        }

        private void InvokeEnded()
        {
            Action handler = Ended;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler();
            }
            catch (Exception exception)
            {
                Debug.LogError("[OdinOnDemand] VLC decoder Ended subscriber failed: " +
                               exception.GetType().Name);
            }
        }

        private static bool TryGetNetworkUri(string value, out Uri uri)
        {
            return Uri.TryCreate(value, UriKind.Absolute, out uri) &&
                   (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        private sealed class PrepareRequest
        {
            public PrepareRequest(Uri videoUri, Uri audioUri, AudioSource output,
                RenderTexture targetTexture, IDictionary<string, string> headers, bool useChunkedInput, bool isLive)
            {
                VideoUri = videoUri;
                AudioUri = audioUri;
                Output = output;
                TargetTexture = targetTexture;
                Headers = headers;
                UseChunkedInput = useChunkedInput;
                IsLive = isLive;
            }

            public Uri VideoUri { get; private set; }
            public Uri AudioUri { get; private set; }
            public AudioSource Output { get; private set; }
            public RenderTexture TargetTexture { get; private set; }
            public IDictionary<string, string> Headers { get; private set; }
            public bool UseChunkedInput { get; private set; }
            public bool IsLive { get; private set; }
        }

        private sealed class PlaybackSession
        {
            private static int nextDiagnosticId;
            private readonly YoutubeDecoder owner;
            private readonly object videoGate = new object();
            private readonly bool renderVideo;
            private VideoFrameBuffer videoBuffer;
            private int nativeVideoErrorRaised;
            private int nativeAudioErrorRaised;
            private bool eventsAttached;
            private long pendingNativeTime;
            private volatile float pendingPosition;
            private volatile float pendingBufferCache;
            private int nativeTimeDirty;
            private int positionDirty;
            private int bufferingDirty;

            public PlaybackSession(
                YoutubeDecoder owner,
                MediaPlayer player,
                PcmRingBuffer audio,
                bool renderVideo,
                bool isLive)
            {
                this.owner = owner;
                this.renderVideo = renderVideo;
                IsLive = isLive;
                OutputGatedPause = isLive;
                Player = player;
                Audio = audio;
                Preparing = true;
            }

            public MediaPlayer Player { get; private set; }

            /// <summary>Media handle kept alive because it owns the callback input.</summary>
            public Media Media { get; set; }

            /// <summary>Chunked reader feeding LibVLC, released after the player stops.</summary>
            public ChunkedHttpMediaInput Input { get; set; }
            public bool HasEnded { get; set; }
            public PcmRingBuffer Audio { get; private set; }
            public bool Preparing { get; set; }
            public bool PreparePauseRequested { get; set; }
            public bool IsLive { get; private set; }
            public bool Seekable { get; set; }
            public bool OutputGatedPause { get; set; }
            public volatile bool VideoOutputEnabled;

            /// <summary>Set once LibVLC negotiates a video format, proving a video track exists.</summary>
            public volatile bool VideoFormatSeen;
            public volatile bool NativePlaying;
            public volatile bool IsBuffering;
            public readonly int DiagnosticId = Interlocked.Increment(ref nextDiagnosticId);
            public float DiagnosticStartedAt;
            public float NextDiagnosticLogAt;
            public int DiagnosticSeeks;

            public void ConfigureCallbacks()
            {
                Player.SetAudioCallbacks(AudioPlay, AudioPause, AudioResume, AudioFlush, AudioDrain);
                // LibVLC 3's amem output supports S16N only, even when another format is requested.
                Player.SetAudioFormat("S16N", AudioSampleRate, AudioChannels);
                Player.SetVideoCallbacks(VideoLock, null, VideoDisplay);

                // LibVLCSharp 3.10.1 forwards the native cleanup callback directly while
                // wrapping format/lock/display callbacks. Session shutdown therefore owns
                // buffer cleanup after Stop has joined all native video threads.
                Player.SetVideoFormatCallbacks(VideoFormat, null);
            }

            public void AttachEvents()
            {
                Player.Playing += OnPlaying;
                Player.Paused += OnPaused;
                Player.TimeChanged += OnTimeChanged;
                Player.PositionChanged += OnPositionChanged;
                Player.Buffering += OnBuffering;
                Player.EndReached += OnEndReached;
                Player.EncounteredError += OnEncounteredError;
                eventsAttached = true;
            }

            public void ResetAfterStop()
            {
                HasEnded = false;
                NativePlaying = false;
                IsBuffering = false;
                Interlocked.Exchange(ref nativeTimeDirty, 0);
                Interlocked.Exchange(ref positionDirty, 0);
                Interlocked.Exchange(ref bufferingDirty, 0);
            }

            public double LengthSeconds { get; set; }


            public void DispatchTimingEvents()
            {
                if (Interlocked.Exchange(ref bufferingDirty, 0) != 0)
                {
                    owner.HandleBuffering(this, pendingBufferCache);
                }

                bool dispatchedTime = Interlocked.Exchange(ref nativeTimeDirty, 0) != 0;
                if (dispatchedTime)
                {
                    Interlocked.Exchange(ref positionDirty, 0);
                    owner.HandleNativeTime(this, Interlocked.Read(ref pendingNativeTime));
                }
                else if (Interlocked.Exchange(ref positionDirty, 0) != 0)
                {
                    owner.HandleNativePosition(this, pendingPosition);
                }
            }

            public bool TryAcquireVideoFrame(out VideoFrameBuffer buffer, out int slot, out byte[] pixels)
            {
                lock (videoGate)
                {
                    buffer = videoBuffer;
                }

                if (buffer == null)
                {
                    slot = -1;
                    pixels = null;
                    return false;
                }

                return buffer.TryAcquire(out slot, out pixels);
            }

            public void Shutdown()
            {
                Audio.SetActive(false);
                VideoOutputEnabled = false;
                try
                {
                    if (eventsAttached)
                    {
                        Player.Playing -= OnPlaying;
                        Player.Paused -= OnPaused;
                        Player.TimeChanged -= OnTimeChanged;
                        Player.PositionChanged -= OnPositionChanged;
                        Player.Buffering -= OnBuffering;
                        Player.EndReached -= OnEndReached;
                        Player.EncounteredError -= OnEncounteredError;
                        eventsAttached = false;
                    }
                    Player.Stop();
                }
                catch
                {
                    // Stop is best-effort during teardown; Dispose still releases native ownership.
                }
                finally
                {
                    try
                    {
                        Player.Dispose();
                    }
                    catch
                    {
                        // Native shutdown is already complete or failed; release frame ownership below.
                    }
                    finally
                    {
                        // The media owns the callback input, so both outlive the player and are
                        // released only once native playback has stopped.
                        if (Media != null)
                        {
                            Media.Dispose();
                            Media = null;
                        }

                        if (Input != null)
                        {
                            Input.Dispose();
                            Input = null;
                        }

                        lock (videoGate)
                        {
                            if (videoBuffer != null)
                            {
                                videoBuffer.DisposeNative();
                                videoBuffer = null;
                            }
                        }
                    }
                }
            }

            private void OnPlaying(object sender, EventArgs args)
            {
                NativePlaying = true;
                owner.EnqueueMainThread(delegate { owner.HandlePlaying(this); });
            }

            private void OnPaused(object sender, EventArgs args)
            {
                NativePlaying = false;
                owner.EnqueueMainThread(delegate { owner.HandlePaused(this); });
            }

            private void OnEndReached(object sender, EventArgs args)
            {
                NativePlaying = false;
                owner.EnqueueMainThread(delegate { owner.HandleEndReached(this); });
            }

            private void OnTimeChanged(object sender, MediaPlayerTimeChangedEventArgs args)
            {
                Interlocked.Exchange(ref pendingNativeTime, args.Time);
                Interlocked.Exchange(ref nativeTimeDirty, 1);
            }

            private void OnPositionChanged(object sender, MediaPlayerPositionChangedEventArgs args)
            {
                pendingPosition = args.Position;
                Interlocked.Exchange(ref positionDirty, 1);
            }

            private void OnBuffering(object sender, MediaPlayerBufferingEventArgs args)
            {
                pendingBufferCache = args.Cache;
                IsBuffering = args.Cache < 100f;
                Interlocked.Exchange(ref bufferingDirty, 1);
            }

            private void OnEncounteredError(object sender, EventArgs args)
            {
                NativePlaying = false;
                owner.EnqueueMainThread(delegate
                {
                    owner.FailCurrent(this, "LibVLC encountered an error while decoding the media stream.");
                });
            }

            private void AudioPlay(IntPtr data, IntPtr samples, uint count, long pts)
            {
                try
                {
                    if (count == 0 || count > int.MaxValue / 8)
                    {
                        return;
                    }
                    Audio.Write(samples, (int)count, pts);
                }
                catch
                {
                    ReportNativeAudioError();
                }
            }

            private void AudioPause(IntPtr data, long pts)
            {
                SafeFlushAudio();
            }

            private void AudioResume(IntPtr data, long pts)
            {
                SafeFlushAudio();
            }

            private void AudioFlush(IntPtr data, long pts)
            {
                SafeFlushAudio();
            }

            private void AudioDrain(IntPtr data)
            {
                try
                {
                    Audio.Drain();
                }
                catch
                {
                    ReportNativeAudioError();
                }
            }

            private uint VideoFormat(
                ref IntPtr opaque,
                IntPtr chroma,
                ref uint width,
                ref uint height,
                ref uint pitches,
                ref uint lines)
            {
                VideoFrameBuffer replacement = null;
                try
                {
                    if (width == 0 || height == 0 || width > int.MaxValue || height > int.MaxValue)
                    {
                        ReportNativeVideoError();
                        return 0;
                    }

                    replacement = new VideoFrameBuffer((int)width, (int)height);

                    // RV32 is BGRA byte order on the little-endian Windows runtime used here.
                    Marshal.WriteByte(chroma, 0, (byte)'R');
                    Marshal.WriteByte(chroma, 1, (byte)'V');
                    Marshal.WriteByte(chroma, 2, (byte)'3');
                    Marshal.WriteByte(chroma, 3, (byte)'2');
                    pitches = (uint)replacement.Pitch;
                    lines = height;
                    opaque = IntPtr.Zero;

                    lock (videoGate)
                    {
                        VideoFrameBuffer previous = videoBuffer;
                        videoBuffer = replacement;
                        replacement = null;
                        if (previous != null)
                        {
                            previous.DisposeNative();
                        }
                    }
                    VideoFormatSeen = true;
                    return 1;
                }
                catch
                {
                    if (replacement != null)
                    {
                        replacement.DisposeNative();
                    }
                    ReportNativeVideoError();
                    return 0;
                }
            }

            private IntPtr VideoLock(IntPtr opaque, IntPtr planes)
            {
                try
                {
                    VideoFrameBuffer current;
                    lock (videoGate)
                    {
                        current = videoBuffer;
                    }

                    if (current == null)
                    {
                        Marshal.WriteIntPtr(planes, IntPtr.Zero);
                        return IntPtr.Zero;
                    }

                    Marshal.WriteIntPtr(planes, current.AlignedPointer);
                    return current.AlignedPointer;
                }
                catch
                {
                    ReportNativeVideoError();
                    return IntPtr.Zero;
                }
            }

            private void VideoDisplay(IntPtr opaque, IntPtr picture)
            {
                if (!renderVideo || !VideoOutputEnabled)
                {
                    return;
                }
                try
                {
                    VideoFrameBuffer current;
                    lock (videoGate)
                    {
                        current = videoBuffer;
                    }

                    if (current != null && picture == current.AlignedPointer)
                    {
                        current.Publish();
                    }
                }
                catch
                {
                    ReportNativeVideoError();
                }
            }


            private void SafeFlushAudio()
            {
                try
                {
                    Audio.Flush();
                }
                catch
                {
                    ReportNativeAudioError();
                }
            }

            private void ReportNativeAudioError()
            {
                if (Interlocked.Exchange(ref nativeAudioErrorRaised, 1) == 0)
                {
                    owner.EnqueueMainThread(delegate
                    {
                        owner.FailCurrent(this, "LibVLC could not buffer decoded audio.");
                    });
                }
            }
            private void ReportNativeVideoError()
            {
                if (Interlocked.Exchange(ref nativeVideoErrorRaised, 1) == 0)
                {
                    owner.EnqueueMainThread(delegate
                    {
                        owner.FailCurrent(this, "LibVLC could not allocate a supported video frame.");
                    });
                }
            }
        }

        private sealed class VideoFrameBuffer
        {
            private readonly object gate = new object();
            private readonly byte[][] managedFrames;
            private readonly byte[] paddedFrame;
            private IntPtr allocation;
            private int readySlot = -1;
            private int uploadingSlot = -1;
            private bool disposed;

            public VideoFrameBuffer(int width, int height)
            {
                Width = width;
                Height = height;

                long tightPitch = checked((long)width * 4L);
                long alignedPitch = checked((tightPitch + 31L) & ~31L);
                long nativeLength = checked(alignedPitch * height);
                long tightLength = checked(tightPitch * height);
                if (nativeLength > int.MaxValue || tightLength > int.MaxValue)
                {
                    throw new ArgumentOutOfRangeException("width");
                }

                Pitch = (int)alignedPitch;
                TightPitch = (int)tightPitch;
                NativeLength = (int)nativeLength;
                TightLength = (int)tightLength;

                allocation = Marshal.AllocHGlobal(checked(NativeLength + 31));
                long address = allocation.ToInt64();
                AlignedPointer = new IntPtr((address + 31L) & ~31L);
                managedFrames = new[] { new byte[TightLength], new byte[TightLength] };
                if (Pitch != TightPitch)
                {
                    paddedFrame = new byte[NativeLength];
                }
            }

            public int Width { get; private set; }
            public int Height { get; private set; }
            public int Pitch { get; private set; }
            public int TightPitch { get; private set; }
            public int NativeLength { get; private set; }
            public int TightLength { get; private set; }
            public IntPtr AlignedPointer { get; private set; }

            public void Publish()
            {
                lock (gate)
                {
                    if (disposed)
                    {
                        return;
                    }

                    int writeSlot = uploadingSlot == 0 ? 1 : 0;
                    byte[] destination = managedFrames[writeSlot];
                    if (paddedFrame == null)
                    {
                        Marshal.Copy(AlignedPointer, destination, 0, TightLength);
                    }
                    else
                    {
                        Marshal.Copy(AlignedPointer, paddedFrame, 0, NativeLength);
                        for (int row = 0; row < Height; ++row)
                        {
                            Buffer.BlockCopy(
                                paddedFrame,
                                row * Pitch,
                                destination,
                                row * TightPitch,
                                TightPitch);
                        }
                    }
                    readySlot = writeSlot;
                }
            }

            public bool TryAcquire(out int slot, out byte[] pixels)
            {
                lock (gate)
                {
                    if (disposed || readySlot < 0)
                    {
                        slot = -1;
                        pixels = null;
                        return false;
                    }

                    slot = readySlot;
                    readySlot = -1;
                    uploadingSlot = slot;
                    pixels = managedFrames[slot];
                    return true;
                }
            }

            public void Release(int slot)
            {
                lock (gate)
                {
                    if (uploadingSlot == slot)
                    {
                        uploadingSlot = -1;
                    }
                }
            }

            public void DisposeNative()
            {
                lock (gate)
                {
                    if (disposed)
                    {
                        return;
                    }
                    disposed = true;
                    readySlot = -1;
                    IntPtr pointer = allocation;
                    allocation = IntPtr.Zero;
                    AlignedPointer = IntPtr.Zero;
                    if (pointer != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(pointer);
                    }
                }
            }
        }

        /// <summary>
        /// A bounded, back-pressured PCM queue. LibVLC PTS values are on libvlc_clock;
        /// the Unity reader schedules against that clock, its queued sample cursor, and DSP latency.
        /// </summary>
        private sealed class PcmRingBuffer
        {
            private const double MicrosecondsPerSecond = 1000000d;
            private const double TimestampToleranceFrames = 2d;
            private const double PacketDiscontinuityMicroseconds = 50000d;
            private const double ReaderLateResetMicroseconds = 250000d;
            private const double ReaderAheadResetMicroseconds = 2000000d;

            private readonly object gate = new object();
            private readonly float[] samples;
            private readonly int sampleRate;
            private readonly int channels;
            private readonly int capacityFrames;
            private readonly long outputLeadMicroseconds;
            private readonly AudioSource output;
            private readonly Action clockFailure;

            private int readFrame;
            private int writeFrame;
            private int countFrames;
            private double firstSamplePts = double.NaN;
            private double readerNextPts = double.NaN;
            private int clipReadPosition;
            private bool active;
            private int clockFailureRaised;
            private int presentationGeneration;
            private volatile bool diagnosticsEnabled;
            private Diagnostics diagnostics;
            private long lastDiagnosticWriteAt;
            private long lastDiagnosticReadAt;

            public struct Diagnostics
            {
                public bool Active;
                public int QueuedFrames, PrefetchFrames;
                public long WriteCallbacks, ReadCallbacks;
                public long ReceivedFrames, WrittenFrames, SubmittedFrames, RequestedFrames;
                public long InactiveWriteFrames, InactiveReadFrames;
                public long Underruns, UnderrunFrames, ScheduledSilenceFrames;
                public long LateFrames, ExpiredFrames, ClearedFrames;
                public long PtsResets, ReaderResets, ActiveCalls, RedundantActiveCalls;
                public long Flushes, PositionCallbacks, FullWaits;
                public double MaxPtsSkewMicroseconds;
                public long MaxWriteGapMicroseconds, MaxReadGapMicroseconds;
            }

            public bool DiagnosticsEnabled { get { return diagnosticsEnabled; } }
            public AudioSource Output { get { return output; } }

            public void SetDiagnosticsEnabled(bool enabled)
            {
                lock (gate)
                {
                    diagnostics = default(Diagnostics);
                    lastDiagnosticWriteAt = 0;
                    lastDiagnosticReadAt = 0;
                    diagnosticsEnabled = enabled;
                }
            }

            public Diagnostics GetDiagnostics()
            {
                lock (gate)
                {
                    Diagnostics result = diagnostics;
                    result.Active = active;
                    result.QueuedFrames = countFrames;
                    return result;
                }
            }

            public PcmRingBuffer(
                int sampleRate,
                int channels,
                int capacityFrames,
                long outputLeadMicroseconds,
                AudioSource output,
                Action clockFailure)
            {
                this.sampleRate = sampleRate;
                this.channels = channels;
                this.capacityFrames = capacityFrames;
                this.outputLeadMicroseconds = Math.Max(0L, outputLeadMicroseconds);
                this.output = output;
                this.clockFailure = clockFailure;
                samples = new float[checked(capacityFrames * channels)];
            }

            public void SetActive(bool value)
            {
                lock (gate)
                {
                    if (diagnosticsEnabled)
                    {
                        diagnostics.ActiveCalls++;
                        if (active == value)
                            diagnostics.RedundantActiveCalls++;
                        else
                            lastDiagnosticWriteAt = lastDiagnosticReadAt = 0;
                    }
                    active = value;
                    ClearLocked();
                    Monitor.PulseAll(gate);
                }
            }

            public void Flush()
            {
                lock (gate)
                {
                    if (diagnosticsEnabled)
                        diagnostics.Flushes++;
                    ClearLocked();
                    Monitor.PulseAll(gate);
                }
            }

            public void SetReadPosition(int position)
            {
                lock (gate)
                {
                    if (diagnosticsEnabled)
                        diagnostics.PositionCallbacks++;
                    // This is the looping carrier clip's cursor, not a seek in the media.
                    // Flush/SetActive own presentation resets; a clip wrap must stay sample-contiguous.
                    clipReadPosition = Math.Max(0, position) % capacityFrames;
                }
            }

            public unsafe void Write(IntPtr source, int frames, long pts)
            {
                if (source == IntPtr.Zero || frames <= 0)
                {
                    return;
                }

                int sourceFrameOffset = 0;
                lock (gate)
                {
                    if (diagnosticsEnabled)
                    {
                        diagnostics.WriteCallbacks++;
                        diagnostics.ReceivedFrames += frames;
                    }
                    if (!active)
                    {
                        if (diagnosticsEnabled)
                            diagnostics.InactiveWriteFrames += frames;
                        return;
                    }

                    long clockNow = NativeClock.Now();
                    if (diagnosticsEnabled && clockNow > 0)
                    {
                        if (lastDiagnosticWriteAt > 0)
                            diagnostics.MaxWriteGapMicroseconds = Math.Max(
                                diagnostics.MaxWriteGapMicroseconds, clockNow - lastDiagnosticWriteAt);
                        lastDiagnosticWriteAt = clockNow;
                    }
                    if (pts <= 0 && clockNow <= 0)
                    {
                        ReportClockFailureLocked();
                        return;
                    }
                    double packetPts = pts > 0 ? pts : clockNow + outputLeadMicroseconds;
                    bool readerUnderrun = countFrames == 0 &&
                        readerNextPts > firstSamplePts + TimestampToleranceFrames * MicrosecondsPerSecond / sampleRate;
                    if (double.IsNaN(firstSamplePts) || readerUnderrun)
                    {
                        firstSamplePts = packetPts;
                    }
                    else if (pts > 0)
                    {
                        double expectedPts = firstSamplePts +
                                             countFrames * MicrosecondsPerSecond / sampleRate;
                        if (diagnosticsEnabled)
                            diagnostics.MaxPtsSkewMicroseconds = Math.Max(
                                diagnostics.MaxPtsSkewMicroseconds, Math.Abs(packetPts - expectedPts));
                        // PTS is a scheduling clock, not an exact PCM sample index. Keep the
                        // sample-derived timeline (even when the queue is exactly drained),
                        // rather than shifting buffered audio on every rounded/jittered packet.
                        // Compare against that timeline so tolerated skew cannot grow unbounded.
                        if (Math.Abs(packetPts - expectedPts) > PacketDiscontinuityMicroseconds)
                        {
                            if (diagnosticsEnabled)
                                diagnostics.PtsResets++;
                            ClearLocked();
                            firstSamplePts = packetPts;
                        }
                    }
                    int generation = presentationGeneration;

                    while (sourceFrameOffset < frames && active)
                    {
                        while (countFrames == capacityFrames && active && generation == presentationGeneration)
                        {
                            // A virtualized/out-of-range Unity source may not request PCM.
                            // Release expired samples instead of stalling the shared native clock.
                            long now = NativeClock.Now();
                            if (now <= 0)
                            {
                                ReportClockFailureLocked();
                                break;
                            }
                            double expiredFrames = (now - firstSamplePts) * sampleRate / MicrosecondsPerSecond;
                            if (expiredFrames >= 1d)
                            {
                                int discarded = (int)Math.Min(countFrames, expiredFrames);
                                if (diagnosticsEnabled)
                                    diagnostics.ExpiredFrames += discarded;
                                DiscardLocked(discarded);
                            }
                            else
                            {
                                if (diagnosticsEnabled)
                                    diagnostics.FullWaits++;
                                Monitor.Wait(gate, 20);
                            }
                        }
                        if (!active || generation != presentationGeneration)
                        {
                            return;
                        }

                        int writableFrames = Math.Min(frames - sourceFrameOffset,
                            capacityFrames - countFrames);
                        int contiguousFrames = Math.Min(writableFrames, capacityFrames - writeFrame);
                        short* input = (short*)source + sourceFrameOffset * channels;
                        int destination = writeFrame * channels;
                        int sampleCount = contiguousFrames * channels;
                        for (int i = 0; i < sampleCount; i++)
                            samples[destination + i] = input[i] * (1f / 32768f);

                        writeFrame = (writeFrame + contiguousFrames) % capacityFrames;
                        countFrames += contiguousFrames;
                        sourceFrameOffset += contiguousFrames;
                        if (diagnosticsEnabled)
                            diagnostics.WrittenFrames += contiguousFrames;
                    }
                }
            }

            public void Drain()
            {
                lock (gate)
                {
                    int deadline = unchecked(Environment.TickCount + 5000);
                    while (active && countFrames > 0 &&
                           unchecked(deadline - Environment.TickCount) > 0)
                    {
                        Monitor.Wait(gate, 50);
                    }
                }
            }

            public void Read(float[] destination)
            {
                if (destination == null || destination.Length == 0)
                {
                    return;
                }

                int requestedFrames = destination.Length / channels;
                int outputFrame = 0;
                long now = NativeClock.Now();

                lock (gate)
                {
                    if (diagnosticsEnabled)
                    {
                        diagnostics.ReadCallbacks++;
                        diagnostics.RequestedFrames += requestedFrames;
                    }
                    int readPosition = clipReadPosition;
                    clipReadPosition = (clipReadPosition + requestedFrames) % capacityFrames;
                    if (!active)
                    {
                        if (diagnosticsEnabled)
                            diagnostics.InactiveReadFrames += requestedFrames;
                        Array.Clear(destination, 0, destination.Length);
                        return;
                    }
                    if (now <= 0)
                    {
                        ReportClockFailureLocked();
                        Array.Clear(destination, 0, destination.Length);
                        return;
                    }
                    if (diagnosticsEnabled)
                    {
                        if (lastDiagnosticReadAt > 0)
                            diagnostics.MaxReadGapMicroseconds = Math.Max(
                                diagnostics.MaxReadGapMicroseconds, now - lastDiagnosticReadAt);
                        lastDiagnosticReadAt = now;
                    }

                    // Unity 6 explicitly makes timeSamples thread-safe. PCMReaderCallback runs
                    // ahead of the mixer; scheduling against wall time alone makes audio late.
                    int queuedFrames = readPosition - output.timeSamples;
                    if (queuedFrames < 0)
                        queuedFrames += capacityFrames;
                    if (diagnosticsEnabled)
                        diagnostics.PrefetchFrames = queuedFrames;
                    double desiredStart = now + outputLeadMicroseconds +
                                          queuedFrames * MicrosecondsPerSecond / sampleRate;
                    if (double.IsNaN(readerNextPts) ||
                        readerNextPts < desiredStart - ReaderLateResetMicroseconds ||
                        readerNextPts > desiredStart + ReaderAheadResetMicroseconds)
                    {
                        if (diagnosticsEnabled && !double.IsNaN(readerNextPts))
                            diagnostics.ReaderResets++;
                        readerNextPts = desiredStart;
                    }

                    while (outputFrame < requestedFrames)
                    {
                        if (countFrames == 0)
                        {
                            int silentFrames = requestedFrames - outputFrame;
                            if (diagnosticsEnabled)
                            {
                                diagnostics.Underruns++;
                                diagnostics.UnderrunFrames += silentFrames;
                            }
                            Array.Clear(destination, outputFrame * channels, silentFrames * channels);
                            readerNextPts += silentFrames * MicrosecondsPerSecond / sampleRate;
                            outputFrame += silentFrames;
                            break;
                        }

                        double deltaFrames = (firstSamplePts - readerNextPts) * sampleRate /
                                             MicrosecondsPerSecond;
                        if (deltaFrames > TimestampToleranceFrames)
                        {
                            int silentFrames = Math.Min(requestedFrames - outputFrame,
                                Math.Max(1, (int)Math.Ceiling(deltaFrames)));
                            if (diagnosticsEnabled)
                                diagnostics.ScheduledSilenceFrames += silentFrames;
                            Array.Clear(destination, outputFrame * channels, silentFrames * channels);
                            readerNextPts += silentFrames * MicrosecondsPerSecond / sampleRate;
                            outputFrame += silentFrames;
                            continue;
                        }

                        if (deltaFrames < -TimestampToleranceFrames)
                        {
                            int staleFrames = Math.Min(countFrames,
                                Math.Max(1, (int)Math.Floor(-deltaFrames)));
                            if (diagnosticsEnabled)
                                diagnostics.LateFrames += staleFrames;
                            DiscardLocked(staleFrames);
                            continue;
                        }

                        int copyFrames = Math.Min(requestedFrames - outputFrame, countFrames);
                        CopyLocked(destination, outputFrame, copyFrames);
                        if (diagnosticsEnabled)
                            diagnostics.SubmittedFrames += copyFrames;
                        outputFrame += copyFrames;
                        readerNextPts += copyFrames * MicrosecondsPerSecond / sampleRate;
                    }

                    Monitor.PulseAll(gate);
                }

                int trailingSamples = destination.Length - requestedFrames * channels;
                if (trailingSamples > 0)
                {
                    Array.Clear(destination, requestedFrames * channels, trailingSamples);
                }
            }


            private void CopyLocked(float[] destination, int destinationFrame, int frames)
            {
                int remaining = frames;
                while (remaining > 0)
                {
                    int contiguous = Math.Min(remaining, capacityFrames - readFrame);
                    Array.Copy(
                        samples,
                        readFrame * channels,
                        destination,
                        destinationFrame * channels,
                        contiguous * channels);
                    readFrame = (readFrame + contiguous) % capacityFrames;
                    countFrames -= contiguous;
                    destinationFrame += contiguous;
                    remaining -= contiguous;
                    firstSamplePts += contiguous * MicrosecondsPerSecond / sampleRate;
                }
            }

            private void DiscardLocked(int frames)
            {
                int discarded = Math.Min(frames, countFrames);
                readFrame = (readFrame + discarded) % capacityFrames;
                countFrames -= discarded;
                firstSamplePts += discarded * MicrosecondsPerSecond / sampleRate;
                Monitor.PulseAll(gate);
            }

            private void ClearLocked()
            {
                if (diagnosticsEnabled)
                    diagnostics.ClearedFrames += countFrames;
                readFrame = 0;
                writeFrame = 0;
                countFrames = 0;
                firstSamplePts = double.NaN;
                readerNextPts = double.NaN;
                presentationGeneration = unchecked(presentationGeneration + 1);
            }

            private void ReportClockFailureLocked()
            {
                active = false;
                ClearLocked();
                Monitor.PulseAll(gate);
                if (Interlocked.Exchange(ref clockFailureRaised, 1) == 0 && clockFailure != null)
                {
                    clockFailure();
                }
            }
        }

        private static class NativeClock
        {
            private static int unavailable;

            [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl, EntryPoint = "libvlc_clock")]
            private static extern long LibVlcClock();

            public static long Now()
            {
                if (Volatile.Read(ref unavailable) != 0)
                {
                    return 0L;
                }

                try
                {
                    return LibVlcClock();
                }
                catch
                {
                    Interlocked.Exchange(ref unavailable, 1);
                    return 0L;
                }
            }
        }
    }
}
