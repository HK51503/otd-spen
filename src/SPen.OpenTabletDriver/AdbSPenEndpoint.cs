using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Devices;
using SPen.Evdev;

namespace SPen.OpenTabletDriver
{
    /// <summary>
    /// A phone's pen digitizer, presented to OpenTabletDriver as if it were a HID device.
    /// </summary>
    public sealed class AdbSPenEndpoint : IDeviceEndpoint
    {
        // Not a real USB vendor; just needs to be unique among OTD's tablet configurations.
        public const int VendorId = 0x5350;  // 'SP'
        public const int ProductId = 0x454E; // 'EN'

        public const byte ManufacturerStringIndex = 1;
        public const byte ProductStringIndex = 2;
        public const byte SerialStringIndex = 3;

        private readonly Adb _adb;
        private readonly Action<AdbSPenEndpoint>? _streamLost;

        /// <param name="streamLost">Called when an open stream ends without being disposed (cable pulled, adb restarted...).</param>
        public AdbSPenEndpoint(Adb adb, SPenDevice device, Action<AdbSPenEndpoint>? streamLost = null)
        {
            _adb = adb;
            Device = device;
            _streamLost = streamLost;
        }

        public SPenDevice Device { get; }

        /// <summary>adb bound to this phone's serial.</summary>
        public Adb Adb => _adb;

        public int VendorID => VendorId;
        public int ProductID => ProductId;
        public int InputReportLength => SPenPacket.Length;
        public int OutputReportLength => 0;
        public int FeatureReportLength => 0;

        public string Manufacturer => Device.Manufacturer;
        public string ProductName => Device.Model;
        public string FriendlyName => $"{Device.Manufacturer} {Device.Model} S Pen (adb)";
        public string SerialNumber => Device.Serial;

        public string DevicePath => $"adb://{Device.Serial}{Device.Pen.Path}";
        public bool CanOpen => true;
        public IDictionary<string, string> DeviceAttributes { get; } = new Dictionary<string, string>();

        public IDeviceEndpointStream Open() => new AdbSPenStream(_adb, Device, () => _streamLost?.Invoke(this));

        public string GetDeviceString(byte index) => index switch
        {
            ManufacturerStringIndex => Device.Manufacturer,
            ProductStringIndex => Device.Model,
            SerialStringIndex => Device.Serial,
            _ => throw new ArgumentOutOfRangeException(nameof(index), index, "No such device string.")
        };
    }

    /// <summary>
    /// Streams <c>/dev/input/eventN</c> over <c>adb exec-out</c> and yields one <see cref="SPenPacket"/> per SYN_REPORT.
    /// </summary>
    public sealed class AdbSPenStream : IDeviceEndpointStream
    {
        private readonly Process _process;
        private readonly InputEventReader _reader;
        private readonly PenStateAccumulator _accumulator = new();
        private readonly SPenDevice _device;
        private readonly Action? _lost;
        private volatile bool _disposed;

        public AdbSPenStream(Adb adb, SPenDevice device, Action? lost = null)
        {
            _device = device;
            _lost = lost;
            // stderr is discarded on the phone so error text can't end up in the binary stream.
            _process = adb.StartExecOut($"cat {device.Pen.Path} 2>/dev/null");
            _reader = new InputEventReader(_process.StandardOutput.BaseStream, device.EventSize);
            Log.Debug("S Pen", $"Streaming {device.Pen.Path} from {device.Serial} (adb pid {_process.Id})");
        }

        public byte[] Read()
        {
            while (true)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(AdbSPenStream));

                if (!_reader.TryRead(out var ev))
                {
                    if (!_disposed)
                        _lost?.Invoke();

                    // DeviceReader recognises this message as a clean disconnect.
                    throw new IOException("I/O disconnected.");
                }

                if (_accumulator.Process(ev))
                    return SPenPacket.Encode(Landscape.Rotate(_accumulator.State, _device));
            }
        }

        public void Write(byte[] buffer)
        {
        }

        public void GetFeature(byte[] buffer)
        {
        }

        public void SetFeature(byte[] buffer)
        {
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            try
            {
                if (!_process.HasExited)
                    _process.Kill();
            }
            catch (InvalidOperationException)
            {
                // Already exited.
            }
            _process.Dispose();
        }
    }
}
