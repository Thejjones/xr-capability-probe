using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Unity.Collections;
using UnityEngine;
using UnityEngine.SubsystemsImplementation;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Android;
using UnityEngine.XR.OpenXR.NativeTypes;

namespace XrProbe
{
    // The real answers: Unity's OpenXR, AR Foundation, XR Hands and the Android XR package, behind the interfaces the probes ask through.
    // Each one throws (or answers "unsupported") when the thing is not there; the runner turns that into an "unsupported" row.

    // Calls into the OpenXR plug-in's native library crash the process (not an exception) when no OpenXR instance exists, as in the editor,
    // so every such call is behind this check.
    public static class XrGuard
    {
        public static bool OpenXrActive
        {
            get
            {
                try
                {
                    var m = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
                    return m != null && m.activeLoader is OpenXRLoaderBase;
                }
                catch (Exception) { return false; }
            }
        }

        public static void Require()
        {
            if (!OpenXrActive) throw new ProbeUnsupportedException("no OpenXR loader is running");
        }
    }

    public sealed class UnityOpenXrInfo : IOpenXrInfo
    {
        public string RuntimeName { get { XrGuard.Require(); return OpenXRRuntime.name; } }
        public string RuntimeVersion { get { XrGuard.Require(); return OpenXRRuntime.version; } }
        public string ApiVersion { get { XrGuard.Require(); return OpenXRRuntime.apiVersion; } }
        public string PluginVersion { get { XrGuard.Require(); return OpenXRRuntime.pluginVersion; } }
        public string[] EnabledExtensions() { XrGuard.Require(); return OpenXRRuntime.GetEnabledExtensions(); }
        public string[] AvailableExtensions() { XrGuard.Require(); return OpenXRRuntime.GetAvailableExtensions(); }
    }

    // Feature flags read from the subsystem descriptors (what each provider says it can do) and from the extension list.
    public static class UnityFeatureFlags
    {
        static bool HasExtension(string name)
        {
            if (!XrGuard.OpenXrActive) return false;
            try
            {
                var all = (OpenXRRuntime.GetAvailableExtensions() ?? new string[0]).Concat(OpenXRRuntime.GetEnabledExtensions() ?? new string[0]);
                return all.Contains(name);
            }
            catch (Exception) { return false; }
        }

        static List<string> ExtensionsMentioning(params string[] words)
        {
            if (!XrGuard.OpenXrActive) return new List<string>();
            try
            {
                return (OpenXRRuntime.GetAvailableExtensions() ?? new string[0]).Where(e => words.Any(w => e.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)).Distinct().ToList();
            }
            catch (Exception) { return new List<string>(); }
        }

        public static FeatureAnswer Depth()
        {
            var a = new FeatureAnswer();
            var l = new List<XROcclusionSubsystemDescriptor>();
            SubsystemManager.GetSubsystemDescriptors(l);
            bool ext = HasExtension("XR_ANDROID_depth_texture");
            a.Values["extension_XR_ANDROID_depth_texture"] = ext;
            a.Values["descriptors"] = l.Count;
            if (l.Count > 0)
            {
                var d = l[0];
                a.Values["environment_depth_image"] = d.environmentDepthImageSupported.ToString();
                a.Values["environment_depth_confidence_image"] = d.environmentDepthConfidenceImageSupported.ToString();
                a.Values["environment_depth_temporal_smoothing"] = d.environmentDepthTemporalSmoothingSupported.ToString();
                a.Values["human_segmentation_depth"] = d.humanSegmentationDepthImageSupported.ToString();
                a.Supported = d.environmentDepthImageSupported == Supported.Supported;
                a.Detail = "environment depth image: " + d.environmentDepthImageSupported + "; extension " + (ext ? "present" : "absent") + " (the depth texture is at most 320x320 on Android XR)";
            }
            else { a.Supported = false; a.Detail = "no occlusion provider is registered (the Occlusion feature is off or the runtime has none); extension " + (ext ? "present" : "absent"); }
            return a;
        }

        public static FeatureAnswer Meshing()
        {
            var a = new FeatureAnswer();
            var l = new List<XRMeshSubsystemDescriptor>();
            SubsystemManager.GetSubsystemDescriptors(l);
            bool ext = HasExtension("XR_ANDROID_scene_meshing");
            a.Values["extension_XR_ANDROID_scene_meshing"] = ext; a.Values["descriptors"] = l.Count;
            a.Supported = l.Count > 0 && ext;
            a.Detail = l.Count > 0 ? "a meshing provider is registered; extension " + (ext ? "present" : "absent") : "no meshing provider is registered; extension " + (ext ? "present" : "absent");
            return a;
        }

