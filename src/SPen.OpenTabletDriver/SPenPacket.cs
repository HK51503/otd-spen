using System;
using System.Buffers.Binary;
using SPen.Evdev;

namespace SPen.OpenTabletDriver
{
    /// <summary>
    /// The fixed-size "HID-like" report our device stream hands to OpenTabletDriver.
    /// </summary>
    /// <remarks>
    /// OTD may parse the same buffer more than once (the tablet debugger re-parses every report),
    /// so all evdev state lives in the stream and each packet is a self-contained snapshot.
    ///
    /// Layout (little-endian):
    ///   [0]      report id (<see cref="ReportId"/>)
    ///   [1]      flags, see <see cref="Flags"/>
    ///   [2..3]   X        (raw digitizer units)
    ///   [4..5]   Y        (raw digitizer units)
    ///   [6..7]   pressure
    ///   [8]      tilt X   (signed)
    ///   [9]      tilt Y   (signed)
    ///   [10]     hover distance
    ///   [11..15] reserved
    /// </remarks>
    public static class SPenPacket
    {
        public const int Length = 16;
        public const byte ReportId = 0x53; // 'S'

        [Flags]
        public enum Flags : byte
        {
            None = 0,
            InRange = 1 << 0,
            Touching = 1 << 1,
            Eraser = 1 << 2,
            Stylus = 1 << 3,
            Stylus2 = 1 << 4,
        }

        public static byte[] Encode(in PenState state)
        {
            var data = new byte[Length];
            var flags = Flags.None;
            if (state.InRange) flags |= Flags.InRange;
            if (state.Touching) flags |= Flags.Touching;
            if (state.Eraser) flags |= Flags.Eraser;
            if (state.Buttons.HasFlag(PenButtons.Stylus)) flags |= Flags.Stylus;
            if (state.Buttons.HasFlag(PenButtons.Stylus2)) flags |= Flags.Stylus2;

            data[0] = ReportId;
            data[1] = (byte)flags;
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), ClampU16(state.X));
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), ClampU16(state.Y));
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), ClampU16(state.Pressure));
            data[8] = (byte)(sbyte)Math.Clamp(state.TiltX, sbyte.MinValue, sbyte.MaxValue);
            data[9] = (byte)(sbyte)Math.Clamp(state.TiltY, sbyte.MinValue, sbyte.MaxValue);
            data[10] = (byte)Math.Clamp(state.Distance, 0, byte.MaxValue);
            return data;
        }

        private static ushort ClampU16(int value) => (ushort)Math.Clamp(value, 0, ushort.MaxValue);
    }
}
