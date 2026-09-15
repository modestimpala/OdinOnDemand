using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using Microsoft.Win32;
using Logger = Jotunn.Logger;

namespace OdinOnDemand.Utils.Net.Explode
{
    /// <summary>
    ///     Locates optional helper executables (JavaScript runtimes, Streamlink).
    ///     Valheim inherits its environment from Steam, so a directory added to PATH after Steam
    ///     started is missing from the process PATH: installing Streamlink and restarting only the
    ///     game leaves it undetectable. The registry always holds the current machine and user
    ///     PATH, so both are searched after the process PATH.
    /// </summary>
    internal static class ExecutableSearch
    {
        private const string MachineEnvironmentKey =
            @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";

        private static readonly bool IsWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;

        /// <summary>Directories to probe, in priority order and without repeats.</summary>
        public static List<string> Directories(params string[] preferredRoots)
        {
            var roots = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (preferredRoots != null)
                foreach (var root in preferredRoots)
                    Add(roots, seen, root);

            foreach (var entry in Split(Environment.GetEnvironmentVariable("PATH")))
                Add(roots, seen, entry);

            if (IsWindows)
            {
                foreach (var entry in Split(ReadRegistryPath(Registry.LocalMachine, MachineEnvironmentKey)))
                    Add(roots, seen, entry);
                foreach (var entry in Split(ReadRegistryPath(Registry.CurrentUser, "Environment")))
                    Add(roots, seen, entry);
            }

            return roots;
        }

        /// <summary>Full path of the named executable inside a directory, or null.</summary>
        public static string Find(string directory, string name)
        {
            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(name)) return null;

            try
            {
                // Only .exe is executable on Windows; a bare name there is a script we cannot run.
                var path = Path.GetFullPath(Path.Combine(directory, IsWindows ? name + ".exe" : name));
                return File.Exists(path) ? path : null;
            }
            // Malformed PATH entries are common; skip them instead of failing detection.
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
            catch (SecurityException) { return null; }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        private static void Add(List<string> roots, HashSet<string> seen, string directory)
        {
            if (string.IsNullOrEmpty(directory)) return;
            if (seen.Add(directory.TrimEnd('\\', '/'))) roots.Add(directory);
        }

        private static IEnumerable<string> Split(string value)
        {
            if (string.IsNullOrEmpty(value)) yield break;

            foreach (var entry in value.Split(Path.PathSeparator))
            {
                var trimmed = entry.Trim().Trim('"').Trim();
                if (trimmed.Length != 0) yield return trimmed;
            }
        }

        private static string ReadRegistryPath(RegistryKey hive, string subKey)
        {
            try
            {
                // GetValue expands the REG_EXPAND_SZ placeholders the user PATH usually contains.
                using (var key = hive.OpenSubKey(subKey))
                {
                    return key?.GetValue("Path") as string;
                }
            }
            catch (SecurityException e) { return LogUnreadable(subKey, e); }
            catch (UnauthorizedAccessException e) { return LogUnreadable(subKey, e); }
            catch (IOException e) { return LogUnreadable(subKey, e); }
        }

        private static string LogUnreadable(string subKey, Exception exception)
        {
            Logger.LogDebug("Could not read PATH from the registry (" + subKey + "): " + exception.Message);
            return null;
        }
    }
}
