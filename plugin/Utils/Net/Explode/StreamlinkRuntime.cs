using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Logger = Jotunn.Logger;

namespace OdinOnDemand.Utils.Net.Explode
{
    /// <summary>Optional live-channel URL extraction; Streamlink never launches a player.</summary>
    internal static class StreamlinkRuntime
    {
        private const int TimeoutSeconds = 45;
        private const int OutputLimit = 131072;
        private static readonly Encoding Utf8 = new UTF8Encoding(false);
        private static readonly bool IsWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;
        private static readonly bool IsWine = DetectWine();
        private static volatile Runtime _runtime;

        private sealed class Runtime
        {
            public string Path;
            public string Python;
        }

        public static bool Detected => _runtime != null;
        public static string RuntimePath => _runtime?.Path;

        /// <summary>
        ///     Live services routed through Streamlink, by registrable domain. Streamlink ships
        ///     plugins for many more, but these are the two the mod names and documents; yt-dlp
        ///     already covers the long tail of on-demand sites.
        /// </summary>
        private static readonly KeyValuePair<string, string>[] Services =
        {
            new KeyValuePair<string, string>("twitch.tv", "Twitch"),
            new KeyValuePair<string, string>("kick.com", "Kick")
        };

        /// <summary>Display name of the live service a URL belongs to, or null.</summary>
        public static string ServiceName(Uri uri)
        {
            if (uri == null || !uri.IsAbsoluteUri ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return null;

            foreach (var service in Services)
                if (uri.Host.Equals(service.Key, StringComparison.OrdinalIgnoreCase) ||
                    uri.Host.EndsWith("." + service.Key, StringComparison.OrdinalIgnoreCase))
                    return service.Value;

            return null;
        }

        public static bool IsLiveChannelUrl(Uri uri)
        {
            return ServiceName(uri) != null;
        }

        /// <summary>
        ///     Promotes a bare live-channel address such as "twitch.tv/xyz" to https. Only the
        ///     live services are promoted: they are the URLs people type by hand, while every
        ///     other form - local paths, local:// media, full URLs - is returned untouched.
        /// </summary>
        public static string NormalizeChannelUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;

            var trimmed = url.Trim();
            if (trimmed.Length == 0 || trimmed.IndexOf("://", StringComparison.Ordinal) >= 0) return url;

            var candidate = "https://" + trimmed;
            return Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && ServiceName(uri) != null
                ? candidate
                : url;
        }

        public static void Refresh()
        {
            Runtime found = null;
            foreach (var root in ExecutableSearch.Directories(
                         Path.GetDirectoryName(typeof(StreamlinkRuntime).Assembly.Location),
                         BepInEx.Paths.GameRootPath))
            {
                var path = ExecutableSearch.Find(root, "streamlink");
                if (path == null) continue;
                found = new Runtime { Path = path };
                break;
            }

            // Windows PATH is not the host's PATH. Probe host paths through Wine's mapping API.
            if (found == null && IsWine)
            {
                var python = FindHostFile("/usr/bin/python3") ?? FindHostFile("/bin/python3");
                if (python != null)
                {
                    foreach (var root in HostSearchRoots())
                    {
                        var path = FindHostFile(root.TrimEnd('/') + "/streamlink");
                        if (path == null) continue;
                        found = new Runtime { Path = path, Python = python };
                        break;
                    }
                }
            }

            var previous = _runtime;
            _runtime = found;
            if (previous?.Path == found?.Path) return;
            if (found != null)
                Logger.LogInfo("Streamlink detected: " + found.Path +
                               (found.Python == null ? "" : " (Linux host via Wine start /unix)"));
        }

