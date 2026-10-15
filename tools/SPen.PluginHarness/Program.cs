using System.Diagnostics;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Devices;
using OpenTabletDriver.Plugin.Tablet;
using SPen.OpenTabletDriver;

// spen-harness: exercises the OTD plugin against a real phone without OpenTabletDriver installed.
//
//   spen-harness <config-dir> [seconds]
//
// Mirrors what OTD does: hub discovery -> configuration match (Driver.GetMatchingDevices) ->
// open endpoint -> DeviceReader loop -> parse each report twice (as the tablet debugger does).

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: spen-harness <config-dir> [seconds]");
    return 2;
}

string configDir = args[0];
double seconds = args.Length > 1 ? double.Parse(args[1]) : 10;

Log.Output += (_, message) => Console.WriteLine($"  [log {message.Level}] {message.Group}: {message.Message}");

// 1. Hub discovery
using var hub = new AdbSPenHub(adbPath: null, () => configDir);
IEnumerable<IDeviceEndpoint>? added = null;
hub.DevicesChanged += (_, e) => added = e.Additions.ToList();
hub.Poll();

var endpoint = added?.FirstOrDefault();
if (endpoint == null)
{
    Console.Error.WriteLine("FAIL: hub reported no devices.");
    return 1;
}
Console.WriteLine($"hub added: {endpoint.FriendlyName} [{endpoint.DevicePath}]");

// 2. Configuration match, same rules as OpenTabletDriver.Driver.GetMatchingDevices
var configPath = Directory.GetFiles(configDir, "SPen-*.json").Single();
var config = JsonConvert.DeserializeObject<TabletConfiguration>(File.ReadAllText(configPath))!;
Console.WriteLine($"config: {configPath}");
Console.WriteLine(File.ReadAllText(configPath));

var identifier = config.DigitizerIdentifiers.Single();
bool matches = identifier.VendorID == endpoint.VendorID
    && identifier.ProductID == endpoint.ProductID
    && endpoint.CanOpen
    && (identifier.InputReportLength == null || identifier.InputReportLength == endpoint.InputReportLength)
    && (identifier.DeviceStrings ?? new()).All(kv => Regex.IsMatch(endpoint.GetDeviceString(kv.Key), kv.Value));
Console.WriteLine($"configuration matches endpoint: {matches}");
if (!matches)
    return 1;

// OTD resolves the parser by full type name.
var parserType = typeof(SPenReportParser).Assembly.GetType(identifier.ReportParser);
Console.WriteLine($"report parser resolves: {parserType != null} ({identifier.ReportParser})");
var parser = (IReportParser<IDeviceReport>)Activator.CreateInstance(parserType!)!;

// 3. Stream + parse
Console.WriteLine();
Console.WriteLine($"Reading for {seconds}s - hover, draw, press the side button...");
using var stream = endpoint.Open();
var counts = new Dictionary<string, int>();
var sw = Stopwatch.StartNew();
var lastPrint = TimeSpan.Zero;
uint maxPressure = 0;
bool sawButton = false;
var readerThread = new Thread(() =>
{
    try
    {
        while (sw.Elapsed.TotalSeconds < seconds)
        {
            var data = stream.Read();
            var report = parser.Parse(data);
            var again = parser.Parse(data); // tablet debugger re-parse must yield the same thing
            if (report.GetType() != again.GetType())
                Console.WriteLine("FAIL: re-parse produced a different report type");

            string kind = report.GetType().Name;
            counts[kind] = counts.GetValueOrDefault(kind) + 1;

            if (report is ITabletReport tablet)
            {
                maxPressure = Math.Max(maxPressure, tablet.Pressure);
                sawButton |= tablet.PenButtons[0];
            }

            if (sw.Elapsed - lastPrint > TimeSpan.FromMilliseconds(500))
            {
                lastPrint = sw.Elapsed;
                Console.WriteLine(report switch
                {
                    SPenReport r => $"  {kind,-16} pos=({r.Position.X,5},{r.Position.Y,5}) p={r.Pressure,4} tilt=({r.Tilt.X,3},{r.Tilt.Y,3}) hover={r.HoverDistance,3} btn={r.PenButtons[0]}",
                    _ => $"  {kind}"
                });
            }
        }
    }
    catch (IOException ex)
    {
        Console.WriteLine($"stream ended: {ex.Message}");
    }
}) { IsBackground = true };
readerThread.Start();

// Pen out of range sends nothing, so the read can block past the deadline; don't wait on it forever.
readerThread.Join(TimeSpan.FromSeconds(seconds + 1));

Console.WriteLine();
Console.WriteLine("reports: " + string.Join(", ", counts.Select(kv => $"{kv.Key}={kv.Value}")));
Console.WriteLine($"max pressure seen: {maxPressure} / {config.Specifications.Pen.MaxPressure}, side button seen: {sawButton}");
return 0;
