using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.SpatialTracking;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Android;

namespace XrProbe
{
    // The probe's scene and Android build:
    //   Unity -batchmode -quit -projectPath <project> -buildTarget Android -executeMethod XrProbe.XrProbeBuild.BuildApk [-probeOut <folder>] [-probeDev] [-probeAllowPlaceholder]
    // writes <folder or BuildOut>/probe.apk (debug-signed). The product name, company, application id and the OpenXR features are set for the build and
    // put back afterwards, so the project's own settings are untouched. Nothing here touches a device.
    public static class XrProbeBuild
    {
        public const string ScenePath = "Assets/XrProbe/Scenes/XrProbe.unity";

        // Permissions the manifest gets, with the reason (README lists the same).
        public static readonly string[][] Permissions =
        {
            new[] { "android.permission.CAMERA", "Camera2 list and a frame count of every camera the unit shows the app" },
            new[] { "android.permission.RECORD_AUDIO", "read the microphone inputs for 2 s to learn channels, rates and peak level" },
            new[] { "android.permission.SCENE_UNDERSTANDING_COARSE", "planes, anchors, light estimation" },
            new[] { "android.permission.SCENE_UNDERSTANDING_FINE", "raycasts, depth and meshing flags" },
            new[] { "android.permission.HAND_TRACKING", "hand joints and the pinch test" },
        };

        // Set by BuildApk so the post-process step only edits the probe's manifest.
        public static bool Active;

        // Features the probe needs, by OpenXR feature id (Android). Eye and face tracking are turned OFF: the Aura has neither, and a manifest that
        // requires them would not install.
        static readonly string[] On =
        {
            ARSessionFeature.featureId, ARPlaneFeature.featureId, ARAnchorFeature.featureId, ARCameraFeature.featureId, ARRaycastFeature.featureId,
            AROcclusionFeature.featureId, ARMeshFeature.featureId, ARBoundingBoxFeature.featureId, DisplayUtilitiesFeature.featureId,
            AndroidXRPerformanceMetrics.featureId, "com.unity.openxr.feature.input.handtracking",
        };
        static readonly string[] Off = { ARFaceFeature.featureId, "com.unity.openxr.feature.input.eyetracking" };

        static Material MakeMaterial(string name, Shader shader, Color colour)
        {
            string path = "Assets/XrProbe/Materials/" + name + ".mat";
            AssetDatabase.DeleteAsset(path);
            var m = new Material(shader) { name = name };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", colour);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.2f);
            AssetDatabase.CreateAsset(m, path);
            return AssetDatabase.LoadAssetAtPath<Material>(path);
        }

        [MenuItem("XR Probe/Build Probe Scene")]
        public static void BuildScene()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            Directory.CreateDirectory("Assets/XrProbe/Materials");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.1f; sun.color = new Color(1f, 0.97f, 0.9f);
            sun.transform.rotation = Quaternion.Euler(42f, 28f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.38f, 0.38f, 0.42f);

            var session = new GameObject("AR Session");
            session.AddComponent<ARSession>();

