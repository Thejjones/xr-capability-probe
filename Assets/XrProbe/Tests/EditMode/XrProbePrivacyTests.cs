using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using XrProbe;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

// Privacy, the first screen and the synthetic figure: what the report, the panel and the log may and may not carry.
public partial class XrProbePrivacyTests
{
    // Values that must never appear anywhere. Each is the kind of thing a platform call or an exception message could carry by accident.
    static readonly string[] Secrets =
    {
        "R58M12ABCDE7", "9X7K2M4Q1Z",                                 // serial-like
        "Jane",                                                       // a personal device name (also in "jane" paths below)
        "aa:bb:cc:dd:ee:ff",                                          // MAC
        "HomeNet5G",                                                  // SSID
        "jane.doe@example.com",                                       // e-mail
        "35.681236", "139.767125",                                    // coordinates
        "123e4567-e89b-12d3-a456-426614174000",                       // GUID
        "0123456789abcdef", "fedcba9876543210",                       // Unity trackable / saved anchor id halves
        "/data/user/0", "AppData", "/Users/jane", "/sdcard/Android/data/com.someone.app",  // user and app paths
        "uid=10234", "10234", "192.168.1.23", "com.someone.app",      // uid, IP, package
    };

    static readonly string Poison = "R58M12ABCDE7 SN-9X7K2M4Q1Z Jane's XREAL Aura aa:bb:cc:dd:ee:ff ssid=HomeNet5G jane.doe@example.com at 35.681236, 139.767125 "
        + "id 123e4567-e89b-12d3-a456-426614174000 anchor 0123456789abcdef-fedcba9876543210 in /data/user/0/com.someone.app/files/x and C:\\Users\\jane\\AppData\\Local\\x "
        + "and /Users/jane/work and /sdcard/Android/data/com.someone.app/files uid=10234 from 192.168.1.23";

    static void AssertClean(string where, string text)
    {
        foreach (var s in Secrets)
            Assert.IsFalse(text.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0, where + " contains \"" + s + "\":\n" + text.Substring(Math.Max(0, text.IndexOf(s, StringComparison.OrdinalIgnoreCase) - 60), Math.Min(160, text.Length - Math.Max(0, text.IndexOf(s, StringComparison.OrdinalIgnoreCase) - 60))));
    }

    // ---------------------------------------------------------------- the sanitiser

    [TestCase("mail me at jane.doe@example.com now", "jane.doe")]
    [TestCase("device aa:bb:cc:dd:ee:ff paired", "aa:bb")]
    [TestCase("host 192.168.1.23 refused", "192.168")]
    [TestCase("joined ssid=HomeNet5G", "HomeNet5G")]
    [TestCase("joined SSID: \"My Home Wifi\"", "My Home")]
    [TestCase("id 123e4567-e89b-12d3-a456-426614174000 failed", "123e4567")]
    [TestCase("saved 0123456789abcdef-fedcba9876543210", "0123456789abcdef")]
    [TestCase("failed at /data/user/0/com.someone.app/files/x.json", "/data/user")]
    [TestCase("failed at C:\\Users\\jane\\AppData\\x.txt", "jane")]
    [TestCase("failed at /Users/jane/work/a", "jane")]
    [TestCase("denied for uid=10234", "10234")]
    [TestCase("at com.someone.app.Activity.onCreate", "someone")]
    [TestCase("here: 35.681236, 139.767125", "35.681236")]
    [TestCase("lat 35.68 lon -139.76", "35.68")]
    [TestCase("serial R58M12ABCDE7 read", "R58M12ABCDE7")]
    [TestCase("it is Jane's AirPods Pro", "Jane")]
    public void Sanitiser_RemovesEachKindOfIdentifier(string text, string mustGo)
    {
        string c = ProbeSanitizer.Clean(text);
        StringAssert.DoesNotContain(mustGo, c);
    }

