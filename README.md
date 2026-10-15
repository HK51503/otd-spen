# otd-spen

Use a Samsung Galaxy phone with an S Pen as a pen tablet in [OpenTabletDriver](https://opentabletdriver.net) (OTD), over USB with adb.

The phone acts only as the digitizer. The plugin reads the pen's raw Linux input device (`/dev/input/eventN`) through `adb exec-out`, so you don't need root, and the pen data doesn't go through any phone app. It hands the samples to OTD as an ordinary tablet. Area, rotation, filters, output mode and bindings are all set in OTD.

A small companion app (`android/`) gives the pen a black screen to draw on, so taps don't reach the phone's UI. It also draws OTD's current area on the screen and walks you through enabling USB debugging.

## Requirements

- OpenTabletDriver **0.6.6 or later** (0.6.x). Tested on 0.6.7 on macOS.
- A Galaxy phone with an S Pen, with USB debugging enabled. The companion app walks you through it.
- adb (Android platform-tools). Optional: if none is installed, the plugin downloads a pinned copy
  (platform-tools 37.0.1, SHA-256 verified) into OTD's data folder.
- Windows only: Samsung's USB driver, or adb won't see the phone.

## Install

1. **Plugin:** in OTD, open **Plugins → Plugin Manager** and install **S Pen over adb**. Or drag
   `SPen.OpenTabletDriver-<version>.zip` from the
   [releases](https://github.com/HK51503/otd-spen/releases) onto the Plugin Manager.
2. **Companion app:** install `otd-spen-companion-<version>.apk` from the same release on the phone, or run
   `adb install otd-spen-companion-<version>.apk`.
3. **Enable the tool:** OTD hides its Tools tab until a tablet is detected, and this tool is what makes the
   phone detectable. So the first time, enable it with OTD's console client while OTD is running:

   | OS | Command |
   |---|---|
   | macOS | `/Applications/OpenTabletDriver.app/Contents/MacOS/OpenTabletDriver.Console enabletools SPen.OpenTabletDriver.SPenTool` |
   | Windows | `OpenTabletDriver.Console.exe enabletools SPen.OpenTabletDriver.SPenTool` (in OTD's install folder) |
   | Linux | `otd enabletools SPen.OpenTabletDriver.SPenTool` |

   Once the phone shows up, choose **Save settings** in OTD so the tool stays enabled. From then on, the
   Tools tab is visible and the tool appears there.

   <details><summary>Alternative: edit settings.json</summary>

   Quit OTD and add this to the `"Tools"` list in OTD's `settings.json` (macOS:
   `~/Library/Application Support/OpenTabletDriver/settings.json`). If the file doesn't exist yet, choose
   **Save settings** in OTD first.

   ```json
   { "Path": "SPen.OpenTabletDriver.SPenTool", "Settings": [], "Enable": true }
   ```
   </details>

## Use

1. Plug the phone in. The first time, it asks "Allow USB debugging?": tick "Always allow from this computer"
   and tap Allow. OTD shows a notification while the phone is waiting for that approval.
2. Within a couple of seconds, the plugin finds the phone and opens the companion app. It writes a tablet
   configuration for that model (`Configurations/SPen-<model>.json` in OTD's app data folder), and OTD
   detects it as "samsung <model> S Pen (adb)".
3. Set the area in OTD as you would for any tablet. The plugin presents the phone in **landscape**, with the
   phone's top edge (the camera end) pointing right. If you hold it the other way round, rotate the area by
   180°: the companion app follows that rotation. After each **Apply**, the app shows the current area.

Tool settings:
- **adb path**: only needed if adb isn't found automatically.
- **Download adb**: if no adb is installed, download the pinned platform-tools (on by default). An installed
  adb (Android Studio, Homebrew, ...) is always preferred. Two different adb versions on one machine keep
  restarting each other's server, which would cut the pen stream. OTD's log shows which adb is used and its
  version.
- **Open companion app**: launches the app when the phone connects (on by default).

The plugin refreshes the generated configuration whenever its output changes. To keep hand edits, remove the
`"GeneratedBy": "otd-spen"` attribute from the file. Delete the file to regenerate it.

Tips:
- Turn off Air Command and Air Actions (Settings → Advanced features → S Pen), so Samsung's features don't
  take over the pen or its button.
- In Developer options, turn on "Disable adb authorization timeout" so you don't have to re-approve the PC
  every week.

## What the plugin does on your computer

So you know what you're installing:
- **Runs adb** (`adb devices` every 2 seconds, `adb exec-out cat /dev/input/eventN` while streaming, and
  `adb shell am ...` to open the companion app and send it the area).
- **Downloads adb** from `dl.google.com` once, only if none is installed and "Download adb" is on. Android
  SDK terms apply to that download.
- **Writes a tablet configuration** into OTD's `Configurations` folder for each phone model.
- **Uses OTD internals via reflection.** OTD has no plugin API for device sources, so the plugin attaches its
  device hub through the driver's internal objects. A future OTD 0.6.x release could break this.

## Build

Needs the .NET 8 SDK, and JDK 17 plus the Android SDK for the app.

```sh
scripts/package.sh
```

This builds into `dist/`:
- the plugin zip (dlls and LICENSE at the top level);
- the companion APK (debug-signed for now);
- the metadata file for a pull request to
  [OpenTabletDriver/Plugin-Repository](https://github.com/OpenTabletDriver/Plugin-Repository), with the
  zip's SHA-256.

The zip is reproducible: the same source gives the same SHA-256. Upload it to a GitHub release tagged with
the version from `Directory.Build.props`, unchanged, because the metadata pins its hash.

For development, copy `SPen.OpenTabletDriver.dll` and `SPen.Evdev.dll` from
`src/SPen.OpenTabletDriver/bin/Release/net8.0/` into a folder under OTD's plugin directory, then restart OTD:

| OS | Plugin directory |
|---|---|
| macOS | `~/Library/Application Support/OpenTabletDriver/Plugins/SPen/` |
| Windows | `%LOCALAPPDATA%\OpenTabletDriver\Plugins\SPen\` |
| Linux | `~/.config/OpenTabletDriver/Plugins/SPen/` |

## Layout

| Path | What |
|---|---|
| `src/SPen.Evdev` | evdev parsing, `getevent -lp` parsing, adb wrapper, phone discovery |
| `src/SPen.OpenTabletDriver` | The OTD plugin: tool, device hub, endpoint and stream, report parser, config generator, adb download |
| `tools/SPen.Probe` | `spen-probe`: prints raw pen data, sample rate and adb jitter |
| `tools/SPen.PluginHarness` | `spen-harness`: runs the plugin against a phone without OTD installed |
| `android/` | Companion app (`dev.otdspen`): input-swallowing screen, OTD area overlay, pen dot, USB debugging guide |
| `scripts/package.sh` | Release artifacts and plugin repository metadata |

## Diagnostics

```sh
dotnet run --project tools/SPen.Probe -- --list   # input devices on the phone
dotnet run --project tools/SPen.Probe             # live rate and jitter stats
dotnet run --project tools/SPen.PluginHarness -- /tmp/spen-config 10
```

Measured on a Galaxy S23 Ultra: about 481 Hz, with adb adding about 2 ms at p99.

## License

[GPL-3.0-only](LICENSE)
