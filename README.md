# otd-spen

Use a Samsung Galaxy phone with an S Pen as a pen tablet in [OpenTabletDriver](https://opentabletdriver.net) (OTD), over USB with adb.

The phone acts only as the digitizer. The plugin reads the pen's raw Linux input device (`/dev/input/eventN`) through `adb exec-out`, so you don't need root, and the pen data doesn't go through any phone app. It hands the samples to OTD as an ordinary tablet. Area, rotation, filters, output mode and bindings are all set in OTD.

A small companion app (`android/`) gives the pen a black screen to draw on, so taps don't reach the phone's UI. It also draws OTD's current area on the screen.

## Layout

| Path | What |
|---|---|
| `src/SPen.Evdev` | evdev parsing, `getevent -lp` parsing, adb wrapper, phone discovery |
| `src/SPen.OpenTabletDriver` | The OTD plugin: tool, device hub, endpoint and stream, report parser, config generator |
| `tools/SPen.Probe` | `spen-probe`: prints raw pen data, sample rate and adb jitter |
| `tools/SPen.PluginHarness` | `spen-harness`: runs the plugin against a phone without OTD installed |
| `android/` | Companion app (`dev.otdspen`): black input-swallowing screen, OTD area overlay, pen dot |

## Requirements

- OpenTabletDriver **0.6.6 or later** (0.6.x)
- adb (Android platform-tools), with USB debugging enabled and authorized on the phone
- .NET 8 SDK to build

## Build and install

```sh
dotnet build -c Release src/SPen.OpenTabletDriver
```

Copy **both** `SPen.OpenTabletDriver.dll` and `SPen.Evdev.dll` from
`src/SPen.OpenTabletDriver/bin/Release/net8.0/` into a new folder in OTD's plugin directory:

| OS | Plugin directory |
|---|---|
| macOS | `~/Library/Application Support/OpenTabletDriver/Plugins/SPen/` |
| Windows | `%LOCALAPPDATA%\OpenTabletDriver\Plugins\SPen\` |
| Linux | `~/.config/OpenTabletDriver/Plugins/SPen/` |

Then restart OTD.

Companion app (needs JDK 17 and the Android SDK):

```sh
cd android
./gradlew assembleDebug
adb install -r app/build/outputs/apk/debug/app-debug.apk
```

## Use

1. Plug the phone in over USB and run `adb devices` once to authorize it.
2. Enable the **S Pen over adb** tool. OTD hides its **Tools** tab until a tablet is detected, and this tool
   is what makes the phone detectable. So the first time, enable it in the settings file:
   1. In OTD's menu, choose **Save settings**. This creates `settings.json` if it doesn't exist yet.
   2. Quit OTD completely.
   3. In OTD's `settings.json` (macOS: `~/Library/Application Support/OpenTabletDriver/settings.json`),
      add this entry to the `"Tools"` list:
      ```json
      { "Path": "SPen.OpenTabletDriver.SPenTool", "Settings": [ { "Property": "AdbPath", "Value": null } ], "Enable": true }
      ```
   4. Start OTD. After the phone is detected, the Tools tab appears and shows the tool as enabled.
3. Within a couple of seconds the plugin finds the phone. It writes a tablet configuration for that model
   (`Configurations/SPen-<model>.json` in OTD's app data folder), and OTD detects it as
   "samsung <model> S Pen (adb)".
4. Set the area in OTD as you would for any tablet. The plugin presents the phone in **landscape**, with the
   phone's top edge (the camera end) pointing right. If you hold it the other way round, rotate the area by
   180°. The companion app follows the phone's rotation either way.
5. If the companion app is installed, it opens when the phone connects. After each **Apply** in OTD, it shows
   the current area.

Tool settings:
- **adb path**: only needed if adb isn't found automatically.
- **Open companion app**: launches the app when the phone connects (on by default).

The plugin refreshes the generated configuration whenever its output changes. To keep hand edits, remove the
`"GeneratedBy": "otd-spen"` attribute from the file. Delete the file to regenerate it.

## Diagnostics

```sh
dotnet run --project tools/SPen.Probe -- --list   # input devices on the phone
dotnet run --project tools/SPen.Probe             # live rate and jitter stats
dotnet run --project tools/SPen.PluginHarness -- /tmp/spen-config 10
```

## Notes

- Without the companion app, pen taps reach the phone's UI. Turn off Air Command and Air Actions, so the S Pen
  button isn't taken over by Samsung's features.
- Measured on a Galaxy S23 Ultra: about 481 Hz, with adb adding about 2 ms at p99.
