using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Devices;
using SPen.Evdev;

namespace SPen.OpenTabletDriver
{
    /// <summary>
    /// Device hub that exposes every adb-connected phone with a readable pen digitizer.
    /// </summary>
    /// <remarks>
    /// Starts out empty and only reports phones through <see cref="DevicesChanged"/> after <see cref="Start"/>.
    /// OTD 0.6.x's RootHub swaps old/new lists when a hub is first connected, so devices present at
    /// connection time would look like removals and never trigger detection.
    /// </remarks>
    public sealed class AdbSPenHub : IDeviceHub, IDisposable
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan RetryUnsupportedAfter = TimeSpan.FromSeconds(30);

        private readonly object _sync = new();
        private readonly Dictionary<string, AdbSPenEndpoint> _endpoints = new();
        private readonly Dictionary<string, DateTime> _unsupported = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Dictionary<string, string> _notReadyStates = new();
        private readonly Func<string?> _configurationDirectory;
        private Thread? _thread;
        private string? _lastAdbError;
        private string? _lastAdbPath;

        /// <param name="adbPath">adb executable; null to locate it automatically.</param>
        /// <param name="configurationDirectory">Where to install tablet configurations; returns null if unknown.</param>
        public AdbSPenHub(string? adbPath, Func<string?> configurationDirectory)
        {
            AdbPath = adbPath;
            _configurationDirectory = configurationDirectory;
        }

        public event EventHandler<DevicesChangedEventArgs>? DevicesChanged;

        /// <summary>adb executable override; null or empty to locate it automatically. Can change while running.</summary>
        public string? AdbPath { get; set; }

        /// <summary>Open the companion app when a phone connects.</summary>
        public bool LaunchCompanion { get; set; } = true;

        /// <summary>Download the pinned platform-tools when no adb is installed.</summary>
        public bool DownloadAdb { get; set; } = true;

        public IEnumerable<IDeviceEndpoint> GetDevices()
        {
            lock (_sync)
                return _endpoints.Values.ToArray();
        }

        public void Start()
        {
            if (_thread != null)
                return;

            _thread = new Thread(PollLoop) { IsBackground = true, Name = "S Pen adb hub" };
            _thread.Start();
        }

        /// <summary>Runs one poll synchronously. Exposed for testing.</summary>
        public void Poll()
        {
            var adb = ResolveAdb();
            if (adb == null)
                return;

            List<string> serials;
            try
            {
                var states = adb.ListDeviceStates();
                serials = states.Where(d => d.State == "device").Select(d => d.Serial).ToList();
                ReportNotReady(states);
                _lastAdbError = null;
            }
            catch (Exception ex)
            {
                LogOnce($"Failed to run adb: {ex.Message}");
                return;
            }

            IDeviceEndpoint[] previous;
            lock (_sync)
                previous = _endpoints.Values.ToArray<IDeviceEndpoint>();

            bool changed = false;

            foreach (var gone in previous.Cast<AdbSPenEndpoint>().Where(e => !serials.Contains(e.Device.Serial)))
            {
                Log.Write("S Pen", $"Disconnected: {gone.FriendlyName}");
                lock (_sync)
                    _endpoints.Remove(gone.Device.Serial);
                changed = true;
            }

            foreach (var serial in serials)
            {
                lock (_sync)
                {
                    if (_endpoints.ContainsKey(serial))
                        continue;
                }

                if (_unsupported.TryGetValue(serial, out var since) && DateTime.UtcNow - since < RetryUnsupportedAfter)
                    continue;

                if (TryAdd(adb.WithSerial(serial)))
                {
                    _unsupported.Remove(serial);
                    changed = true;
                }
                else
                {
                    _unsupported[serial] = DateTime.UtcNow;
                }
            }

            if (changed)
                DevicesChanged?.Invoke(this, new DevicesChangedEventArgs(previous, GetDevices()));
        }

        private bool TryAdd(Adb adb)
        {
            SPenDevice? device;
            try
            {
                device = SPenDevice.Discover(adb);
            }
            catch (Exception ex)
            {
                Log.Write("S Pen", $"Failed to query {adb.Serial}: {ex.Message}", LogLevel.Warning);
                return false;
            }

            if (device == null)
            {
                Log.Write("S Pen", $"{adb.Serial} has no pen digitizer readable over adb; ignoring it.", LogLevel.Warning);
                return false;
            }

            // The configuration must exist before OTD hears about the device, or detection won't match it.
            var directory = _configurationDirectory();
            if (directory != null)
            {
                try
                {
                    SPenTabletConfiguration.Install(device, directory);
                }
                catch (Exception ex)
                {
                    Log.Write("S Pen", $"Failed to write tablet configuration: {ex.Message}", LogLevel.Error);
                }
            }
            else
            {
                Log.Write("S Pen", "Could not find OpenTabletDriver's configuration directory. Add this as "
                    + $"Configurations/{SPenTabletConfiguration.FileName(device)}:\n"
                    + SPenTabletConfiguration.Serialize(SPenTabletConfiguration.Create(device)), LogLevel.Error);
            }

            var endpoint = new AdbSPenEndpoint(adb, device, OnStreamLost);
            lock (_sync)
                _endpoints[device.Serial] = endpoint;

            Log.Write("S Pen", $"Connected: {device}");

            if (LaunchCompanion)
                Companion.Launch(adb);

            return true;
        }