        public static FeatureAnswer Planes()
        {
            var a = new FeatureAnswer();
            var l = new List<XRPlaneSubsystemDescriptor>();
            SubsystemManager.GetSubsystemDescriptors(l);
            a.Values["descriptors"] = l.Count;
            if (l.Count == 0) { a.Detail = "no plane provider is registered"; return a; }
            var d = l[0];
            a.Values["horizontal"] = d.supportsHorizontalPlaneDetection; a.Values["vertical"] = d.supportsVerticalPlaneDetection;
            a.Values["arbitrary"] = d.supportsArbitraryPlaneDetection; a.Values["boundary_vertices"] = d.supportsBoundaryVertices;
            a.Values["classification"] = d.supportsClassification;
            a.Supported = true;
            a.Detail = "plane provider registered; horizontal " + d.supportsHorizontalPlaneDetection + ", vertical " + d.supportsVerticalPlaneDetection + ", classification " + d.supportsClassification
                + "; labels Android XR documents: WALL, FLOOR, CEILING, TABLE";
            return a;
        }

        public static FeatureAnswer Anchors()
        {
            var a = new FeatureAnswer();
            var l = new List<XRAnchorSubsystemDescriptor>();
            SubsystemManager.GetSubsystemDescriptors(l);
            bool ext = HasExtension("XR_ANDROID_device_anchor_persistence");
            a.Values["extension_XR_ANDROID_device_anchor_persistence"] = ext; a.Values["descriptors"] = l.Count;
            if (l.Count == 0) { a.Detail = "no anchor provider is registered; persistence extension " + (ext ? "present" : "absent"); return a; }
            var d = l[0];
            a.Values["save"] = d.supportsSaveAnchor; a.Values["load"] = d.supportsLoadAnchor; a.Values["erase"] = d.supportsEraseAnchor; a.Values["list_saved"] = d.supportsGetSavedAnchorIds;
            a.Supported = true;
            a.Detail = "anchor provider registered; save " + d.supportsSaveAnchor + ", load " + d.supportsLoadAnchor + ", erase " + d.supportsEraseAnchor + ", list saved " + d.supportsGetSavedAnchorIds + "; persistence extension " + (ext ? "present" : "absent");
            return a;
        }

        public static FeatureAnswer LightEstimation()
        {
            var a = new FeatureAnswer();
            var l = new List<XRCameraSubsystemDescriptor>();
            SubsystemManager.GetSubsystemDescriptors(l);
            bool ext = HasExtension("XR_ANDROID_light_estimation");
            a.Values["extension_XR_ANDROID_light_estimation"] = ext; a.Values["descriptors"] = l.Count;
            if (l.Count == 0) { a.Detail = "no camera provider is registered; extension " + (ext ? "present" : "absent"); return a; }
            var d = l[0];
            a.Values["world_ambient_intensity"] = d.supportsWorldTrackingAmbientIntensityLightEstimation;
            a.Values["world_hdr"] = d.supportsWorldTrackingHDRLightEstimation;
            a.Values["average_brightness"] = d.supportsAverageBrightness;
            a.Supported = d.supportsWorldTrackingAmbientIntensityLightEstimation || d.supportsWorldTrackingHDRLightEstimation || d.supportsAverageBrightness;
            a.Detail = "ambient intensity " + d.supportsWorldTrackingAmbientIntensityLightEstimation + ", HDR " + d.supportsWorldTrackingHDRLightEstimation + ", average brightness " + d.supportsAverageBrightness + "; extension " + (ext ? "present" : "absent");
            return a;
        }

        public static FeatureAnswer Markers()
        {
            var a = new FeatureAnswer();
            var l = new List<XRMarkerSubsystemDescriptor>();
            SubsystemManager.GetSubsystemDescriptors(l);
            var names = ExtensionsMentioning("marker", "qr", "barcode");
            a.Values["descriptors"] = l.Count; a.Values["extension_names"] = names.ToArray();
            a.Supported = l.Count > 0;
            a.Detail = l.Count > 0 ? "a marker provider is registered" : "no marker provider is registered; extensions naming markers or QR codes: " + (names.Count > 0 ? string.Join(", ", names.ToArray()) : "none");
            return a;
        }

