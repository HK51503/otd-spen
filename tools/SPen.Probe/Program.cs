using System.Diagnostics;
using SPen.Evdev;

// spen-probe: stream the raw S Pen digitizer over adb and report sample rate / jitter.
//
//   spen-probe [--serial SERIAL] [--device /dev/input/eventN] [--event-size 16|24]
//              [--list] [--raw] [--frames]

var options = Options.Parse(args);
if (options == null)
    return 2;

var adbPath = Adb.Locate();
if (adbPath == null)
{
    Console.Error.WriteLine("adb not found. Put it on PATH or set $ADB.");
    return 1;
}

var adb = new Adb(adbPath);
var serials = adb.ListDevices();
if (options.Serial != null)
{
    adb = adb.WithSerial(options.Serial);
}
else if (serials.Count == 1)
{
    adb = adb.WithSerial(serials[0]);
}
else
{
    Console.Error.WriteLine(serials.Count == 0
        ? "No adb devices in 'device' state. Check the USB cable and that USB debugging is authorized."
        : $"Multiple devices attached ({string.Join(", ", serials)}); pass --serial.");
    return 1;
}

Console.WriteLine($"adb:    {adb.Executable}");
Console.WriteLine($"device: {adb.Serial} ({adb.Shell("getprop ro.product.model").Trim()}, " +
                  $"Android {adb.Shell("getprop ro.build.version.release").Trim()})");

var devices = InputDeviceInfo.ParseGeteventLp(adb.Shell("getevent -lp"));

if (options.List)
{
    foreach (var d in devices)
    {
        Console.WriteLine();
        Console.WriteLine($"{d}{(d.LooksLikePen ? "   <-- pen" : "")}");
        foreach (var axis in d.Abs.Values)
            Console.WriteLine($"    {axis}");
    }
    return 0;
}

var pen = options.DevicePath != null
    ? devices.FirstOrDefault(d => d.Path == options.DevicePath)
    : devices.FirstOrDefault(d => d.LooksLikePen)
      // Fallback for drivers whose KEY labels we don't recognise.
      ?? devices.FirstOrDefault(d => d.Name.Contains("pen", StringComparison.OrdinalIgnoreCase) && d.GetAbs("ABS_X") != null);

if (pen == null)
{
    Console.Error.WriteLine(options.DevicePath != null
        ? $"{options.DevicePath} not found. Run with --list."
        : "No pen-like input device found (needs BTN_TOOL_PEN/BTN_DIGI + ABS_X/Y/PRESSURE). Run with --list.");
    return 1;
}

Console.WriteLine($"pen:    {pen}");
foreach (var axis in pen.Abs.Values)
    Console.WriteLine($"          {axis}");

// Make sure the shell user can actually read the node (DAC + SELinux) before streaming.
var probe = adb.Run("shell", $"timeout 0.3 cat {pen.Path} > /dev/null; echo exit=$?");
if (!probe.StdOut.Contains("exit=124") && !probe.StdOut.Contains("exit=0"))
{
    Console.Error.WriteLine($"Cannot read {pen.Path} as the adb shell user:");
    Console.Error.WriteLine((probe.StdErr + probe.StdOut).Trim());
    Console.Error.WriteLine("The raw-evdev approach won't work on this device without root; fall back to the app-based approach.");
    return 1;
}

int eventSize = options.EventSize ?? (adb.Shell("getprop ro.product.cpu.abi").Contains("64") ? 24 : 16);
Console.WriteLine($"input_event size: {eventSize} bytes");
Console.WriteLine();
Console.WriteLine(options.Raw || options.Frames
    ? "Streaming... (Ctrl+C to stop)"
    : "Hover/draw with the S Pen. One line per second. (Ctrl+C to stop)");
Console.WriteLine();

using var process = adb.StartExecOut($"cat {pen.Path} 2>/dev/null");
var stats = new Stats();
var accumulator = new PenStateAccumulator();
var seenUnhandled = new HashSet<(ushort, ushort)>();

accumulator.UnhandledEvent += ev =>
{
    // Unknown codes are how we discover Samsung-specific buttons etc.
    if (seenUnhandled.Add((ev.Type, ev.Code)))
        Console.WriteLine($"  [new unhandled event] {EvdevCodes.Describe(ev.Type, ev.Code)} value={ev.Value}");
};

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
    try { process.Kill(); } catch { /* already gone */ }
};

var readerThread = new Thread(() =>
{
    var reader = new InputEventReader(process.StandardOutput.BaseStream, eventSize);
    while (reader.TryRead(out var ev))
    {
        long arrivalTicks = Stopwatch.GetTimestamp();

        if (options.Raw)
            Console.WriteLine(ev);

        if (accumulator.Process(ev))
        {
            var state = accumulator.State;
            stats.AddFrame(state, arrivalTicks);
            if (options.Frames)
                Console.WriteLine($"{state.TimestampMicros / 1e6:F6} {state}");
        }
    }
    cts.Cancel();
}) { IsBackground = true, Name = "evdev-reader" };
readerThread.Start();

if (!options.Raw && !options.Frames)
{
    while (!cts.IsCancelled())
    {
        try { await Task.Delay(1000, cts.Token); } catch (TaskCanceledException) { }
        Console.WriteLine(stats.TakeWindowSummary(accumulator.DroppedCount));
    }
}
else
{
    try { await Task.Delay(Timeout.Infinite, cts.Token); } catch (TaskCanceledException) { }
}