        public static async Task<string> ResolveAsync(string url, int maxHeight, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var service = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? ServiceName(uri) : null;
            if (service == null || !string.IsNullOrEmpty(uri.UserInfo) ||
                url.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
                throw new ArgumentException("Streamlink requires an HTTP(S) Twitch or Kick URL without credentials.",
                    nameof(url));
            if (maxHeight <= 0) throw new ArgumentOutOfRangeException(nameof(maxHeight));

            Refresh();
            var runtime = _runtime;
            if (runtime == null)
                throw new InvalidOperationException(service + " needs Streamlink. Install streamlink.exe on Windows " +
                    "PATH or beside OdinOnDemand.dll. Under Wine/Proton, install Linux Streamlink in /usr/bin, " +
                    "/usr/local/bin or ~/.local/bin and Python 3 in /usr/bin/python3.");

            // The next integer height admits e.g. 720p60 while excluding all 721p+ video.
            // Never select best-unfiltered: it would silently defeat the configured height cap.
            var arguments = new[]
            {
                "--no-config", "--loglevel", "error", "--stream-url", "--http-timeout", "15",
                "--stream-sorting-excludes", ">=" + ((long)maxHeight + 1).ToString(CultureInfo.InvariantCulture) + "p",
                "--", url, "best,audio_only"
            };
            var result = runtime.Python == null
                ? await ResolveNativeAsync(runtime.Path, arguments, token).ConfigureAwait(false)
                : await ResolveHostAsync(runtime, arguments, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (result.ExitCode != 0)
                throw new InvalidOperationException("Streamlink could not resolve " + service + " (exit " +
                    result.ExitCode + "). The channel may be offline, restricted, or have no stream within the " +
                    "height cap. Update Streamlink if " + service + " extraction has changed. " +
                    ErrorDetail(result.Error, result.Output));

            // Validate, but return the original text: Uri.AbsoluteUri can rewrite signed query escaping.
            var streamUrl = result.Output.TrimEnd('\r', '\n');
            if (streamUrl.Length == 0 || streamUrl.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0 ||
                !Uri.TryCreate(streamUrl, UriKind.Absolute, out var streamUri) ||
                (streamUri.Scheme != Uri.UriSchemeHttp && streamUri.Scheme != Uri.UriSchemeHttps) ||
                string.IsNullOrEmpty(streamUri.Host) || !string.IsNullOrEmpty(streamUri.UserInfo))
                throw new InvalidOperationException("Streamlink did not return a single HTTP(S) stream URL. " +
                                                    "Update Streamlink and check that the " + service +
                                                    " channel is live.");
            return streamUrl;
        }

        private sealed class Result
        {
            public int ExitCode;
            public string Output;
            public string Error;
        }

        private static async Task<Result> ResolveNativeAsync(string executable, string[] arguments, CancellationToken token)
        {
            using (var process = new Process { StartInfo = StartInfo(executable, arguments, true) })
            {
                Task<string> stdout = null;
                Task<string> stderr = null;
                var started = false;
                try
                {
                    started = process.Start();
                    if (!started) throw new InvalidOperationException("Could not start Streamlink: " + executable);
                    stdout = DrainAsync(process.StandardOutput);
                    stderr = DrainAsync(process.StandardError);
                    var clock = Stopwatch.StartNew();
                    while (!process.HasExited || !stdout.IsCompleted || !stderr.IsCompleted)
                    {
                        token.ThrowIfCancellationRequested();
                        if (clock.Elapsed.TotalSeconds >= TimeoutSeconds)
                            throw new TimeoutException("Streamlink timed out after 45 seconds. Check connectivity and whether the channel is live.");
                        await Task.Delay(50, token).ConfigureAwait(false);
                    }
                    return new Result
                    {
                        ExitCode = process.ExitCode,
                        Output = await stdout.ConfigureAwait(false),
                        Error = await stderr.ConfigureAwait(false)
                    };
                }
                finally
                {
                    if (started) await StopProcessAsync(process, true).ConfigureAwait(false);
                    // Closing readers also releases drains if an unexpected descendant retained a pipe.
                    if (stdout != null && !stdout.IsCompleted) process.StandardOutput.Close();
                    if (stderr != null && !stderr.IsCompleted) process.StandardError.Close();
                }
            }
        }

        private static async Task<string> DrainAsync(StreamReader reader)
        {
            var output = new StringBuilder();
            var buffer = new char[4096];
            int count;
            while ((count = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) != 0)
            {
                var remaining = OutputLimit - output.Length;
                if (remaining > 0) output.Append(buffer, 0, Math.Min(count, remaining));
            }
            return output.ToString();
        }

        private static async Task<Result> ResolveHostAsync(Runtime runtime, string[] arguments, CancellationToken token)
        {
            var directory = Path.Combine(Path.GetTempPath(), "OdinOnDemand-Streamlink-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            Process launcher = null;
            var launched = false;
            var done = Path.Combine(directory, "done");
            try
            {
                var script = Path.Combine(directory, "resolve.py");
                File.WriteAllText(script, HostBridge, Utf8);
                File.WriteAllText(Path.Combine(directory, "request.json"),
                    JsonConvert.SerializeObject(new { executable = runtime.Path, arguments }), Utf8);
                var unixScript = ToUnixPath(script);
                launcher = new Process
                {
                    StartInfo = StartInfo(Path.Combine(Environment.SystemDirectory, "start.exe"),
                        new[] { "/unix", runtime.Python, unixScript }, true)
                };
                launched = launcher.Start();
                if (!launched) throw new InvalidOperationException("Wine could not launch the Linux Streamlink bridge.");
                var stdout = DrainAsync(launcher.StandardOutput);
                var stderr = DrainAsync(launcher.StandardError);
                var clock = Stopwatch.StartNew();
                while (!File.Exists(done))
                {
                    token.ThrowIfCancellationRequested();
                    if (clock.Elapsed.TotalSeconds >= TimeoutSeconds + 5)
                        throw new TimeoutException("Linux Streamlink timed out. Check Wine start /unix support and host Python 3/Streamlink installation.");
                    if (launcher.HasExited && launcher.ExitCode != 0)
                        throw new InvalidOperationException("Wine start /unix could not launch host Python 3. " +
                            (stderr.IsCompleted ? await stderr.ConfigureAwait(false) : "Check the Wine installation."));
                    await Task.Delay(100, token).ConfigureAwait(false);
                }
                var status = File.ReadAllText(done, Utf8);
                if (status == "timeout") throw new TimeoutException("Linux Streamlink timed out after 45 seconds. Check connectivity to the streaming service.");
                if (status == "cancelled") throw new OperationCanceledException(token);
                if (!int.TryParse(status, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
                    throw new InvalidOperationException("Linux Streamlink bridge failed: " + ReadOutput(directory, "stderr"));
                return new Result { ExitCode = code, Output = ReadOutput(directory, "stdout"), Error = ReadOutput(directory, "stderr") };
            }
            finally
            {
                if (launched && !File.Exists(done))
                {
                    // Do not kill start.exe and assume it killed a Unix child: Unix processes are
                    // outside Wine's process tree. The bridge owns termination and wait/reaping.
                    File.WriteAllText(Path.Combine(directory, "cancel"), "", Utf8);
                    var grace = Stopwatch.StartNew();
                    while (!File.Exists(done) && grace.Elapsed.TotalSeconds < 5)
                        await Task.Delay(50).ConfigureAwait(false);
                }
                if (launcher != null)
                {
                    if (launched) await StopProcessAsync(launcher, false).ConfigureAwait(false);
                    launcher.Dispose();
                }
                try { Directory.Delete(directory, true); }
                catch (IOException e) { Logger.LogWarning("Could not remove Streamlink temporary files: " + e.Message); }
                catch (UnauthorizedAccessException e) { Logger.LogWarning("Could not remove Streamlink temporary files: " + e.Message); }
            }
        }

        // Python is already a host Streamlink dependency. No shell receives either URL or arguments.
        // File redirection avoids Wine's non-inherited Unix stdout handles. The bridge runs for at
        // most 45 seconds (+2 seconds kill grace), even if Valheim itself disappears.
        private const string HostBridge = @"import json, os, resource, signal, subprocess, sys, time
root = os.path.dirname(os.path.abspath(__file__))
os.umask(0o077)
os.chmod(root, 0o700)
def path(name):
    return os.path.join(root, name)
def stop(child):
    if child.poll() is None:
        try:
            os.killpg(child.pid, signal.SIGTERM)
        except ProcessLookupError:
            pass
        try:
            child.wait(timeout=2)
        except subprocess.TimeoutExpired:
            try:
                os.killpg(child.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            child.wait()
status = 'bridge-error'
child = None
try:
    with open(path('request.json'), encoding='utf-8') as request:
        data = json.load(request)
    if os.path.exists(path('cancel')):
        status = 'cancelled'
    else:
        # Prevent any misbehaving resolver from filling the disk with captured output.
        resource.setrlimit(resource.RLIMIT_FSIZE, (131072, 131072))
        with open(path('stdout'), 'wb') as out, open(path('stderr'), 'wb') as err:
            child = subprocess.Popen([data['executable']] + data['arguments'],
                stdin=subprocess.DEVNULL, stdout=out, stderr=err, start_new_session=True)
            deadline = time.monotonic() + 45
            while child.poll() is None:
                if os.path.exists(path('cancel')):
                    status = 'cancelled'
                    break
                if time.monotonic() >= deadline:
                    status = 'timeout'
                    break
                time.sleep(0.1)
            else:
                status = str(child.returncode)
            stop(child)
except Exception as error:
    with open(path('stderr'), 'a', encoding='utf-8') as err:
        err.write(str(error))
finally:
    if child is not None:
        stop(child)
    with open(path('done.tmp'), 'w', encoding='ascii') as done:
        done.write(status)
    os.replace(path('done.tmp'), path('done'))
";

        private static string ReadOutput(string directory, string name)
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path)) return "";
            using (var reader = new StreamReader(path, Utf8))
            {
                var buffer = new char[OutputLimit];
                var count = reader.ReadBlock(buffer, 0, buffer.Length);
                return new string(buffer, 0, count);
            }
        }

        private static async Task StopProcessAsync(Process process, bool tree)
        {
            if (process.HasExited) return;
            if (tree && IsWindows)
            {
                // .NET Framework lacks Kill(entireProcessTree). Include frozen Python launchers.
                using (var killer = new Process
                {
                    StartInfo = StartInfo(Path.Combine(Environment.SystemDirectory, "taskkill.exe"),
                        new[] { "/F", "/T", "/PID", process.Id.ToString(CultureInfo.InvariantCulture) }, true)
                })
                {
                    if (killer.Start())
                    {
                        var output = DrainAsync(killer.StandardOutput);
                        var error = DrainAsync(killer.StandardError);
                        var clock = Stopwatch.StartNew();
                        while (!killer.HasExited && clock.Elapsed.TotalSeconds < 3)
                            await Task.Delay(50).ConfigureAwait(false);
                        if (!killer.HasExited) killer.Kill();
                        killer.WaitForExit(2000);
                    }
                }
            }
            try { if (!process.HasExited) process.Kill(); }
            catch (InvalidOperationException) { }
            if (!process.WaitForExit(5000))
                throw new InvalidOperationException("Streamlink did not exit after termination.");
        }

        private static ProcessStartInfo StartInfo(string executable, IEnumerable<string> arguments, bool redirect)
        {
            var commandLine = new StringBuilder();
            foreach (var argument in arguments)
            {
                if (commandLine.Length != 0) commandLine.Append(' ');
                commandLine.Append(QuoteArgument(argument));
            }
            return new ProcessStartInfo
            {
                FileName = executable, Arguments = commandLine.ToString(), UseShellExecute = false,
                CreateNoWindow = true, RedirectStandardOutput = redirect, RedirectStandardError = redirect,
                StandardOutputEncoding = Utf8, StandardErrorEncoding = Utf8
            };
        }

        // Windows argv escaping (also used by Wine start); not cmd.exe or shell quoting.
        private static string QuoteArgument(string value)
        {
            var result = new StringBuilder("\"");
            var slashes = 0;
            foreach (var c in value)
            {
                if (c == '\\') { slashes++; continue; }
                result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
                result.Append(c);
                slashes = 0;
            }
            result.Append('\\', slashes * 2);
            return result.Append('"').ToString();
        }

        private static string ErrorDetail(string error, string output)
        {
            var text = string.IsNullOrWhiteSpace(error) ? output : error;
            text = (text ?? "").Trim();
            return text.Length <= 1000 ? text : text.Substring(0, 1000);
        }

        private static IEnumerable<string> HostSearchRoots()
        {
            yield return "/usr/bin";
            yield return "/usr/local/bin";
            var home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrEmpty(home) && home.StartsWith("/", StringComparison.Ordinal))
                yield return home.TrimEnd('/') + "/.local/bin";
        }

        private static string FindHostFile(string unixPath)
        {
            var pointer = wine_get_dos_file_name(Utf8.GetBytes(unixPath + "\0"));
            if (pointer == IntPtr.Zero) return null;
            try { return File.Exists(Marshal.PtrToStringUni(pointer)) ? unixPath : null; }
            finally { HeapFree(GetProcessHeap(), 0, pointer); }
        }

        private static string ToUnixPath(string windowsPath)
        {
            var pointer = wine_get_unix_file_name(windowsPath);
            if (pointer == IntPtr.Zero)
                throw new InvalidOperationException("Wine could not map Streamlink's temporary directory to a Linux path.");
            try
            {
                var length = 0;
                while (Marshal.ReadByte(pointer, length) != 0) length++;
                var bytes = new byte[length];
                Marshal.Copy(pointer, bytes, 0, length);
                return Utf8.GetString(bytes);
            }
            finally { HeapFree(GetProcessHeap(), 0, pointer); }
        }

        private static bool DetectWine()
        {
            if (!IsWindows) return false;
            try { return GetProcAddress(GetModuleHandle("ntdll.dll"), "wine_get_version") != IntPtr.Zero; }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern IntPtr wine_get_dos_file_name(byte[] name);
        [DllImport("kernel32.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern IntPtr wine_get_unix_file_name(string name);
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetProcessHeap();
        [DllImport("kernel32.dll")]
        private static extern bool HeapFree(IntPtr heap, uint flags, IntPtr memory);
    }
}