        public static FeatureAnswer Objects()
        {
            var a = new FeatureAnswer();
            var l = new List<XRBoundingBoxSubsystemDescriptor>();
            SubsystemManager.GetSubsystemDescriptors(l);
            bool ext = HasExtension("XR_ANDROID_trackables_object");
            a.Values["extension_XR_ANDROID_trackables_object"] = ext; a.Values["descriptors"] = l.Count;
            a.Values["android_xr_documented_categories"] = new[] { "Keyboard", "Mouse", "Laptop", "Other(UNKNOWN)" };
            if (l.Count == 0) { a.Detail = "no bounding box provider is registered; extension " + (ext ? "present" : "absent"); return a; }
            a.Values["classifications_supported"] = l[0].supportsClassifications;
            a.Supported = ext || l[0].supportsClassifications;
            a.Detail = "bounding box provider registered; classifications " + l[0].supportsClassifications + "; Android XR documents only Keyboard, Mouse, Laptop and Other; extension " + (ext ? "present" : "absent");
            return a;
        }

        public static FeatureAnswer Geospatial()
        {
            var a = new FeatureAnswer();
            var names = ExtensionsMentioning("geospatial", "vps", "earth");
            a.Values["extension_names"] = names.ToArray();
            a.Supported = names.Count > 0;
            a.Detail = names.Count > 0 ? "extensions that look like geospatial: " + string.Join(", ", names.ToArray()) + " (Unity's Android XR package 1.5.0 has no Geospatial API, so use needs native code)" : "no OpenXR extension names geospatial anchoring, and Unity's Android XR package 1.5.0 has no Geospatial API; Google ties Geospatial to its Jetpack XR SDK";
            return a;
        }
    }

    // ----------------------------------------------------------------------------------- planes from AR Foundation

    public sealed class UnityPlaneSource : IPlaneSource
    {
        readonly ARPlaneManager mgr;
        public UnityPlaneSource(ARPlaneManager mgr) { this.mgr = mgr; }

        public string Start()
        {
            if (mgr == null) return "the scene has no plane manager";
            mgr.enabled = true;
            if (mgr.subsystem == null) return "no plane provider is running (the Planes feature is off or the runtime lacks it)";
            mgr.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
            return null;
        }

        public IList<PlaneInfo> Snapshot()
        {
            var l = new List<PlaneInfo>();
            foreach (var p in mgr.trackables)
                l.Add(new PlaneInfo { Id = p.trackableId.ToString(), Label = Label(p.classifications), AreaM2 = p.size.x * p.size.y, Alignment = p.alignment.ToString(), HasHeight = true, HeightY = p.transform.position.y });
            return l;
        }

        // The classification flags as one word (several can be set; the first listed wins), "Unlabeled" when none.
        public static string Label(PlaneClassifications c)
        {
            if (c == PlaneClassifications.None) return "Unlabeled";
            foreach (PlaneClassifications v in Enum.GetValues(typeof(PlaneClassifications)))
                if (v != PlaneClassifications.None && (c & v) == v) return v.ToString();
            return c.ToString();
        }

        // The manager is switched off again: the probe leaves the session as it found it.
        public void Stop() { if (mgr != null) mgr.enabled = false; }
    }

    // ----------------------------------------------------------------------------------- anchors from AR Foundation

    public sealed class UnityAnchorSource : IAnchorSource
    {
        readonly ARAnchorManager mgr;
        readonly Transform head;
        public UnityAnchorSource(ARAnchorManager mgr, Transform head) { this.mgr = mgr; this.head = head; }

        public string Start()
        {
            if (mgr == null) return "the scene has no anchor manager";
            mgr.enabled = true;
            if (mgr.subsystem == null) return "no anchor provider is running (the Anchors feature is off or the runtime lacks it)";
            return null;
        }

        readonly Dictionary<string, ARAnchor> live = new Dictionary<string, ARAnchor>();

        // Waits for an Awaitable from a coroutine: the continuation sets the flag the coroutine polls.
        static IEnumerator Await<T>(Awaitable<T> aw, Action<T> done)
        {
            bool finished = false; Exception err = null; T val = default(T);
            Run(aw, v => { val = v; finished = true; }, e => { err = e; finished = true; });
            while (!finished) yield return null;
            if (err != null) throw err;
            done(val);
        }

