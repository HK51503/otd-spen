using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace SPen.Evdev
{
    /// <summary>
    /// Everything needed to stream and describe one phone's pen digitizer.
    /// </summary>
    public sealed class SPenDevice
    {
        // From `dumpsys display`: "DisplayDeviceInfo{... 1440 x 3088, modeId 3, ... 501.041 x 502.789 dpi, ..."
        private static readonly Regex DisplayPixels = new(@"(\d+) x (\d+), modeId", RegexOptions.Compiled);
        private static readonly Regex DisplayDpi = new(@"([\d.]+) x ([\d.]+) dpi", RegexOptions.Compiled);

        private SPenDevice(string serial, string manufacturer, string model, InputDeviceInfo pen, int eventSize,
            double widthMm, double heightMm)
        {
            Serial = serial;
            Manufacturer = manufacturer;
            Model = model;
            Pen = pen;
            EventSize = eventSize;
            WidthMm = widthMm;
            HeightMm = heightMm;
        }

        public string Serial { get; }
        public string Manufacturer { get; }
        public string Model { get; }
        public InputDeviceInfo Pen { get; }

        /// <summary>Size of <c>struct input_event</c> as read by <c>cat</c> on the phone (16 or 24).</summary>
        public int EventSize { get; }

        /// <summary>Physical size of the digitizer along its ABS_X axis, in mm.</summary>
        public double WidthMm { get; }

        /// <summary>Physical size of the digitizer along its ABS_Y axis, in mm.</summary>
        public double HeightMm { get; }

        public int MaxX => Pen.GetAbs("ABS_X")!.Max;
        public int MaxY => Pen.GetAbs("ABS_Y")!.Max;
        public int MaxPressure => Pen.GetAbs("ABS_PRESSURE")!.Max;

        public override string ToString() =>
            $"{Manufacturer} {Model} ({Serial}) pen={Pen.Path} {MaxX}x{MaxY} {WidthMm:F1}x{HeightMm:F1}mm";

        /// <summary>
        /// Queries a phone over adb. Returns null if it has no readable pen digitizer.
        /// </summary>
        /// <param name="adb">An <see cref="Adb"/> bound to the phone's serial.</param>
        public static SPenDevice? Discover(Adb adb)
        {
            if (adb.Serial == null)
                throw new ArgumentException("adb must be bound to a device serial.", nameof(adb));

            var devices = InputDeviceInfo.ParseGeteventLp(adb.Shell("getevent -lp"));
            var pen = devices.FirstOrDefault(d => d.LooksLikePen)
                // Fallback for drivers whose KEY labels we don't recognise.
                ?? devices.FirstOrDefault(d => d.Name.Contains("pen", StringComparison.OrdinalIgnoreCase)
                    && d.GetAbs("ABS_X") != null && d.GetAbs("ABS_Y") != null && d.GetAbs("ABS_PRESSURE") != null);

            if (pen == null || !CanRead(adb, pen.Path))
                return null;

            string manufacturer = adb.Shell("getprop ro.product.manufacturer").Trim();
            string model = adb.Shell("getprop ro.product.model").Trim();
            int eventSize = adb.Shell("getprop ro.product.cpu.abi").Contains("64") ? 24 : 16;
            var (widthMm, heightMm) = GetDigitizerSizeMm(adb, pen);

            return new SPenDevice(adb.Serial, manufacturer, model, pen, eventSize, widthMm, heightMm);
        }

        /// <summary>
        /// Checks the shell user can read the node (DAC + SELinux), without blocking on it.
        /// </summary>
        public static bool CanRead(Adb adb, string path)
        {
            var result = adb.Run("shell", $"timeout 0.3 cat {path} > /dev/null; echo exit=$?");
            return result.StdOut.Contains("exit=124") || result.StdOut.Contains("exit=0");
        }

        /// <summary>
        /// Physical digitizer size. Prefers the kernel's ABS resolution; otherwise assumes the digitizer
        /// covers the built-in display exactly (true on Galaxy devices) and uses its physical DPI.
        /// </summary>
        private static (double WidthMm, double HeightMm) GetDigitizerSizeMm(Adb adb, InputDeviceInfo pen)
        {
            var x = pen.GetAbs("ABS_X")!;
            var y = pen.GetAbs("ABS_Y")!;
            if (x.LengthMm is double xMm && y.LengthMm is double yMm)
                return (xMm, yMm);

            string dumpsys = adb.Shell("dumpsys display");
            string? info = dumpsys.Split('\n').FirstOrDefault(l => l.Contains("DisplayDeviceInfo{"));
            var pixels = info == null ? Match.Empty : DisplayPixels.Match(info);
            var dpi = info == null ? Match.Empty : DisplayDpi.Match(info);
            if (!pixels.Success || !dpi.Success)
                throw new InvalidOperationException("Could not read the display's physical size from `dumpsys display`.");

            double Num(Match m, int g) => double.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture);
            double displayW = Num(pixels, 1) / Num(dpi, 1) * 25.4;
            double displayH = Num(pixels, 2) / Num(dpi, 2) * 25.4;

            // The display reports its natural orientation; line its axes up with the digitizer's.
            bool digitizerPortrait = y.Max - y.Min >= x.Max - x.Min;
            bool displayPortrait = displayH >= displayW;
            return digitizerPortrait == displayPortrait ? (displayW, displayH) : (displayH, displayW);
        }
    }
}
