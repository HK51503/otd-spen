using System;
using System.Threading;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Components;
using OpenTabletDriver.Plugin.DependencyInjection;

namespace SPen.OpenTabletDriver
{
    /// <summary>
    /// Entry point. OpenTabletDriver has no plugin hook for device hubs, so this tool attaches
    /// <see cref="AdbSPenHub"/> to the driver's composite hub when it's initialized.
    /// </summary>
    [PluginName("S Pen over adb")]
    public class SPenTool : ITool
    {
        // OTD disposes and re-creates every tool whenever settings are applied, which also happens
        // right after a tablet is detected. Keep one hub for the process lifetime so attaching it
        // doesn't trigger another detect -> apply settings -> re-attach cycle.
        private static readonly object HubSync = new();
        private static AdbSPenHub? _hub;

        [Resolved]
        public IDriver? Driver { set; get; }

        [Property("adb path")]
        [ToolTip("Path to the adb executable. Leave empty to find it automatically (PATH, ANDROID_HOME, default SDK locations).")]
        public string? AdbPath { set; get; }

        [BooleanProperty("Download adb", "If no adb is installed, download Google's Android platform-tools "
            + AdbProvisioner.Version + " (Android SDK terms apply) into OpenTabletDriver's data folder.")]
        [DefaultPropertyValue(true)]
        public bool DownloadAdb { set; get; } = true;

        [BooleanProperty("Open companion app", "Open the otd-spen app on the phone when it connects.")]
        [DefaultPropertyValue(true)]
        public bool LaunchCompanion { set; get; } = true;

        public bool Initialize()
        {
            lock (HubSync)
            {
                if (_hub != null)
                {
                    _hub.AdbPath = AdbPath;
                    _hub.LaunchCompanion = LaunchCompanion;
                    _hub.DownloadAdb = DownloadAdb;

                    // Tools are re-initialized after every settings apply, once output modes are set up,
                    // so this is where area changes reach the phone. adb is slow-ish; don't block the apply.
                    var driver = Driver;
                    if (driver != null)
                        ThreadPool.QueueUserWorkItem(_ => Companion.SendAreas(driver));
                    return true;
                }

                var composite = FindCompositeHub();
                if (composite == null)
                {
                    Log.Write("S Pen", "Could not access OpenTabletDriver's device hub; this OTD version isn't supported.", LogLevel.Error);
                    return false;
                }

                _hub = new AdbSPenHub(AdbPath, SPenTabletConfiguration.FindOtdConfigurationDirectory)
                {
                    LaunchCompanion = LaunchCompanion,
                    DownloadAdb = DownloadAdb,
                };
                composite.ConnectDeviceHub(_hub);
                _hub.Start();
                Log.Write("S Pen", "S Pen device hub started.");
                return true;
            }
        }

        /// <summary>
        /// <see cref="IDriver"/> doesn't expose the hub, but the concrete Driver has a public CompositeDeviceHub.
        /// </summary>
        private ICompositeDeviceHub? FindCompositeHub()
        {
            try
            {
                return Driver?.GetType().GetProperty("CompositeDeviceHub")?.GetValue(Driver) as ICompositeDeviceHub;
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
                return null;
            }
        }

        public void Dispose()
        {
            // Intentionally keeps the hub alive; see HubSync.
        }
    }
}