        /// <summary>
        /// Drops an endpoint whose stream died. If the phone is still (or again) connected, the next poll
        /// re-adds it, which makes OTD re-detect; otherwise a quick replug would leave OTD with a dead device.
        /// </summary>
        private void OnStreamLost(AdbSPenEndpoint endpoint)
        {
            IDeviceEndpoint[] previous;
            lock (_sync)
            {
                if (!_endpoints.TryGetValue(endpoint.Device.Serial, out var current) || current != endpoint)
                    return;
                previous = _endpoints.Values.ToArray<IDeviceEndpoint>();
                _endpoints.Remove(endpoint.Device.Serial);
            }

            Log.Write("S Pen", $"Lost stream from {endpoint.FriendlyName}; will reconnect when it's available.", LogLevel.Warning);
            DevicesChanged?.Invoke(this, new DevicesChangedEventArgs(previous, GetDevices()));
        }

        /// <summary>
        /// The tool's adb path, else an installed adb, else the pinned download.
        /// </summary>
        /// <remarks>
        /// An installed adb wins over the download: two adb versions on one machine keep killing each other's
        /// server ("adb server version doesn't match this client"), which would cut the pen stream whenever
        /// e.g. Android Studio runs its own.
        /// </remarks>
        private Adb? ResolveAdb()
        {
            string? path = !string.IsNullOrWhiteSpace(AdbPath) ? AdbPath
                : Adb.Locate()
                ?? (AdbProvisioner.IsInstalled || DownloadAdb ? AdbProvisioner.Ensure() : null);

            if (path == null)
            {
                if (!DownloadAdb)
                    LogOnce("adb not found. Install Android platform-tools, set the adb path, or enable \"Download adb\" in the S Pen tool.");
                return null;
            }

            var adb = new Adb(path);
            if (path != _lastAdbPath)
            {
                _lastAdbPath = path;
                string version;
                try { version = adb.Version(); }
                catch (Exception ex) { version = $"version unknown: {ex.Message}"; }
                Log.Write("S Pen", $"Using adb '{path}' ({version})");
            }
            return adb;
        }

        /// <summary>
        /// Tells the user, once per change, about phones adb sees but can't use yet.
        /// </summary>
        private void ReportNotReady(List<(string Serial, string State)> states)
        {
            foreach (var (serial, state) in states)
            {
                if (state == "device")
                {
                    _notReadyStates.Remove(serial);
                    continue;
                }

                if (_notReadyStates.TryGetValue(serial, out var reported) && reported == state)
                    continue;
                _notReadyStates[serial] = state;

                if (state == "unauthorized")
                {
                    Log.WriteNotify("S Pen", $"Phone {serial} is waiting for USB debugging approval. On the phone, " +
                        "tick \"Always allow from this computer\" and tap Allow. No prompt? Unplug and replug the cable.",
                        LogLevel.Warning);
                }
                else if (state.StartsWith("no permissions"))
                {
                    Log.WriteNotify("S Pen", $"No permission to access phone {serial} over USB ({state}). " +
                        "On Linux, install udev rules for Android devices (e.g. the android-udev-rules package).",
                        LogLevel.Warning);
                }
                else
                {
                    Log.Write("S Pen", $"Phone {serial} is {state}; waiting for it to come online.", LogLevel.Warning);
                }
            }

            // Forget unplugged phones so the message shows again next time.
            foreach (var serial in _notReadyStates.Keys.Except(states.Select(s => s.Serial)).ToList())
                _notReadyStates.Remove(serial);
        }

        private void LogOnce(string message)
        {
            if (message == _lastAdbError)
                return;
            _lastAdbError = message;
            Log.Write("S Pen", message, LogLevel.Error);
        }

        private void PollLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    Poll();
                }
                catch (Exception ex)
                {
                    Log.Exception(ex);
                }

                _cts.Token.WaitHandle.WaitOne(PollInterval);
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _thread?.Join(TimeSpan.FromSeconds(5));
            _cts.Dispose();
        }
    }
}
