using System;
using System.Collections.Generic;
using Logger = Jotunn.Logger;

namespace OdinOnDemand.Utils.Net.Explode
{
    /// <summary>
    ///     Tracks the external JavaScript runtime that yt-dlp needs for YouTube's challenge
    ///     solvers (EJS). The official yt-dlp.exe bundles the solver scripts, so only the
    ///     runtime has to be present: beside yt-dlp.exe, or on the process or registry PATH.
    /// </summary>
    internal static class ExternalJsRuntime
    {
        public const string SetupGuideUrl = "https://github.com/yt-dlp/yt-dlp/wiki/EJS";

        // Deno is the only runtime yt-dlp enables by default; the rest need --js-runtimes.
        private static readonly string[] CandidateRuntimes = { "deno", "node", "bun", "qjs" };

        private static bool _lastLoggedDetected;
        private static bool _loggedOnce;
        private static bool _loggedChallengeFailure;

        /// <summary>Name yt-dlp uses for the runtime, or null when none was found.</summary>
        public static string RuntimeName { get; private set; }

        /// <summary>Full path of the runtime executable, or null when none was found.</summary>
        public static string RuntimePath { get; private set; }

        public static bool Detected => RuntimeName != null;

        /// <summary>True once yt-dlp has reported a challenge solver failure this session.</summary>
        public static bool ChallengeFailed { get; private set; }

        /// <summary>
        ///     yt-dlp option value for a runtime it does not enable by default, or null when the
        ///     default lookup already covers it.
        /// </summary>
        public static string JsRuntimesArgument =>
            RuntimeName == null || RuntimeName == "deno" ? null : RuntimeName + ":" + RuntimePath;

        /// <summary>Re-probes the search locations and logs any change in availability.</summary>
        public static void Refresh()
        {
            var searchRoots = ExecutableSearch.Directories(BepInEx.Paths.GameRootPath);
            RuntimeName = null;
            RuntimePath = null;

            foreach (var runtime in CandidateRuntimes)
            {
                foreach (var root in searchRoots)
                {
                    var path = ExecutableSearch.Find(root, runtime);
                    if (path == null) continue;
                    RuntimeName = runtime;
                    RuntimePath = path;
                    break;
                }

                if (RuntimeName != null) break;
            }

            if (_loggedOnce && _lastLoggedDetected == Detected) return;
            _loggedOnce = true;
            _lastLoggedDetected = Detected;

            if (Detected)
            {
                Logger.LogInfo($"EJS JavaScript runtime detected: {RuntimeName} ({RuntimePath})");
                ChallengeFailed = false;
                _loggedChallengeFailure = false;
            }
            else
            {
                Logger.LogWarning(
                    "EJS not detected, full formats not available. yt-dlp needs a JavaScript runtime " +
                    "(deno, node, bun or qjs) on PATH or next to yt-dlp.exe to solve YouTube's challenges. " +
                    $"Setup guide: {SetupGuideUrl}");
            }
        }

        /// <summary>
        ///     Scans yt-dlp's error output for challenge solver failures. yt-dlp warns on its own,
        ///     but that output never reaches the BepInEx log.
        /// </summary>
        public static void InspectYtDlpOutput(IEnumerable<string> output)
        {
            if (output == null) return;

            foreach (var line in output)
            {
                if (line == null) continue;
                if (line.IndexOf("challenge solving failed", StringComparison.OrdinalIgnoreCase) < 0 &&
                    line.IndexOf("supported JavaScript runtime", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                ChallengeFailed = true;
                if (_loggedChallengeFailure) return;
                _loggedChallengeFailure = true;
                Logger.LogWarning(
                    "yt-dlp could not solve YouTube's JavaScript challenge, so full formats are not available. " +
                    (Detected
                        ? $"The detected runtime ({RuntimeName}) may be too old or unusable. "
                        : "No JavaScript runtime was found. ") +
                    $"Setup guide: {SetupGuideUrl}");
                return;
            }
        }
    }
}
