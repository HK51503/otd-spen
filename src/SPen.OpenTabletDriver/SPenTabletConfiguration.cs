using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Tablet;
using SPen.Evdev;

namespace SPen.OpenTabletDriver
{
    /// <summary>
    /// Builds and installs the OTD tablet configuration for a phone, so each model "just works"
    /// without shipping hand-written configs.
    /// </summary>
    public static class SPenTabletConfiguration
    {
        public static TabletConfiguration Create(SPenDevice device)
        {
            return new TabletConfiguration
            {
                Name = $"{device.Manufacturer} {device.Model} S Pen (adb)",
                Specifications = new TabletSpecifications
                {
                    // Reports are rotated to landscape by the stream; see Landscape.
                    Digitizer = new DigitizerSpecifications
                    {
                        Width = (float)Math.Round(Landscape.WidthMm(device), 2),
                        Height = (float)Math.Round(Landscape.HeightMm(device), 2),
                        MaxX = Landscape.MaxX(device),
                        MaxY = Landscape.MaxY(device),
                    },
                    Pen = new PenSpecifications
                    {
                        MaxPressure = (uint)device.MaxPressure,
                        ButtonCount = 1,
                    },
                },
                DigitizerIdentifiers = new List<DeviceIdentifier>
                {
                    new()
                    {
                        VendorID = AdbSPenEndpoint.VendorId,
                        ProductID = AdbSPenEndpoint.ProductId,
                        InputReportLength = SPenPacket.Length,
                        ReportParser = typeof(SPenReportParser).FullName!,
                        // One configuration per phone model, matched on the model string.
                        DeviceStrings = new Dictionary<byte, string>
                        {
                            [AdbSPenEndpoint.ProductStringIndex] = $"^{Regex.Escape(device.Model)}$",
                        },
                    },
                },
                Attributes = new Dictionary<string, string>
                {
                    ["GeneratedBy"] = "otd-spen",
                },
            };
        }

        public static string FileName(SPenDevice device) =>
            $"SPen-{string.Concat(device.Model.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_'))}.json";

        public static string Serialize(TabletConfiguration configuration) =>
            JsonConvert.SerializeObject(configuration, Formatting.Indented, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
            });

        private const string GeneratedByAttribute = "\"GeneratedBy\": \"otd-spen\"";

        /// <summary>
        /// Writes the configuration into <paramref name="directory"/>. An existing file is kept if it was
        /// hand-edited (no GeneratedBy attribute); a generated one is refreshed when the plugin's output changes.
        /// Returns the file path.
        /// </summary>
        public static string Install(SPenDevice device, string directory)
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, FileName(device));
            string json = Serialize(Create(device));

            if (File.Exists(path))
            {
                string existing = File.ReadAllText(path);
                if (existing == json)
                {
                    Log.Debug("S Pen", $"Using existing tablet configuration '{path}'");
                    return path;
                }
                if (!existing.Contains(GeneratedByAttribute))
                {
                    Log.Write("S Pen", $"Keeping hand-edited tablet configuration '{path}'");
                    return path;
                }
            }

            File.WriteAllText(path, json);
            Log.Write("S Pen", $"Wrote tablet configuration for {device.Model} to '{path}'");
            return path;
        }

        /// <summary>
        /// OTD's user configuration directory.
        /// </summary>
        /// <remarks>
        /// When no Configurations folder exists yet, OTD resolves it to <c>$CWD/Configurations</c>, which for a
        /// daemon launched from the GUI is somewhere like <c>/</c>. In that case use <c>&lt;AppData&gt;/Configurations</c>:
        /// OTD re-resolves the path on every detect and checks that location first, so it picks the new folder up.
        /// </remarks>
        public static string? FindOtdConfigurationDirectory()
        {
            var configurationDirectory = OtdAppInfo.ConfigurationDirectory;
            if (configurationDirectory != null && Directory.Exists(configurationDirectory))
                return configurationDirectory;

            var appDataDirectory = OtdAppInfo.AppDataDirectory;
            return appDataDirectory != null ? Path.Combine(appDataDirectory, "Configurations") : configurationDirectory;
        }
    }
}
