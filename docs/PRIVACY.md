# Privacy

What the probe records, what it does not, and what it leaves on the device.

## What is in the report

- **Device facts, from a short whitelist:** Unity version, platform, device model, operating system string, processor type and count, memory size, graphics device name and API, the active XR loader, and from Android: manufacturer, model, device, product, release, SDK level, security patch and ABIs.
- **Capability facts and measurements:** what the runtime reports and what the checks measured (see [REPORT-FORMAT.md](REPORT-FORMAT.md)).
- **Short per-run labels** such as `#a1b2c3` in place of anchor ids. A label is a hash of the id with a random salt made for each run, so it means "the same one within this report" and two reports cannot be linked through it.

## What is not

No serial number, no name you gave the device, no network name, no IP or MAC address, no account, no location, no photos and no audio. Microphone input names are left out except for the built-in microphone's.

## How it is kept out

1. The sources never ask the platform for those things. Device facts come through a whitelist by key.
2. Everything written as a string passes a sanitiser at write time and again when the report is serialised. It replaces e-mail addresses, MAC and IP addresses, network names, GUIDs and long hex ids, file paths, user ids, package names, coordinates, possessive device names ("Someone's headset") and serial-like tokens. Exception text goes through it too. A four-part version number such as `1.0.26.0` is kept when it follows a cue word (version, runtime, API, driver and so on) or sits alone in a field whose key names a version; the same digits anywhere else are treated as an address.
3. An automated test (`XrProbePrivacyTests`) feeds poison values (a serial-like token, a personal device name, a MAC address, a Wi-Fi name, an e-mail, coordinates, GUIDs, user paths, a user id, an IP address, a package name) through every check that takes text from the platform, and through the permission and eye-check probes, and fails if any reaches the JSON, the text, the log lines, the prompts or the panel. A second test checks that realistic runtime, extension, graphics and build strings are left alone.

The sanitiser is the second layer, not a promise. **Read the report before you share it.** If you find something personal in one, say so in the issue without pasting it; it will be treated as a bug.

## Camera and microphone

The camera rows show "camera in use: frames counted and dropped" while they run; the microphone row shows "microphone in use: level measured, nothing kept". A camera frame is counted and its buffer released at once. Microphone samples are reduced to a peak level and discarded. Nothing is stored or sent. The app does nothing to disable the unit's camera indicator; whether one lights while the camera rows run is unconfirmed, and the issue template asks you to say.

## No network

The app has no network code of its own and no `INTERNET` permission (`aapt2 dump permissions` on the APK shows none). Unity's own network-capable engine modules are present in any Unity build; without that permission they cannot reach the network. Nothing leaves the device unless you copy the report off it yourself.

## What stays on the device, and how to remove it

In the app's own files folder (`/sdcard/Android/data/<application id>/files/`):

- `aura-probe-report.json` and `aura-probe-report.txt`, replaced after every row;
- a small `probe-state` folder holding the id of one saved anchor, so a second run can check whether it survives a restart;
- `probe-soak-*.json` files holding where the soak figure was placed, if a soak was run.

And in the system's anchor store: **one saved anchor** the anchor check made. The next run loads it for the "survives a restart" row and then erases it. To remove it yourself, click the trackpad on the final screen ("clean-up"), or uninstall the app (`adb uninstall <application id>`); the files go with the app. Android backup is switched off for the app (`allowBackup=false`).

## What it changes on the unit

Only the lens dimming level and the environment blend mode, each put back in a `finally` block, again when the app is paused or quit, and read back; the report says what it read back. It only reads the refresh rate. If the lenses stay dark, see [SAFETY.md](SAFETY.md).
