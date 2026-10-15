using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using OpenTabletDriver.Plugin;

namespace SPen.OpenTabletDriver
{
    /// <summary>
    /// Downloads a pinned build of Google's Android platform-tools when no adb is installed.
    /// </summary>
    /// <remarks>
    /// Pinned (not "latest") so every user runs the same adb and the archive can be checked against a
    /// known SHA-256. Hashes were taken from Google's archives after checking them against the SHA-1s in
    /// https://dl.google.com/android/repository/repository2-3.xml. To bump: update <see cref="Version"/> and
    /// the three hashes together.
    /// </remarks>
    public static class AdbProvisioner
    {
        public const string Version = "37.0.1";
        private const string BaseUrl = "https://dl.google.com/android/repository/";

        private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(5);
        private static readonly object Sync = new();
        private static DateTime _lastFailure = DateTime.MinValue;

        private static (string File, string Sha256)? Archive
        {
            get
            {
                if (OperatingSystem.IsMacOS())
                    return ($"platform-tools_r{Version}-darwin.zip", "ee39ad5967e95c2a07f04dbcbde96b1a0c916ba376096db5d2f498b7727a5d1d");
                if (OperatingSystem.IsWindows())
                    return ($"platform-tools_r{Version}-win.zip", "45f4d63113e895ebde0c90f194099a4676b6ac653bd28d54314a9e022bbc1a99");
                // Google only publishes x86-64 Linux builds.
                if (OperatingSystem.IsLinux() && RuntimeInformation.OSArchitecture == Architecture.X64)
                    return ($"platform-tools_r{Version}-linux.zip", "d230f13842f60f782a8645f9c813f8f845bf36089ea7289f28c48f17979313f1");
                return null;
            }
        }

        /// <summary>Where the pinned adb is (or would be) installed.</summary>
        public static string AdbPath => Path.Combine(InstallDirectory, "platform-tools",
            OperatingSystem.IsWindows() ? "adb.exe" : "adb");

        private static string Root => Path.Combine(
            OtdAppInfo.AppDataDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "otd-spen"),
            "SPen");

        private static string InstallDirectory => Path.Combine(Root, $"platform-tools-{Version}");

        public static bool IsInstalled => File.Exists(AdbPath);

        /// <summary>
        /// Returns the pinned adb, downloading it first if needed. Returns null on failure (and won't retry
        /// for a few minutes). Blocks while downloading (~10-16 MB).
        /// </summary>
        public static string? Ensure()
        {
            lock (Sync)
            {
                if (IsInstalled)
                    return AdbPath;

                if (DateTime.UtcNow - _lastFailure < RetryAfterFailure)
                    return null;

                if (Archive is not (string file, string sha256))
                {
                    _lastFailure = DateTime.UtcNow;
                    Log.Write("S Pen", $"No platform-tools download for {RuntimeInformation.OSDescription} " +
                        $"({RuntimeInformation.OSArchitecture}); install adb yourself and set its path in the tool.", LogLevel.Error);
                    return null;
                }

                string download = Path.Combine(Root, $"download-{Guid.NewGuid():N}.zip");
                string staging = Path.Combine(Root, $"staging-{Guid.NewGuid():N}");
                try
                {
                    Directory.CreateDirectory(Root);
                    Log.Write("S Pen", $"adb not found; downloading Android platform-tools {Version} from Google " +
                        $"(subject to the Android SDK terms: https://developer.android.com/studio/terms) to '{InstallDirectory}'...");

                    using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
                    using (var response = http.GetAsync(BaseUrl + file, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult())
                    {
                        response.EnsureSuccessStatusCode();
                        using var source = response.Content.ReadAsStream();
                        using var target = File.Create(download);
                        source.CopyTo(target);
                    }

                    string actual;
                    using (var stream = File.OpenRead(download))
                        actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
                    if (actual != sha256)
                        throw new InvalidDataException($"Checksum mismatch for {file}: expected {sha256}, got {actual}.");

                    ZipFile.ExtractToDirectory(download, staging);
                    string adb = Path.Combine(staging, "platform-tools", Path.GetFileName(AdbPath));
                    if (!File.Exists(adb))
                        throw new InvalidDataException($"{file} doesn't contain platform-tools/{Path.GetFileName(AdbPath)}.");

                    if (!OperatingSystem.IsWindows())
                    {
                        File.SetUnixFileMode(adb, File.GetUnixFileMode(adb)
                            | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
                    }

                    if (Directory.Exists(InstallDirectory))
                        Directory.Delete(InstallDirectory, recursive: true);
                    Directory.Move(staging, InstallDirectory);

                    Log.Write("S Pen", $"Installed adb {Version} to '{AdbPath}'.");
                    return AdbPath;
                }
                catch (Exception ex)
                {
                    _lastFailure = DateTime.UtcNow;
                    Log.Write("S Pen", $"Failed to download adb: {ex.Message}. Will retry in {RetryAfterFailure.TotalMinutes:F0} minutes, " +
                        "or install Android platform-tools yourself.", LogLevel.Error);
                    return null;
                }
                finally
                {
                    TryDelete(download);
                    if (Directory.Exists(staging))
                        try { Directory.Delete(staging, recursive: true); } catch { /* best effort */ }
                }
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // best effort
            }
        }
    }
}
