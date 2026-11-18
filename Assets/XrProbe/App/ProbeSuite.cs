using System.Collections.Generic;

namespace XrProbe
{
    // The probes in the order they run. Real platform sources everywhere; every one answers "unsupported" off the unit.
    public static class ProbeSuite
    {
        public static List<IProbe> Build(ProbeApp app, string mode, float soakSeconds)
        {
            var l = new List<IProbe>();
            bool survey = mode != "soak";
            bool soak = (mode == "all" || mode == "soak") && soakSeconds > 0f;
            var xr = new UnityOpenXrInfo();
            var device = new AndroidDeviceSource();
            if (survey)
            {
                l.Add(new PermissionProbe());
                l.Add(new OpenXrRuntimeProbe(xr));
                l.Add(new ExtensionListProbe(xr));
                l.Add(new FeatureProbe("depth", "Depth map", UnityFeatureFlags.Depth));
                l.Add(new FeatureProbe("meshing", "Scene meshing", UnityFeatureFlags.Meshing));
                l.Add(new FeatureProbe("planes", "Planes (provider flags)", UnityFeatureFlags.Planes));
                l.Add(new FeatureProbe("anchors", "Anchors (provider flags)", UnityFeatureFlags.Anchors));
                l.Add(new FeatureProbe("light", "Light estimation (provider flags)", UnityFeatureFlags.LightEstimation));
                l.Add(new FeatureProbe("markers", "Marker and QR tracking", UnityFeatureFlags.Markers));
                l.Add(new FeatureProbe("objects", "Object tracking and its categories", UnityFeatureFlags.Objects));
                l.Add(new FeatureProbe("geospatial", "Geospatial", UnityFeatureFlags.Geospatial));
                var display = new UnityDisplaySource(app.head);
                l.Add(new EyeProbe(display));
                l.Add(new RefreshRateProbe(display));
                l.Add(new BlendModeProbe(new UnityBlendSource()));
                l.Add(new EyeCheckProbe(app, 15));
                l.Add(new DimmingProbe(new UnityDimmingSource()));
                l.Add(new MicrophoneProbe(new AndroidAudioSource()));
                l.Add(new CameraProbe(new AndroidCameraSource()));
                l.Add(new HandTrackingProbe(new UnityHandSource(), 30));
                l.Add(new LightEstimationProbe(new UnityLightSource(app.cameraManager), 10));
                l.Add(new AnchorProbe(app.AnchorSource ?? new UnityAnchorSource(app.anchors, app.head.transform)));
                l.Add(new PlaneSurveyProbe(new UnityPlaneSource(app.planes), 60));
                l.Add(new DeviceMonitorProbe(device, 60, 5));
            }
            if (soak) l.Add(new SoakProbe(new SoakFigureSource(app.soakMaterials, app.head.transform, app.FloorY), device, soakSeconds, app.StopRequested));
            return l;
        }
    }
}