readerThread.Join(500);
if (!process.HasExited)
    process.Kill();
else if (!cts.IsCancellationRequested)
    Console.Error.WriteLine($"adb exited with code {process.ExitCode}: {process.StandardError.ReadToEnd().Trim()}");

Console.WriteLine();
Console.WriteLine(stats.TotalSummary(accumulator.DroppedCount));
return 0;

sealed class Options
{
    public string? Serial;
    public string? DevicePath;
    public int? EventSize;
    public bool List;
    public bool Raw;
    public bool Frames;

    public static Options? Parse(string[] args)
    {
        var o = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
            switch (args[i])
            {
                case "--serial" or "-s": o.Serial = Next(); break;
                case "--device" or "-d": o.DevicePath = Next(); break;
                case "--event-size": o.EventSize = int.Parse(Next()); break;
                case "--list" or "-l": o.List = true; break;
                case "--raw": o.Raw = true; break;
                case "--frames": o.Frames = true; break;
                case "--help" or "-h":
                default:
                    Console.Error.WriteLine("usage: spen-probe [--serial SERIAL] [--device /dev/input/eventN] [--event-size 16|24] [--list] [--raw] [--frames]");
                    return null;
            }
        }
        return o;
    }
}

/// <summary>
/// Per-window timing statistics.
/// "kernel dt" is the interval between digitizer reports as timestamped on the phone (true sample rate).
/// "pc dt" is the interval between the same reports arriving on the PC (what OTD would see).
/// "queue" is how much later than the best-case a report arrived: (arrival - kernel timestamp) minus its window minimum.
/// </summary>
sealed class Stats
{
    // Gaps longer than this mean the pen left range; don't count them as sample intervals.
    private const long GapMicros = 50_000;

    private readonly object _lock = new();
    private readonly List<double> _kernelDt = new();
    private readonly List<double> _pcDt = new();
    private readonly List<double> _offset = new();
    private long _lastKernel = -1;
    private long _lastArrival;
    private int _frames;
    private PenState _last;
    private long _totalFrames;
    private readonly List<double> _allKernelDt = new();
    private readonly List<double> _allQueue = new();

    public void AddFrame(PenState state, long arrivalTicks)
    {
        double arrivalMicros = arrivalTicks * 1e6 / Stopwatch.Frequency;
        lock (_lock)
        {
            if (_lastKernel >= 0 && state.TimestampMicros - _lastKernel < GapMicros)
            {
                _kernelDt.Add((state.TimestampMicros - _lastKernel) / 1000.0);
                _pcDt.Add((arrivalTicks - _lastArrival) * 1000.0 / Stopwatch.Frequency);
            }
            _offset.Add(arrivalMicros - state.TimestampMicros);
            _lastKernel = state.TimestampMicros;
            _lastArrival = arrivalTicks;
            _last = state;
            _frames++;
            _totalFrames++;
        }
    }

    public string TakeWindowSummary(int dropped)
    {
        lock (_lock)
        {
            string line;
            if (_frames == 0)
            {
                line = "no reports (pen out of range?)";
            }
            else
            {
                var queue = Queue(_offset);
                line = $"{_frames,4} rpt | kernel dt p50 {P(_kernelDt, 50),5:F2} max {Max(_kernelDt),6:F2} ms " +
                       $"(~{Hz(_kernelDt),3:F0} Hz) | pc dt p50 {P(_pcDt, 50),5:F2} p99 {P(_pcDt, 99),6:F2} max {Max(_pcDt),6:F2} ms " +
                       $"| queue p99 {P(queue, 99),5:F2} ms | {_last}";
                _allKernelDt.AddRange(_kernelDt);
                _allQueue.AddRange(queue);
            }

            if (dropped > 0)
                line += $" | SYN_DROPPED x{dropped}";

            _kernelDt.Clear();
            _pcDt.Clear();
            _offset.Clear();
            _frames = 0;
            return line;
        }
    }

    public string TotalSummary(int dropped)
    {
        lock (_lock)
        {
            _allKernelDt.AddRange(_kernelDt);
            _allQueue.AddRange(Queue(_offset));
            return $"total: {_totalFrames} reports, digitizer ~{Hz(_allKernelDt):F0} Hz, " +
                   $"queue p50 {P(_allQueue, 50):F2} / p99 {P(_allQueue, 99):F2} / max {Max(_allQueue):F2} ms, " +
                   $"SYN_DROPPED x{dropped}";
        }
    }

    private static List<double> Queue(List<double> offsetsMicros)
    {
        if (offsetsMicros.Count == 0)
            return new List<double>();
        double min = offsetsMicros.Min();
        return offsetsMicros.Select(o => (o - min) / 1000.0).ToList();
    }

    private static double Hz(List<double> dtMs) => dtMs.Count == 0 ? 0 : 1000.0 / P(dtMs, 50);
    private static double Max(List<double> v) => v.Count == 0 ? 0 : v.Max();

    private static double P(List<double> v, double percentile)
    {
        if (v.Count == 0)
            return 0;
        var sorted = v.OrderBy(x => x).ToList();
        int index = (int)Math.Ceiling(percentile / 100.0 * sorted.Count) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }
}

static class CancellationExtensions
{
    public static bool IsCancelled(this CancellationTokenSource cts) => cts.IsCancellationRequested;
}
