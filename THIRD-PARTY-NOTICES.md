# Third-party notices

This project's own code (Assets/XrProbe, the Java plug-in and the shader) has no third-party code or assets in it: the test figure is generated in code, and no model, texture or font is included. The project's own code is under the Apache License 2.0 (see LICENSE and NOTICE).

Below is what the **built app and the Unity project** contain or depend on. It was checked against the APK built on 2026-11-18 (its `META-INF/*.version` files, its native libraries and its dex classes), against the Gradle dependency list of that build, and against the licence files and POM files of each item, read in the installed Unity packages and the Gradle cache. Where an item's licence could not be read from the item itself, it says so. The Unity Companion License and the Unity Package Distribution License are Unity's own terms; their text is at the links given and is not copied here. If you redistribute, read the originals.

## Unity engine

The app is built with the Unity engine (IL2CPP, Android); the runtime in the APK (`libunity.so`, `libil2cpp.so`, `libmain.so` and the managed class library compiled into it) is under the terms of the Unity edition you build with. Unity's own third-party notices ship with the editor (`Editor/Data` in the install, including the .NET and MSBuild notices) and apply to what the engine includes. Unity is a trademark of Unity Technologies; this project is not affiliated with Unity.

The APK contains Unity's default app icon, because no icon of this project's own has been set yet.

## Unity packages (exact versions pinned in Packages/manifest.json and Packages/packages-lock.json)

