using System.Collections.Generic;

namespace SPen.Evdev
{
    /// <summary>
    /// Subset of linux/input-event-codes.h relevant to pen digitizers.
    /// </summary>
    public static class EvdevCodes
    {
        // Event types
        public const ushort EV_SYN = 0x00;
        public const ushort EV_KEY = 0x01;
        public const ushort EV_ABS = 0x03;
        public const ushort EV_MSC = 0x04;

        // EV_SYN codes
        public const ushort SYN_REPORT = 0;
        public const ushort SYN_DROPPED = 3;

        // EV_ABS codes
        public const ushort ABS_X = 0x00;
        public const ushort ABS_Y = 0x01;
        public const ushort ABS_PRESSURE = 0x18;
        public const ushort ABS_DISTANCE = 0x19;
        public const ushort ABS_TILT_X = 0x1a;
        public const ushort ABS_TILT_Y = 0x1b;

        // EV_KEY codes
        public const ushort BTN_TOOL_PEN = 0x140;
        public const ushort BTN_TOOL_RUBBER = 0x141;
        public const ushort BTN_TOUCH = 0x14a;
        public const ushort BTN_STYLUS = 0x14b;
        public const ushort BTN_STYLUS2 = 0x14c;

        public static readonly IReadOnlyDictionary<string, ushort> AbsByName = new Dictionary<string, ushort>
        {
            ["ABS_X"] = ABS_X,
            ["ABS_Y"] = ABS_Y,
            ["ABS_PRESSURE"] = ABS_PRESSURE,
            ["ABS_DISTANCE"] = ABS_DISTANCE,
            ["ABS_TILT_X"] = ABS_TILT_X,
            ["ABS_TILT_Y"] = ABS_TILT_Y,
        };

        public static string Describe(ushort type, ushort code)
        {
            string typeName = type switch
            {
                EV_SYN => "EV_SYN",
                EV_KEY => "EV_KEY",
                EV_ABS => "EV_ABS",
                EV_MSC => "EV_MSC",
                _ => $"EV_{type:x2}"
            };

            string? codeName = (type, code) switch
            {
                (EV_SYN, SYN_REPORT) => "SYN_REPORT",
                (EV_SYN, SYN_DROPPED) => "SYN_DROPPED",
                (EV_ABS, ABS_X) => "ABS_X",
                (EV_ABS, ABS_Y) => "ABS_Y",
                (EV_ABS, ABS_PRESSURE) => "ABS_PRESSURE",
                (EV_ABS, ABS_DISTANCE) => "ABS_DISTANCE",
                (EV_ABS, ABS_TILT_X) => "ABS_TILT_X",
                (EV_ABS, ABS_TILT_Y) => "ABS_TILT_Y",
                (EV_KEY, BTN_TOOL_PEN) => "BTN_TOOL_PEN",
                (EV_KEY, BTN_TOOL_RUBBER) => "BTN_TOOL_RUBBER",
                (EV_KEY, BTN_TOUCH) => "BTN_TOUCH",
                (EV_KEY, BTN_STYLUS) => "BTN_STYLUS",
                (EV_KEY, BTN_STYLUS2) => "BTN_STYLUS2",
                _ => null
            };

            return $"{typeName} {codeName ?? $"0x{code:x4}"}";
        }
    }
}
