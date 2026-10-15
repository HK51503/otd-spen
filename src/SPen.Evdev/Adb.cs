using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace SPen.Evdev
{
    /// <summary>
    /// Minimal wrapper around the adb executable.
    /// </summary>
    public sealed class Adb
    {
        public Adb(string executable, string? serial = null)
        {
            Executable = executable;
            Serial = serial;
        }

        public string Executable { get; }
        public string? Serial { get; }

        public Adb WithSerial(string serial) => new(Executable, serial);

        /// <summary>
        /// Locates adb from $ADB, PATH, $ANDROID_HOME / $ANDROID_SDK_ROOT, then the default SDK locations.
        /// </summary>
        public static string? Locate()
        {
            string exe = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "adb.exe" : "adb";
            var candidates = new List<string?>();

            candidates.Add(Environment.GetEnvironmentVariable("ADB"));

            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            candidates.AddRange(path.Split(System.IO.Path.PathSeparator).Select(dir => System.IO.Path.Combine(dir, exe)));

            foreach (var envVar in new[] { "ANDROID_HOME", "ANDROID_SDK_ROOT" })
            {
                var sdk = Environment.GetEnvironmentVariable(envVar);
                if (!string.IsNullOrEmpty(sdk))
                    candidates.Add(System.IO.Path.Combine(sdk, "platform-tools", exe));
            }

            // GUI apps on macOS (e.g. OpenTabletDriver's daemon) don't inherit the shell's PATH.
            candidates.Add("/opt/homebrew/bin/adb");
            candidates.Add("/usr/local/bin/adb");

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            candidates.Add(System.IO.Path.Combine(home, "Library", "Android", "sdk", "platform-tools", exe));
            candidates.Add(System.IO.Path.Combine(home, "Android", "Sdk", "platform-tools", exe));
            candidates.Add(System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk", "platform-tools", exe));

            return candidates.FirstOrDefault(c => !string.IsNullOrEmpty(c) && File.Exists(c));
        }

        /// <summary>Serials of devices in the "device" state.</summary>
        public List<string> ListDevices()
        {
            var output = Run("devices").StdOut;
            return output.Split('\n')
                .Skip(1)
                .Select(l => l.Trim().Split('\t'))
                .Where(parts => parts.Length == 2 && parts[1] == "device")
                .Select(parts => parts[0])
                .ToList();
        }

        public (int ExitCode, string StdOut, string StdErr) Run(params string[] args)
        {
            using var process = Process.Start(CreateStartInfo(args))!;
            var stderrTask = process.StandardError.ReadToEndAsync();
            string stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, stdout, stderrTask.Result);
        }

        /// <summary>Runs a shell command on the device and returns stdout (throws on non-zero exit).</summary>
        public string Shell(string command)
        {
            var result = Run("shell", command);
            if (result.ExitCode != 0)
                throw new InvalidOperationException($"adb shell '{command}' failed ({result.ExitCode}): {result.StdErr}{result.StdOut}".Trim());
            return result.StdOut;
        }

        /// <summary>
        /// Starts <c>adb exec-out &lt;command&gt;</c> and returns the process; read binary data from
        /// <c>process.StandardOutput.BaseStream</c>. Caller owns (and must kill/dispose) the process.
        /// </summary>
        public Process StartExecOut(string command)
        {
            var process = Process.Start(CreateStartInfo("exec-out", command))!;
            return process;
        }

        private ProcessStartInfo CreateStartInfo(params string[] args)
        {
            var psi = new ProcessStartInfo(Executable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            if (Serial != null)
            {
                psi.ArgumentList.Add("-s");
                psi.ArgumentList.Add(Serial);
            }

            foreach (var arg in args)
                psi.ArgumentList.Add(arg);

            return psi;
        }
    }
}
