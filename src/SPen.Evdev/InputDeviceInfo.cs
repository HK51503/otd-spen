using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace SPen.Evdev
{
    public sealed class AbsAxisInfo
    {
        public AbsAxisInfo(string name, int min, int max, int fuzz, int flat, int resolution)
        {
            Name = name;
            Min = min;
            Max = max;
            Fuzz = fuzz;
            Flat = flat;
            Resolution = resolution;
        }

        public string Name { get; }
        public int Min { get; }
        public int Max { get; }
        public int Fuzz { get; }
        public int Flat { get; }

        /// <summary>Units per millimetre for X/Y axes (0 when the driver doesn't report it).</summary>
        public int Resolution { get; }

        /// <summary>Physical length in mm, or null if the driver doesn't report a resolution.</summary>
        public double? LengthMm => Resolution > 0 ? (double)(Max - Min) / Resolution : null;

        public override string ToString() =>
            $"{Name}: min={Min} max={Max} fuzz={Fuzz} flat={Flat} res={Resolution}" +
            (LengthMm is double mm ? $" ({mm:F1} mm)" : "");
    }

    /// <summary>
    /// One input device as described by <c>getevent -lp</c>.
    /// </summary>
    public sealed class InputDeviceInfo
    {
        private static readonly Regex AddDevice = new(@"^add device \d+:\s*(\S+)", RegexOptions.Compiled);
        private static readonly Regex NameLine = new(@"^\s*name:\s*""(.*)""", RegexOptions.Compiled);
        private static readonly Regex AbsLine = new(
            @"(\w+)\s*:\s*value\s+(-?\d+),\s*min\s+(-?\d+),\s*max\s+(-?\d+),\s*fuzz\s+(-?\d+),\s*flat\s+(-?\d+),\s*resolution\s+(-?\d+)",
            RegexOptions.Compiled);

        private readonly HashSet<string> _tokens = new(StringComparer.Ordinal);

        private InputDeviceInfo(string path)
        {
            Path = path;
        }

        public string Path { get; }
        public string Name { get; private set; } = "";
        public Dictionary<string, AbsAxisInfo> Abs { get; } = new(StringComparer.Ordinal);

        /// <summary>True if the device advertises a given capability label, e.g. "BTN_TOOL_PEN".</summary>
        public bool Has(string label) => _tokens.Contains(label);

        public AbsAxisInfo? GetAbs(string name) => Abs.TryGetValue(name, out var info) ? info : null;

        /// <summary>
        /// Heuristic: an absolute-position device with a pen tool and pressure.
        /// Android's getevent labels 0x140 as BTN_DIGI (an alias of BTN_TOOL_PEN).
        /// </summary>
        public bool LooksLikePen =>
            (Has("BTN_TOOL_PEN") || Has("BTN_DIGI")) && Abs.ContainsKey("ABS_X") && Abs.ContainsKey("ABS_Y") && Abs.ContainsKey("ABS_PRESSURE");

        public override string ToString() => $"{Path} \"{Name}\"";

        /// <summary>
        /// Parses the output of <c>getevent -lp</c> (labels enabled).
        /// </summary>
        public static List<InputDeviceInfo> ParseGeteventLp(string output)
        {
            var devices = new List<InputDeviceInfo>();
            InputDeviceInfo? current = null;

            foreach (var rawLine in output.Split('\n'))
            {
                string line = rawLine.TrimEnd('\r');

                var add = AddDevice.Match(line);
                if (add.Success)
                {
                    current = new InputDeviceInfo(add.Groups[1].Value);
                    devices.Add(current);
                    continue;
                }

                if (current == null)
                    continue;

                var name = NameLine.Match(line);
                if (name.Success)
                {
                    current.Name = name.Groups[1].Value;
                    continue;
                }

                var abs = AbsLine.Match(line);
                if (abs.Success)
                {
                    int Int(int group) => int.Parse(abs.Groups[group].Value, CultureInfo.InvariantCulture);
                    var axis = new AbsAxisInfo(abs.Groups[1].Value, Int(3), Int(4), Int(5), Int(6), Int(7));
                    current.Abs[axis.Name] = axis;
                    current._tokens.Add(axis.Name);
                    continue;
                }

                // Capability lists (KEY, SW, MSC, input props) wrap across lines; just collect every label.
                foreach (var token in line.Split(new[] { ' ', '\t', ':' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (token.Length > 3 && token.All(c => char.IsUpper(c) || char.IsDigit(c) || c == '_'))
                        current._tokens.Add(token);
                }
            }

            return devices;
        }
    }
}