    [Test]
    public void Sanitiser_LeavesTheTextTheReportIsMadeOfAlone()
    {
        foreach (var ok in new[]
        {
            "XR_ANDROID_depth_texture", "1920x1080 px", "OpenXR 1.1.0, Unity plug-in 1.19.0; 30 extensions enabled, 80 available", "arm64-v8a", "6000.6.9f1", "android.permission.CAMERA",
            "frame time p50 13.9 ms, p95 14.2, p99 14.9, max 22.1", "0.45 as a ratio (1.0 = at the throttling limit)", "thermal status 0 to 1 (0 none, 1 light, 2 moderate, 3 severe)",
            "builtin mic \"array\": channels 1/2/4, rates 16000/48000 Hz", "camera in use: frames counted and dropped", "SCENE_UNDERSTANDING_COARSE", "2026-11-05", "Adreno (TM) 830",
        })
            Assert.AreEqual(ok, ProbeSanitizer.Clean(ok), "must not be changed: " + ok);
        Assert.AreEqual("planes.survey.label.floor", ProbeSanitizer.CleanId("planes.survey.label.floor"));
        Assert.AreEqual("device.thermal_battery.thermal", ProbeSanitizer.CleanId("device.thermal_battery.thermal"));
    }

    [Test]
    public void DeviceFacts_KeepOnlyTheAllowedKeys_AndCleanTheValues()
    {
        string json = "{\"manufacturer\":\"Acme\",\"model\":\"Glasses 1\",\"serial\":\"R58M12ABCDE7\",\"account\":\"jane.doe@example.com\",\"device_name\":\"Jane's XREAL Aura\",\"wifi_ssid\":\"HomeNet5G\",\"android_release\":\"15\",\"sdk_int\":35,\"security_patch\":\"2026-11-05\",\"abis\":[\"arm64-v8a\"],\"android_id\":\"0123456789abcdef\"}";
        var d = new SortedDictionary<string, string>();
        DeviceFacts.Collect(json, d);
        CollectionAssert.AreEquivalent(new[] { "manufacturer", "model", "android_release", "sdk_int", "security_patch", "abis" }, d.Keys);
        AssertClean("device facts", string.Join("|", d.Select(kv => kv.Key + "=" + kv.Value).ToArray()));
        // A poisoned value under an allowed key is cleaned too.
        var p = new SortedDictionary<string, string>();
        DeviceFacts.Collect("{\"model\":\"" + Poison.Replace("\\", "\\\\") + "\"}", p);
        AssertClean("poisoned model", p["model"]);
    }

    // ---------------------------------------------------------------- the identifiers test: every probe against fakes that carry poison

    sealed class PoisonXr : IOpenXrInfo
    {
        public string RuntimeName { get { return "Android XR"; } }
        public string RuntimeVersion { get { return Poison; } }
        public string ApiVersion { get { return "1.1.0 " + Poison; } }
        public string PluginVersion { get { return "1.19.0"; } }
        public string[] EnabledExtensions() { return new[] { "XR_X_" + Poison, "XR_ANDROID_depth_texture" }; }
        public string[] AvailableExtensions() { return new[] { "XR_ANDROID_depth_texture", "XR_MARKER_192.168.1.23", "XR_ANDROID_trackables" }; }
    }

    sealed class PoisonPlanes : IPlaneSource
    {
        public ManualProbeClock Clock;
        public string Start() { return null; }
        public IList<PlaneInfo> Snapshot() { return new List<PlaneInfo> { new PlaneInfo { Id = "123e4567-e89b-12d3-a456-426614174000", Label = "Floor", AreaM2 = 4, HasHeight = true, HeightY = 0 }, new PlaneInfo { Id = Poison, Label = "Wall " + Poison, AreaM2 = 2 } }; }
        public void Stop() { }
    }

    sealed class PoisonAnchors : IAnchorSource
    {
        const string Guid1 = "0123456789abcdef-fedcba9876543210";
        public string Start() { return null; }
        public IEnumerator Create(AnchorResult r) { yield return null; r.Ok = true; r.Id = Guid1; r.HasPose = true; }
        public IEnumerator Save(string id, AnchorResult r) { yield return null; r.Ok = true; r.Id = "123e4567-e89b-12d3-a456-426614174000"; }
        public IEnumerator ListSaved(AnchorListResult r) { yield return null; r.Ok = true; r.Ids.Add("123e4567-e89b-12d3-a456-426614174000"); }
        public IEnumerator Release(string id, AnchorResult r) { yield return null; r.Ok = true; }
        public IEnumerator Load(string id, AnchorResult r) { yield return null; r.Ok = true; r.Id = "fedcba9876543210-0123456789abcdef"; r.HasPose = true; }
        public IEnumerator Erase(string id, AnchorResult r) { yield return null; r.Ok = false; r.Error = Poison; }
    }

