using System;
using System.Buffers.Binary;
using System.IO;

namespace SPen.Evdev
{
    /// <summary>
    /// A single Linux <c>struct input_event</c>.
    /// </summary>
    public readonly struct InputEvent
    {
        public InputEvent(long timestampMicros, ushort type, ushort code, int value)
        {
            TimestampMicros = timestampMicros;
            Type = type;
            Code = code;
            Value = value;
        }

        /// <summary>Kernel timestamp of the event (CLOCK_MONOTONIC on Android), in microseconds.</summary>
        public long TimestampMicros { get; }
        public ushort Type { get; }
        public ushort Code { get; }
        public int Value { get; }

        public override string ToString() => $"{TimestampMicros / 1e6:F6} {EvdevCodes.Describe(Type, Code)} {Value}";
    }

    /// <summary>
    /// Reads binary <c>struct input_event</c> records from a raw stream,
    /// e.g. the stdout of <c>adb exec-out cat /dev/input/eventN</c>.
    /// </summary>
    /// <remarks>
    /// The record size depends on the ABI of the process doing the read on the phone:
    /// 24 bytes for 64-bit (two 8-byte timeval fields), 16 bytes for 32-bit.
    /// All Android ABIs are little-endian.
    /// </remarks>
    public sealed class InputEventReader
    {
        private readonly Stream _stream;
        private readonly byte[] _buffer;

        public InputEventReader(Stream stream, int eventSize)
        {
            if (eventSize != 16 && eventSize != 24)
                throw new ArgumentOutOfRangeException(nameof(eventSize), "input_event is 16 (32-bit) or 24 (64-bit) bytes.");

            _stream = stream;
            EventSize = eventSize;
            _buffer = new byte[eventSize];
        }

        public int EventSize { get; }

        /// <summary>
        /// Blocks until a full event is read. Returns false at end of stream.
        /// </summary>
        public bool TryRead(out InputEvent ev)
        {
            int read = 0;
            while (read < EventSize)
            {
                int n = _stream.Read(_buffer, read, EventSize - read);
                if (n == 0)
                {
                    ev = default;
                    return false;
                }
                read += n;
            }

            ev = Parse(_buffer, EventSize);
            return true;
        }

        public static InputEvent Parse(ReadOnlySpan<byte> data, int eventSize)
        {
            long sec, usec;
            int offset;
            if (eventSize == 24)
            {
                sec = BinaryPrimitives.ReadInt64LittleEndian(data);
                usec = BinaryPrimitives.ReadInt64LittleEndian(data.Slice(8));
                offset = 16;
            }
            else
            {
                sec = BinaryPrimitives.ReadInt32LittleEndian(data);
                usec = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(4));
                offset = 8;
            }

            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset));
            ushort code = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset + 2));
            int value = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset + 4));
            return new InputEvent(sec * 1_000_000 + usec, type, code, value);
        }
    }
}
