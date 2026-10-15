using System;
using System.Buffers.Binary;
using System.Numerics;
using OpenTabletDriver.Plugin.Tablet;

namespace SPen.OpenTabletDriver
{
    /// <summary>
    /// Decodes <see cref="SPenPacket"/>s. Referenced by name from the generated tablet configuration.
    /// </summary>
    public class SPenReportParser : IReportParser<IDeviceReport>
    {
        public IDeviceReport Parse(byte[] data)
        {
            if (data.Length < SPenPacket.Length || data[0] != SPenPacket.ReportId)
                return new DeviceReport(data);

            var flags = (SPenPacket.Flags)data[1];
            if (!flags.HasFlag(SPenPacket.Flags.InRange))
                return new OutOfRangeReport(data);

            return new SPenReport(data, flags);
        }
    }

    public struct SPenReport : ITabletReport, ITiltReport, IEraserReport, IProximityReport
    {
        internal SPenReport(byte[] data, SPenPacket.Flags flags)
        {
            Raw = data;
            Position = new Vector2(
                BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2)),
                BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4)));

            // Some firmware reports residual pressure while hovering; only trust it while touching.
            Pressure = flags.HasFlag(SPenPacket.Flags.Touching)
                ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(6))
                : 0u;

            Tilt = new Vector2((sbyte)data[8], (sbyte)data[9]);
            Eraser = flags.HasFlag(SPenPacket.Flags.Eraser);
            NearProximity = true;
            HoverDistance = data[10];
            PenButtons = new[] { flags.HasFlag(SPenPacket.Flags.Stylus) };
        }

        public byte[] Raw { set; get; }
        public Vector2 Position { set; get; }
        public uint Pressure { set; get; }
        public bool[] PenButtons { set; get; }
        public Vector2 Tilt { set; get; }
        public bool Eraser { set; get; }
        public bool NearProximity { set; get; }
        public uint HoverDistance { set; get; }
    }
}
