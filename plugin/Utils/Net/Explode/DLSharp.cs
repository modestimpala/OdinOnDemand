using System;
using System.Collections;
using System.IO;
using System.Threading;
using Newtonsoft.Json.Linq;
using OdinOnDemand.Utils.Config;
using UnityEngine;
using YoutubeDLSharp;
using YoutubeDLSharp.Options;

namespace OdinOnDemand.Utils.Net.Explode
{
    public sealed class YoutubeStreams
    {
        public string VideoUrl { get; }
        public string AudioUrl { get; }

        public YoutubeStreams(string videoUrl, string audioUrl = null)
        {
            VideoUrl = videoUrl;
            AudioUrl = audioUrl;
        }
    }

    public class DLSharp : MonoBehaviour
    {
        private const int DefaultTimeoutSeconds = 120;
        private static readonly string YtDlpPath = Path.Combine(BepInEx.Paths.GameRootPath, "yt-dlp.exe");
        private YoutubeDL Ytdl { get; set; }
        private OptionSet UpdateOptions { get; } = new OptionSet
        {
            Update = true,
            NoPostOverwrites = true
        };

        internal static YoutubeStreams ParseStreams(string output)
        {
            var json = JObject.Parse(output);
            if (json["requested_formats"] is JArray formats)
            {
                string videoUrl = null;
                string audioUrl = null;
                foreach (var format in formats)
                {
                    var url = (string)format["url"];
                    var videoCodec = (string)format["vcodec"];
                    var audioCodec = (string)format["acodec"];
                    if (videoCodec != null && videoCodec != "none" && audioCodec == "none")
                        videoUrl = url;
                    else if (audioCodec != null && audioCodec != "none" && videoCodec == "none")
                        audioUrl = url;
                }
                if (IsStreamUrl(videoUrl) && IsStreamUrl(audioUrl))
                    return new YoutubeStreams(videoUrl, audioUrl);
            }
            else
            {
                var url = (string)json["url"];
                var videoCodec = (string)json["vcodec"];
                var audioCodec = (string)json["acodec"];
                if (IsStreamUrl(url) && !string.IsNullOrEmpty(videoCodec) && videoCodec != "none" &&
                    !string.IsNullOrEmpty(audioCodec) && audioCodec != "none")
                    return new YoutubeStreams(url);
            }
            throw new FormatException("yt-dlp did not return a complete video/audio stream selection.");
        }

        private static bool IsStreamUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
        }

        public IEnumerator Setup(Action<bool> onComplete = null, int timeoutSeconds = DefaultTimeoutSeconds)
        {
            if (!File.Exists(YtDlpPath))
            {
                Jotunn.Logger.LogInfo("yt-dlp.exe not found. Downloading...");
                var download = YoutubeDLSharp.Utils.DownloadYtDlp(BepInEx.Paths.GameRootPath);
                float elapsed = 0;
                while (!download.IsCompleted && elapsed < timeoutSeconds)
                {
                    elapsed += Time.deltaTime;
                    yield return null;
                }
                if (!download.IsCompleted || download.IsFaulted || download.IsCanceled || !File.Exists(YtDlpPath))
                {
                    Jotunn.Logger.LogError("yt-dlp download failed or timed out.");
                    onComplete?.Invoke(false);
                    yield break;
                }
            }

            Ytdl = new YoutubeDL { YoutubeDLPath = YtDlpPath };
            UpdateOptions.UpdateTo = OODConfig.UseNightlyYtDlp.Value ? "nightly" : null;
            // Updating is best-effort; extraction also applies the selected nightly channel.
            var update = Ytdl.RunWithOptions(Array.Empty<string>(), UpdateOptions, CancellationToken.None);
            float updateElapsed = 0;
            while (!update.IsCompleted && updateElapsed < timeoutSeconds)
            {
                updateElapsed += Time.deltaTime;
                yield return null;
            }
            if (update.IsFaulted)
                Jotunn.Logger.LogWarning($"yt-dlp update failed: {update.Exception.GetBaseException().Message}");
            onComplete?.Invoke(true);
        }

        public IEnumerator GetStreams(string url, Action<YoutubeStreams> onComplete, int timeoutSeconds = DefaultTimeoutSeconds, bool legacyPlayback = false)
        {
            if (Ytdl == null)
            {
                bool setupSuccess = false;
                yield return Setup(success => setupSuccess = success, timeoutSeconds);
                if (!setupSuccess)
                {
                    onComplete?.Invoke(null);
                    yield break;
                }
            }

            using var cts = new CancellationTokenSource();
            // Keep selection request-local: a reload can overlap an older extraction.
            var options = new OptionSet
            {
                ExtractorArgs = "youtube:player_client=default,web_embedded",
                Format = legacyPlayback ? "18" :
                    "bestvideo[ext=mp4][vcodec^=avc1][protocol=https]+bestaudio[ext=m4a][acodec^=mp4a][protocol=https]/best[ext=mp4][vcodec^=avc1][acodec^=mp4a][protocol=https]",
                DumpSingleJson = true,
                NoPlaylist = true,
                UpdateTo = OODConfig.UseNightlyYtDlp.Value ? "nightly" : null
            };
            var operation = Ytdl.RunWithOptions(new[] { url }, options, cts.Token);
            try
            {
                float elapsedTime = 0;
                while (!operation.IsCompleted && elapsedTime < timeoutSeconds)
                {
                    elapsedTime += Time.deltaTime;
                    yield return null;
                }
                if (!operation.IsCompleted)
                {
                    Jotunn.Logger.LogError($"GetStreams timed out after {timeoutSeconds} seconds");
                    onComplete?.Invoke(null);
                    yield break;
                }

                YoutubeStreams streams = null;
                try
                {
                    var result = operation.GetAwaiter().GetResult();
                    if (result.Success)
                        streams = ParseStreams(string.Join("\n", result.Data));
                    else
                        Jotunn.Logger.LogError($"Failed to get YouTube streams. Errors: {string.Join(", ", result.ErrorOutput)}");
                }
                catch (Exception ex)
                {
                    Jotunn.Logger.LogError($"Failed to resolve YouTube streams: {ex.Message}");
                }
                onComplete?.Invoke(streams);
            }
            finally
            {
                if (!operation.IsCompleted)
                    cts.Cancel();
            }
        }

        public IEnumerator GetStreamsWithRetry(string url, Action<YoutubeStreams> onComplete, int maxRetries = 3, int timeoutSeconds = DefaultTimeoutSeconds, bool legacyPlayback = false)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                YoutubeStreams result = null;
                yield return GetStreams(url, streams => result = streams, timeoutSeconds, legacyPlayback);
                if (result != null)
                {
                    onComplete?.Invoke(result);
                    yield break;
                }
                if (i < maxRetries - 1)
                    yield return new WaitForSeconds(Mathf.Pow(2, i));
            }
            Jotunn.Logger.LogError($"Failed to get YouTube streams after {maxRetries} attempts");
            onComplete?.Invoke(null);
        }
    }
}