    sealed class PoisonBlend : IBlendSource
    {
        public IEnumerator Probe(BlendResult r) { r.Original = "Additive " + Poison; r.Tried.Add("Opaque " + Poison); r.RestoreAttempted = true; r.ReadBack = "Additive " + Poison; yield return null; }
    }

    sealed class PoisonDisplay : IDisplaySource
    {
        public string Start() { return null; }
        public float[] RefreshRates() { return new[] { 72f, 90f }; }
        public float CurrentRefreshRate() { return 72f; }
        public EyeInfo[] Eyes() { return new[] { new EyeInfo { Width = 1920, Height = 1200 }, new EyeInfo { Width = 1920, Height = 1200 } }; }
        public double RecommendedScale() { return 1; }
    }

    sealed class PoisonCamera : ICameraSource
    {
        public string ListJson()
        {
            return "{\"cameras\":[{\"id\":\"0\",\"facing\":\"back " + Poison.Replace("\\", "\\\\") + "\",\"sizes\":[\"1920x1080\",\"" + Poison.Replace("\\", "\\\\") + "\"],\"fps_ranges\":[\"15-30\"],\"capabilities\":[0],\"physical_ids\":[\"" + Poison.Replace("\\", "\\\\") + "\"]}]}";
        }
        public IEnumerator RequestPermission(Action<bool> g) { yield return null; g(true); }
        public IEnumerator CountFrames(string id, double seconds, FrameTest r) { yield return null; r.Ok = false; r.Error = Poison; }
    }

    sealed class PoisonAudio : IAudioSource
    {
        public string ListJson()
        {
            string p = Poison.Replace("\\", "\\\\");
            return "{\"inputs\":[{\"id\":7,\"type\":\"builtin mic\",\"name\":\"" + p + "\",\"channel_counts\":[2],\"sample_rates\":[48000]},{\"id\":9,\"type\":\"bluetooth sco\",\"name\":\"" + p + "\",\"channel_counts\":[1],\"sample_rates\":[16000]}]}";
        }
        public IEnumerator RequestPermission(Action<bool> g) { yield return null; g(true); }
        public IEnumerator ReadTest(double seconds, int channels, AudioTest r) { yield return null; r.Ok = false; r.Error = Poison; }
    }

    sealed class PoisonDevice : IDeviceSource
    {
        public string Start() { return null; }
        public DeviceSample Read() { return new DeviceSample { ThermalStatus = 0, ThermalHeadroom = 0.3, BatteryPercent = 70, BatteryTempC = 30 }; }
    }

    sealed class PoisonSoak : ISoakSource
    {
        public string Description { get { return "a generated figure; " + Poison; } }
        public string Start() { return null; }
        public void Stop() { }
    }

    sealed class PoisonDimming : IDimmingSource
    {
        public string Reason; float level = 0.3f;
        public string Start() { return Reason; }
        public float[] Levels() { return new[] { 0f, 0.5f, 1f }; }
        public float Current() { return level; }
        public bool Request(float l) { level = l; return true; }
    }

    sealed class PoisonHands : IHandSource
    {
        public string Reason;
        public string Start() { return Reason; }
        public HandFrame Read() { return new HandFrame { RightTracked = true, RightJoints = 26, RightPinchM = 0.01 }; }
        public void Stop() { }
    }

    sealed class PoisonLight : ILightSource
    {
        public string Reason;
        public string Start() { return Reason; }
        public IDictionary<string, double> Read() { return new Dictionary<string, double> { { "average_brightness", 0.5 }, { Poison, 1.0 } }; }
        public void Stop() { }
    }

