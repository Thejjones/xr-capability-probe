# XR Glasses Capability Probe (for XREAL AURA)

One small app that asks a pair of XR glasses what they can actually do, and writes the answers to a report you can read, keep and, if you choose, share.

**Status: not yet run on a retail unit. First reports are wanted.** It is built and tested against simulated data only. If you have an XREAL Aura, please run it and open an issue with the result. Expect surprises, and say what happened.

Not affiliated with XREAL, Google or Unity.

## What you get

- A plain-text and a JSON report of what the runtime reports and what an ordinary app can do on the unit: OpenXR extensions, planes, anchors, hands, light estimation, display, camera and microphone lists, heat and battery.
- Every answer labelled as it is: a measurement, a flag the runtime reports but this app did not exercise, "unsupported" (the unit does not offer it) or "denied (your choice)".
- A 10-minute soak of a synthetic skinned figure, with frame-time percentiles, heat and battery, that stops by itself if the unit gets hot or low.
- No network access: the app asks for no `INTERNET` permission. Nothing is sent anywhere, by anyone, unless you choose to share the report.
- A first screen that tells you what it will do before it does anything, and a start that only you can trigger.
- Source only for now (a Unity project, Apache 2.0). A signed APK will follow once a real unit has run it.

## Quick start

Needs an Android XR device (the app declares the XR features as required, so it installs only on Android XR devices and cannot be tried on a phone), `adb`, and Unity 6000.6.9f1 with Android Build Support. Details in [docs/BUILDING.md](docs/BUILDING.md).

1. Clone this repository and open it with Unity 6000.6.9f1. The application id is `io.github.thejjones.xrcapabilityprobe`. If you fork the project and hand your own build to others, change `Owner` in `Assets/XrProbe/Editor/ProbeIdentity.cs` first, so your build cannot be mistaken for this one.
2. Apply the project settings and build (two commands, in [docs/BUILDING.md](docs/BUILDING.md)). The result is `probe.apk`, signed with Android's public debug key.
3. `adb install -r probe.apk`, then start "XR Glasses Capability Probe (for XREAL AURA)" on the glasses. Sit down and stay still: the lenses will dim and colours will flash for a few seconds. Read the first screen, then start it with a pinch or a click on the puck's trackpad. Nothing starts on its own.
4. Answer the permission dialogs (a plain reason is shown before each). Declining one skips only the rows that need it. The checks take about 5 minutes; the optional soak 10 more. At the end the panel shows a summary you can read off.
5. Get the report: `adb pull /sdcard/Android/data/io.github.thejjones.xrcapabilityprobe/files/aura-probe-report.txt`, read it, and if you are happy, open a "Device report" issue. If the pull does not work, see [docs/REPORT-FORMAT.md](docs/REPORT-FORMAT.md) for the other ways.

If the lenses stay dark afterwards, use the glasses' own tint control to clear them, and read the `display.dimming` row of the report: it says whether the original level was confirmed restored.

## What it checks

Each check is wrapped: a feature the unit does not offer, a missing native library or an exception becomes a row that says `unsupported` or `error`; the run goes on, and the report is saved after every row.

| Group | Rows | What it asks |
|---|---|---|
| runtime | `permissions`, `runtime.openxr`, `runtime.extensions` | the permissions the app needs; the OpenXR runtime's name and versions; enabled and available extensions and which key ones are present |
| features | `feature.*` | the provider flags of the Android XR package (depth, scene meshing, planes, anchors, light estimation, markers, object tracking, Geospatial). Labelled "reported by the runtime, not exercised": a flag is what the package claims, not proof that it works |
| display | `display.eyes`, `display.refresh`, `display.blend`, `display.eyecheck`, `display.dimming` | per-eye size and field of view as reported; the refresh rates offered and the current one (read only); the blend modes the runtime accepts; a left/right colour test; the dimming levels (each is requested and read back, and the original is restored) |
| audio | `audio.mic`, `audio.mic.read` | microphone inputs (channels and sample rates; only the built-in microphone is named) and a 2-second read that reports format and peak level |
| camera | `camera`, `camera.list.<id>`, `camera.frames.<id>` | the Camera2 list and a 4-second frame count per camera the unit shows to apps. Frames are counted and dropped |
| input | `hands`, `hands.pinch` | joints tracked per hand and a pinch test |
| scene | `light.estimation`, `anchors.*`, `planes.survey` | light estimate values; anchor create, save, list and load; planes and their labels over time |
| device | `device.thermal_battery` | thermal status and headroom, battery level and temperature, sampled for a minute |
| soak | `soak` | a synthetic skinned figure drawn for 10 minutes: frame-time percentiles, share of frames over the budget, heat and battery |

