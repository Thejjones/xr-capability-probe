# Building

The first release is source only. A signed APK will follow once a real unit has run the probe.

## Requirements

- Unity **6000.6.9f1** with the Android Build Support module (OpenJDK, SDK and NDK). No other tools.
- `adb` from the Android platform tools, to install and to get the report.
- An Android XR device to run it on. The app declares the Android XR features as required, so it installs only on Android XR devices and cannot be tried on a phone.

## Names, in one place

`Assets/XrProbe/Editor/ProbeIdentity.cs` holds the product name, the company name and the owner. The application id is `io.github.<Owner>.xrcapabilityprobe`, a form that implies no domain. `Owner` is `thejjones` in this repository, so the id is `io.github.thejjones.xrcapabilityprobe`. A fork that will be given to others should set its own `Owner`. **A build whose owner is the placeholder value `OWNER` is refused** with a message saying why, because an application id cannot be changed once anyone has installed the app. For a test build that must not be given to anyone, pass `-probeAllowPlaceholder`; a development build (`-probeDev`) is not a way round the refusal. The Java plug-in's package (`xrprobe.plugin`) is internal and implies no domain.

## Steps

```text
Unity -batchmode -quit -projectPath . -buildTarget Android -executeMethod XrProbe.ProbeProjectSetup.Apply
Unity -batchmode -quit -projectPath . -buildTarget Android -executeMethod XrProbe.XrProbeBuild.BuildApk -probeOut ./build [-probeAllowPlaceholder]
```

The first command applies the project settings once (Vulkan, IL2CPP, ARM64, the OpenXR loader, a minimal render pipeline, managed stripping High); you can also open the project in the editor and use the "XR Probe" menu. The second builds the scene, sets the OpenXR features and manifest entries the probe needs (and turns eye and face tracking off), and writes `build/probe.apk`, signed with Android's public debug key. **No keystore is created or used.** Anything else you set is put back afterwards. A first build takes several minutes.

`Assets/XrProbe/link.xml` keeps the report types and Newtonsoft.Json whole under stripping.

## Check the result

```text
aapt2 dump permissions probe.apk      (no INTERNET)
aapt2 dump badging probe.apk          (package, launchable activity, features)
adb install -r probe.apk
adb shell am start -n <application id>/com.unity3d.player.UnityPlayerGameActivity
```

Launch extras: `--es mode all|survey|soak` (default `all`; `survey` skips the soak) and `--es soak_seconds 600`.

## Tests

```text
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -testResults results.xml
```

They need no device: the report writer, the runner, each check against fake sources (the path where the unit does not offer the thing, the failing path and the passing path), the privacy and identifiers tests, the first screen, the soak safety rules, the report under stripping, and the real platform sources run in the editor, where all must answer `unsupported` and none may crash. Native OpenXR calls crash the editor, and not with an exception, when no OpenXR instance exists, so every such call sits behind `XrGuard.OpenXrActive`.

What the tests cannot prove: that the IL2CPP build on a unit writes its files, that the permissions dialogs behave as described, and anything about the unit's real answers.

## Layout

`Assets/XrProbe/Core` (no Unity references: rows, runner, every check, the privacy rules), `Platform` (OpenXR, AR Foundation, XR Hands and the Java bridge), `App` (the app, the panel, the figure), `Editor` (scene builder, build, manifest, project setup), `Plugins/Android` (the Java plug-in), `Shaders` (the left/right eye test), `Tests/EditMode`.
