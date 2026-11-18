using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using XrProbe;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

// The probe app without a unit: the report writer, the runner that turns every failure into a row, and each probe's
// answers against fake sources, including the path where the unit does not offer the thing.
public partial class XrProbeTests
{
    static string Dir { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "XrProbeTest")); } }

    [SetUp]
    public void SetUp() { if (Directory.Exists(Dir)) Directory.Delete(Dir, true); }

    [TearDown]
    public void TearDown() { if (Directory.Exists(Dir)) Directory.Delete(Dir, true); }

    // One probe through the runner on a manual clock, 60 frames a second. Returns the report.
    static ProbeReport Run(IProbe probe, MemoryProbeStore store = null, List<string> prompts = null)
    {
        var report = new ProbeReport();
        var clock = new ManualProbeClock();
        var runner = new ProbeRunner(report, clock, store ?? new MemoryProbeStore(), null, null);
        if (prompts != null) runner.PromptSink = prompts.Add;
        ProbeRunner.RunToEnd(runner.RunProbe(probe), clock);
        return report;
    }

    static ProbeRow Row(ProbeReport r, string id) { var x = r.Find(id); Assert.IsNotNull(x, "no row " + id + "; rows: " + string.Join(", ", r.Rows.Select(q => q.Id).ToArray())); return x; }

    // ---------------------------------------------------------------- the report and its writer

    [Test]
    public void Writer_WritesOneJsonAndOneTextReport_ReplacesThemAndLeavesNoTemporaryFile()
    {
        var rep = new ProbeReport { Version = "1.2.3", StartedUtc = "2026-11-18T00:00:00Z", FinishedUtc = "2026-11-18T00:10:00Z", Complete = true };
        rep.Device["device_model"] = "TestUnit";
        var row = rep.Add(new ProbeRow { Id = "a.b", Group = "grp", Name = "A thing", Status = ProbeStatus.Pass, Detail = "worked", DurationS = 1.5 });
        row.Set("count", 3); row.Set("names", new[] { "x", "y" }); row.Set("ratio", 0.123456789);
        rep.Add(new ProbeRow { Id = "a.c", Group = "grp", Name = "Another", Status = ProbeStatus.Unsupported, Detail = "unsupported: none" });
        var s = new ProbeSample { T = 1 }; s.V["status"] = 2; rep.AddSample("thermal", s);
        string err;
        Assert.IsTrue(ProbeReportWriter.Write(Dir, rep, out err), err);
        var json = JObject.Parse(File.ReadAllText(Path.Combine(Dir, ProbeReportWriter.JsonName)));
        Assert.AreEqual(1, (int)json["format"]);
        Assert.AreEqual("1.2.3", (string)json["version"]);
        Assert.IsTrue((bool)json["complete"]);
        Assert.AreEqual("TestUnit", (string)json["device"]["device_model"]);
        Assert.AreEqual(1, (int)json["summary"]["pass"]); Assert.AreEqual(1, (int)json["summary"]["unsupported"]);
        var rows = (JArray)json["rows"];
        Assert.AreEqual(2, rows.Count);
        Assert.AreEqual("pass", (string)rows[0]["status"]);
        Assert.AreEqual(3, (int)rows[0]["values"]["count"]);
        Assert.AreEqual("y", (string)rows[0]["values"]["names"][1]);
        Assert.AreEqual(0.1235, (double)rows[0]["values"]["ratio"], 1e-9);
        Assert.AreEqual(2, (int)json["series"]["thermal"][0]["status"]);
        var text = File.ReadAllText(Path.Combine(Dir, ProbeReportWriter.TextName));
        StringAssert.Contains("[PASS] A thing: worked", text);
        StringAssert.Contains("[UNSUPPORTED] Another", text);
        StringAssert.Contains("1 pass", text); StringAssert.Contains("complete: yes", text);
        StringAssert.Contains("device device_model: TestUnit", text);
        StringAssert.Contains("thermal.status", text);
        // A second write replaces both files and leaves nothing else behind.
        rep.Add(new ProbeRow { Id = "a.d", Group = "grp", Name = "Third", Status = ProbeStatus.Fail });
        Assert.IsTrue(ProbeReportWriter.Write(Dir, rep, out err), err);
        Assert.AreEqual(3, ((JArray)JObject.Parse(File.ReadAllText(Path.Combine(Dir, ProbeReportWriter.JsonName)))["rows"]).Count);
        CollectionAssert.AreEquivalent(new[] { ProbeReportWriter.JsonName, ProbeReportWriter.TextName }, Directory.GetFiles(Dir).Select(Path.GetFileName).ToArray());
    }

    [Test]
    public void Writer_ReportsAnUnwritableFolderInsteadOfThrowing()
    {
        Directory.CreateDirectory(Dir);
        string file = Path.Combine(Dir, "afile");
        File.WriteAllText(file, "x");
        string err;
        Assert.IsFalse(ProbeReportWriter.Write(Path.Combine(file, "sub"), new ProbeReport(), out err));
        Assert.IsNotEmpty(err);
    }

    // ---------------------------------------------------------------- the runner

    sealed class Throwing : ProbeBase
    {
        readonly Exception e; readonly bool afterYield;
        public Throwing(Exception e, bool afterYield = false) { this.e = e; this.afterYield = afterYield; }
        public override string Id { get { return "t"; } }
        public override string Group { get { return "g"; } }
        public override string Name { get { return "Throws"; } }
        public override IEnumerator Run(ProbeContext ctx) { if (afterYield) yield return null; throw e; }
    }

    [TestCase(typeof(ProbeUnsupportedException))]
    [TestCase(typeof(NotSupportedException))]
    [TestCase(typeof(NotImplementedException))]
    [TestCase(typeof(MissingMethodException))]
    [TestCase(typeof(EntryPointNotFoundException))]
    [TestCase(typeof(DllNotFoundException))]
    [TestCase(typeof(TypeLoadException))]
    public void Runner_AFeatureTheUnitLacks_BecomesAnUnsupportedRowNotACrash(Type exceptionType)
    {
        var ex = (Exception)Activator.CreateInstance(exceptionType, new object[] { "no such thing" });
        foreach (bool afterYield in new[] { false, true })
        {
            var r = Run(new Throwing(ex, afterYield));
            Assert.AreEqual(ProbeStatus.Unsupported, Row(r, "t").Status);
            StringAssert.StartsWith("unsupported:", Row(r, "t").Detail);
        }
    }

    [Test]
    public void Runner_AnyOtherException_BecomesAnErrorRowWithTheTypeAndMessage()
    {
        var r = Run(new Throwing(new InvalidOperationException("boom")));
        Assert.AreEqual(ProbeStatus.Error, Row(r, "t").Status);
        StringAssert.Contains("InvalidOperationException: boom", Row(r, "t").Detail);
    }

    sealed class Silent : ProbeBase
    {
        public override string Id { get { return "s"; } }
        public override string Group { get { return "g"; } }
        public override string Name { get { return "Silent"; } }
        public override IEnumerator Run(ProbeContext ctx) { ctx.Sub("extra", "Extra row"); yield return null; }
    }

    [Test]
    public void Runner_AProbeThatEndsWithoutAnAnswer_IsAnErrorAndSoIsItsOpenSubRow()
    {
        var r = Run(new Silent());
        Assert.AreEqual(ProbeStatus.Error, Row(r, "s").Status);
        Assert.AreEqual(ProbeStatus.Error, Row(r, "s.extra").Status);
        StringAssert.Contains("without an answer", Row(r, "s").Detail);
    }

    sealed class Hangs : ProbeBase
    {
        public override string Id { get { return "h"; } }
        public override string Group { get { return "g"; } }
        public override string Name { get { return "Hangs"; } }
        public override double TimeoutSeconds { get { return 5; } }
        public override IEnumerator Run(ProbeContext ctx) { while (true) yield return null; }
    }

    [Test]
    public void Runner_AProbeThatNeverEnds_IsCutAtItsTimeoutAsAnError()
    {
        var r = Run(new Hangs());
        Assert.AreEqual(ProbeStatus.Error, Row(r, "h").Status);
        StringAssert.Contains("timed out after 5 s", Row(r, "h").Detail);
    }

    [Test]
    public void Runner_GoesOnAfterAFailingProbe_SavesAfterEveryRow_AndTheNestedWaitsWork()
    {
        var report = new ProbeReport();
        var clock = new ManualProbeClock();
        int saves = 0, savedRows = 0;
        var runner = new ProbeRunner(report, clock, new MemoryProbeStore(), r => { saves++; savedRows = r.Rows.Count; }, null);
        var probes = new List<IProbe> { new Throwing(new InvalidOperationException("x")), new FeatureProbe("ok", "Fine", () => new FeatureAnswer { Supported = true, Detail = "yes" }), new Throwing(new NotSupportedException("no")) };
        ProbeRunner.RunToEnd(runner.RunAll(probes), clock);
        Assert.AreEqual(3, report.Rows.Count);
        Assert.AreEqual(new[] { ProbeStatus.Error, ProbeStatus.Pass, ProbeStatus.Unsupported }, report.Rows.Select(x => x.Status).ToArray());
        Assert.GreaterOrEqual(saves, 3);
        Assert.AreEqual(3, savedRows);
    }

    // ---------------------------------------------------------------- the runtime and the extension list

    sealed class FakeXr : IOpenXrInfo
    {
        public string Name = "", Version = "", Api = "", Plugin = "";
        public string[] Enabled = new string[0], Available = new string[0];
        public bool Throw;
        public string RuntimeName { get { if (Throw) throw new DllNotFoundException("openxr"); return Name; } }
        public string RuntimeVersion { get { return Version; } }
        public string ApiVersion { get { return Api; } }
        public string PluginVersion { get { return Plugin; } }
        public string[] EnabledExtensions() { return Enabled; }
        public string[] AvailableExtensions() { return Available; }
    }

    [Test]
    public void OpenXrRuntime_NoRuntimeOrNoLibrary_IsUnsupported_AndARuntimeIsReported()
    {
        Assert.AreEqual(ProbeStatus.Unsupported, Row(Run(new OpenXrRuntimeProbe(new FakeXr())), "runtime.openxr").Status);
        Assert.AreEqual(ProbeStatus.Unsupported, Row(Run(new OpenXrRuntimeProbe(new FakeXr { Throw = true })), "runtime.openxr").Status);
        var ok = Row(Run(new OpenXrRuntimeProbe(new FakeXr { Name = "Android XR", Version = "1.2", Api = "1.1.0", Plugin = "1.19.0", Enabled = new[] { "XR_KHR_x" }, Available = new[] { "XR_KHR_x", "XR_ANDROID_y" } })), "runtime.openxr");
        Assert.AreEqual(ProbeStatus.Pass, ok.Status);
        Assert.AreEqual("Android XR", ok.Values["runtime_name"]);
        Assert.AreEqual(2, ok.Values["extensions_available_count"]);
    }

    [Test]
    public void ExtensionList_NamesKeyExtensionsPresentAndAbsent_AndFlagsMarkerQrGeospatialNames()
    {
        Assert.AreEqual(ProbeStatus.Unsupported, Row(Run(new ExtensionListProbe(new FakeXr())), "runtime.extensions").Status);
        var xr = new FakeXr { Available = new[] { "XR_ANDROID_depth_texture", "XR_ANDROID_trackables_marker", "XR_ANDROID_geospatial_thing", "XR_KHR_other" }, Enabled = new[] { "XR_ANDROID_depth_texture" } };
        var r = Row(Run(new ExtensionListProbe(xr)), "runtime.extensions");
        Assert.AreEqual(ProbeStatus.Pass, r.Status);
        var present = (string[])r.Values["key_present"];
        Assert.IsTrue(present.Any(p => p.Contains("XR_ANDROID_depth_texture")));
        Assert.IsTrue(present.Any(p => p.Contains("XR_ANDROID_trackables_marker")));
        Assert.IsTrue(((string[])r.Values["key_absent"]).Any(p => p.Contains("XR_ANDROID_scene_meshing")));
        var named = (string[])r.Values["names_mentioning_markers_qr_objects_geospatial_etc"];
        CollectionAssert.Contains(named, "XR_ANDROID_geospatial_thing");
        CollectionAssert.Contains(named, "XR_ANDROID_trackables_marker");
    }

    // ---------------------------------------------------------------- feature flags

    [Test]
    public void Feature_SupportedUnsupportedThrowingAndNoAnswer()
    {
        var yes = new FeatureAnswer { Supported = true, Detail = "environment depth: Supported" }; yes.Values["descriptors"] = 1;
        var r = Row(Run(new FeatureProbe("depth", "Depth", () => yes)), "feature.depth");
        Assert.AreEqual(ProbeStatus.Pass, r.Status); Assert.AreEqual(1, r.Values["descriptors"]);
        Assert.AreEqual(ProbeStatus.Unsupported, Row(Run(new FeatureProbe("depth", "Depth", () => new FeatureAnswer { Supported = false, Detail = "no provider" })), "feature.depth").Status);
        Assert.AreEqual(ProbeStatus.Unsupported, Row(Run(new FeatureProbe("depth", "Depth", () => { throw new NotSupportedException("x"); })), "feature.depth").Status);
        Assert.AreEqual(ProbeStatus.Unsupported, Row(Run(new FeatureProbe("depth", "Depth", () => null)), "feature.depth").Status);
        Assert.AreEqual(ProbeStatus.Error, Row(Run(new FeatureProbe("depth", "Depth", () => { throw new NullReferenceException("bug"); })), "feature.depth").Status);
    }

    // ---------------------------------------------------------------- planes

    sealed class FakePlanes : IPlaneSource
    {
        public string Reason; public Func<double, IList<PlaneInfo>> At = t => new List<PlaneInfo>(); public ManualProbeClock Clock; public bool Stopped;
        public string Start() { return Reason; }
        public IList<PlaneInfo> Snapshot() { return At(Clock.Now); }
        public void Stop() { Stopped = true; }
    }

    [Test]
    public void Planes_NotOffered_IsUnsupported_NoneFound_IsFail_AndLabelsAndCountsAreRecordedOverTime()
    {
        Assert.AreEqual(ProbeStatus.Unsupported, Row(Run(new PlaneSurveyProbe(new FakePlanes { Reason = "no provider" })), "planes.survey").Status);

        var clock = new ManualProbeClock();
        var none = new FakePlanes { Clock = clock };
        var report = new ProbeReport();
        var runner = new ProbeRunner(report, clock, new MemoryProbeStore(), null, null);
        ProbeRunner.RunToEnd(runner.RunProbe(new PlaneSurveyProbe(none, 20)), clock);
        Assert.AreEqual(ProbeStatus.Fail, report.Find("planes.survey").Status);
        Assert.IsTrue(none.Stopped);

        clock = new ManualProbeClock();
        var fake = new FakePlanes { Clock = clock };
        fake.At = t =>
        {
            var l = new List<PlaneInfo>();
            if (t >= 3) l.Add(new PlaneInfo { Id = "f1", Label = "Floor", AreaM2 = 8 });
            if (t >= 10) { l.Add(new PlaneInfo { Id = "w1", Label = "Wall", AreaM2 = 5 }); l.Add(new PlaneInfo { Id = "w2", Label = "Wall", AreaM2 = 4 }); }
            if (t >= 15) l.Add(new PlaneInfo { Id = "x", Label = "Unlabeled", AreaM2 = 0.5 });
            return l;
        };
        report = new ProbeReport();
        var prompts = new List<string>();
        runner = new ProbeRunner(report, clock, new MemoryProbeStore(), null, null) { PromptSink = prompts.Add };
        ProbeRunner.RunToEnd(runner.RunProbe(new PlaneSurveyProbe(fake, 20)), clock);
        var main = report.Find("planes.survey");
        Assert.AreEqual(ProbeStatus.Pass, main.Status);
        Assert.AreEqual(4, main.Values["max_planes_at_once_count"]);
        Assert.AreEqual(4, main.Values["distinct_planes_count"]);
        Assert.AreEqual(3.0, (double)main.Values["first_plane_s"], 1.1);
        var wall = report.Find("planes.survey.label.wall");
        Assert.AreEqual(ProbeStatus.Pass, wall.Status); Assert.AreEqual(2, wall.Values["max_count"]); Assert.AreEqual(9.0, (double)wall.Values["max_area_m2"], 1e-9);
        Assert.AreEqual(ProbeStatus.Pass, report.Find("planes.survey.label.floor").Status);
        Assert.AreEqual(ProbeStatus.Info, report.Find("planes.survey.label.ceiling").Status, "an unseen label is information, not a failure");
        Assert.AreEqual(ProbeStatus.Info, report.Find("planes.survey.label.table").Status);
        var series = report.Series["planes"];
        Assert.GreaterOrEqual(series.Count, 19);
        Assert.AreEqual(4, series[series.Count - 1].V["total_count"]);
        Assert.AreEqual(2, series[series.Count - 1].V["count.Wall"]);
        Assert.IsTrue(fake.Stopped);
        Assert.AreEqual("", prompts.Last(), "the prompt is cleared at the end");
    }

    // ---------------------------------------------------------------- anchors

    sealed class FakeAnchors : IAnchorSource
    {
        public string Reason; public bool FailCreate, FailSave, DropFromList, FailLoad, FailRelease, LoadReturnsLive, LoadFar;
        public readonly HashSet<string> Saved = new HashSet<string>();
        public int Counter; public readonly List<string> Erased = new List<string>(); public readonly List<string> Released = new List<string>();
        public string Start() { return Reason; }
        public IEnumerator Create(AnchorResult r) { yield return null; if (FailCreate) { r.Error = "no tracking"; yield break; } r.Ok = true; r.Id = "live-" + (++Counter); r.HasPose = true; r.X = 1; r.Y = 1; r.Z = 1; }
        public IEnumerator Save(string id, AnchorResult r) { yield return null; if (FailSave) { r.Error = "storage full"; yield break; } r.Ok = true; r.Id = "saved-" + Counter; if (!DropFromList) Saved.Add(r.Id); }
        public IEnumerator ListSaved(AnchorListResult r) { yield return null; r.Ok = true; r.Ids.AddRange(Saved); }
        public IEnumerator Release(string id, AnchorResult r) { yield return null; if (FailRelease) { r.Error = "refused"; yield break; } r.Ok = true; Released.Add(id); }
        public IEnumerator Load(string id, AnchorResult r)
        {
            yield return null;
            if (FailLoad || !Saved.Contains(id)) { r.Error = "not found"; yield break; }
            r.Ok = true; r.Id = LoadReturnsLive ? "live-" + Counter : "loaded-" + id; r.HasPose = true; r.X = LoadFar ? 3 : 1.05; r.Y = 1; r.Z = 1;
        }
        public IEnumerator Erase(string id, AnchorResult r) { yield return null; Saved.Remove(id); Erased.Add(id); r.Ok = true; }
    }

    [Test]
    public void Anchors_Unsupported_CreateFails_SaveFails_AndTheFullPathAcrossTwoRuns()
    {
        Assert.AreEqual(ProbeStatus.Unsupported, Row(Run(new AnchorProbe(new FakeAnchors { Reason = "no provider" })), "anchors").Status);

        var r1 = Run(new AnchorProbe(new FakeAnchors { FailCreate = true }));
        Assert.AreEqual(ProbeStatus.Fail, Row(r1, "anchors.create").Status);
        Assert.AreEqual(ProbeStatus.Skipped, Row(r1, "anchors.persist").Status);
        var r2 = Run(new AnchorProbe(new FakeAnchors { FailSave = true }));
        Assert.AreEqual(ProbeStatus.Pass, Row(r2, "anchors.create").Status);
        Assert.AreEqual(ProbeStatus.Fail, Row(r2, "anchors.persist").Status);
        var r3 = Run(new AnchorProbe(new FakeAnchors { DropFromList = true }));
        Assert.AreEqual(ProbeStatus.Fail, Row(r3, "anchors.persist").Status, "saved but not in the saved list");

        // First run: everything passes except "restart", which says to run again. The saved id is remembered.
        var src = new FakeAnchors(); var store = new MemoryProbeStore();
        var first = Run(new AnchorProbe(src), store);
        Assert.AreEqual(ProbeStatus.Pass, Row(first, "anchors.create").Status);
        Assert.AreEqual(ProbeStatus.Pass, Row(first, "anchors.persist").Status);
        Assert.AreEqual(ProbeStatus.Pass, Row(first, "anchors.reload").Status);
        Assert.AreEqual(ProbeStatus.Info, Row(first, "anchors.restart").Status);
        StringAssert.Contains("clean-up", Row(first, "anchors.restart").Detail);
        Assert.AreEqual("saved-1", store.Read(AnchorProbe.StoreKey));
        CollectionAssert.Contains(src.Released, "live-1", "the live anchor is released before the saved copy is loaded");
        // Second run (a new app start, same provider storage): the earlier anchor loads again and is erased.
        var second = Run(new AnchorProbe(src), store);
        Assert.AreEqual(ProbeStatus.Pass, Row(second, "anchors.restart").Status);
        CollectionAssert.Contains(src.Erased, "saved-1");
        // A second run where the runtime has lost it: a fail, not a pass.
        var lost = new FakeAnchors(); store.Write(AnchorProbe.StoreKey, "saved-gone");
        Assert.AreEqual(ProbeStatus.Fail, Row(Run(new AnchorProbe(lost), store), "anchors.restart").Status);
    }

    [Test]
    public void Anchors_ReloadCannotBeAnsweredByTheLiveAnchor()
    {
        // The load hands back the very anchor that was released: that is not a reload.
        var same = Row(Run(new AnchorProbe(new FakeAnchors { LoadReturnsLive = true })), "anchors.reload");
        Assert.AreEqual(ProbeStatus.Fail, same.Status);
        StringAssert.Contains("not a reload", same.Detail);
        // The live anchor cannot be released: a reload would prove nothing, so it is not attempted and not a pass.
        var stuck = Row(Run(new AnchorProbe(new FakeAnchors { FailRelease = true })), "anchors.reload");
        Assert.AreEqual(ProbeStatus.Info, stuck.Status);
        // It comes back far from where it was made: a fail with the distance.
        var far = Row(Run(new AnchorProbe(new FakeAnchors { LoadFar = true })), "anchors.reload");
        Assert.AreEqual(ProbeStatus.Fail, far.Status);
        Assert.Greater((double)far.Values["distance_from_original_m"], 1.0);
        // A proper reload carries the distance too, small.
        var ok = Row(Run(new AnchorProbe(new FakeAnchors())), "anchors.reload");
        Assert.AreEqual(ProbeStatus.Pass, ok.Status);
        Assert.Less((double)ok.Values["distance_from_original_m"], 0.1);
    }

    [Test]
    public void Anchors_Labels_AreShortHashesThatChangeEveryRun_AndNeverTheRealId()
    {
        var a = Run(new AnchorProbe(new FakeAnchors()));
        var b = Run(new AnchorProbe(new FakeAnchors()));
        string da = Row(a, "anchors.persist").Detail, db = Row(b, "anchors.persist").Detail;
        StringAssert.DoesNotContain("saved-1", da);
        StringAssert.DoesNotContain("live-1", Row(a, "anchors.create").Detail);
        StringAssert.Contains("#", da);
        Assert.AreNotEqual(da, db, "the same saved id gets a different label in another run, so two reports cannot be linked");
    }

    [Test]
    public void Anchors_Cleanup_ErasesTheSavedAnchorAndForgetsIt_AndSaysWhenThereIsNone()
    {
        var src = new FakeAnchors(); var store = new MemoryProbeStore();
        Run(new AnchorProbe(src), store);
        Assert.IsNotEmpty(store.Read(AnchorProbe.StoreKey));
        var r = Row(Run(new AnchorCleanupProbe(src), store), "anchors.cleanup");
        Assert.AreEqual(ProbeStatus.Pass, r.Status);
        CollectionAssert.Contains(src.Erased, "saved-1");
        Assert.IsEmpty(store.Read(AnchorProbe.StoreKey));
        Assert.AreEqual(ProbeStatus.Info, Row(Run(new AnchorCleanupProbe(src), store), "anchors.cleanup").Status);
    }

    [Test]
    public void PermissionDenied_IsReportedAsYourChoice_AndTheRowsThatNeedItAreSkippedNotFailed()
    {
        // The probes ask the shared state; the permission probe (Android only) writes it. Here it is written by hand.
        foreach (var make in new Func<IProbe>[] {
            () => new PlaneSurveyProbe(new FakePlanes { Clock = new ManualProbeClock() }, 5),
            () => new AnchorProbe(new FakeAnchors()),
            () => new LightEstimationProbe(new FakeLight { GiveAfter = 1, Clock = new ManualProbeClock() }, 5),
            () => new HandTrackingProbe(new FakeHands { Clock = new ManualProbeClock(), At = t => new HandFrame() }, 5) })
        {
            var report = new ProbeReport(); var clock = new ManualProbeClock();
            var runner = new ProbeRunner(report, clock, new MemoryProbeStore(), null, null);
            runner.Shared.SetPermission(PlaneSurveyProbe.Permission, false);
            runner.Shared.SetPermission(HandTrackingProbe.Permission, false);
            var probe = make();
            ProbeRunner.RunToEnd(runner.RunProbe(probe), clock);
            var row = report.Rows[0];
            Assert.AreEqual(ProbeStatus.Skipped, row.Status, probe.Id);
            StringAssert.Contains("denied (your choice)", row.Detail);
            Assert.AreEqual(1, report.Rows.Count, "no failing sub-rows were made");
        }
    }

    // ---------------------------------------------------------------- light, hands, blend, dimming

    sealed class FakeLight : ILightSource
    {
        public string Reason; public double GiveAfter = -1; public ManualProbeClock Clock;
        public string Start() { return Reason; }
        public IDictionary<string, double> Read() { return GiveAfter >= 0 && Clock.Now >= GiveAfter ? new Dictionary<string, double> { { "average_brightness", 0.6 }, { "color_temperature_k", 4200 } } : null; }
        public void Stop() { }
    }

    static ProbeReport RunClocked(Func<ManualProbeClock, IProbe> make, MemoryProbeStore store = null)
    {
        var report = new ProbeReport(); var clock = new ManualProbeClock();
        var runner = new ProbeRunner(report, clock, store ?? new MemoryProbeStore(), null, null);
        ProbeRunner.RunToEnd(runner.RunProbe(make(clock)), clock);
        return report;
    }

    [Test]
    public void Light_Unsupported_NoValue_AndAValue()
    {
        Assert.AreEqual(ProbeStatus.Unsupported, Row(RunClocked(c => new LightEstimationProbe(new FakeLight { Reason = "no camera provider", Clock = c })), "light.estimation").Status);
        Assert.AreEqual(ProbeStatus.Fail, Row(RunClocked(c => new LightEstimationProbe(new FakeLight { Clock = c })), "light.estimation").Status);
        var r = RunClocked(c => new LightEstimationProbe(new FakeLight { GiveAfter = 2, Clock = c }));
        Assert.AreEqual(ProbeStatus.Pass, Row(r, "light.estimation").Status);
        Assert.AreEqual(0.6, (double)Row(r, "light.estimation").Values["average_brightness"], 1e-9);
        Assert.IsTrue(r.Series.ContainsKey("light"));
    }

    sealed class FakeHands : IHandSource
    {
        public string Reason; public Func<double, HandFrame> At; public ManualProbeClock Clock;
        public string Start() { return Reason; }
        public HandFrame Read() { return At(Clock.Now); }
        public void Stop() { }
    }

    [Test]
    public void Hands_Unsupported_NoHand_JointsWithoutPinch_AndTrackedPinch()
    {
        Assert.AreEqual(ProbeStatus.Unsupported, Row(RunClocked(c => new HandTrackingProbe(new FakeHands { Reason = "no subsystem", Clock = c })), "hands").Status);
        var none = RunClocked(c => new HandTrackingProbe(new FakeHands { Clock = c, At = t => new HandFrame() }, 10));
        Assert.AreEqual(ProbeStatus.Fail, Row(none, "hands").Status); Assert.AreEqual(ProbeStatus.Fail, Row(none, "hands.pinch").Status);
        var open = RunClocked(c => new HandTrackingProbe(new FakeHands { Clock = c, At = t => new HandFrame { RightTracked = true, RightJoints = 26, RightPinchM = 0.08 } }, 10));
        Assert.AreEqual(ProbeStatus.Pass, Row(open, "hands").Status); Assert.AreEqual(ProbeStatus.Fail, Row(open, "hands.pinch").Status);
        var few = RunClocked(c => new HandTrackingProbe(new FakeHands { Clock = c, At = t => new HandFrame { LeftTracked = true, LeftJoints = 7 } }, 10));
        Assert.AreEqual(ProbeStatus.Fail, Row(few, "hands").Status);
        var pinch = RunClocked(c => new HandTrackingProbe(new FakeHands { Clock = c, At = t => new HandFrame { RightTracked = true, RightJoints = 26, RightPinchM = t > 3 ? 0.01 : 0.09 } }, 30));
        Assert.AreEqual(ProbeStatus.Pass, Row(pinch, "hands").Status); Assert.AreEqual(ProbeStatus.Pass, Row(pinch, "hands.pinch").Status);
        Assert.Less((double)Row(pinch, "hands.pinch").Values["first_pinch_s"], 5.0);
    }

    // A blend source that behaves like the real one: it changes a state, restores it in a finally block and reads it back.
    sealed class FakeBlend : IBlendSource
    {
        public string Error = ""; public string Current = "Additive"; public string[] Accept = new string[0]; public bool IgnoreRestore;
        public int Frames = 5;
        public IEnumerator Probe(BlendResult r)
        {
            r.Error = Error; if (Error.Length > 0) yield break;
            string original = Current; r.Original = original;
            try
            {
                foreach (var m in new[] { "Opaque", "Additive", "AlphaBlend" })
                {
                    r.Tried.Add(m);
                    if (Accept.Contains(m)) Current = m;
                    for (int i = 0; i < Frames; i++) yield return null;
                    if (Current == m) r.Accepted.Add(m);
                }
            }
            finally
            {
                r.RestoreAttempted = true;
                if (!IgnoreRestore) Current = original;
            }
            yield return null;
            r.ReadBack = Current;
        }
    }

    [Test]
    public void Blend_Unsupported_TheModesAcceptedAreListed_AndTheOriginalIsRestoredAndSaidSo()
    {
        Assert.AreEqual(ProbeStatus.Unsupported, Row(Run(new BlendModeProbe(new FakeBlend { Error = "no native config" })), "display.blend").Status);
        var src = new FakeBlend { Accept = new[] { "Additive", "Opaque" }, Current = "Additive" };
        var r = Row(Run(new BlendModeProbe(src)), "display.blend");
        Assert.AreEqual(ProbeStatus.Pass, r.Status); Assert.AreEqual("Additive", r.Values["current_at_start"]);
        CollectionAssert.AreEqual(new[] { "Opaque", "Additive" }, (string[])r.Values["accepted_when_set"]);
        Assert.AreEqual(true, r.Values["original_restored"]);
        Assert.AreEqual("Additive", src.Current);
        StringAssert.Contains("was restored", r.Detail);
        // A runtime that will not take the original back: the row says so instead of claiming it.
        var stuck = new FakeBlend { Accept = new[] { "Opaque" }, Current = "Additive", IgnoreRestore = true };
        var s = Row(Run(new BlendModeProbe(stuck)), "display.blend");
        Assert.AreEqual(false, s.Values["original_restored"]);
        StringAssert.Contains("NOT CONFIRMED RESTORED", s.Detail);
    }

    [Test]
    public void Blend_CutShort_ByTheRunnersAbort_StillPutsTheOriginalBack()
    {
        var src = new FakeBlend { Accept = new[] { "Opaque", "Additive", "AlphaBlend" }, Current = "Additive", Frames = 50 };
        var report = new ProbeReport(); var clock = new ManualProbeClock();
        var runner = new ProbeRunner(report, clock, new MemoryProbeStore(), null, null);
        var it = runner.RunProbe(new BlendModeProbe(src));
        for (int i = 0; i < 20; i++) { it.MoveNext(); clock.Advance(1.0 / 60); }
        Assert.AreEqual("Opaque", src.Current, "mid-probe the mode has been changed");
        runner.Abort();
        Assert.AreEqual("Additive", src.Current, "an abandoned blend probe restored the original in its finally block");
    }

    sealed class FakeDimming : IDimmingSource
    {
        public string Reason; public float[] Steps = new float[0]; public float Level; public bool Snap, IgnoreRestore; public readonly List<float> Requests = new List<float>();
        float restoreTarget = -1; public float Original = -1;
        public string Start() { return Reason; }
        public float[] Levels() { return Steps; }
        public float Current() { return Level; }
        public bool Request(float l)
        {
            Requests.Add(l);
            if (Original < 0) Original = Level;
            if (IgnoreRestore && Math.Abs(l - Original) < 1e-4f && Requests.Count > Steps.Length) return true;
            Level = Snap ? Steps.OrderBy(s => Math.Abs(s - l)).First() : l; return true;
        }
    }

    [Test]
    public void Dimming_Unsupported_NoLevels_MismatchedReadback_AndASetReadAndRestoreThatIsReportedHonestly()
    {
        Assert.AreEqual(ProbeStatus.Unsupported, Row(RunClocked(c => new DimmingProbe(new FakeDimming { Reason = "extension absent" })), "display.dimming").Status);
        Assert.AreEqual(ProbeStatus.Unsupported, Row(RunClocked(c => new DimmingProbe(new FakeDimming())), "display.dimming").Status);
        // The runtime overrides every request to 0.5 (a safety override): the probe says fail and says which.
        var bad = RunClocked(c => new DimmingProbe(new StuckDimming()));
        Assert.AreEqual(ProbeStatus.Fail, Row(bad, "display.dimming").Status);
        var ok = new FakeDimming { Steps = new[] { 0f, 0.25f, 0.5f, 0.75f, 1f }, Level = 0.5f };
        var r = RunClocked(c => new DimmingProbe(ok));
        Assert.AreEqual(ProbeStatus.Pass, Row(r, "display.dimming").Status);
        Assert.AreEqual(0.5f, ok.Level, "the original level is restored");
        Assert.AreEqual(true, Row(r, "display.dimming").Values["original_restored"]);
        StringAssert.Contains("was restored (read back 0.5)", Row(r, "display.dimming").Detail);
        // A runtime that ignores the restore: not claimed.
        var ign = new FakeDimming { Steps = new[] { 0f, 0.5f, 1f }, Level = 0.5f, IgnoreRestore = true };
        var rr = RunClocked(c => new DimmingProbe(ign));
        Assert.AreEqual(false, Row(rr, "display.dimming").Values["original_restored"]);
        StringAssert.Contains("NOT CONFIRMED RESTORED", Row(rr, "display.dimming").Detail);
    }

    sealed class StuckDimming : IDimmingSource
    {
        public string Start() { return null; }
        public float[] Levels() { return new[] { 0f, 0.5f, 1f }; }
        public float Current() { return 0.5f; }
        public bool Request(float l) { return true; }
    }

    [Test]
    public void Dimming_OnATimeout_TheAbandonedProbeStillRestoresTheLevel_AndPauseAndQuitRestoreToo()
    {
        // 200 levels at 0.7 s is far over the probe's 60 s: the runner cuts it, disposes it, and the finally block puts the level back.
        var many = new FakeDimming { Steps = Enumerable.Range(0, 200).Select(i => i / 199f).ToArray(), Level = 0.25f };
        var report = new ProbeReport(); var clock = new ManualProbeClock();
        var runner = new ProbeRunner(report, clock, new MemoryProbeStore(), null, null);
        ProbeRunner.RunToEnd(runner.RunProbe(new DimmingProbe(many)), clock);
        Assert.AreEqual(ProbeStatus.Error, Row(report, "display.dimming").Status);
        StringAssert.Contains("timed out", Row(report, "display.dimming").Detail);
        Assert.AreEqual(0.25f, many.Level, 1e-4f, "the wearer's level is back after the timeout");

        // The app is paused half way: RestoreAll puts it back at once (the probe's own restore is registered on the context).
        var src = new FakeDimming { Steps = new[] { 0f, 0.25f, 0.5f, 0.75f, 1f }, Level = 0.5f };
        report = new ProbeReport(); clock = new ManualProbeClock();
        runner = new ProbeRunner(report, clock, new MemoryProbeStore(), null, null);
        var it = runner.RunProbe(new DimmingProbe(src));
        for (int i = 0; i < 60; i++) { it.MoveNext(); clock.Advance(1.0 / 60); }
        Assert.AreNotEqual(0.5f, src.Level, "mid-probe the level has been changed");
        runner.RestoreAll();
        Assert.AreEqual(0.5f, src.Level, 1e-4f);
        // Quit and destroy: Abort disposes what is running, then restores.
        src.Request(1f); runner.Abort();
        Assert.AreEqual(0.5f, src.Level, 1e-4f);
    }

    // ---------------------------------------------------------------- display

    sealed class FakeDisplay : IDisplaySource
    {
        public string Reason; public float[] Rates; public float Current = 72; public EyeInfo[] EyeData; public double Scale = 1;
        public string Start() { return Reason; }
        public float[] RefreshRates() { return Rates; }
        public float CurrentRefreshRate() { return Current; }
        public EyeInfo[] Eyes() { return EyeData; }
        public double RecommendedScale() { return Scale; }
    }

    [Test]
    public void Display_RefreshRatesAndEyes_UnsupportedAndReported()
    {
        Assert.AreEqual(ProbeStatus.Unsupported, Row(Run(new RefreshRateProbe(new FakeDisplay { Reason = "no display" })), "display.refresh").Status);
        Assert.AreEqual(ProbeStatus.Unsupported, Row(Run(new RefreshRateProbe(new FakeDisplay { Current = 0 })), "display.refresh").Status);
        Assert.AreEqual(ProbeStatus.Info, Row(Run(new RefreshRateProbe(new FakeDisplay { Current = 72 })), "display.refresh").Status, "no list but a current rate");
        var r = Row(Run(new RefreshRateProbe(new FakeDisplay { Rates = new[] { 72f, 90f, 120f } })), "display.refresh");
        Assert.AreEqual(ProbeStatus.Pass, r.Status); StringAssert.Contains("72, 90, 120", r.Detail);
        Assert.AreEqual(ProbeStatus.Unsupported, Row(Run(new EyeProbe(new FakeDisplay { Reason = "no display" })), "display.eyes").Status);
        Assert.AreEqual(ProbeStatus.Unsupported, Row(Run(new EyeProbe(new FakeDisplay())), "display.eyes").Status);
        var e = new EyeInfo { Width = 1920, Height = 1200, FovLeftDeg = 30, FovRightDeg = 30, FovUpDeg = 18, FovDownDeg = 18 };
        var eyes = Row(Run(new EyeProbe(new FakeDisplay { EyeData = new[] { e, e }, Scale = 1.2 })), "display.eyes");
        Assert.AreEqual(ProbeStatus.Pass, eyes.Status); Assert.AreEqual("1920x1200", eyes.Values["left_resolution_px"]); Assert.AreEqual(60.0, (double)eyes.Values["right_fov_h_deg"], 1e-9);
    }

    // ---------------------------------------------------------------- camera and microphone

    sealed class FakeCamera : ICameraSource
    {
        public string Json = "{\"cameras\":[]}"; public bool Grant = true; public int Frames = 30; public string Error = ""; public bool ThrowOnList;
        public readonly List<string> Order = new List<string>();
        public string ListJson() { if (ThrowOnList) throw new MissingMethodException("ProbeNative.cameraListJson"); return Json; }
        public IEnumerator RequestPermission(Action<bool> g) { Order.Add("dialog"); yield return null; g(Grant); }
        public IEnumerator CountFrames(string id, double seconds, FrameTest r) { Order.Add("frames"); yield return null; r.Ok = Frames > 0; r.Frames = Frames; r.Seconds = seconds; r.Width = 640; r.Height = 480; r.Error = Error; }
    }

    const string TwoCameras = "{\"cameras\":[{\"id\":\"0\",\"facing\":\"back (world-facing)\",\"sizes\":[\"1920x1080\",\"640x480\"],\"fps_ranges\":[\"15-30\"],\"capabilities\":[0,1],\"physical_ids\":[]},{\"id\":\"1\",\"facing\":\"front (user-facing)\",\"sizes\":[\"640x480\"],\"fps_ranges\":[\"30-30\"],\"capabilities\":[0],\"physical_ids\":[]}]}";

    [Test]
    public void Camera_NoListNoCamerasBadJsonDeniedAndFrames()
    {
        Assert.AreEqual(ProbeStatus.Unsupported, Row(Run(new CameraProbe(new FakeCamera { ThrowOnList = true })), "camera").Status);
        Assert.AreEqual(ProbeStatus.Unsupported, Row(Run(new CameraProbe(new FakeCamera())), "camera").Status);
        Assert.AreEqual(ProbeStatus.Unsupported, Row(Run(new CameraProbe(new FakeCamera { Json = "{\"cameras\":[],\"error\":\"no camera service\"}" })), "camera").Status);
        Assert.AreEqual(ProbeStatus.Unsupported, Row(Run(new CameraProbe(new FakeCamera { Json = "not json" })), "camera").Status);

        // Denied: the wearer's choice, not a failure; the frame rows are skipped, not failed.
        var denied = Run(new CameraProbe(new FakeCamera { Json = TwoCameras, Grant = false }));
        Assert.AreEqual(ProbeStatus.Skipped, Row(denied, "camera").Status);
        StringAssert.Contains("denied (your choice)", Row(denied, "camera").Detail);
        Assert.AreEqual(ProbeStatus.Info, Row(denied, "camera.list.0").Status);
        Assert.AreEqual(ProbeStatus.Skipped, Row(denied, "camera.frames.0").Status);
        Assert.AreEqual(0, denied.Rows.Count(r => r.Status == ProbeStatus.Fail));

        var ok = Run(new CameraProbe(new FakeCamera { Json = TwoCameras, Frames = 120 }, 4));
        Assert.AreEqual(ProbeStatus.Pass, Row(ok, "camera").Status);
        Assert.AreEqual(ProbeStatus.Pass, Row(ok, "camera.frames.1").Status);
        Assert.AreEqual(2, Row(ok, "camera").Values["readable_cameras_count"]);
        CollectionAssert.AreEqual(new[] { "1920x1080", "640x480" }, (string[])Row(ok, "camera.list.0").Values["output_sizes_px"]);
        StringAssert.Contains("counted and dropped", Row(ok, "camera.frames.0").Detail);

        var none = Run(new CameraProbe(new FakeCamera { Json = TwoCameras, Frames = 0, Error = "camera error code 4" }));
        Assert.AreEqual(ProbeStatus.Fail, Row(none, "camera").Status);
        StringAssert.Contains("camera error code 4", Row(none, "camera.frames.0").Detail);
    }

    [Test]
    public void Camera_ExplainsBeforeTheDialog_AndShowsTheInUseBannerOnlyWhileFramesAreCounted()
    {
        var report = new ProbeReport(); var clock = new ManualProbeClock();
        var runner = new ProbeRunner(report, clock, new MemoryProbeStore(), null, null);
        var events = new List<string>();
        var cam = new FakeCamera { Json = TwoCameras, Frames = 10 };
        runner.PromptSink = p => events.Add("prompt:" + p);
        runner.BannerSink = b => events.Add("banner:" + b);
        // Record the source's own calls in the same list so the order is visible.
        var tracing = new TracingCamera(cam, events);
        ProbeRunner.RunToEnd(runner.RunProbe(new CameraProbe(tracing, 2)), clock);
        int reason = events.FindIndex(e => e.StartsWith("prompt:Next, Android will ask to allow the CAMERA"));
        int dialog = events.IndexOf("dialog");
        Assert.GreaterOrEqual(reason, 0, "a plain reason is shown first");
        Assert.Greater(dialog, reason, "the dialog comes after the reason");
        int on = events.IndexOf("banner:camera in use: frames counted and dropped");
        int frames = events.IndexOf("frames");
        Assert.Greater(on, dialog); Assert.Greater(frames, on, "the banner is up before the camera is opened");
        Assert.Greater(events.FindIndex(frames, e => e == "banner:"), frames, "and cleared after it");
        StringAssert.Contains("never stored, never sent", CameraProbe.Reason);
    }

    sealed class TracingCamera : ICameraSource
    {
        readonly FakeCamera inner; readonly List<string> events;
        public TracingCamera(FakeCamera c, List<string> e) { inner = c; events = e; }
        public string ListJson() { return inner.ListJson(); }
        public IEnumerator RequestPermission(Action<bool> g) { events.Add("dialog"); return inner.RequestPermission(g); }
        public IEnumerator CountFrames(string id, double s, FrameTest r) { events.Add("frames"); return inner.CountFrames(id, s, r); }
    }

    sealed class FakeAudio : IAudioSource
    {
        public string Json = "{\"inputs\":[]}"; public bool Grant = true; public AudioTest Result = new AudioTest { Ok = true, Frames = 40, SampleRate = 48000, Channels = 2, Seconds = 2, PeakLevel = 0.2 }; public bool ThrowOnList;
        public string ListJson() { if (ThrowOnList) throw new ProbeUnsupportedException("not on Android"); return Json; }
        public IEnumerator RequestPermission(Action<bool> g) { yield return null; g(Grant); }
        public IEnumerator ReadTest(double seconds, int channels, AudioTest r) { yield return null; r.Ok = Result.Ok; r.Frames = Result.Frames; r.SampleRate = Result.SampleRate; r.Channels = Result.Channels; r.Seconds = Result.Seconds; r.PeakLevel = Result.PeakLevel; r.Error = Result.Error; }
    }

    const string FourMics = "{\"inputs\":[{\"id\":7,\"type\":\"builtin mic\",\"name\":\"array\",\"channel_counts\":[1,2,4],\"sample_rates\":[16000,48000],\"encodings\":[2]}],\"native_sample_rate\":48000}";

    [Test]
    public void Microphone_UnsupportedDeniedReadAndNothingDelivered()
    {
        Assert.AreEqual(ProbeStatus.Unsupported, Row(Run(new MicrophoneProbe(new FakeAudio { ThrowOnList = true })), "audio.mic").Status);
        Assert.AreEqual(ProbeStatus.Unsupported, Row(Run(new MicrophoneProbe(new FakeAudio())), "audio.mic").Status);
        var denied = Run(new MicrophoneProbe(new FakeAudio { Json = FourMics, Grant = false }));
        Assert.AreEqual(ProbeStatus.Info, Row(denied, "audio.mic").Status); Assert.AreEqual(ProbeStatus.Skipped, Row(denied, "audio.mic.read").Status);
        StringAssert.Contains("denied (your choice)", Row(denied, "audio.mic.read").Detail);
        var ok = Run(new MicrophoneProbe(new FakeAudio { Json = FourMics }));
        Assert.AreEqual(ProbeStatus.Pass, Row(ok, "audio.mic").Status); Assert.AreEqual(4, Row(ok, "audio.mic").Values["max_channels_count"]);
        CollectionAssert.AreEqual(new[] { 16000, 48000 }, (int[])Row(ok, "audio.mic").Values["sample_rates_hz"]);
        Assert.AreEqual(ProbeStatus.Pass, Row(ok, "audio.mic.read").Status);
        StringAssert.Contains("nothing stored", Row(ok, "audio.mic.read").Detail);
        StringAssert.Contains("of full scale", Row(ok, "audio.mic.read").Detail);
        Assert.AreEqual(0.2, (double)Row(ok, "audio.mic.read").Values["peak_level_fraction_of_full_scale"], 1e-9);
        var silent = Run(new MicrophoneProbe(new FakeAudio { Json = FourMics, Result = new AudioTest { Ok = false, Error = "the input would not initialise" } }));
        Assert.AreEqual(ProbeStatus.Fail, Row(silent, "audio.mic.read").Status);
    }

    [Test]
    public void Microphone_ExplainsBeforeTheDialog_ShowsTheBannerWhileReading_AndNamesOnlyTheBuiltInMic()
    {
        var report = new ProbeReport(); var clock = new ManualProbeClock();
        var runner = new ProbeRunner(report, clock, new MemoryProbeStore(), null, null);
        var events = new List<string>();
        runner.PromptSink = p => events.Add("prompt:" + p);
        runner.BannerSink = b => events.Add("banner:" + b);
        string json = "{\"inputs\":[{\"id\":7,\"type\":\"builtin mic\",\"name\":\"array\",\"channel_counts\":[2],\"sample_rates\":[48000]},{\"id\":9,\"type\":\"bluetooth sco\",\"name\":\"Jane's Headset\",\"channel_counts\":[1],\"sample_rates\":[16000]}]}";
        ProbeRunner.RunToEnd(runner.RunProbe(new MicrophoneProbe(new FakeAudio { Json = json })), clock);
        Assert.GreaterOrEqual(events.FindIndex(e => e.StartsWith("prompt:Next, Android will ask to allow the MICROPHONE")), 0);
        CollectionAssert.Contains(events, "banner:microphone in use: level measured, nothing kept");
        var inputs = (string[])report.Find("audio.mic").Values["inputs"];
        Assert.IsTrue(inputs.Any(i => i.Contains("builtin mic \"array\"")), "the built-in microphone may be named");
        Assert.IsTrue(inputs.Any(i => i.StartsWith("bluetooth sco:")), "another input is only its type");
        Assert.IsFalse(string.Join("|", inputs).Contains("Jane"), "a personal device name never reaches the report");
    }

    // ---------------------------------------------------------------- thermal, battery and the soak

    sealed class FakeDevice : IDeviceSource
    {
        public string Reason; public Func<int, DeviceSample> At = n => new DeviceSample { ThermalStatus = 0, ThermalHeadroom = 0.4, BatteryPercent = 80, BatteryTempC = 31, Charging = false }; int n;
        public string Start() { return Reason; }
        public DeviceSample Read() { return At(n++); }
    }

    [Test]
    public void Device_UnsupportedAndSampledOverTime_WithThermalAndBatteryRows()
    {
        Assert.AreEqual(ProbeStatus.Unsupported, Row(RunClocked(c => new DeviceMonitorProbe(new FakeDevice { Reason = "not Android" })), "device.thermal_battery").Status);
        var r = RunClocked(c => new DeviceMonitorProbe(new FakeDevice { At = n => new DeviceSample { ThermalStatus = n < 6 ? 0 : 1, ThermalHeadroom = 0.3 + 0.05 * n, BatteryPercent = 80 - n * 0.2, Charging = false } }, 60, 5));
        Assert.AreEqual(ProbeStatus.Pass, Row(r, "device.thermal_battery.thermal").Status);
        Assert.AreEqual(ProbeStatus.Pass, Row(r, "device.thermal_battery.battery").Status);
        Assert.AreEqual(1, Row(r, "device.thermal_battery.thermal").Values["status_code_max"]);
        StringAssert.Contains("as a ratio", Row(r, "device.thermal_battery.thermal").Detail);
        Assert.GreaterOrEqual(r.Series["thermal"].Count, 12); Assert.GreaterOrEqual(r.Series["battery"].Count, 12);
        Assert.IsTrue(r.Series["thermal"][3].V.ContainsKey("headroom_ratio"));
        var none = RunClocked(c => new DeviceMonitorProbe(new FakeDevice { At = n => new DeviceSample { ThermalHeadroom = double.NaN } }, 20, 5));
        Assert.AreEqual(ProbeStatus.Unsupported, Row(none, "device.thermal_battery.thermal").Status);
        Assert.AreEqual(ProbeStatus.Unsupported, Row(none, "device.thermal_battery.battery").Status);
    }

    sealed class FakeSoak : ISoakSource
    {
        public string Reason; public bool Started, Stopped;
        public string Description { get { return "a generated figure of 31680 triangles, 55 bones and 4 materials (synthetic load, not a real character)"; } }
        public string Start() { Started = true; return Reason; }
        public void Stop() { Stopped = true; }
    }

    static ProbeReport RunSoak(ISoakSource src, IDeviceSource dev, double seconds, double frame, Func<bool> stop = null, double refreshHz = 0, SafetyLimits limits = null)
    {
        var report = new ProbeReport(); var clock = new ManualProbeClock();
        var runner = new ProbeRunner(report, clock, new MemoryProbeStore(), null, null);
        if (refreshHz > 0) runner.Shared.RefreshHz = refreshHz;
        ProbeRunner.RunToEnd(runner.RunProbe(new SoakProbe(src, dev, seconds, stop, limits)), clock, frame);
        return report;
    }

    [Test]
    public void Soak_Unsupported_AFastUnitPasses_ASlowOneIsAMeasurementNotAFail_AndItStopsTheFigureEitherWay()
    {
        var okDev = new FakeDevice { At = n => new DeviceSample { ThermalStatus = n < 3 ? 0 : 1, ThermalHeadroom = 0.5, BatteryPercent = 90 - n * 0.1 } };
        Assert.AreEqual(ProbeStatus.Unsupported, Row(RunSoak(new FakeSoak { Reason = "no figure" }, okDev, 30, 1.0 / 72), "soak").Status);

        // The unit reports 72 Hz and delivers it: a pass, judged against the unit's own rate.
        var soak = new FakeSoak();
        var report = RunSoak(soak, okDev, 60, 1.0 / 72.0, null, 72);
        var r = report.Find("soak");
        Assert.AreEqual(ProbeStatus.Pass, r.Status, r.Detail);
        Assert.AreEqual(60.0 * 72, (int)r.Values["frames_count"], 5);
        Assert.AreEqual(13.89, (double)r.Values["frame_ms_p50"], 0.05);
        Assert.AreEqual(0.0, (double)r.Values["frames_over_budget_pct"], 1e-9);
        Assert.AreEqual(1, r.Values["thermal_status_code_max"]);
        Assert.IsTrue(r.Values.ContainsKey("battery_drop_per_hour_pct_extrapolated"));
        StringAssert.Contains("synthetic load", (string)r.Values["what_is_drawn"]);
        StringAssert.Contains("unit's reported refresh rate", (string)r.Values["budget_source"]);
        Assert.AreEqual(6, report.Series["soak_frames"].Count, 1);
        Assert.IsTrue(soak.Started && soak.Stopped);

        // 30 Hz frames when the unit reports no rate: the 13.8 ms budget is an assumption, so it is a measurement, never a FAIL.
        var slow = new FakeSoak();
        var sr = RunSoak(slow, okDev, 20, 1.0 / 30.0).Find("soak");
        Assert.AreEqual(ProbeStatus.Info, sr.Status);
        StringAssert.Contains("ASSUMPTION", sr.Detail);
        StringAssert.Contains("a measurement, not a verdict", sr.Detail);
        StringAssert.Contains("assumed 72 Hz", (string)sr.Values["budget_source"]);
        Assert.AreEqual(100.0, (double)sr.Values["frames_over_budget_pct"], 1e-6);
        Assert.IsTrue(slow.Stopped);

        // The unit reports 72 Hz and delivers 30: its own rate is missed, which is a fail.
        var missed = RunSoak(new FakeSoak(), okDev, 20, 1.0 / 30.0, null, 72).Find("soak");
        Assert.AreEqual(ProbeStatus.Fail, missed.Status);
        StringAssert.Contains("reported refresh rate was missed", missed.Detail);
        // The unit reports 30 Hz and delivers it: judged against that, a pass.
        Assert.AreEqual(ProbeStatus.Pass, RunSoak(new FakeSoak(), okDev, 20, 1.0 / 30.0, null, 30).Find("soak").Status);

        // A soak cut short by an exception still stops the figure.
        var boom = new FakeSoak(); var n = 0;
        var boomDevice = new FakeDevice { At = k => { if (++n > 1) throw new InvalidOperationException("x"); return new DeviceSample { ThermalStatus = 0, BatteryPercent = 90, BatteryTempC = 30 }; } };
        var br = RunSoak(boom, boomDevice, 12, 1.0 / 72.0);
        Assert.IsTrue(boom.Stopped);
        Assert.AreNotEqual(ProbeStatus.Running, br.Find("soak").Status);
    }

    [Test]
    public void Soak_DoesNotStart_WhenTheUnitIsAlreadyHotOrLowOrUnreadable_AndSaysWhy()
    {
        var src = new FakeSoak();
        var hot = Row(RunSoak(src, new FakeDevice { At = n => new DeviceSample { ThermalStatus = 3 } }, 60, 1.0 / 72), "soak");
        Assert.AreEqual(ProbeStatus.Skipped, hot.Status); StringAssert.Contains("not started: thermal status 3", hot.Detail); StringAssert.Contains("conservative default", hot.Detail);
        Assert.IsFalse(src.Started, "the figure is never created");
        var warm = Row(RunSoak(new FakeSoak(), new FakeDevice { At = n => new DeviceSample { ThermalStatus = 0, BatteryTempC = 43.5 } }, 60, 1.0 / 72), "soak");
        Assert.AreEqual(ProbeStatus.Skipped, warm.Status); StringAssert.Contains("battery temperature 43.5 C", warm.Detail);
        var low = Row(RunSoak(new FakeSoak(), new FakeDevice { At = n => new DeviceSample { ThermalStatus = 0, BatteryPercent = 18, Charging = false } }, 60, 1.0 / 72), "soak");
        Assert.AreEqual(ProbeStatus.Skipped, low.Status); StringAssert.Contains("battery at 18 percent and not charging", low.Detail);
        // Low but charging is allowed.
        Assert.AreEqual(ProbeStatus.Pass, RunSoak(new FakeSoak(), new FakeDevice { At = n => new DeviceSample { ThermalStatus = 0, BatteryPercent = 18, Charging = true } }, 20, 1.0 / 72, null, 72).Find("soak").Status);
        // No thermal or battery reading at all: the soak does not run blind.
        var blind = Row(RunSoak(new FakeSoak(), new FakeDevice { Reason = "not Android" }, 60, 1.0 / 72), "soak");
        Assert.AreEqual(ProbeStatus.Skipped, blind.Status); StringAssert.Contains("could not be read", blind.Detail);
    }

    [Test]
    public void Soak_StopsByItself_OnThermalBatteryTemperatureOrLowBattery_WithTheReasonInTheRow()
    {
        // Heats up: status 0 at the start, 3 after a while.
        int calls = 0;
        var heating = new FakeDevice { At = n => new DeviceSample { ThermalStatus = ++calls > 8 ? 3 : 0, BatteryPercent = 80, BatteryTempC = 30 } };
        var src = new FakeSoak();
        var r = RunSoak(src, heating, 600, 1.0 / 72).Find("soak");
        Assert.AreEqual(ProbeStatus.Info, r.Status);
        StringAssert.Contains("stopped by a safety limit: thermal status 3", r.Detail);
        Assert.IsTrue(((string)r.Values["stop_reason"]).Contains("thermal status 3"));
        Assert.Less((double)r.Values["measured_seconds"], 60.0, "it stopped long before the 10 minutes");
        Assert.IsTrue(src.Stopped);
        calls = 0;
        var temp = new FakeDevice { At = n => new DeviceSample { ThermalStatus = 0, BatteryPercent = 80, BatteryTempC = ++calls > 5 ? 42.5 : 35 } };
        StringAssert.Contains("battery temperature 42.5 C", RunSoak(new FakeSoak(), temp, 600, 1.0 / 72).Find("soak").Detail);
        calls = 0;
        var drain = new FakeDevice { At = n => new DeviceSample { ThermalStatus = 0, BatteryPercent = ++calls > 5 ? 19 : 25, Charging = false } };
        var dr = RunSoak(new FakeSoak(), drain, 600, 1.0 / 72).Find("soak");
        StringAssert.Contains("battery at 19 percent and not charging", dr.Detail);
        StringAssert.Contains("conservative defaults", (string)dr.Values["safety_limits"]);
    }

    [Test]
    public void Soak_StopsEarly_OnATapPinchOrKey_ButNotInTheFirstSecondsAfterStarting()
    {
        var src = new FakeSoak();
        var clockRef = new ManualProbeClock();
        var report = new ProbeReport();
        var runner = new ProbeRunner(report, clockRef, new MemoryProbeStore(), null, null);
        // A press at 1 s is ignored (it is the press that started the run); one at 20 s stops it.
        Func<bool> stop = () => clockRef.Now > 20 || (clockRef.Now > 1 && clockRef.Now < 1.1);
        ProbeRunner.RunToEnd(runner.RunProbe(new SoakProbe(src, new FakeDevice(), 600, stop)), clockRef, 1.0 / 72.0);
        var r = report.Find("soak");
        Assert.AreEqual(ProbeStatus.Info, r.Status);
        StringAssert.Contains("stopped early by the wearer", r.Detail);
        Assert.Greater((double)r.Values["measured_seconds"], 19.0); Assert.Less((double)r.Values["measured_seconds"], 22.0);
        Assert.IsTrue(src.Stopped);
    }

    [Test]
    public void SafetyLimits_AreTheStatedConservativeDefaults()
    {
        var l = new SafetyLimits();
        Assert.AreEqual(3, l.MaxThermalStatus); Assert.AreEqual(42.0, l.MaxBatteryTempC); Assert.AreEqual(20.0, l.MinBatteryPercent);
        Assert.IsNull(l.Check(new DeviceSample { ThermalStatus = 2, BatteryTempC = 41.9, BatteryPercent = 20, Charging = false }));
        Assert.IsNull(l.Check(new DeviceSample()), "unreadable values are not a reason");
        Assert.IsNotNull(l.Check(new DeviceSample { ThermalStatus = 4 }));
    }

    [Test]
    public void FrameStats_PercentilesAreNearestRank()
    {
        var v = Enumerable.Range(1, 100).Select(i => (double)i).ToList();
        Assert.AreEqual(50, FrameStats.Percentile(v, 0.5));
        Assert.AreEqual(99, FrameStats.Percentile(v, 0.99));
        Assert.AreEqual(100, FrameStats.Percentile(v, 1.0));
        Assert.AreEqual(0, FrameStats.Percentile(new List<double>(), 0.5));
    }

    // ---------------------------------------------------------------- the real sources with no unit (the editor has no OpenXR runtime and no Android)

    [Test]
    public void RealSources_InTheEditor_AnswerUnsupportedOrSkippedForEverythingAndNeverCrash()
    {
        var camGo = new GameObject("probe-test-cam"); var cam = camGo.AddComponent<Camera>();
        var appGo = new GameObject("probe-test-app"); var app = appGo.AddComponent<ProbeApp>(); app.head = cam;
        try
        {
            var probes = ProbeSuite.Build(app, "all", 600f);
            Assert.GreaterOrEqual(probes.Count, 20);
            var report = new ProbeReport(); var clock = new ManualProbeClock();
            var runner = new ProbeRunner(report, clock, new MemoryProbeStore(), null, null);
            ProbeRunner.RunToEnd(runner.RunAll(probes), clock);
            var bad = report.Rows.Where(r => r.Status == ProbeStatus.Running || r.Status == ProbeStatus.Error).ToList();
            Assert.IsEmpty(bad, "rows that crashed or never answered: " + string.Join("; ", bad.Select(r => r.Id + " " + r.Status + " " + r.Detail).ToArray()));
            foreach (var id in new[] { "permissions", "runtime.openxr", "feature.depth", "feature.meshing", "feature.anchors", "display.eyes", "display.blend", "display.dimming", "audio.mic", "camera", "hands", "light.estimation", "anchors", "planes.survey", "device.thermal_battery" })
                Assert.AreEqual(ProbeStatus.Unsupported, Row(report, id).Status, id + ": " + Row(report, id).Detail);
            // The soak does not run without a thermal and battery reading, and the colour test needs its quad.
            Assert.AreEqual(ProbeStatus.Skipped, Row(report, "soak").Status);
            Assert.AreEqual(ProbeStatus.Skipped, Row(report, "display.eyecheck").Status);
        }
        finally { UnityEngine.Object.DestroyImmediate(appGo); UnityEngine.Object.DestroyImmediate(camGo); }
    }

    // ---------------------------------------------------------------- the panel text

    [Test]
    public void Panel_ShowsTheRunningRowThePromptTheBannerTheLatestRowsInColourAndTheCounts()
    {
        var go = new GameObject("probe-test-panel");
        try
        {
            var panel = ProbePanel.Create(go.transform);
            var rep = new ProbeReport();
            for (int i = 0; i < 30; i++) rep.Add(new ProbeRow { Id = "r" + i, Group = "g", Name = "Row " + i, Status = i % 3 == 0 ? ProbeStatus.Pass : i % 3 == 1 ? ProbeStatus.Fail : ProbeStatus.Unsupported, Detail = "detail " + i });
            var running = rep.Add(new ProbeRow { Id = "now", Group = "g", Name = "Planes over 60 s" });
            panel.Prompt = "look at the floor"; panel.Headline = "mode all 01:02"; panel.Banner = "camera in use: frames counted and dropped";
            string t = panel.Compose(rep, running);
            StringAssert.Contains("running: Planes over 60 s", t);
            StringAssert.Contains("look at the floor", t);
            StringAssert.Contains("camera in use: frames counted and dropped", t);
            StringAssert.Contains("Row 29", t); StringAssert.DoesNotContain("Row 9", t);
            StringAssert.Contains(ProbePanel.Colour(ProbeStatus.Pass), t); StringAssert.Contains(ProbePanel.Colour(ProbeStatus.Fail), t); StringAssert.Contains(ProbePanel.Colour(ProbeStatus.Unsupported), t);
            StringAssert.Contains("10 pass   10 fail   10 unsupported   0 error", t);
            Assert.LessOrEqual(t.Split('\n').Length, 26, "the panel stays within what the field of view can show");
            rep.Complete = true; panel.Footer = "Clean-up: click";
            string done = panel.Compose(rep, null);
            StringAssert.Contains("DONE", done); StringAssert.Contains("Clean-up: click", done);
            panel.Intro = "INTRO TEXT";
            Assert.AreEqual("INTRO TEXT", panel.Compose(rep, null), "the first screen replaces the rows");
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }
}