Status values: `pass`, `fail`, `unsupported`, `info`, `error`, `skipped`. `unsupported` is an answer ("the unit does not offer this"); `fail` is a feature that is there and did not work; `error` is a probe that broke or timed out; `info` and `skipped` are not judgements. The soak's figure is a **synthetic load of comparable cost to a character (55 bones, 4 materials, about 31,700 triangles), not a real character**. Its frame budget is the unit's own reported refresh rate; if the unit reports none, an assumed 72 Hz (13.9 ms) is used and labelled as an assumption, and a slow soak is then a measurement, not a failure.

## Privacy

The report holds device facts from a short whitelist (model, Android release, SDK level, architecture, graphics device, runtime name and version), capability facts and measurements, and short per-run labels in place of anchor ids. It holds no serial number, device name you gave it, network name, address, account, location, photo or audio. The app has no network code of its own and no `INTERNET` permission; Unity's own network-capable engine modules are present in any Unity build and cannot reach the network without that permission. What stays on the device, and how to remove it, is in [docs/PRIVACY.md](docs/PRIVACY.md). Read your report before you share it.

## Safety

Do not run it on a unit you cannot afford to have misbehave. The soak does not start, and stops by itself, at thermal status 3 or more, a battery temperature of 42 C or more, or a battery under 20 % and not charging; a tap, pinch or key also stops it. The app does nothing to disable the unit's camera indicator; whether one lights while the camera rows run is unconfirmed, and the report asks you to say. Full list in [docs/SAFETY.md](docs/SAFETY.md).

## Building

Unity 6000.6.9f1, Android, Vulkan, IL2CPP, ARM64; managed stripping High; debug-signed APK; no keystore in the repository. See [docs/BUILDING.md](docs/BUILDING.md).

## The report format

Format 1: a JSON file for machines and a text file for people, replaced after every row. See [docs/REPORT-FORMAT.md](docs/REPORT-FORMAT.md). A sample made from simulated data (it says so on its first line) is in [docs/sample-report.txt](docs/sample-report.txt) and [docs/sample-report.json](docs/sample-report.json).

## Known limitations

- Not run on a retail unit. Everything about real behaviour is unconfirmed until a report comes back.
- Provider-flag rows only say what the runtime reports; the rows with values and series show what works.
- The anchor reload check cannot be satisfied by the live anchor: "survives a restart" needs a second run.
- Field of view and per-eye size are read from the projection and the eye texture, which may be scaled; the camera list may be empty or closed to third-party apps.
- The panel is text only (the display adds light, so black is see-through); its size on the glasses is unconfirmed.
- The thermal and battery limits are the authors' conservative defaults, not vendor figures.
- Getting the report off the unit with `adb pull` from the app's external files folder is expected to work and is untested; the fallbacks are in [docs/REPORT-FORMAT.md](docs/REPORT-FORMAT.md).

## Licence

Apache License 2.0. See [LICENSE](LICENSE) and [NOTICE](NOTICE). What the build contains from others, under their own licences, is in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). See also [CONTRIBUTING.md](CONTRIBUTING.md) and [CHANGELOG.md](CHANGELOG.md).

## Not affiliated

XREAL, Android XR, Google and Unity are trademarks of their owners. This project is independent and is not affiliated with, endorsed by or sponsored by XREAL, Google or Unity.