        static async void Run<T>(Awaitable<T> aw, Action<T> ok, Action<Exception> fail)
        {
            try { ok(await aw); } catch (Exception e) { fail(e); }
        }

        public IEnumerator Create(AnchorResult r)
        {
            var pose = new Pose(head.position + head.forward * 1.0f, Quaternion.identity);
            Result<ARAnchor> res = default(Result<ARAnchor>);
            yield return Await(mgr.TryAddAnchorAsync(pose), v => res = v);
            if (res.status.IsError()) { r.Error = res.status.statusCode.ToString(); yield break; }
            r.Ok = true; r.Id = res.value.trackableId.ToString();
            live[r.Id] = res.value;
            var p = res.value.transform.position; r.HasPose = true; r.X = p.x; r.Y = p.y; r.Z = p.z;
        }

        public IEnumerator Save(string anchorId, AnchorResult r)
        {
            ARAnchor a;
            if (!live.TryGetValue(anchorId, out a)) { r.Error = "no such live anchor"; yield break; }
            Result<SerializableGuid> res = default(Result<SerializableGuid>);
            yield return Await(mgr.TrySaveAnchorAsync(a), v => res = v);
            if (res.status.IsError()) { r.Error = res.status.statusCode.ToString(); yield break; }
            r.Ok = true; r.Id = res.value.ToString();
        }

        public IEnumerator ListSaved(AnchorListResult r)
        {
            Result<NativeArray<SerializableGuid>> res = default(Result<NativeArray<SerializableGuid>>);
            yield return Await(mgr.TryGetSavedAnchorIdsAsync(Allocator.Temp), v => res = v);
            if (res.status.IsError()) { r.Error = res.status.statusCode.ToString(); yield break; }
            r.Ok = true;
            foreach (var g in res.value) r.Ids.Add(g.ToString());
            res.value.Dispose();
        }

        public IEnumerator Load(string savedId, AnchorResult r)
        {
            SerializableGuid guid;
            if (!TryParse(savedId, out guid)) { r.Error = "unreadable saved id"; yield break; }
            Result<ARAnchor> res = default(Result<ARAnchor>);
            yield return Await(mgr.TryLoadAnchorAsync(guid), v => res = v);
            if (res.status.IsError()) { r.Error = res.status.statusCode.ToString(); yield break; }
            r.Ok = true; r.Id = res.value.trackableId.ToString();
            var p = res.value.transform.position; r.HasPose = true; r.X = p.x; r.Y = p.y; r.Z = p.z;
            live[r.Id] = res.value;
        }

        // Removes the live anchor from the session; the saved copy is untouched.
        public IEnumerator Release(string liveId, AnchorResult r)
        {
            ARAnchor a;
            if (!live.TryGetValue(liveId, out a)) { r.Error = "no such live anchor"; yield break; }
            bool removed = mgr.TryRemoveAnchor(a);
            live.Remove(liveId);
            yield return null;
            yield return null;
            r.Ok = removed; if (!removed) r.Error = "the provider refused to remove it";
        }

        public IEnumerator Erase(string savedId, AnchorResult r)
        {
            SerializableGuid guid;
            if (!TryParse(savedId, out guid)) { r.Error = "unreadable saved id"; yield break; }
            XRResultStatus res = default(XRResultStatus);
            yield return Await(mgr.TryEraseAnchorAsync(guid), v => res = v);
            r.Ok = !res.IsError(); if (!r.Ok) r.Error = res.statusCode.ToString();
        }

