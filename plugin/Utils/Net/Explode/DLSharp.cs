using System;
using System.Collections;
using System.Threading;
using System.IO;
using UnityEngine;
using YoutubeDLSharp;
using YoutubeDLSharp.Options;

namespace OdinOnDemand.Utils.Net.Explode
{
    public class DLSharp : MonoBehaviour
    {
        private const int DefaultTimeoutSeconds = 120;
        private YoutubeDL Ytdl { get; set; }
        private OptionSet Options { get; } = new OptionSet()
        {
            Format = "best",
            GetUrl = true
        };

        private OptionSet UpdateOptions { get; } = new OptionSet()
        {
            Update = true,
            NoPostOverwrites = true
        };

        private string _videoUrl = "";
        private readonly Progress<string> _output;

        private static readonly string YtDlpPath = Path.Combine(BepInEx.Paths.GameRootPath, "yt-dlp.exe");

        public DLSharp()
        { ;
            _output = new Progress<string>(s =>
            {
                if (s != null && s.StartsWith("http"))
                {
                    _videoUrl = s.Trim();
                }
            });
        }

        private bool CheckYtDlpExists()
        {
            return File.Exists(YtDlpPath);
        }

        public IEnumerator Setup(Action<bool> onComplete = null, int timeoutSeconds = DefaultTimeoutSeconds)
        {
            float elapsedTime = 0;
            bool setupComplete = false;

            // Check if yt-dlp exists first
            if (CheckYtDlpExists())
            {
                try
                {
                    Ytdl = new YoutubeDL
                    {
                        YoutubeDLPath = YtDlpPath
                    };
                    // Run update to ensure it's the latest version
                    var updateOperation = Ytdl.RunWithOptions(
                        "",
                        UpdateOptions,
                        ct: CancellationToken.None,
                        progress: null,
                        output: _output,
                        showArgs: false
                    );
                    setupComplete = true;
                    onComplete?.Invoke(true);
                    yield break;
                }
                catch (Exception ex)
                {
                    Jotunn.Logger.LogError($"Setup failed with existing yt-dlp: {ex.Message}");
                    // Continue to download attempt if setup with existing file fails
                }
            }

            Jotunn.Logger.LogInfo("yt-dlp.exe not found or invalid. Downloading...");
            var downloadOperation = YoutubeDLSharp.Utils.DownloadYtDlp();
            

            while (!setupComplete && elapsedTime < timeoutSeconds)
            {
                elapsedTime += Time.deltaTime;
                
                if (downloadOperation.IsCompleted)
                {
                    try
                    {
                        Ytdl = new YoutubeDL
                        {
                            YoutubeDLPath = YtDlpPath
                        };
                        setupComplete = true;
                    }
                    catch (Exception ex)
                    {
                        Jotunn.Logger.LogError($"Setup failed: {ex.Message}");
                        onComplete?.Invoke(false);
                        yield break;
                    }
                }

                yield return null;
            }

            if (!setupComplete)
            {
                Jotunn.Logger.LogError($"Setup timed out after {timeoutSeconds} seconds");
                onComplete?.Invoke(false);
                yield break;
            }

            onComplete?.Invoke(true);
        }

        public IEnumerator GetVideoUrl(string url, Action<string> onComplete, int timeoutSeconds = DefaultTimeoutSeconds)
        {
            if (Ytdl == null)
            {
                Jotunn.Logger.LogError("GetVideoUrl called before Setup");
                onComplete?.Invoke(string.Empty);
                yield break;
            }

            float elapsedTime = 0;
            _videoUrl = ""; // Reset the URL before each request
            bool operationComplete = false;

            var cts = new CancellationTokenSource();
            var operation = Ytdl.RunWithOptions(
                url,
                Options,
                ct: cts.Token,
                progress: null,
                output: _output,
                showArgs: false
            );

            while (!operationComplete && elapsedTime < timeoutSeconds)
            {
                elapsedTime += Time.deltaTime;

                if (operation.IsCompleted)
                {
                    var result = operation.Result;
                    
                    if (!result.Success)
                    {
                        Jotunn.Logger.LogError($"Failed to get video URL. Errors: {string.Join(", ", result.ErrorOutput)}");
                        onComplete?.Invoke(string.Empty);
                        yield break;
                    }

                    if (string.IsNullOrEmpty(_videoUrl))
                    {
                        Jotunn.Logger.LogError("No video URL was found in the output");
                        onComplete?.Invoke(string.Empty);
                        yield break;
                    }

                    operationComplete = true;
                }

                yield return null;
            }

            if (!operationComplete)
            {
                cts.Cancel();
                Jotunn.Logger.LogError($"GetVideoUrl operation timed out after {timeoutSeconds} seconds");
                onComplete?.Invoke(string.Empty);
                yield break;
            }

            onComplete?.Invoke(_videoUrl);
        }

        public IEnumerator GetVideoUrlWithRetry(string url, Action<string> onComplete, int maxRetries = 3, int timeoutSeconds = DefaultTimeoutSeconds)
        {
            string result = string.Empty;
            
            for (int i = 0; i < maxRetries; i++)
            {
                bool attemptComplete = false;
                
                StartCoroutine(GetVideoUrl(url, (videoUrl) =>
                {
                    result = videoUrl;
                    attemptComplete = true;
                }, timeoutSeconds));

                while (!attemptComplete)
                {
                    yield return null;
                }

                if (!string.IsNullOrEmpty(result))
                {
                    onComplete?.Invoke(result);
                    yield break;
                }

                if (i < maxRetries - 1)
                {
                    float waitTime = Mathf.Pow(2, i);
                    yield return new WaitForSeconds(waitTime);
                }
            }

            Jotunn.Logger.LogError($"Failed to get video URL after {maxRetries} attempts");
            onComplete?.Invoke(string.Empty);
        }

        public void BeginVideoUrlFetch(string url, Action<string> onComplete)
        {
            StartCoroutine(SetupAndFetch(url, onComplete));
        }

        private IEnumerator SetupAndFetch(string url, Action<string> onComplete)
        {
            bool setupSuccess = false;
            yield return StartCoroutine(Setup((success) => setupSuccess = success));

            if (!setupSuccess)
            {
                onComplete?.Invoke(string.Empty);
                yield break;
            }

            yield return StartCoroutine(GetVideoUrlWithRetry(url, onComplete));
        }
    }
}