            var originGo = new GameObject("XR Origin");
            var origin = originGo.AddComponent<XROrigin>();
            var offset = new GameObject("Camera Offset");
            offset.transform.SetParent(originGo.transform, false);
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(offset.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
            cam.nearClipPlane = 0.05f; cam.farClipPlane = 30f;
            camGo.AddComponent<TrackedPoseDriver>();
            origin.Camera = cam;
            origin.CameraFloorOffsetObject = offset;

            // Managers start disabled: the app enables each after its permission is granted, and switches it off again after its probe.
            var planes = originGo.AddComponent<ARPlaneManager>(); planes.enabled = false;
            var anchors = originGo.AddComponent<ARAnchorManager>(); anchors.enabled = false;
            var camMgr = camGo.AddComponent<ARCameraManager>(); camMgr.enabled = false;

            var app = new GameObject("ProbeApp").AddComponent<ProbeApp>();
            var panel = ProbePanel.Create(null);
            panel.transform.position = new Vector3(0f, 1.5f, 1.8f);

            // The colour test: red in the left eye, blue in the right.
            string eyePath = "Assets/XrProbe/Materials/ProbeEyeTest.mat";
            AssetDatabase.DeleteAsset(eyePath);
            AssetDatabase.CreateAsset(new Material(Shader.Find("XrProbe/EyeTest")), eyePath);
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "EyeTestQuad";
            UnityEngine.Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(eyePath);
            quad.SetActive(false);

            // The soak's synthetic figure draws with four plain lit materials made here.
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            app.soakMaterials = new[]
            {
                MakeMaterial("SoakLegs", lit, new Color(0.35f, 0.4f, 0.6f)),
                MakeMaterial("SoakTorso", lit, new Color(0.6f, 0.35f, 0.35f)),
                MakeMaterial("SoakArms", lit, new Color(0.35f, 0.6f, 0.4f)),
                MakeMaterial("SoakHead", lit, new Color(0.85f, 0.75f, 0.6f)),
            };
            app.eyeQuad = quad; app.head = cam; app.planes = planes; app.anchors = anchors; app.cameraManager = camMgr; app.panel = panel;

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("XrProbeBuild: scene " + ScenePath);
        }

        // The settings before the build, so they can be put back.
        sealed class Saved
        {
            public string productName, companyName, bundleId;
            public bool forceInternet;
            public readonly Dictionary<string, bool> features = new Dictionary<string, bool>();
        }

        static void SetFeature(Saved saved, string id, bool enabled)
        {
            var f = FeatureHelpers.GetFeatureWithIdForBuildTarget(BuildTargetGroup.Android, id);
            if (f == null) { Debug.LogWarning("XrProbeBuild: OpenXR feature not found: " + id); return; }
            if (!saved.features.ContainsKey(id)) saved.features[id] = f.enabled;
            f.enabled = enabled;
            EditorUtility.SetDirty(f);
        }

        static void Restore(Saved saved)
        {
            foreach (var kv in saved.features)
            {
                var f = FeatureHelpers.GetFeatureWithIdForBuildTarget(BuildTargetGroup.Android, kv.Key);
                if (f != null) { f.enabled = kv.Value; EditorUtility.SetDirty(f); }
            }
            PlayerSettings.productName = saved.productName;
            PlayerSettings.companyName = saved.companyName;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, saved.bundleId);
            PlayerSettings.Android.forceInternetPermission = saved.forceInternet;
            AssetDatabase.SaveAssets();
        }

        // True when the build may go ahead: the owner is named, or a test build was asked for with -probeAllowPlaceholder.
        public static bool OwnerAllowsBuild(string[] args, out string reason)
        {
            reason = null;
            if (!ProbeIdentity.OwnerIsPlaceholder || System.Array.IndexOf(args, "-probeAllowPlaceholder") >= 0) return true;
            reason = "refusing to build: the owner in ProbeIdentity.cs is still the placeholder \"" + ProbeIdentity.Placeholder + "\", so the application id (" + ProbeIdentity.ApplicationId + ") "
                + "would be a made-up one that cannot be kept once anyone has installed the app. Set ProbeIdentity.Owner to the project's account name, or pass -probeAllowPlaceholder for a test build that must not be given to anyone.";
            return false;
        }

