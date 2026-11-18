using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

namespace XrProbe
{
    // Where a small fact that must survive a restart is kept: one text file per key in the app's own files folder.
    public sealed class FileProbeStore : IProbeStore
    {
        readonly string dir;
        public FileProbeStore(string dir) { this.dir = dir; }

        string PathOf(string key)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) key = key.Replace(c, '_');
            return Path.Combine(dir, key + ".txt");
        }

        public string Read(string key)
        {
            try { var p = PathOf(key); return File.Exists(p) ? File.ReadAllText(p).Trim() : null; } catch (Exception) { return null; }
        }

        public void Write(string key, string value)
        {
            try { Directory.CreateDirectory(dir); File.WriteAllText(PathOf(key), value); } catch (Exception) { }
        }
    }

    // Asks for the runtime permissions the scene features need, one at a time, with a plain reason on the panel before each dialog. A refusal is the wearer's
    // choice: it is recorded as "denied (your choice)", never as a failure, and the probes that depend on it are skipped.
    public sealed class PermissionProbe : ProbeBase
    {
        public static readonly string[][] Wanted =
        {
            new[] { PlaneSurveyProbe.Permission, "planes, anchors and light estimation", "Next, Android will ask to allow SCENE UNDERSTANDING (coarse). The probe needs it to look for planes, anchors and light estimates. It reads shapes and light levels; no pictures." },
            new[] { "android.permission.SCENE_UNDERSTANDING_FINE", "depth and meshing", "Next, Android will ask to allow SCENE UNDERSTANDING (fine). Some runtimes ask for it before they answer about depth and meshing. No pictures are kept." },
            new[] { HandTrackingProbe.Permission, "hand tracking", "Next, Android will ask to allow HAND TRACKING. The probe needs it to count tracked hand joints and see a pinch. Nothing about your hands is kept except the counts." },
        };
        public override string Id { get { return "permissions"; } }
        public override string Group { get { return "runtime"; } }
        public override string Name { get { return "Runtime permissions"; } }
        public override double TimeoutSeconds { get { return 360; } }

        public override IEnumerator Run(ProbeContext ctx)
        {
            if (!AndroidBridge.OnAndroid) throw new ProbeUnsupportedException("not running on an Android device");
            int granted = 0;
            foreach (var w in Wanted)
            {
                if (AndroidBridge.Has(w[0])) { ctx.Shared.SetPermission(w[0], true); ctx.Set(w[0].Replace("android.permission.", ""), "granted"); granted++; continue; }
                ctx.Prompt(w[2]);
                yield return ctx.Wait(5);
                ctx.Prompt("");
                bool ok = false;
                yield return AndroidBridge.Request(w[0], g => ok = g);
                ctx.Shared.SetPermission(w[0], ok);
                ctx.Set(w[0].Replace("android.permission.", ""), ok ? "granted" : Words.Denied);
                if (ok) granted++;
            }
            if (granted == Wanted.Length) ctx.Pass("all " + Wanted.Length + " scene and hand permissions granted");
            else ctx.Info((Wanted.Length - granted) + " of " + Wanted.Length + " " + Words.Denied + ": the rows that need them are skipped, not failed");
        }
    }

    // The both-eyes check: a quad that is red in the left eye and blue in the right one, in front of the wearer for a few seconds. The answer is the wearer's
    // (close one eye, then the other), so the row is information.
    public sealed class EyeCheckProbe : ProbeBase
    {
        readonly ProbeApp app;
        readonly double seconds;
        public EyeCheckProbe(ProbeApp app, double seconds = 15) { this.app = app; this.seconds = seconds; }
        public override string Id { get { return "display.eyecheck"; } }
        public override string Group { get { return "display"; } }
        public override string Name { get { return "Both eyes: left red, right blue (looked at, not measured)"; } }
        public override double TimeoutSeconds { get { return seconds + 15; } }

        public override IEnumerator Run(ProbeContext ctx)
        {
            if (app.eyeQuad == null) { ctx.Skip("no colour-test quad in the scene"); yield break; }
            var t = app.head.transform;
            Vector3 f = Vector3.ProjectOnPlane(t.forward, Vector3.up); if (f.sqrMagnitude < 1e-4f) f = Vector3.forward; f.Normalize();
            app.eyeQuad.transform.position = t.position + f * 1.2f;
            app.eyeQuad.transform.rotation = Quaternion.LookRotation(f, Vector3.up);
            app.eyeQuad.transform.localScale = new Vector3(0.8f, 0.5f, 1f);
            ctx.OnRestore(() => { if (app.eyeQuad != null) app.eyeQuad.SetActive(false); });
            try
            {
                app.eyeQuad.SetActive(true);
                ctx.Prompt("Close your RIGHT eye: you should see RED. Close your LEFT eye: BLUE. (" + seconds.ToString("0") + " s)");
                yield return ctx.Wait(seconds);
            }
            finally
            {
                app.eyeQuad.SetActive(false);
                ctx.Prompt("");
            }
            ctx.Info("shown for " + seconds.ToString("0") + " s; the wearer reports what each eye saw (left should be red, right blue)");
        }
    }

    // Draws the synthetic figure for the soak and sways it.
    public sealed class SoakFigureSource : ISoakSource
    {
        readonly Material[] materials;
        readonly Transform head;
        readonly Func<float?> floorY;
        SyntheticFigure figure;
        GameObject driver;

        public SoakFigureSource(Material[] materials, Transform head, Func<float?> floorY) { this.materials = materials; this.head = head; this.floorY = floorY; }

        public string Description { get { return figure != null ? figure.Describe() : "a generated figure of about 31,700 triangles, 55 bones and 4 materials (synthetic load, not a real character)"; } }

        public string Start()
        {
            float fy = floorY() ?? (head.position.y - 1.6f);
            figure = SyntheticFigure.Build(materials);
            Vector3 fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            figure.Root.transform.position = new Vector3(head.position.x, fy, head.position.z) + fwd * 2.4f;
            figure.Root.transform.rotation = Quaternion.LookRotation(-fwd, Vector3.up);
            driver = new GameObject("ProbeSoakDriver");
            driver.AddComponent<Sway>().figure = figure;
            return null;
        }

        public void Stop()
        {
            if (driver != null) UnityEngine.Object.Destroy(driver);
            if (figure != null) figure.Destroy();
            driver = null; figure = null;
        }

        sealed class Sway : MonoBehaviour
        {
            public SyntheticFigure figure;
            void Update() { if (figure != null) figure.Animate(Time.time); }
        }
    }

    // The probe app: shows the first screen, waits for a deliberate start, runs the suite, shows a world-locked panel, writes the reports, and puts back
    // whatever it changed when it ends, pauses or is destroyed.
    public class ProbeApp : MonoBehaviour
    {
        public Camera head;
        public ARPlaneManager planes;
        public ARAnchorManager anchors;
        public ARCameraManager cameraManager;
        public Material[] soakMaterials;
        public ProbePanel panel;
        [Tooltip("A quad drawn with the left/right eye test shader (left eye red, right eye blue); shown for the both-eyes check.")]
        public GameObject eyeQuad;
        [Tooltip("Seconds of the soak; 0 skips it. The launch extra soak_seconds overrides it.")]
        public float soakSeconds = 600f;
        [Tooltip("all, survey or soak. The launch extra mode overrides it.")]
        public string mode = "all";

        public ProbeReport Report { get; private set; }
        public ProbeRunner Runner { get; private set; }
        public StartGate Gate { get; private set; }
        public bool Finished { get; private set; }
        public IAnchorSource AnchorSource { get; private set; }

        float startTime;
        string headline = "starting";
        bool cleanupDone;
        Vector3 lastMouse;
        XRHandSubsystem pinchHands;
        ReportSaver saver;

        string FilesDir { get { return Application.persistentDataPath; } }

        // A deliberate press: the puck's trackpad click (a mouse click or touch), a key, or a pinch when hand tracking is already allowed.
        public bool PressedNow()
        {
            if (Input.GetMouseButtonDown(0) || Input.touchCount > 0 || Input.anyKeyDown) return true;
            return PinchNow();
        }

        // Any sign that some input works at all (a finger moving on the trackpad counts).
        bool InputSignal()
        {
            bool moved = (Input.mousePosition - lastMouse).sqrMagnitude > 0.5f;
            lastMouse = Input.mousePosition;
            return moved || Input.anyKey || Input.touchCount > 0;
        }

        bool PinchNow()
        {
            try
            {
                if (!AndroidBridge.Has(HandTrackingProbe.Permission)) return false;
                if (pinchHands == null)
                {
                    var l = new List<XRHandSubsystem>();
                    SubsystemManager.GetSubsystems(l);
                    if (l.Count == 0) return false;
                    pinchHands = l[0];
                }
                if (!pinchHands.running) return false;
                foreach (var h in new[] { pinchHands.leftHand, pinchHands.rightHand })
                {
                    Pose a, b;
                    if (h.isTracked && h.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out a) && h.GetJoint(XRHandJointID.IndexTip).TryGetPose(out b) && Vector3.Distance(a.position, b.position) < 0.02f) return true;
                }
            }
            catch (Exception) { }
            return false;
        }

        // Stops the soak early.
        public bool StopRequested() { return PressedNow(); }

        IEnumerator Start()
        {
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;
            Report = new ProbeReport { Version = Application.version, StartedUtc = DateTime.UtcNow.ToString("o") };
            FillDevice(Report);
            var clock = new StopwatchClock();
            startTime = Time.realtimeSinceStartup;
            saver = new ReportSaver(FilesDir, line => Debug.LogWarning("XrProbe: " + line));
            Runner = new ProbeRunner(Report, clock, new FileProbeStore(Path.Combine(FilesDir, "probe-state")), SaveReport, line => Debug.Log("XrProbe: " + line));
            Runner.PromptSink = s => { if (panel != null) panel.Prompt = s; };
            Runner.BannerSink = s => { if (panel != null) panel.Banner = s; };
            Runner.RowChanged += _ => RefreshPanel();
            AnchorSource = new UnityAnchorSource(anchors, head.transform);

            string m = AndroidBridge.IntentExtra("mode"); if (!string.IsNullOrEmpty(m)) mode = m;
            string ss = AndroidBridge.IntentExtra("soak_seconds"); float v; if (!string.IsNullOrEmpty(ss) && float.TryParse(ss, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) soakSeconds = v;

            // A moment for tracking to settle, then the panel goes where the wearer is looking and stays there.
            yield return new WaitForSeconds(1.5f);
            if (panel != null) panel.PlaceInFrontOf(head);

            // The first screen. Nothing runs until the wearer starts it on purpose.
            Gate = new StartGate(Time.realtimeSinceStartup);
            headline = "waiting for you to start";
            while (Gate.Current != StartGate.State.Started)
            {
                Gate.Tick(Time.realtimeSinceStartup, InputSignal(), PressedNow());
                if (panel != null) panel.Intro = string.Join("\n", ProbeTexts.Intro) + "\n\n<color=#FFF1A8>" + Gate.StatusLine(Time.realtimeSinceStartup) + "</color>";
                yield return null;
            }
            if (panel != null) panel.Intro = null;
            yield return null; // the press that started the run must not also stop or answer anything
            Report.Notice = "";
            headline = "mode " + mode;

            var probes = ProbeSuite.Build(this, mode, soakSeconds);
            yield return Runner.RunAll(probes);
            Report.Complete = true;
            Report.FinishedUtc = DateTime.UtcNow.ToString("o");
            SaveReport(Report);
            Finished = true;
            headline = "finished";
            RefreshPanel();
            Debug.Log("XrProbe: finished; the reports are in the app's files folder");
            // The same report as log lines, so it can be read with adb logcat if the files cannot be pulled.
            foreach (var l in ReportSaver.LogLines(Report)) Debug.Log("XrProbe: " + l);

            // The final screen: one click erases the test anchor this run left on the device.
            if (panel != null) panel.Footer = "Clean-up: click the trackpad to erase the test anchor this run saved on the device.";
            while (!cleanupDone)
            {
                if (Input.GetMouseButtonDown(0) || Input.touchCount > 0)
                {
                    yield return Runner.RunProbe(new AnchorCleanupProbe(AnchorSource));
                    cleanupDone = true;
                    SaveReport(Report);
                    if (panel != null) panel.Footer = "Clean-up done. You can take the glasses off.";
                    RefreshPanel();
                }
                yield return null;
            }
        }

        void Update()
        {
            if (panel != null)
            {
                // After the first screen, a click brings the panel back in front of you (not while a soak could read it as a stop).
                if (Gate != null && Gate.Current == StartGate.State.Started && !Finished && Input.GetMouseButtonDown(0)) panel.PlaceInFrontOf(head);
                panel.Headline = headline + "   " + FormatTime(Time.realtimeSinceStartup - startTime);
                panel.Tick();
            }
        }

        // The glasses come off or the system menu opens: put back whatever a probe changed, and keep what is known so far.
        void OnApplicationPause(bool paused)
        {
            if (!paused) return;
            if (Runner != null) Runner.RestoreAll();
            if (Report != null) SaveReport(Report);
        }

        void OnApplicationQuit()
        {
            if (Runner != null) Runner.Abort();
            if (Report != null) SaveReport(Report);
        }

        void OnDestroy()
        {
            if (Runner != null) Runner.Abort();
        }

        static string FormatTime(float s) { int t = (int)s; return (t / 60).ToString("00") + ":" + (t % 60).ToString("00"); }

        void RefreshPanel() { if (panel != null) panel.Show(Report, Runner != null ? Runner.Current : null); }

        public void SaveReport(ProbeReport r)
        {
            if (saver == null) saver = new ReportSaver(FilesDir, line => Debug.LogWarning("XrProbe: " + line));
            saver.Save(r);
            if (panel != null) panel.Warning = saver.WarningLine;
        }

        static void FillDevice(ProbeReport r)
        {
            r.Device["unity_version"] = Application.unityVersion;
            r.Device["platform"] = Application.platform.ToString();
            r.Device["device_model"] = SystemInfo.deviceModel;
            r.Device["operating_system"] = SystemInfo.operatingSystem;
            r.Device["processor"] = SystemInfo.processorType + " x" + SystemInfo.processorCount;
            r.Device["memory_mb"] = SystemInfo.systemMemorySize.ToString();
            r.Device["graphics_device"] = SystemInfo.graphicsDeviceName + " (" + SystemInfo.graphicsDeviceType + " " + SystemInfo.graphicsDeviceVersion + ")";
            r.Device["xr_loader"] = XRGeneralSettings.Instance != null && XRGeneralSettings.Instance.Manager != null && XRGeneralSettings.Instance.Manager.activeLoader != null ? XRGeneralSettings.Instance.Manager.activeLoader.name : "none active";
            try
            {
                DeviceFacts.Collect(AndroidBridge.IdentityJson(), r.Device);
            }
            catch (Exception) { }
        }

        // The y of the biggest floor plane the survey saw (the soak stands the figure on it), or null.
        public float? FloorY()
        {
            var y = Runner != null ? Runner.Shared.FloorY : null;
            return y.HasValue ? (float?)y.Value : null;
        }
    }
}