    sealed class PoisonThrower : ProbeBase
    {
        readonly Exception e;
        public PoisonThrower(Exception e) { this.e = e; }
        public override string Id { get { return "poison.thrower"; } }
        public override string Group { get { return "poison"; } }
        public override string Name { get { return "Throws " + Poison; } }
        public override IEnumerator Run(ProbeContext ctx) { ctx.Log(Poison); ctx.Prompt(Poison); ctx.Set("note", Poison); ctx.Set("notes", new[] { Poison }); yield return null; throw e; }
    }

    [Test]
    public void Identifiers_NoProbeWritesAnIdentifierToTheJsonTheTextTheLogTheLabelsOrThePanel()
    {
        var report = new ProbeReport { Notice = "sample " + Poison };
        var clock = new ManualProbeClock();
        var log = new List<string>(); var prompts = new List<string>(); var banners = new List<string>();
        var runner = new ProbeRunner(report, clock, new MemoryProbeStore(), null, log.Add) { PromptSink = prompts.Add, BannerSink = banners.Add };
        runner.Shared.SetPermission(PlaneSurveyProbe.Permission, true);
        var planes = new PoisonPlanes { Clock = clock };
        // The eye check and the permission probe need the app object and the scene objects it drives; their text is fixed, so the poison comes in through the
        // report notice, the prompts and everything else the run shares with them.
        var appGo = new GameObject("identifiers-app");
        var eyeApp = appGo.AddComponent<ProbeApp>();
        eyeApp.head = appGo.AddComponent<Camera>();
        eyeApp.eyeQuad = new GameObject("eye-quad");
        var probes = new List<IProbe>
        {
            new OpenXrRuntimeProbe(new PoisonXr()), new ExtensionListProbe(new PoisonXr()),
            new FeatureProbe("depth", "Depth " + Poison, () => { var a = new FeatureAnswer { Supported = true, Detail = Poison }; a.Values["k"] = Poison; a.Values["arr"] = new[] { Poison }; return a; }),
            new FeatureProbe("bad", "Bad", () => { throw new NotSupportedException(Poison); }),
            new PlaneSurveyProbe(planes, 5), new AnchorProbe(new PoisonAnchors()), new AnchorCleanupProbe(new PoisonAnchors()),
            new BlendModeProbe(new PoisonBlend()), new EyeProbe(new PoisonDisplay()), new RefreshRateProbe(new PoisonDisplay()),
            new CameraProbe(new PoisonCamera(), 1), new MicrophoneProbe(new PoisonAudio()),
            new DeviceMonitorProbe(new PoisonDevice(), 10, 5), new SoakProbe(new PoisonSoak(), new PoisonDevice(), 8),
            new DimmingProbe(new PoisonDimming()), new DimmingProbe(new PoisonDimming { Reason = Poison }),
            new HandTrackingProbe(new PoisonHands(), 5), new HandTrackingProbe(new PoisonHands { Reason = Poison }, 5),
            new LightEstimationProbe(new PoisonLight(), 4), new LightEstimationProbe(new PoisonLight { Reason = Poison }, 4),
            new PermissionProbe(), new EyeCheckProbe(eyeApp, 2),
            new PoisonThrower(new InvalidOperationException(Poison)), new PoisonThrower(new MissingMethodException(Poison)),
            new FeatureProbe("nonex", "Nope", () => { throw new ProbeUnsupportedException(Poison); }),
        };
        var store = new MemoryProbeStore(); store.Write(AnchorProbe.StoreKey, "fedcba9876543210-0123456789abcdef");
        runner = new ProbeRunner(report, clock, store, null, log.Add) { PromptSink = prompts.Add, BannerSink = banners.Add };
        ProbeRunner.RunToEnd(runner.RunAll(probes), clock);
        report.Device["note"] = Poison;
        string json = report.ToJson(), text = report.ToText();
        AssertClean("the JSON report", json);
        AssertClean("the text report", text);
        AssertClean("the log lines", string.Join("\n", log.ToArray()));
        AssertClean("the prompts and banners", string.Join("\n", prompts.Concat(banners).ToArray()));
        var go = new GameObject("identifiers-panel");
        try
        {
            var panel = ProbePanel.Create(go.transform);
            panel.Show(report, null);
            panel.visibleRows = 200;
            AssertClean("the panel", panel.Compose(report, null));
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
        UnityEngine.Object.DestroyImmediate(eyeApp.eyeQuad); UnityEngine.Object.DestroyImmediate(appGo);
        foreach (var id in new[] { "display.dimming", "hands", "light.estimation", "permissions", "display.eyecheck" }) Assert.IsNotNull(report.Find(id), "the identifiers test must cover " + id);
        // The report is still a report: rows exist, and it parses.
        Assert.Greater(JObject.Parse(json)["rows"].Count(), 25);
        Assert.AreEqual(0, report.Rows.Count(r => r.Status == ProbeStatus.Running));
    }

    // Strings a real runtime or build gives. None of them is personal, so none may be turned into a placeholder.
    [TestCase("Android XR OpenXR Runtime")]
    [TestCase("Monado(XRT) by Collabora et al!")]
    [TestCase("OpenXR runtime 1.0.26.0, API 1.1.43, Unity plug-in 1.19.0.1")]
    [TestCase("Runtime version 2.3.1.4012 (build 20260105)")]
    [TestCase("XR_ANDROID_depth_texture")]
    [TestCase("XR_EXT_hand_tracking, XR_KHR_android_create_instance, XR_FB_display_refresh_rate")]
    [TestCase("XR_ANDROID_trackables_marker")]
    [TestCase("ext.hand_tracking.v1")]
    [TestCase("Adreno (TM) 750 (Vulkan 1.3.128)")]
    [TestCase("Qualcomm Adreno (TM) 740")]
    [TestCase("Mali-G720-Immortalis MC12")]
    [TestCase("Vulkan 1.3.275, driver 512.615.0.1")]
    [TestCase("Android OS 14 / API-34 (UP1A.231005.007/eng.build.20240101)")]
    [TestCase("SQ3A.220705.003.A1")]
    [TestCase("TQ1A.230105.002")]
    [TestCase("arm64-v8a,armeabi-v7a,armeabi")]
    [TestCase("Qualcomm Snapdragon XR2+ Gen 2 x8")]
    [TestCase("6000.6.9f1")]
    [TestCase("UnityEngine.XR.OpenXR.OpenXRLoader")]
    [TestCase("android.permission.CAMERA")]
    [TestCase("XREAL One Pro")]
    [TestCase("Samsung SM-I610")]
    [TestCase("2026-11-05")]
    public void Sanitiser_LeavesRealisticRuntimeAndBuildStringsAlone(string text)
    {
        Assert.AreEqual(text, ProbeSanitizer.Clean(text));
    }

    [TestCase("runtime_version", "1.0.26.0")]
    [TestCase("api_version", "1.1.43.7")]
    [TestCase("unity_plugin_version", "1.19.0.1")]
    public void Sanitiser_AFieldThatIsAVersionByItsKeyKeepsAFourPartVersion_EverywhereItIsWritten(string key, string version)
    {
        var report = new ProbeReport();
        var row = report.Add(new ProbeRow { Id = "v", Group = "g", Name = "N", Status = ProbeStatus.Pass });
        row.Set(key, ProbeSanitizer.CleanValue(version, key));
        StringAssert.Contains(version, report.ToJson());
        StringAssert.Contains(version, report.ToText());
        Assert.AreEqual("<ip>", ProbeSanitizer.Clean("10.1.2.3"), "the same digits with no version cue are an address");
        Assert.AreEqual("from <ip> now", ProbeSanitizer.Clean("from 10.1.2.3 now"));
    }

    // ---------------------------------------------------------------- the report that cannot be written

    [Test]
    public void ReportSaver_AFailedWriteIsSaidOnceInTheLog_ShownOnThePanel_AndTheRowsStayOnThePanel()
    {
        string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "Library", "XrProbeSaverTest"));
        if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true);
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            string blocked = System.IO.Path.Combine(dir, "a-file");
            System.IO.File.WriteAllText(blocked, "x");             // a file where the folder should be: the write must fail
            var log = new List<string>();
            var saver = new ReportSaver(System.IO.Path.Combine(blocked, "sub"), log.Add);
            var report = new ProbeReport();
            report.Add(new ProbeRow { Id = "a", Group = "g", Name = "Alpha", Status = ProbeStatus.Pass, Detail = "worked" });
            Assert.IsFalse(saver.Save(report));
            Assert.IsFalse(saver.Save(report));
            Assert.AreEqual(1, log.Count, "the same failure is said once, not after every row");
            StringAssert.StartsWith("report could not be written: ", log[0]);
            AssertClean("the log", string.Join("\n", log.ToArray()));
            StringAssert.Contains("report could not be written", saver.WarningLine);

            var go = new GameObject("saver-panel");
            try
            {
                var panel = ProbePanel.Create(go.transform);
                panel.Warning = saver.WarningLine;
                string text = panel.Compose(report, null);
                StringAssert.Contains("report could not be written", text);
                StringAssert.Contains("Alpha", text);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }

            // A folder that works clears the warning, and nothing is left half-written.
            var good = new ReportSaver(dir, log.Add);
            Assert.IsTrue(good.Save(report));
            Assert.AreEqual("", good.WarningLine);
            Assert.AreEqual(2, System.IO.Directory.GetFiles(dir).Count(f => !f.EndsWith("a-file")), "the JSON and the text, and no .tmp");
        }
        finally { System.IO.Directory.Delete(dir, true); }
    }

    [Test]
    public void ReportSaver_TheLogLines_CarryTheWholeTextReport_WithoutIdentifiers()
    {
        var report = new ProbeReport { Notice = Poison };
        report.Device["note"] = Poison;
        var row = report.Add(new ProbeRow { Id = "x", Group = "g", Name = "Row " + Poison, Status = ProbeStatus.Fail, Detail = Poison });
        row.Set("k", Poison);
        var lines = ReportSaver.LogLines(report);
        Assert.Greater(lines.Length, 4);
        foreach (var l in lines) StringAssert.StartsWith("report| ", l);
        AssertClean("the report log lines", string.Join("\n", lines));
    }

    [Test]
    public void Panel_AtTheEnd_ShowsASummaryTheWearerCanReadOff_FailuresFirst_AndHowTheLensesWereLeft()
    {
        var report = new ProbeReport { Complete = true };
        report.Add(new ProbeRow { Id = "ok.1", Group = "g", Name = "Fine", Status = ProbeStatus.Pass, Detail = "good" });
        report.Add(new ProbeRow { Id = "hands", Group = "input", Name = "Hands", Status = ProbeStatus.Fail, Detail = "no hand seen" });
        report.Add(new ProbeRow { Id = "feature.depth", Group = "f", Name = "Depth", Status = ProbeStatus.Unsupported, Detail = "no provider" });
        var dim = report.Add(new ProbeRow { Id = "display.dimming", Group = "display", Name = "Dimming", Status = ProbeStatus.Pass });
        dim.Set("original_restored", false);
        var go = new GameObject("summary-panel");
        try
        {
            string t = ProbePanel.Create(go.transform).Compose(report, null);
            StringAssert.Contains("DONE", t);
            StringAssert.Contains("Hands", t);
            StringAssert.Contains("unsupported: feature.depth", t);
            StringAssert.Contains("display.dimming: original NOT CONFIRMED RESTORED", t);
            Assert.Less(t.IndexOf("Hands", StringComparison.Ordinal), t.IndexOf("unsupported:", StringComparison.Ordinal), "failures come before the unsupported list");
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    // ---------------------------------------------------------------- the report under managed stripping

    [Test]
    public void Stripping_TheReportTypesAreKept_ByLinkXml_AndTheCoreSourceUsesNoReflectionSerialisation()
    {
        string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "XrProbe"));
        string xml = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "link.xml"));
        StringAssert.Contains("fullname=\"XrProbe.Core\" preserve=\"all\"", xml, "the report types live in the Core assembly");
        StringAssert.Contains("fullname=\"Newtonsoft.Json\" preserve=\"all\"", xml, "the report is built from JObject and JToken");
        // The JSON is built by hand from JObject/JToken; nothing serialises a probe type by reflection, which is what stripping would break.
        foreach (var f in System.IO.Directory.GetFiles(System.IO.Path.Combine(root, "Core"), "*.cs"))
        {
            string t = System.IO.File.ReadAllText(f);
            foreach (var bad in new[] { "JsonConvert.", ".ToObject<", "JsonProperty", "[Serializable]", "Activator.CreateInstance", "GetType().GetMethod" })
                Assert.IsFalse(t.Contains(bad), System.IO.Path.GetFileName(f) + " uses " + bad);
        }
        // And the same writer produces a report with every kind of value in it that parses back.
        var report = new ProbeReport { Complete = true };
        var row = report.Add(new ProbeRow { Id = "t", Group = "g", Name = "Types", Status = ProbeStatus.Info });
        row.Set("i", 3); row.Set("d", 0.12345); row.Set("f", 1.5f); row.Set("b", true); row.Set("s", "text"); row.Set("sa", new[] { "a", "b" }); row.Set("ia", new[] { 1, 2 }); row.Set("n", null);
        var s = new ProbeSample { T = 1 }; s.V["x"] = 2.5; report.AddSample("series", s);
        string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "Library", "XrProbeStripTest"));
        try
        {
            string err;
            Assert.IsTrue(ProbeReportWriter.Write(dir, report, out err), err);
            var j = JObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(dir, ProbeReportWriter.JsonName)));
            Assert.AreEqual(3, (int)j["rows"][0]["values"]["i"]);
            Assert.AreEqual(2, (int)j["rows"][0]["values"]["ia"][1]);
            Assert.AreEqual(2.5, (double)j["series"]["series"][0]["x"], 1e-9);
        }
        finally { if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true); }
        // What this cannot prove: that the IL2CPP build with High stripping on a unit writes the file, and that the storage folder accepts the write.
    }

    // ---------------------------------------------------------------- the application id and the build

    [Test]
    public void Identity_TheApplicationIdImpliesNoDomain_AndAPlaceholderOwnerRefusesABuild()
    {
        StringAssert.StartsWith("io.github.", ProbeIdentity.ApplicationId);
        StringAssert.EndsWith(".xrcapabilityprobe", ProbeIdentity.ApplicationId);
        StringAssert.Contains(ProbeIdentity.Owner, ProbeIdentity.ApplicationId);
        Assert.AreEqual("OWNER", ProbeIdentity.Placeholder);
        string reason;
        bool allowed = XrProbeBuild.OwnerAllowsBuild(new string[0], out reason);
        if (ProbeIdentity.OwnerIsPlaceholder)
        {
            Assert.IsFalse(allowed);
            StringAssert.Contains("placeholder", reason);
            StringAssert.Contains("-probeAllowPlaceholder", reason);
            Assert.IsTrue(XrProbeBuild.OwnerAllowsBuild(new[] { "-probeAllowPlaceholder" }, out reason), "a test build can still be asked for");
            Assert.IsFalse(XrProbeBuild.OwnerAllowsBuild(new[] { "-probeDev" }, out reason), "a development build is not a way round it");
        }
        else Assert.IsTrue(allowed);
    }

    // ---------------------------------------------------------------- the first screen

    [Test]
    public void IntroText_SaysWhatTheProbeDoes_NeverRunOnARetailUnit_SitStill_WhatIsAndIsNotRecorded_AndNotAffiliated()
    {
        string t = string.Join("\n", ProbeTexts.Intro);
        StringAssert.Contains("never been run on a retail unit", t);
        StringAssert.Contains("Sit down and stay still", t);
        StringAssert.Contains("lenses will dim and colours will", t);
        StringAssert.Contains("flash for a few seconds", t);
        StringAssert.Contains("Camera frames are counted and dropped", t);
        StringAssert.Contains("microphone level is", t);
        StringAssert.Contains("There is no network access at all", t);
        StringAssert.Contains("Not affiliated with XREAL, Google or Unity", t);
        StringAssert.Contains("stops by itself if the unit heats up", t);
        foreach (var line in ProbeTexts.Intro) Assert.LessOrEqual(System.Text.RegularExpressions.Regex.Replace(line, "<[^>]+>", "").Length, 72, "a line must fit the panel: " + line);
    }

    [Test]
    public void StartGate_OnlyAPressStartsIt_NeverATimer_AndSaysWhenNoInputWorks()
    {
        // Nothing happens by itself while input works: a finger that moves, no press, a long time.
        var g = new StartGate(0);
        for (int t = 0; t < 600; t++) g.Tick(t, true, false);
        Assert.AreEqual(StartGate.State.Waiting, g.Current, "input works, so the wearer is waited for");
        Assert.IsFalse(g.NoInputDetected);
        StringAssert.Contains("Nothing runs until you do", g.StatusLine(600));
        g.Tick(601, true, true);
        Assert.AreEqual(StartGate.State.Started, g.Current);

        // No input of any kind: nothing is started, however long it takes. After a minute the screen says so and goes on waiting.
        var h = new StartGate(0);
        h.Tick(59, false, false); Assert.IsFalse(h.NoInputDetected);
        h.Tick(60, false, false); Assert.IsTrue(h.NoInputDetected);
        StringAssert.Contains("No input detected: nothing was run", h.StatusLine(60));
        foreach (var t in new[] { 61.0, 70, 120, 3600, 86400 }) { h.Tick(t, false, false); Assert.AreEqual(StartGate.State.Waiting, h.Current, "no timer ever starts the run (t=" + t + ")"); }
        StringAssert.DoesNotContain("starts by itself", h.StatusLine(3600));
        StringAssert.DoesNotContain("countdown", h.StatusLine(3600).ToLowerInvariant());

        // Input that turns up late is seen, the message goes, and a press then starts it.
        h.Tick(3601, true, false); Assert.IsFalse(h.NoInputDetected); Assert.AreEqual(StartGate.State.Waiting, h.Current);
        h.Tick(3602, true, true); Assert.AreEqual(StartGate.State.Started, h.Current);
    }

    // ---------------------------------------------------------------- the synthetic figure

    [Test]
    public void SyntheticFigure_IsBuiltFromPrimitives_AtAnAvatarLikeLoad_WithNoImportedAssetBehindIt()
    {
        var f = SyntheticFigure.Build(null);
        try
        {
            Assert.AreEqual(55, f.Bones);
            Assert.AreEqual(4, f.Materials);
            Assert.That(f.Triangles, Is.InRange(25000, 40000));
            var smr = f.Root.GetComponentInChildren<SkinnedMeshRenderer>();
            Assert.IsNotNull(smr);
            Assert.AreEqual(55, smr.bones.Length);
            Assert.AreEqual(f.Triangles, Enumerable.Range(0, smr.sharedMesh.subMeshCount).Sum(i => (int)smr.sharedMesh.GetIndexCount(i) / 3));
            Assert.AreEqual(f.Vertices, smr.sharedMesh.vertexCount);
            Assert.AreEqual(f.Vertices, smr.sharedMesh.boneWeights.Length);
            Assert.AreEqual(55, smr.sharedMesh.bindposes.Length);
            StringAssert.Contains("synthetic load, not a real character", f.Describe());
            StringAssert.Contains(f.Triangles + " triangles", f.Describe());
            // It sways: the pose changes with time.
            var head = smr.bones[5]; Quaternion a = head.localRotation; f.Animate(1.3f); Assert.AreNotEqual(a, head.localRotation);
        }
        finally { f.Destroy(); }
    }

    [Test]
    public void TheProbeAssembliesReferenceNoModelLoaderNoNetworkAndNothingOutsideTheProbe()
    {
        string[] allowed = { "Unity", "UnityEngine", "UnityEditor", "System", "mscorlib", "netstandard", "Newtonsoft", "Mono.", "Microsoft.", "nunit", "XrProbe", "XrProbe" };
        foreach (var asm in new[] { typeof(ProbeReport).Assembly, typeof(UnityOpenXrInfo).Assembly, typeof(ProbeApp).Assembly })
            foreach (var r in asm.GetReferencedAssemblies())
            {
                string n = r.Name;
                Assert.IsTrue(allowed.Any(p => n.StartsWith(p, StringComparison.Ordinal)), asm.GetName().Name + " references " + n);
                Assert.IsFalse(n.Contains("WebRequest") || n.Contains("Http") || n.Contains("WebSockets") || n.Contains("Sockets"), asm.GetName().Name + " references network assembly " + n);
            }
    }
}
