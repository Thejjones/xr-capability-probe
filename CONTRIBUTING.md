# Contributing

The most useful thing you can do is **run the probe on a real XREAL Aura and share the result.** Open a "Device report" issue (the template lists what to include) and paste the text report. Read the report before you share it: it is meant to hold no personal identifiers, but you are the last check. If you find something personal in one, say so in the issue without pasting it, and it will be treated as a bug.

## Bug reports and ideas

Open an issue. For a bug, say what unit and firmware you used, what the app showed, and what you expected. For a new check, say what question it answers and how it can be asked without storing or sending anything about people.

## Code

- The project is Unity 6000.6.9f1 (Android, Vulkan, IL2CPP, ARM64). Open it, or use the commands in the README.
- Layout: `Assets/XrProbe/Core` (no Unity references; the rows, the runner, every check and the privacy rules), `Platform` (OpenXR, AR Foundation, XR Hands and the Java bridge), `App` (the app, the panel, the figure), `Editor` (scene builder, build, manifest), `Plugins/Android` (the Java plug-in), `Tests/EditMode`.
- Run the EditMode tests before you send a change: `Unity -batchmode -projectPath . -runTests -testPlatform EditMode -testResults results.xml`. A new check needs tests for the path where the unit does not offer it, the path where it fails and the path where it passes, using a fake source, and a place in the identifiers test if it handles text from the platform.
- Rules the code keeps, and a change must keep:
  - **No network.** No network code of our own and no `INTERNET` permission. (Unity's own network-capable engine modules are in any Unity build; without that permission they cannot reach the network.)
  - **Nothing about people.** A camera frame is counted and dropped; microphone samples are reduced to a level. No frame or sample is stored or sent.
  - **No identifiers in a report.** New device facts go through the whitelist and the sanitiser, and the identifiers test (`XrProbePrivacyTests`) must still pass; add your new check's poison values to it.
  - **Leave the device as found.** Anything a check changes (today: the lens dimming level and the blend mode) is put back in a `finally` block and again on pause and quit; refresh rates are only read.
  - **Be honest in the wording.** A provider flag is "reported by the runtime, not exercised". An assumed number is labelled as an assumption. Units go on every number.
- Native OpenXR calls crash the editor, and not with an exception, when no OpenXR instance exists, so each sits behind `XrGuard.OpenXrActive`.
- Keep the comments short and about what the code does.

## Licence of contributions

The project is under the Apache License 2.0. By sending a change you agree that it is offered under the same licence.

## Not in scope

Signing keys, store releases, anything that needs an account, and anything that phones home.