| Package | Version | Licence as stated in the package |
|---|---|---|
| com.unity.xr.openxr | 1.19.0 | Source: Unity Companion License (https://unity3d.com/legal/licenses/unity_companion_license); otherwise Unity Package Distribution License (https://unity3d.com/legal/licenses/Unity_Package_Distribution_License) |
| com.unity.xr.androidxr-openxr | 1.5.0 | Unity Companion License for Unity-dependent projects |
| com.unity.xr.arfoundation | 6.7.1 | Unity Companion License for Unity-dependent projects |
| com.unity.xr.hands | 1.10.0 | Unity Package Distribution License |
| com.unity.xr.management, com.unity.xr.core-utils, com.unity.xr.legacyinputhelpers | 4.7.0, 2.6.0, 3.0.1 | Unity Companion License for Unity-dependent projects |
| com.unity.xr.compositionlayers | 2.6.1 | Source: Unity Companion License; otherwise Unity Package Distribution License |
| com.unity.render-pipelines.universal, .core, .universal-config, com.unity.shadergraph | 17.6.0 | Unity Companion License for Unity-dependent projects |
| com.unity.ugui (contains TextMeshPro; no TextMeshPro fonts or essentials are imported by this project) | 2.6.0 | Unity Companion License for Unity-dependent projects |
| com.unity.inputsystem | 1.20.1 | Unity Companion License for Unity-dependent projects |
| com.unity.collections, com.unity.mathematics, com.unity.profiling.core, com.unity.editorcoroutines, com.unity.nuget.mono-cecil | 6.6.0, 1.4.0, 1.0.3, 6.6.0, 1.11.6 | Unity Companion License for Unity-dependent projects |
| com.unity.nuget.newtonsoft-json | 3.2.2 | Unity Companion License for Unity-dependent projects (Unity's packaging); contains Newtonsoft.Json under the MIT licence (below) |
| com.unity.searcher | 4.9.5 | Unity Companion Package License v1.0 |
| com.unity.ext.nunit | 2.1.0 | Unity Package Distribution License |
| com.unity.test-framework, com.unity.test-framework.performance | 1.8.0, 6.6.0 | Unity Companion License for Unity-dependent projects (editor tests only; not in the APK) |
| com.unity.burst | 2.0.0 | **No licence file in the package and no licence field in its package.json; not verified.** Unity distributes it with the Unity Package terms; its generated code is in the APK as `lib_burst_generated.so` |
| com.unity.graph-authoring | 1.0.0 | **No licence file in the package; not verified** (editor only; not in the APK) |

## Components named in those packages' own third-party notices

- **Newtonsoft.Json** (copyright 2007 James Newton-King), with Json.Net.Unity3D (2016 SaladLab), Newtonsoft.Json-for-Unity (2019 Kalle Jillheden) and com.newtonsoft.json (2019 Mike Wuetherick): MIT licence. The probe uses it to write its report.
- **OpenXR-SDK-Source** (Khronos Group; copyright 2014-2025 The Khronos Group Inc.), named in the OpenXR, Android XR and XR Hands packages: Apache License 2.0. The APK ships an OpenXR loader, `libopenxr_loader.so`, from Unity's OpenXR loader archive (`openxr_loader.aar`).
- **Oculus OpenXR Mobile SDK** (copyright Facebook Technologies, LLC and its affiliates), named in the OpenXR and XR Hands packages: Oculus SDK License Agreement (https://developer.oculus.com/licenses/oculussdk/).
- **FXAA3_11.h** (copyright 2014-2015 NVIDIA Corporation), named in the Universal Render Pipeline: BSD-style licence reproduced in that package's notices.
- **ArmModel.cs, SwingArmModel.cs, TransitionArmModel.cs** (copyright 2016 Google Inc.), named in XR Legacy Input Helpers: Apache License 2.0.

## Android libraries bundled in the APK (Gradle dependencies of the Unity Android build)

The Gradle build declares `androidx.appcompat:appcompat:1.6.1`, `androidx.core:core:1.9.0`, `androidx.constraintlayout:constraintlayout:2.1.4`, `androidx.games:games-activity:4.4.0`, `androidx.games:games-frame-pacing:2.1.2` and the OpenXR loader archive; the APK's `META-INF/*.version` files list what came with them. Each POM read in the Gradle cache states **The Apache Software License, Version 2.0** (http://www.apache.org/licenses/LICENSE-2.0.txt):

- **AndroidX:** activity 1.6.0, annotation-experimental 1.3.0, appcompat and appcompat-resources 1.6.1, arch.core core-runtime 2.1.0, core and core-ktx 1.9.0, constraintlayout and constraintlayout-core, cursoradapter 1.0.0, customview 1.0.0, drawerlayout 1.0.0, emoji2 and emoji2-views-helper 1.2.0, fragment 1.3.6, interpolator 1.0.0, lifecycle (livedata, livedata-core, process, runtime, viewmodel, viewmodel-savedstate) 2.0.0 to 2.5.1, loader 1.0.0, savedstate 1.2.0, startup-runtime 1.1.1, tracing 1.0.0, vectordrawable and vectordrawable-animated 1.1.0, versionedparcelable 1.1.1, viewpager 1.0.0. (The Java classes also include `androidx.concurrent`, `androidx.collection` and `androidx.resourceinspection` as parts of these.)
- **GameActivity and GameTextInput** (`androidx.games:games-activity` 4.4.0; native libraries `libgame.so` and `libmain.so`) and **Swappy frame pacing** (`androidx.games:games-frame-pacing` 2.1.2; native library `libswappywrapper.so`), from the Android Game Development Kit: Apache License 2.0.
- **Kotlin standard library** (the Gradle cache of this build holds versions 1.7.10 to 2.4.21 and the APK does not record which one it bundles; the 1.7.10 POM states Apache License 2.0): Apache License 2.0.
- **kotlinx.coroutines** core and android 1.6.1 (read from the APK's `META-INF` and the POM): Apache License 2.0.
- **ListenableFuture** (`com.google.common.util.concurrent`, the Guava project's stub artefact): Apache License 2.0 as published by the Guava project. The artefact's POM in the build cache carries no licence element, so this one is not read from the artefact itself.
- **Google Play core and Play services task classes** (`com.google.android.play.core.assetpacks`, `com.google.android.gms.tasks`), which are present in the APK's dex as part of Unity's Android player classes: not verified; covered by the terms of Unity's Android player and the notices that ship with the editor.

## Other native libraries in the APK

- **libc++_shared.so** (the C++ standard library from the Android NDK that Unity uses): the NDK's NOTICE file in the Unity install includes the libc++ licence (SPDX identifier `MIT OR Apache-2.0 WITH LLVM-exception` in that file, and the Apache License 2.0 text).
- **libUnityOpenXR.so, libUnityOpenXRHands.so, libUnityARFoundationAndroidXR.so:** built from the Unity packages above, under those packages' licences.
- **lib_burst_generated.so:** generated by Burst (see the table).

## Android, XREAL

The APK runs on Android and uses the Android platform APIs (Camera2, audio, thermal and battery). The Android SDK and NDK used to build it are Google's, under their own terms. Android, Android XR and Google are trademarks of Google LLC; this project is not affiliated with Google. XREAL and AURA are trademarks of their owners; this project is not affiliated with XREAL and names the device only to say what it is meant to run on.