        // SerializableGuid prints as two 16-digit hex halves joined by a dash.
        static bool TryParse(string s, out SerializableGuid g)
        {
            g = default(SerializableGuid);
            if (string.IsNullOrEmpty(s)) return false;
            var parts = s.Split('-');
            if (parts.Length != 2) return false;
            ulong a, b;
            if (!ulong.TryParse(parts[0], System.Globalization.NumberStyles.HexNumber, null, out a)) return false;
            if (!ulong.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber, null, out b)) return false;
            g = new SerializableGuid(a, b);
            return true;
        }
    }

    // ----------------------------------------------------------------------------------- light estimation

    public sealed class UnityLightSource : ILightSource
    {
        readonly ARCameraManager mgr;
        IDictionary<string, double> last;
        public UnityLightSource(ARCameraManager mgr) { this.mgr = mgr; }

        public string Start()
        {
            if (mgr == null) return "the scene has no camera manager";
            mgr.enabled = true;
            if (mgr.subsystem == null) return "no camera provider is running (the Camera feature is off or the runtime lacks it)";
            mgr.requestedLightEstimation = LightEstimation.AmbientIntensity | LightEstimation.AmbientColor;
            if (mgr.currentLightEstimation == LightEstimation.None && !mgr.subsystem.subsystemDescriptor.supportsWorldTrackingAmbientIntensityLightEstimation && !mgr.subsystem.subsystemDescriptor.supportsAverageBrightness)
                return "the camera provider does not offer light estimation";
            mgr.frameReceived += OnFrame;
            return null;
        }

        void OnFrame(ARCameraFrameEventArgs e)
        {
            var d = new Dictionary<string, double>();
            var l = e.lightEstimation;
            if (l.averageBrightness.HasValue) d["average_brightness"] = l.averageBrightness.Value;
            if (l.averageColorTemperature.HasValue) d["color_temperature_k"] = l.averageColorTemperature.Value;
            if (l.averageIntensityInLumens.HasValue) d["intensity_lumens"] = l.averageIntensityInLumens.Value;
            if (l.colorCorrection.HasValue) { d["color_r"] = l.colorCorrection.Value.r; d["color_g"] = l.colorCorrection.Value.g; d["color_b"] = l.colorCorrection.Value.b; }
            if (d.Count > 0) last = d;
        }

        public IDictionary<string, double> Read() { var v = last; return v; }
        public void Stop() { if (mgr != null) { mgr.frameReceived -= OnFrame; mgr.enabled = false; } }
    }

    // ----------------------------------------------------------------------------------- hands (XR Hands)

    public sealed class UnityHandSource : IHandSource
    {
        XRHandSubsystem hands;
        bool startedByProbe;

        public string Start()
        {
            var l = new List<XRHandSubsystem>();
            SubsystemManager.GetSubsystems(l);
            if (l.Count == 0) return "no hand tracking subsystem is registered (the Hand Tracking feature is off or the runtime lacks it)";
            hands = l[0];
            if (!hands.running) { hands.Start(); startedByProbe = true; }
            return hands.running ? null : "the hand tracking subsystem would not start (permission refused?)";
        }

        // The subsystem is stopped again only if the probe was the one that started it.
        void StopIfStarted() { if (hands != null && startedByProbe && hands.running) hands.Stop(); startedByProbe = false; }

        static int Joints(XRHand h)
        {
            if (!h.isTracked) return 0;
            int n = 0;
            for (int i = XRHandJointID.BeginMarker.ToIndex() + 1; i < XRHandJointID.EndMarker.ToIndex(); i++)
            {
                var j = h.GetJoint(XRHandJointIDUtility.FromIndex(i));
                if ((j.trackingState & XRHandJointTrackingState.Pose) != 0) n++;
            }
            return n;
        }

        static double Pinch(XRHand h)
        {
            if (!h.isTracked) return -1;
            Pose a, b;
            if (!h.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out a) || !h.GetJoint(XRHandJointID.IndexTip).TryGetPose(out b)) return -1;
            return Vector3.Distance(a.position, b.position);
        }

        public HandFrame Read()
        {
            var f = new HandFrame();
            if (hands == null) return f;
            f.LeftTracked = hands.leftHand.isTracked; f.RightTracked = hands.rightHand.isTracked;
            f.LeftJoints = Joints(hands.leftHand); f.RightJoints = Joints(hands.rightHand);
            f.LeftPinchM = Pinch(hands.leftHand); f.RightPinchM = Pinch(hands.rightHand);
            return f;
        }

        public void Stop() { StopIfStarted(); }
    }

    // ----------------------------------------------------------------------------------- environment blend, dimming, display

    // OpenXRFeature keeps the blend mode calls protected; a subclass nobody instantiates gives them a public door.
    public abstract class BlendAccess : OpenXRFeature
    {
        public static XrEnvironmentBlendMode Get() { return GetEnvironmentBlendMode(); }
        public static void Set(XrEnvironmentBlendMode m) { SetEnvironmentBlendMode(m); }
    }

    public sealed class UnityBlendSource : IBlendSource
    {
        public IEnumerator Probe(BlendResult r)
        {
            if (!XrGuard.OpenXrActive) { r.Error = "no OpenXR loader is running"; yield break; }
            XrEnvironmentBlendMode original;
            try { original = BlendAccess.Get(); }
            catch (Exception e) { r.Error = e.GetType().Name + ": " + e.Message; yield break; }
            r.Original = original.ToString();
            try
            {
                foreach (var m in new[] { XrEnvironmentBlendMode.Opaque, XrEnvironmentBlendMode.Additive, XrEnvironmentBlendMode.AlphaBlend })
                {
                    r.Tried.Add(m.ToString());
                    bool set = false;
                    try { BlendAccess.Set(m); set = true; } catch (Exception) { }
                    if (!set) continue;
                    for (int i = 0; i < 5; i++) yield return null;
                    string got = "";
                    try { got = BlendAccess.Get().ToString(); } catch (Exception) { }
                    if (got == m.ToString()) r.Accepted.Add(m.ToString());
                }
            }
            finally
            {
                // Put back whatever the wearer had, also when the probe is cut short; the read-back below is what is reported.
                r.RestoreAttempted = true;
                try { BlendAccess.Set(original); } catch (Exception) { }
            }
            for (int i = 0; i < 3; i++) yield return null;
            try { r.ReadBack = BlendAccess.Get().ToString(); } catch (Exception) { }
        }
    }

    public sealed class UnityDimmingSource : IDimmingSource
    {
        public string Start()
        {
            if (!XrGuard.OpenXrActive) return "no OpenXR loader is running";
            var r = ARCameraFeature.TryGetSupportedDimmingLevels(Allocator.Temp);
            if (r.status.IsError()) return "dimming is not available: " + r.status.statusCode;
            if (r.value.IsCreated) r.value.Dispose();
            return null;
        }

        public float[] Levels()
        {
            XrGuard.Require();
            var r = ARCameraFeature.TryGetSupportedDimmingLevels(Allocator.Temp);
            if (r.status.IsError()) return new float[0];
            var a = r.value.ToArray(); r.value.Dispose();
            return a;
        }

        public float Current()
        {
            XrGuard.Require();
            var r = ARCameraFeature.TryGetGlobalDimmingLevel();
            if (r.status.IsError()) throw new ProbeUnsupportedException("the current dimming level cannot be read: " + r.status.statusCode);
            return r.value;
        }

        public bool Request(float level) { XrGuard.Require(); return !ARCameraFeature.TryRequestGlobalDimmingLevel(level).IsError(); }
    }

    public sealed class UnityDisplaySource : IDisplaySource
    {
        XRDisplaySubsystem display;
        readonly Camera cam;
        public UnityDisplaySource(Camera cam) { this.cam = cam; }

        public string Start()
        {
            var l = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(l);
            foreach (var d in l) if (d.running) { display = d; break; }
            if (display == null && l.Count > 0) display = l[0];
            return display == null ? "no XR display subsystem is running" : null;
        }

        public float[] RefreshRates()
        {
            NativeArray<float> rates;
            var st = display.TryGetSupportedDisplayRefreshRates(Allocator.Temp, out rates);
            if (st.IsError()) return null;
            var a = rates.ToArray(); rates.Dispose();
            return a;
        }

        public float CurrentRefreshRate()
        {
            float hz;
            return display.TryGetDisplayRefreshRate(out hz) ? hz : 0f;
        }

        public EyeInfo[] Eyes()
        {
            var w = XRSettings.eyeTextureWidth; var h = XRSettings.eyeTextureHeight;
            if (w <= 0 || h <= 0 || cam == null) return null;
            var eyes = new EyeInfo[2];
            for (int i = 0; i < 2; i++)
            {
                var m = cam.GetStereoProjectionMatrix(i == 0 ? Camera.StereoscopicEye.Left : Camera.StereoscopicEye.Right);
                // Tangents of the four half-angles from an off-axis projection matrix.
                double l = (m[0, 2] - 1.0) / m[0, 0], r = (m[0, 2] + 1.0) / m[0, 0], b = (m[1, 2] - 1.0) / m[1, 1], t = (m[1, 2] + 1.0) / m[1, 1];
                eyes[i] = new EyeInfo { Width = w, Height = h, FovLeftDeg = -Math.Atan(l) * Mathf.Rad2Deg, FovRightDeg = Math.Atan(r) * Mathf.Rad2Deg, FovUpDeg = Math.Atan(t) * Mathf.Rad2Deg, FovDownDeg = -Math.Atan(b) * Mathf.Rad2Deg };
            }
            return eyes;
        }

        public double RecommendedScale() { return XRSettings.eyeTextureResolutionScale; }
    }
}
