using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;
using SPen.Evdev;

namespace SPen.OpenTabletDriver
{
    /// <summary>
    /// Talks to the companion app on the phone (android/ in this repo) through adb.
    /// </summary>
    public static class Companion
    {
        public const string Package = "dev.otdspen";
        private const string Activity = Package + "/.MainActivity";
        private const string AreaReceiver = Package + "/.AreaReceiver";
        private const string SetAreaAction = Package + ".SET_AREA";

        // FLAG_RECEIVER_FOREGROUND | FLAG_INCLUDE_STOPPED_PACKAGES: deliver promptly, even if the app never ran.
        private const string BroadcastFlags = "0x10000020";

        /// <summary>Brings the companion app to the front, if it's installed.</summary>
        public static void Launch(Adb adb)
        {
            try
            {
                var result = adb.Run("shell", $"am start -n {Activity}");
                if (result.StdOut.Contains("Error") || result.StdErr.Contains("Error"))
                    Log.Debug("S Pen", $"Companion app not started on {adb.Serial}: {(result.StdOut + result.StdErr).Trim()}");
            }
            catch (Exception ex)
            {
                Log.Debug("S Pen", $"Companion app not started: {ex.Message}");
            }
        }

        /// <summary>
        /// Sends the tablet-space area (mm, as configured in OTD) to the app, or clears it when null.
        /// </summary>
        public static void SendArea(Adb adb, TabletSpecifications specifications, Area? area)
        {
            string extras = area == null
                ? "--ez enabled false"
                : string.Join(' ',
                    "--ez enabled true",
                    Float("tabletWidth", specifications.Digitizer.Width),
                    Float("tabletHeight", specifications.Digitizer.Height),
                    Float("width", area.Width),
                    Float("height", area.Height),
                    Float("x", area.Position.X),
                    Float("y", area.Position.Y),
                    Float("rotation", area.Rotation));

            try
            {
                adb.Run("shell", $"am broadcast -a {SetAreaAction} -n {AreaReceiver} -f {BroadcastFlags} {extras}");
            }
            catch (Exception ex)
            {
                Log.Debug("S Pen", $"Failed to send area to companion app: {ex.Message}");
            }

            static string Float(string name, float value) =>
                $"--ef {name} {value.ToString("R", CultureInfo.InvariantCulture)}";
        }

        /// <summary>
        /// Sends each detected S Pen tablet's current absolute-mode area to its phone.
        /// </summary>
        /// <remarks>
        /// The driver's device trees and their output modes live in OpenTabletDriver's core assembly, which plugins
        /// can't reference, so they're read reflectively.
        /// </remarks>
        public static void SendAreas(IDriver driver)
        {
            if (driver.GetType().GetProperty("InputDevices")?.GetValue(driver) is not IEnumerable trees)
                return;

            foreach (var tree in trees)
            {
                var type = tree.GetType();
                var configuration = type.GetProperty("Properties")?.GetValue(tree) as TabletConfiguration;
                var outputMode = type.GetProperty("OutputMode")?.GetValue(tree);
                var endpoint = (type.GetProperty("InputDevices")?.GetValue(tree) as IEnumerable)?
                    .Cast<object>()
                    .Select(d => d.GetType().GetProperty("Endpoint")?.GetValue(d))
                    .OfType<AdbSPenEndpoint>()
                    .FirstOrDefault();

                if (configuration == null || endpoint == null)
                    continue;

                SendArea(endpoint.Adb, configuration.Specifications, (outputMode as AbsoluteOutputMode)?.Input);
            }
        }
    }
}