        public static void BuildApk()
        {
            string refusal;
            if (!OwnerAllowsBuild(Environment.GetCommandLineArgs(), out refusal)) { Debug.LogError("XrProbeBuild: " + refusal); EditorApplication.Exit(2); return; }
            if (ProbeIdentity.OwnerIsPlaceholder) Debug.LogWarning("XrProbeBuild: test build with the placeholder owner; do not distribute it.");
            BuildScene();
            var saved = new Saved
            {
                productName = PlayerSettings.productName, companyName = PlayerSettings.companyName,
                bundleId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android), forceInternet = PlayerSettings.Android.forceInternetPermission,
            };
            Active = true;
            try
            {
                PlayerSettings.productName = ProbeIdentity.ProductName;
                PlayerSettings.companyName = ProbeIdentity.CompanyName;
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, ProbeIdentity.ApplicationId);
                // The probe opens no socket: the manifest must not ask for the network (the post-process step removes the permission Unity adds on its own).
                PlayerSettings.Android.forceInternetPermission = false;
                foreach (var id in On) SetFeature(saved, id, true);
                foreach (var id in Off) SetFeature(saved, id, false);
                AssetDatabase.SaveAssets();
                string outDir = "BuildOut";
                var args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-probeOut") outDir = args[i + 1];
                Directory.CreateDirectory(outDir);
                bool dev = args.Contains("-probeDev");
                var opts = new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = Path.Combine(outDir, "probe.apk"),
                    target = BuildTarget.Android,
                    options = dev ? BuildOptions.Development : BuildOptions.None,
                };
                var report = BuildPipeline.BuildPlayer(opts);
                var s = report.summary;
                Debug.Log(string.Format("XrProbeBuild.BuildApk result {0}, size {1} bytes, errors {2}, warnings {3}, time {4}", s.result, s.totalSize, s.totalErrors, s.totalWarnings, s.totalTime));
                if (s.result != BuildResult.Succeeded) EditorApplication.Exit(1);
            }
            finally
            {
                Active = false;
                Restore(saved);
            }
        }
    }

    // Edits the generated manifests, only during the probe build: the probe's permissions, no network permission, no backup of app data.
    public sealed class XrProbeManifest : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder { get { return 100; } }

        const string Ns = "http://schemas.android.com/apk/res/android";
        const string Tools = "http://schemas.android.com/tools";

        static void NoBackup(XmlDocument doc)
        {
            var app = doc.DocumentElement.SelectSingleNode("application") as XmlElement;
            if (app == null) return;
            app.SetAttribute("allowBackup", Ns, "false");
            app.SetAttribute("fullBackupContent", Ns, "false");
        }

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            if (!XrProbeBuild.Active) return;
            string file = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(file)) { Debug.LogWarning("XrProbeManifest: no manifest at " + file); return; }
            var doc = new XmlDocument();
            doc.Load(file);
            var root = doc.DocumentElement;
            var have = new HashSet<string>();
            foreach (XmlNode n in root.SelectNodes("uses-permission")) have.Add(((XmlElement)n).GetAttribute("name", Ns));
            foreach (var p in XrProbeBuild.Permissions)
            {
                if (have.Contains(p[0])) continue;
                var e = doc.CreateElement("uses-permission");
                e.SetAttribute("name", Ns, p[0]);
                root.AppendChild(e);
            }
            // Camera and microphone are asked for, never required: the app must install on a unit that lacks either.
            foreach (var feature in new[] { "android.hardware.camera", "android.hardware.microphone" })
            {
                bool exists = false;
                foreach (XmlNode n in root.SelectNodes("uses-feature")) if (((XmlElement)n).GetAttribute("name", Ns) == feature) exists = true;
                if (exists) continue;
                var e = doc.CreateElement("uses-feature");
                e.SetAttribute("name", Ns, feature);
                e.SetAttribute("required", Ns, "false");
                root.AppendChild(e);
            }
            NoBackup(doc);
            doc.Save(file);
            Debug.Log("XrProbeManifest: permissions added, backup off, in the library manifest");
            // The app manifest (the launcher module) is where a permission another module asked for is removed.
            string launcher = Path.GetFullPath(Path.Combine(path, "..", "launcher", "src", "main", "AndroidManifest.xml"));
            if (File.Exists(launcher))
            {
                var l = new XmlDocument();
                l.Load(launcher);
                var lr = l.DocumentElement;
                if (lr.GetAttribute("xmlns:tools") == "") lr.SetAttribute("xmlns:tools", Tools);
                var rm = l.CreateElement("uses-permission");
                rm.SetAttribute("name", Ns, "android.permission.INTERNET");
                rm.SetAttribute("node", Tools, "remove");
                lr.AppendChild(rm);
                NoBackup(l);
                l.Save(launcher);
                Debug.Log("XrProbeManifest: INTERNET removal marked, backup off, in the app manifest");
            }
            else Debug.LogWarning("XrProbeManifest: no launcher manifest at " + launcher);
        }
    }
}
