using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace XrProbe
{
    // Wording used by several probes.
    public static class Words
    {
        public const string NotExercised = " (reported by the runtime, not exercised)";
        public const string Reported = " (as reported by the runtime)";
        public const string Denied = "denied (your choice)";
        public static string Num(double v, string fmt = "0.###") { return v.ToString(fmt, CultureInfo.InvariantCulture); }
    }

    // ------------------------------------------------------------------------------------------ OpenXR runtime and extensions

    public interface IOpenXrInfo
    {
        string RuntimeName { get; }
        string RuntimeVersion { get; }
        string ApiVersion { get; }
        string PluginVersion { get; }
        string[] EnabledExtensions();
        string[] AvailableExtensions();
    }

    public sealed class OpenXrRuntimeProbe : ProbeBase
    {
        readonly IOpenXrInfo info;
        public OpenXrRuntimeProbe(IOpenXrInfo info) { this.info = info; }
        public override string Id { get { return "runtime.openxr"; } }
        public override string Group { get { return "runtime"; } }
        public override string Name { get { return "OpenXR runtime"; } }

        public override IEnumerator Run(ProbeContext ctx)
        {
            string name = info.RuntimeName;
            if (string.IsNullOrEmpty(name)) throw new ProbeUnsupportedException("no OpenXR runtime is active (the app is not running under OpenXR)");
            var enabled = info.EnabledExtensions() ?? new string[0];
            var available = info.AvailableExtensions() ?? new string[0];
            ctx.Set("runtime_name", name);
            ctx.Set("runtime_version", info.RuntimeVersion);
            ctx.Set("api_version", info.ApiVersion);
            ctx.Set("unity_plugin_version", info.PluginVersion);
            ctx.Set("extensions_enabled_count", enabled.Length);
            ctx.Set("extensions_available_count", available.Length);
            ctx.Pass(name + " " + info.RuntimeVersion + ", OpenXR " + info.ApiVersion + ", Unity plug-in " + info.PluginVersion + "; " + enabled.Length + " extensions enabled, " + available.Length + " available");
            yield break;
        }
    }

    // Which of the extensions this project cares about the runtime has, and which names mention markers, QR codes, objects or geospatial anchoring.
    public sealed class ExtensionListProbe : ProbeBase
    {
        public static readonly string[][] Key =
        {
            new[] { "XR_ANDROID_depth_texture", "depth map" },
            new[] { "XR_ANDROID_scene_meshing", "room mesh" },
            new[] { "XR_ANDROID_trackables", "planes and anchors (trackables)" },
            new[] { "XR_ANDROID_trackables_object", "object tracking" },
            new[] { "XR_ANDROID_trackables_marker", "marker / QR tracking" },
            new[] { "XR_ANDROID_light_estimation", "light estimation" },
            new[] { "XR_ANDROID_global_passthrough_dimming", "lens dimming" },
            new[] { "XR_ANDROID_device_anchor_persistence", "anchor persistence" },
            new[] { "XR_ANDROID_passthrough_camera_state", "passthrough camera state" },
            new[] { "XR_ANDROID_raycast", "raycast" },
            new[] { "XR_EXT_hand_tracking", "hand tracking" },
            new[] { "XR_ANDROID_hand_mesh", "hand mesh" },
            new[] { "XR_FB_display_refresh_rate", "refresh rates" },
            new[] { "XR_ANDROID_performance_metrics", "performance metrics" },
            new[] { "XR_EXT_user_presence", "wear detection" },
            new[] { "XR_ANDROID_eye_tracking", "eye tracking" },
            new[] { "XR_ANDROID_face_tracking", "face tracking" },
        };

        static readonly string[] WordList = { "marker", "qr", "geospatial", "object", "image", "depth", "mesh", "light", "dimming", "camera", "audio" };

        readonly IOpenXrInfo info;
        public ExtensionListProbe(IOpenXrInfo info) { this.info = info; }
        public override string Id { get { return "runtime.extensions"; } }
        public override string Group { get { return "runtime"; } }
        public override string Name { get { return "OpenXR extension list"; } }

        public override IEnumerator Run(ProbeContext ctx)
        {
            var available = (info.AvailableExtensions() ?? new string[0]).Concat(info.EnabledExtensions() ?? new string[0]).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToArray();
            if (available.Length == 0) throw new ProbeUnsupportedException("the runtime listed no extensions");
            var present = new List<string>(); var absent = new List<string>();
            foreach (var k in Key)
            {
                bool has = available.Contains(k[0]);
                (has ? present : absent).Add(k[1] + " (" + k[0] + ")");
            }
            var hits = available.Where(e => WordList.Any(w => e.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)).ToArray();
            ctx.Set("all_extensions", available);
            ctx.Set("key_present", present.ToArray());
            ctx.Set("key_absent", absent.ToArray());
            ctx.Set("names_mentioning_markers_qr_objects_geospatial_etc", hits);
            ctx.Pass(present.Count + " of " + Key.Length + " key extensions present; " + available.Length + " extensions in all" + Words.Reported);
            yield break;
        }
    }

    // ------------------------------------------------------------------------------------------ feature flags

    public sealed class FeatureAnswer
    {
        public bool Supported;
        public string Detail = "";
        public readonly Dictionary<string, object> Values = new Dictionary<string, object>();
    }

    // One yes/no feature flag read from the unit. The answer is what the provider or the extension list claims; nothing is exercised, and the row says so.
    public sealed class FeatureProbe : ProbeBase
    {
        readonly string id, name;
        readonly Func<FeatureAnswer> query;
        public FeatureProbe(string id, string name, Func<FeatureAnswer> query) { this.id = id; this.name = name; this.query = query; }
        public override string Id { get { return "feature." + id; } }
        public override string Group { get { return "features"; } }
        public override string Name { get { return name; } }

        public override IEnumerator Run(ProbeContext ctx)
        {
            var a = query();
            if (a == null) throw new ProbeUnsupportedException("no answer");
            foreach (var kv in a.Values) ctx.Set(kv.Key, kv.Value);
            if (a.Supported) ctx.Pass((string.IsNullOrEmpty(a.Detail) ? "supported" : a.Detail) + Words.NotExercised);
            else ctx.Unsupported((string.IsNullOrEmpty(a.Detail) ? "not supported on this unit" : a.Detail) + Words.NotExercised);
            yield break;
        }
    }

    // ------------------------------------------------------------------------------------------ planes over time

    public sealed class PlaneInfo
    {
        public string Id = "", Label = "unknown", Alignment = "";
        public double AreaM2;
        public bool HasHeight;
        public double HeightY;   // metres in the app's tracking space
    }

    public interface IPlaneSource
    {
        // Null when started, otherwise why the unit cannot (that is an "unsupported" answer).
        string Start();
        IList<PlaneInfo> Snapshot();
        void Stop();
    }

    // Looks for planes for a time (60 s in the app): when the first appears, how many of each label there are at most, how big.
    public sealed class PlaneSurveyProbe : ProbeBase
    {
        public const string Permission = "android.permission.SCENE_UNDERSTANDING_COARSE";
        readonly IPlaneSource src;
        readonly double seconds;
        public PlaneSurveyProbe(IPlaneSource src, double seconds = 60) { this.src = src; this.seconds = seconds; }
        public override string Id { get { return "planes.survey"; } }
        public override string Group { get { return "scene"; } }
        public override string Name { get { return "Planes and their labels over " + seconds.ToString("0") + " s"; } }
        public override double TimeoutSeconds { get { return seconds + 30; } }

        public override IEnumerator Run(ProbeContext ctx)
        {
            if (ctx.Shared.Denied(Permission)) { ctx.Skip(Words.Denied + ": the scene understanding permission was refused, so planes were not looked for"); yield break; }
            string why = src.Start();
            if (why != null) throw new ProbeUnsupportedException(why);
            try
            {
                ctx.Prompt("Planes: slowly look at the floor, a wall and a table for " + seconds.ToString("0") + " s");
                double start = ctx.Clock.Now, nextSample = start, firstPlane = -1;
                var maxCount = new SortedDictionary<string, int>(StringComparer.Ordinal);
                var maxArea = new SortedDictionary<string, double>(StringComparer.Ordinal);
                var firstSeen = new SortedDictionary<string, double>(StringComparer.Ordinal);
                var allIds = new HashSet<string>();
                int maxTotal = 0; double floorArea = 0;
                while (ctx.Clock.Now - start < seconds)
                {
                    if (ctx.Clock.Now >= nextSample)
                    {
                        nextSample += 1.0;
                        var snap = src.Snapshot() ?? new List<PlaneInfo>();
                        var s = new ProbeSample { T = ctx.Clock.Now - start };
                        s.V["total_count"] = snap.Count;
                        foreach (var g in snap.GroupBy(p => p.Label ?? "unknown"))
                        {
                            int n = g.Count(); double area = g.Sum(p => p.AreaM2);
                            s.V["count." + g.Key] = n; s.V["area_m2." + g.Key] = area;
                            int m; if (!maxCount.TryGetValue(g.Key, out m) || n > m) maxCount[g.Key] = n;
                            double a; if (!maxArea.TryGetValue(g.Key, out a) || area > a) maxArea[g.Key] = area;
                            if (!firstSeen.ContainsKey(g.Key)) firstSeen[g.Key] = ctx.Clock.Now - start;
                        }
                        foreach (var p in snap) allIds.Add(p.Id);
                        // The biggest floor plane seen so far gives the soak its floor.
                        foreach (var p in snap.Where(q => q.HasHeight && string.Equals(q.Label, "Floor", StringComparison.OrdinalIgnoreCase))) if (p.AreaM2 >= floorArea) { floorArea = p.AreaM2; ctx.Shared.FloorY = p.HeightY; }
                        if (snap.Count > 0 && firstPlane < 0) firstPlane = ctx.Clock.Now - start;
                        if (snap.Count > maxTotal) maxTotal = snap.Count;
                        ctx.Report.AddSample("planes", s);
                    }
                    yield return null;
                }
                ctx.Prompt("");
                ctx.Set("first_plane_s", firstPlane);
                ctx.Set("max_planes_at_once_count", maxTotal);
                ctx.Set("distinct_planes_count", allIds.Count);
                foreach (var kv in maxCount)
                {
                    var r = ctx.Sub("label." + ProbeSanitizer.Clean(kv.Key).ToLowerInvariant(), "Plane label " + kv.Key);
                    r.Set("max_count", kv.Value); r.Set("max_area_m2", maxArea[kv.Key]); r.Set("first_seen_s", firstSeen[kv.Key]);
                    ctx.Finish(r, ProbeStatus.Pass, kv.Value + " plane(s) at most, up to " + Words.Num(maxArea[kv.Key], "0.0") + " m2 in all, first seen after " + Words.Num(firstSeen[kv.Key], "0") + " s");
                }
                foreach (var expected in new[] { "Floor", "Wall", "Ceiling", "Table" })
                    if (!maxCount.ContainsKey(expected))
                    {
                        var r = ctx.Sub("label." + expected.ToLowerInvariant(), "Plane label " + expected);
                        ctx.Finish(r, ProbeStatus.Info, "not seen in " + seconds.ToString("0") + " s (it may not have been in view)");
                    }
                if (maxTotal > 0) ctx.Pass("first plane after " + Words.Num(firstPlane, "0.0") + " s; up to " + maxTotal + " planes at once, labels: " + string.Join(", ", maxCount.Keys.ToArray()));
                else ctx.Fail("no plane found in " + seconds.ToString("0") + " s (look at the floor and walls in good light)");
            }
            finally { src.Stop(); }
        }
    }

    // ------------------------------------------------------------------------------------------ anchors: create, persist, reload, restart

    public sealed class AnchorResult
    {
        public bool Ok;
        public string Id = "";      // the live anchor's trackable id, or the saved id, whichever the step returns
        public string Error = "";
        public bool HasPose;        // metres in the app's tracking space, when the step knows them
        public double X, Y, Z;
    }

    public sealed class AnchorListResult
    {
        public bool Ok;
        public List<string> Ids = new List<string>();
        public string Error = "";
    }

    public interface IAnchorSource
    {
        string Start();   // null when ready, else why not
        IEnumerator Create(AnchorResult r);
        IEnumerator Save(string anchorId, AnchorResult r);          // r.Id = the saved id
        IEnumerator ListSaved(AnchorListResult r);
        // Removes the live anchor from the session (the saved copy stays), so a later load cannot be answered by it.
        IEnumerator Release(string liveId, AnchorResult r);
        IEnumerator Load(string savedId, AnchorResult r);
        IEnumerator Erase(string savedId, AnchorResult r);
    }

    public sealed class AnchorProbe : ProbeBase
    {
        public const string StoreKey = "last_saved_anchor";
        public const double ReloadToleranceM = 0.5;
        readonly IAnchorSource src;
        public AnchorProbe(IAnchorSource src) { this.src = src; }
        public override string Id { get { return "anchors"; } }
        public override string Group { get { return "scene"; } }
        public override string Name { get { return "Anchors: create, persist, reload, restart"; } }
        public override double TimeoutSeconds { get { return 90; } }

        public override IEnumerator Run(ProbeContext ctx)
        {
            if (ctx.Shared.Denied(PlaneSurveyProbe.Permission)) { ctx.Skip(Words.Denied + ": the scene understanding permission was refused, so anchors were not tried"); yield break; }
            string why = src.Start();
            if (why != null) throw new ProbeUnsupportedException(why);
            var create = ctx.Sub("create", "Anchor: create");
            var persist = ctx.Sub("persist", "Anchor: save (persist)");
            var reload = ctx.Sub("reload", "Anchor: release the live one, then load the saved copy");
            var restart = ctx.Sub("restart", "Anchor: survives an app restart");
            var sh = ctx.Shared;

            var c = new AnchorResult();
            yield return src.Create(c);
            if (!c.Ok) { ctx.Finish(create, ProbeStatus.Fail, "could not create an anchor: " + c.Error); ctx.Finish(persist, ProbeStatus.Skipped, "no anchor"); ctx.Finish(reload, ProbeStatus.Skipped, "no anchor"); }
            else
            {
                ctx.Finish(create, ProbeStatus.Pass, "created anchor " + sh.Label(c.Id) + " (labels change every run; real ids are not recorded)");
                var s = new AnchorResult();
                yield return src.Save(c.Id, s);
                if (!s.Ok) { ctx.Finish(persist, ProbeStatus.Fail, "save failed: " + s.Error); ctx.Finish(reload, ProbeStatus.Skipped, "nothing saved"); }
                else
                {
                    var list = new AnchorListResult();
                    yield return src.ListSaved(list);
                    bool listed = list.Ok && list.Ids.Contains(s.Id);
                    persist.Set("saved_anchors_listed_count", list.Ids.Count);
                    ctx.Finish(persist, listed ? ProbeStatus.Pass : ProbeStatus.Fail, listed ? "saved as " + sh.Label(s.Id) + "; the runtime lists " + list.Ids.Count + " saved anchor(s)" : "saved as " + sh.Label(s.Id) + " but the saved list does not show it (" + (list.Ok ? list.Ids.Count + " listed" : list.Error) + ")");
                    // The live anchor is released before the saved copy is loaded: the load cannot be answered by the anchor that is already there.
                    var rel = new AnchorResult();
                    yield return src.Release(c.Id, rel);
                    if (!rel.Ok) ctx.Finish(reload, ProbeStatus.Info, "the live anchor could not be released (" + rel.Error + "), so a reload would prove nothing and was not attempted");
                    else
                    {
                        var l = new AnchorResult();
                        yield return src.Load(s.Id, l);
                        if (!l.Ok) ctx.Finish(reload, ProbeStatus.Fail, "load failed: " + l.Error);
                        else if (l.Id == c.Id) ctx.Finish(reload, ProbeStatus.Fail, "the load returned the anchor that had been released (the same trackable), so it is not a reload");
                        else
                        {
                            string where = "position not compared";
                            bool moved = false;
                            if (c.HasPose && l.HasPose)
                            {
                                double d = Math.Sqrt((c.X - l.X) * (c.X - l.X) + (c.Y - l.Y) * (c.Y - l.Y) + (c.Z - l.Z) * (c.Z - l.Z));
                                reload.Set("distance_from_original_m", d);
                                moved = d > ReloadToleranceM;
                                where = "it came back " + Words.Num(d, "0.00") + " m from where it was made";
                            }
                            ctx.Finish(reload, moved ? ProbeStatus.Fail : ProbeStatus.Pass, (moved ? "loaded, but " : "released, then loaded again as a new live anchor " + sh.Label(l.Id) + "; ") + where + (moved ? " (more than " + Words.Num(ReloadToleranceM, "0.0") + " m)" : ""));
                        }
                    }
                    // What this run saved is what the next run tries to load; the earlier one is erased after it has been checked.
                    string previous = ctx.Store != null ? ctx.Store.Read(StoreKey) : null;
                    if (ctx.Store != null) ctx.Store.Write(StoreKey, s.Id);
                    restart.Set("this_run_left_one_saved_anchor", true);
                    if (string.IsNullOrEmpty(previous)) ctx.Finish(restart, ProbeStatus.Info, "no earlier run left an anchor: close the app, start it again and read this row on the second run. This run left ONE saved anchor on the device; the final screen has a clean-up action that erases it");
                    else
                    {
                        var r = new AnchorResult();
                        yield return src.Load(previous, r);
                        string erased = "";
                        if (r.Ok) { var e = new AnchorResult(); yield return src.Erase(previous, e); restart.Set("earlier_anchor_erased", e.Ok); erased = e.Ok ? "; the earlier anchor was then erased" : "; erasing the earlier anchor failed (" + e.Error + ")"; }
                        ctx.Finish(restart, r.Ok ? ProbeStatus.Pass : ProbeStatus.Fail, r.Ok ? "the anchor saved by the earlier run (" + sh.Label(previous) + ") loaded again after the restart" + erased : "the anchor saved by the earlier run could not be loaded: " + r.Error);
                    }
                }
            }
            ctx.Info("see the four sub-rows");
        }
    }

    // On demand from the final screen: erases the saved anchor the last run left, and forgets it.
    public sealed class AnchorCleanupProbe : ProbeBase
    {
        readonly IAnchorSource src;
        public AnchorCleanupProbe(IAnchorSource src) { this.src = src; }
        public override string Id { get { return "anchors.cleanup"; } }
        public override string Group { get { return "scene"; } }
        public override string Name { get { return "Anchor clean-up"; } }
        public override double TimeoutSeconds { get { return 30; } }

        public override IEnumerator Run(ProbeContext ctx)
        {
            string saved = ctx.Store != null ? ctx.Store.Read(AnchorProbe.StoreKey) : null;
            if (string.IsNullOrEmpty(saved)) { ctx.Info("no saved anchor from this app is recorded: nothing to erase"); yield break; }
            string why = src.Start();
            if (why != null) throw new ProbeUnsupportedException(why);
            var e = new AnchorResult();
            yield return src.Erase(saved, e);
            if (e.Ok) { ctx.Store.Write(AnchorProbe.StoreKey, ""); ctx.Pass("the saved anchor was erased and forgotten"); }
            else ctx.Fail("erasing the saved anchor failed: " + e.Error + ". It stays recorded so a later clean-up can try again");
        }
    }

    // ------------------------------------------------------------------------------------------ light estimation

    public interface ILightSource
    {
        string Start();
        // Null until the runtime has given a value; then the values it gives (brightness, colour temperature, ...).
        IDictionary<string, double> Read();
        void Stop();
    }

    public sealed class LightEstimationProbe : ProbeBase
    {
        readonly ILightSource src;
        readonly double seconds;
        public LightEstimationProbe(ILightSource src, double seconds = 10) { this.src = src; this.seconds = seconds; }
        public override string Id { get { return "light.estimation"; } }
        public override string Group { get { return "scene"; } }
        public override string Name { get { return "Light estimation"; } }
        public override double TimeoutSeconds { get { return seconds + 20; } }

        public override IEnumerator Run(ProbeContext ctx)
        {
            if (ctx.Shared.Denied(PlaneSurveyProbe.Permission)) { ctx.Skip(Words.Denied + ": the scene understanding permission was refused, so light estimation was not tried"); yield break; }
            string why = src.Start();
            if (why != null) throw new ProbeUnsupportedException(why);
            try
            {
                double start = ctx.Clock.Now; IDictionary<string, double> got = null; double at = 0; int n = 0;
                while (ctx.Clock.Now - start < seconds)
                {
                    var v = src.Read();
                    if (v != null) { n++; got = v; if (n == 1) at = ctx.Clock.Now - start; var s = new ProbeSample { T = ctx.Clock.Now - start }; foreach (var kv in v) s.V[kv.Key] = kv.Value; ctx.Report.AddSample("light", s); }
                    yield return null;
                }
                if (got == null) { ctx.Fail("the runtime gave no light estimate in " + seconds.ToString("0") + " s"); yield break; }
                foreach (var kv in got) ctx.Set(kv.Key, kv.Value);
                ctx.Set("first_value_s", at);
                ctx.Pass("values after " + Words.Num(at, "0.0") + " s (units as the runtime gives them): " + string.Join(", ", got.Select(kv => kv.Key + " " + Words.Num(kv.Value)).ToArray()));
            }
            finally { src.Stop(); }
        }
    }

    // ------------------------------------------------------------------------------------------ hands and a pinch

    public sealed class HandFrame
    {
        public bool LeftTracked, RightTracked;
        public int LeftJoints, RightJoints;
        // Thumb tip to index tip, metres; negative when not known.
        public double LeftPinchM = -1, RightPinchM = -1;
    }

    public interface IHandSource
    {
        string Start();
        HandFrame Read();
        void Stop();
    }

    public sealed class HandTrackingProbe : ProbeBase
    {
        public const string Permission = "android.permission.HAND_TRACKING";
        public const double PinchMetres = 0.025;
        readonly IHandSource src;
        readonly double seconds;
        public HandTrackingProbe(IHandSource src, double seconds = 30) { this.src = src; this.seconds = seconds; }
        public override string Id { get { return "hands"; } }
        public override string Group { get { return "input"; } }
        public override string Name { get { return "Hand tracking: joints and pinch"; } }
        public override double TimeoutSeconds { get { return seconds + 20; } }

        public override IEnumerator Run(ProbeContext ctx)
        {
            if (ctx.Shared.Denied(Permission)) { ctx.Skip(Words.Denied + ": the hand tracking permission was refused, so hands were not looked for"); yield break; }
            string why = src.Start();
            if (why != null) throw new ProbeUnsupportedException(why);
            var pinch = ctx.Sub("pinch", "Hand tracking: pinch");
            try
            {
                ctx.Prompt("Hands: hold a hand in front of your chest, then pinch thumb to index (" + seconds.ToString("0") + " s)");
                double start = ctx.Clock.Now; int maxL = 0, maxR = 0; bool pinched = false; double minPinch = 9; double firstTracked = -1, firstPinch = -1; int framesTracked = 0, frames = 0;
                while (ctx.Clock.Now - start < seconds)
                {
                    var f = src.Read();
                    frames++;
                    if (f != null)
                    {
                        if (f.LeftTracked || f.RightTracked) { framesTracked++; if (firstTracked < 0) firstTracked = ctx.Clock.Now - start; }
                        maxL = Math.Max(maxL, f.LeftJoints); maxR = Math.Max(maxR, f.RightJoints);
                        foreach (var d in new[] { f.LeftPinchM, f.RightPinchM })
                            if (d >= 0) { minPinch = Math.Min(minPinch, d); if (d < PinchMetres && !pinched) { pinched = true; firstPinch = ctx.Clock.Now - start; } }
                    }
                    if (pinched && maxL + maxR >= 20 && ctx.Clock.Now - start > 5) break; // enough
                    yield return null;
                }
                ctx.Prompt("");
                int joints = Math.Max(maxL, maxR);
                ctx.Set("joints_tracked_max_left_count", maxL); ctx.Set("joints_tracked_max_right_count", maxR);
                ctx.Set("first_tracked_s", firstTracked); ctx.Set("frames_tracked_count", framesTracked); ctx.Set("frames_count", frames);
                if (joints >= 20) ctx.Pass("up to " + joints + " joints tracked on one hand (the standard set has 26); first hand after " + Words.Num(firstTracked, "0.0") + " s");
                else if (joints > 0) ctx.Fail("only " + joints + " joints tracked (expected about 26)");
                else ctx.Fail("no hand was tracked in " + seconds.ToString("0") + " s (hands in front of the chest, in good light)");
                pinch.Set("min_thumb_index_m", minPinch < 9 ? minPinch : -1.0); pinch.Set("first_pinch_s", firstPinch);
                ctx.Finish(pinch, pinched ? ProbeStatus.Pass : ProbeStatus.Fail, pinched ? "pinch seen (thumb to index under " + Words.Num(PinchMetres * 100, "0.#") + " cm) after " + Words.Num(firstPinch, "0.0") + " s" : "no pinch seen (closest " + (minPinch < 9 ? Words.Num(minPinch * 100, "0.0") + " cm" : "no hand") + ")");
            }
            finally { src.Stop(); }
        }
    }

    // ------------------------------------------------------------------------------------------ environment blend and dimming

    public sealed class BlendResult
    {
        public string Original = "";
        public readonly List<string> Tried = new List<string>(), Accepted = new List<string>();
        public string Error = "";
        // After the last step: the mode read back, and whether it is the original.
        public string ReadBack = "";
        public bool RestoreAttempted;
    }

    public interface IBlendSource
    {
        // Reads the current mode, tries each mode (setting it, waiting a few frames, reading it back), puts the original back in a finally block and reads
        // it back once more. `r.RestoreAttempted` and `r.ReadBack` say what happened, also when the probe was cut short.
        IEnumerator Probe(BlendResult r);
    }

    public sealed class BlendModeProbe : ProbeBase
    {
        readonly IBlendSource src;
        public BlendModeProbe(IBlendSource src) { this.src = src; }
        public override string Id { get { return "display.blend"; } }
        public override string Group { get { return "display"; } }
        public override string Name { get { return "Environment blend modes"; } }

        public override IEnumerator Run(ProbeContext ctx)
        {
            var r = new BlendResult();
            yield return src.Probe(r);
            if (r.Error.Length > 0 && r.Original.Length == 0) throw new ProbeUnsupportedException(r.Error);
            bool restored = r.RestoreAttempted && r.ReadBack == r.Original;
            ctx.Set("current_at_start", r.Original); ctx.Set("tried", r.Tried.ToArray()); ctx.Set("accepted_when_set", r.Accepted.ToArray());
            ctx.Set("original_restored", restored); ctx.Set("read_back_after_restore", r.ReadBack);
            string tail = restored ? "; the original mode was restored (read back: " + r.ReadBack + ")" : "; THE ORIGINAL MODE IS NOT CONFIRMED RESTORED (read back: " + (r.ReadBack.Length > 0 ? r.ReadBack : "nothing") + ")";
            ctx.Pass("at start " + r.Original + "; accepted when requested: " + (r.Accepted.Count > 0 ? string.Join(", ", r.Accepted.ToArray()) : "none") + " (tried " + string.Join(", ", r.Tried.ToArray()) + ")" + tail);
        }
    }

    public interface IDimmingSource
    {
        // Null when the unit can dim, else why not.
        string Start();
        float[] Levels();
        float Current();
        bool Request(float level);
    }

    public sealed class DimmingProbe : ProbeBase
    {
        readonly IDimmingSource src;
        public DimmingProbe(IDimmingSource src) { this.src = src; }
        public override string Id { get { return "display.dimming"; } }
        public override string Group { get { return "display"; } }
        public override string Name { get { return "Lens dimming: levels read and set"; } }
        public override double TimeoutSeconds { get { return 60; } }

        public override IEnumerator Run(ProbeContext ctx)
        {
            string why = src.Start();
            if (why != null) throw new ProbeUnsupportedException(why);
            var levels = src.Levels() ?? new float[0];
            if (levels.Length == 0) throw new ProbeUnsupportedException("the runtime reported no dimming levels");
            float original = src.Current();
            // Whatever happens, the level the wearer had goes back: here on a timeout or an error, and on pause, quit and destroy.
            ctx.OnRestore(() => src.Request(original));
            var readback = new List<double>(); int matches = 0; var notes = new List<string>();
            try
            {
                ctx.Prompt("Dimming: the lenses will step through each level");
                foreach (var l in levels)
                {
                    bool ok = src.Request(l);
                    yield return ctx.Wait(0.7);
                    float got = src.Current();
                    readback.Add(got);
                    bool match = Math.Abs(got - l) < 0.02f;
                    if (ok && match) matches++; else notes.Add("asked " + Words.Num(l, "0.##") + " got " + Words.Num(got, "0.##") + (ok ? "" : " (request refused)"));
                }
            }
            finally
            {
                src.Request(original);
                ctx.Prompt("");
            }
            yield return ctx.Wait(0.4);
            float back = src.Current();
            bool restored = Math.Abs(back - original) < 0.02f;
            ctx.Set("levels_fraction_of_full_dimming", levels); ctx.Set("original_level", original); ctx.Set("read_back", readback.ToArray());
            ctx.Set("original_restored", restored); ctx.Set("level_after_restore", back);
            string tail = restored ? "; the original level " + Words.Num(original, "0.##") + " was restored (read back " + Words.Num(back, "0.##") + ")" : "; THE ORIGINAL LEVEL " + Words.Num(original, "0.##") + " IS NOT CONFIRMED RESTORED (read back " + Words.Num(back, "0.##") + "): set it with the glasses' own tint button";
            if (matches == levels.Length) ctx.Pass(levels.Length + " levels (" + string.Join(", ", levels.Select(x => Words.Num(x, "0.##")).ToArray()) + "; 0 = clearest, 1 = darkest, as the runtime reports them); each could be set and read back" + tail);
            else ctx.Fail(matches + " of " + levels.Length + " levels set and read back: " + string.Join("; ", notes.ToArray()) + tail);
        }
    }

    // ------------------------------------------------------------------------------------------ display: refresh rates, per-eye size and field of view

    public sealed class EyeInfo
    {
        public int Width, Height;
        public double FovLeftDeg, FovRightDeg, FovUpDeg, FovDownDeg;
    }

    public interface IDisplaySource
    {
        string Start();
        float[] RefreshRates();     // null if the unit does not offer the list
        float CurrentRefreshRate(); // 0 when unknown
        EyeInfo[] Eyes();           // left, right
        double RecommendedScale();  // eye texture scale, 1 = the runtime's recommended
    }

    public sealed class RefreshRateProbe : ProbeBase
    {
        readonly IDisplaySource src;
        public RefreshRateProbe(IDisplaySource src) { this.src = src; }
        public override string Id { get { return "display.refresh"; } }
        public override string Group { get { return "display"; } }
        public override string Name { get { return "Display refresh rates offered"; } }

        public override IEnumerator Run(ProbeContext ctx)
        {
            string why = src.Start();
            if (why != null) throw new ProbeUnsupportedException(why);
            var rates = src.RefreshRates();
            float cur = src.CurrentRefreshRate();
            if (cur > 0) ctx.Shared.RefreshHz = cur;
            ctx.Set("current_hz", cur);
            ctx.Set("refresh_rate_changed_by_probe", false);
            if (rates == null || rates.Length == 0)
            {
                if (cur > 0) { ctx.Info("the unit offers no list of rates; the current rate is " + Words.Num(cur, "0.#") + " Hz" + Words.Reported); yield break; }
                throw new ProbeUnsupportedException("no refresh rate list and no current rate");
            }
            ctx.Set("offered_hz", rates);
            ctx.Pass("offered: " + string.Join(", ", rates.Select(x => Words.Num(x, "0.#")).ToArray()) + " Hz; current " + Words.Num(cur, "0.#") + " Hz" + Words.Reported + "; the probe does not change the rate");
            yield break;
        }
    }

    public sealed class EyeProbe : ProbeBase
    {
        readonly IDisplaySource src;
        public EyeProbe(IDisplaySource src) { this.src = src; }
        public override string Id { get { return "display.eyes"; } }
        public override string Group { get { return "display"; } }
        public override string Name { get { return "Per-eye resolution and field of view as reported"; } }

        public override IEnumerator Run(ProbeContext ctx)
        {
            string why = src.Start();
            if (why != null) throw new ProbeUnsupportedException(why);
            var eyes = src.Eyes();
            if (eyes == null || eyes.Length == 0 || eyes[0] == null || eyes[0].Width <= 0) throw new ProbeUnsupportedException("no eye texture size is reported (no XR display running)");
            for (int i = 0; i < eyes.Length; i++)
            {
                var e = eyes[i]; string n = i == 0 ? "left" : "right";
                ctx.Set(n + "_resolution_px", e.Width + "x" + e.Height);
                ctx.Set(n + "_fov_h_deg", e.FovLeftDeg + e.FovRightDeg); ctx.Set(n + "_fov_v_deg", e.FovUpDeg + e.FovDownDeg);
                ctx.Set(n + "_fov_left_right_up_down_deg", new[] { e.FovLeftDeg, e.FovRightDeg, e.FovUpDeg, e.FovDownDeg });
            }
            ctx.Set("eye_texture_scale_ratio", src.RecommendedScale());
            var a = eyes[0];
            ctx.Pass("per eye " + a.Width + "x" + a.Height + " px (texture scale " + Words.Num(src.RecommendedScale(), "0.##") + "), field of view " + Words.Num(a.FovLeftDeg + a.FovRightDeg, "0.#") + " x " + Words.Num(a.FovUpDeg + a.FovDownDeg, "0.#") + " degrees" + Words.Reported + "; the published figures are 1920x1200 per eye and 70 degrees on the diagonal");
            yield break;
        }
    }

    // ------------------------------------------------------------------------------------------ cameras (Camera2) and the microphone

    public sealed class FrameTest
    {
        public bool Ok;
        public int Frames;
        public double Seconds;
        public int Width, Height;
        public string Error = "";
    }

    public interface ICameraSource
    {
        // The Camera2 list as JSON: {"cameras":[{"id","facing","sizes":["WxH"],"fps_ranges":["a-b"],"capabilities":[..],"physical_ids":[..]}],"error":".."}
        string ListJson();
        // Asks for the camera permission; the result says whether it was granted.
        IEnumerator RequestPermission(Action<bool> granted);
        // Opens the camera and counts frames for some seconds. Frames are counted and dropped, never stored or sent.
        IEnumerator CountFrames(string cameraId, double seconds, FrameTest result);
    }

    public sealed class CameraProbe : ProbeBase
    {
        public const string Permission = "android.permission.CAMERA";
        public const string Reason = "Next, Android will ask to allow the CAMERA. The probe needs it to count frames from each camera the unit shows it. Frames are counted and dropped: never stored, never sent. You can say no.";
        readonly ICameraSource src;
        readonly double frameSeconds;
        public CameraProbe(ICameraSource src, double frameSeconds = 4) { this.src = src; this.frameSeconds = frameSeconds; }
        public override string Id { get { return "camera"; } }
        public override string Group { get { return "camera"; } }
        public override string Name { get { return "Camera2: cameras and whether a frame can be read"; } }
        public override double TimeoutSeconds { get { return 120; } }

        public override IEnumerator Run(ProbeContext ctx)
        {
            JObject o;
            try { o = JObject.Parse(src.ListJson()); }
            catch (Exception e) when (!(e is ProbeUnsupportedException)) { throw new ProbeUnsupportedException("the camera list could not be read: " + e.Message); }
            var cams = o["cameras"] as JArray;
            string err = (string)o["error"];
            if (cams == null || cams.Count == 0) throw new ProbeUnsupportedException(string.IsNullOrEmpty(err) ? "Camera2 lists no camera to this app" : err);
            ctx.Set("camera_count", cams.Count);
            var ids = new List<string>();
            foreach (var c in cams)
            {
                string id = (string)c["id"]; ids.Add(id);
                var r = ctx.Sub("list." + id, "Camera " + id + " (" + (string)c["facing"] + ")");
                r.Set("facing", (string)c["facing"]);
                r.Set("output_sizes_px", c["sizes"] == null ? new string[0] : c["sizes"].Select(t => (string)t).ToArray());
                r.Set("fps_ranges", c["fps_ranges"] == null ? new string[0] : c["fps_ranges"].Select(t => (string)t).ToArray());
                r.Set("capability_codes", c["capabilities"] == null ? new string[0] : c["capabilities"].Select(t => (string)t).ToArray());
                r.Set("physical_camera_count", c["physical_ids"] == null ? 0 : c["physical_ids"].Count());
                var sizes = c["sizes"] == null ? new string[0] : c["sizes"].Select(t => (string)t).ToArray();
                ctx.Finish(r, ProbeStatus.Info, (string)c["facing"] + "; " + sizes.Length + " output sizes (largest " + (sizes.Length > 0 ? sizes[0] : "none") + "); frame rates " + (c["fps_ranges"] == null ? "unknown" : string.Join(", ", c["fps_ranges"].Select(t => (string)t).ToArray())));
            }
            ctx.Prompt(Reason);
            yield return ctx.Wait(4);
            ctx.Prompt("");
            bool granted = false;
            yield return src.RequestPermission(g => granted = g);
            ctx.Shared.SetPermission(Permission, granted);
            if (!granted)
            {
                foreach (var id in ids) { var r = ctx.Sub("frames." + id, "Camera " + id + ": read frames"); ctx.Finish(r, ProbeStatus.Skipped, Words.Denied + ": no frames were read"); }
                ctx.Skip(Words.Denied + ": " + cams.Count + " camera(s) listed, none opened");
                yield break;
            }
            int readable = 0;
            foreach (var id in ids)
            {
                var r = ctx.Sub("frames." + id, "Camera " + id + ": read frames");
                var t = new FrameTest();
                ctx.Banner("camera in use: frames counted and dropped");
                try { yield return src.CountFrames(id, frameSeconds, t); }
                finally { ctx.Banner(""); }
                r.Set("frames_count", t.Frames); r.Set("measured_seconds", t.Seconds); r.Set("size_px", t.Width + "x" + t.Height);
                if (t.Ok && t.Frames > 0) { readable++; ctx.Finish(r, ProbeStatus.Pass, t.Frames + " frames in " + Words.Num(t.Seconds, "0.0") + " s (" + (t.Seconds > 0 ? Words.Num(t.Frames / t.Seconds, "0.#") : "0") + " per second) at " + t.Width + "x" + t.Height + " px; counted and dropped"); }
                else ctx.Finish(r, ProbeStatus.Fail, "no frames: " + (t.Error.Length > 0 ? t.Error : "the camera opened but delivered nothing"));
            }
            ctx.Set("readable_cameras_count", readable);
            if (readable > 0) ctx.Pass(cams.Count + " camera(s) listed, " + readable + " delivered frames");
            else ctx.Fail(cams.Count + " camera(s) listed, none delivered frames (third-party camera access may be closed)");
        }
    }

    public sealed class AudioTest
    {
        public bool Ok;
        public int Frames, SampleRate, Channels;
        public double Seconds, PeakLevel;   // peak as a fraction of full scale, 0 to 1
        public string Error = "";
    }

    public interface IAudioSource
    {
        // {"inputs":[{"id","type","channel_counts":[..],"sample_rates":[..],"encodings":[..]}],"native_sample_rate":n,"error":".."}. A product name may
        // be present for the built-in microphone only; the probe ignores it for any other type.
        string ListJson();
        IEnumerator RequestPermission(Action<bool> granted);
        // Reads audio from the default input for some seconds and reports the format and peak level; nothing is stored or sent.
        IEnumerator ReadTest(double seconds, int channels, AudioTest result);
    }

    public sealed class MicrophoneProbe : ProbeBase
    {
        public const string Permission = "android.permission.RECORD_AUDIO";
        public const string Reason = "Next, Android will ask to allow the MICROPHONE. The probe needs it to read the input for 2 seconds and learn its channels and sample rate. Only the level is measured; nothing is kept or sent. You can say no.";
        readonly IAudioSource src;
        public MicrophoneProbe(IAudioSource src) { this.src = src; }
        public override string Id { get { return "audio.mic"; } }
        public override string Group { get { return "audio"; } }
        public override string Name { get { return "Microphone: channels and sample rates"; } }
        public override double TimeoutSeconds { get { return 60; } }

        public override IEnumerator Run(ProbeContext ctx)
        {
            JObject o;
            try { o = JObject.Parse(src.ListJson()); }
            catch (Exception e) when (!(e is ProbeUnsupportedException)) { throw new ProbeUnsupportedException("the audio input list could not be read: " + e.Message); }
            var inputs = o["inputs"] as JArray;
            string err = (string)o["error"];
            if (inputs == null || inputs.Count == 0) throw new ProbeUnsupportedException(string.IsNullOrEmpty(err) ? "no audio input is listed" : err);
            var maxCh = 0; var rates = new SortedSet<int>(); var lines = new List<string>();
            foreach (var i in inputs)
            {
                var ch = i["channel_counts"] == null ? new int[0] : i["channel_counts"].Select(t => (int)t).ToArray();
                var sr = i["sample_rates"] == null ? new int[0] : i["sample_rates"].Select(t => (int)t).ToArray();
                if (ch.Length > 0) maxCh = Math.Max(maxCh, ch.Max());
                foreach (var r in sr) rates.Add(r);
                // The device type; a name only for the built-in microphone. Another input's name could be a person's own (a headset they named).
                string type = (string)i["type"] ?? "unknown";
                string label = type == "builtin mic" && !string.IsNullOrEmpty((string)i["name"]) ? type + " \"" + (string)i["name"] + "\"" : type;
                lines.Add(label + ": channels " + (ch.Length > 0 ? string.Join("/", ch.Select(x => x.ToString()).ToArray()) : "any") + ", rates " + (sr.Length > 0 ? string.Join("/", sr.Select(x => x.ToString()).ToArray()) : "any") + " Hz");
            }
            ctx.Set("inputs", lines.ToArray()); ctx.Set("max_channels_count", maxCh); ctx.Set("sample_rates_hz", rates.ToArray());
            ctx.Set("native_output_sample_rate_hz", o["native_sample_rate"] == null ? 0 : (int)o["native_sample_rate"]);
            ctx.Prompt(Reason);
            yield return ctx.Wait(4);
            ctx.Prompt("");
            bool granted = false;
            yield return src.RequestPermission(g => granted = g);
            ctx.Shared.SetPermission(Permission, granted);
            var test = ctx.Sub("read", "Microphone: read audio");
            if (!granted) { ctx.Finish(test, ProbeStatus.Skipped, Words.Denied + ": the microphone was not opened"); ctx.Info(inputs.Count + " input(s) listed; not opened (" + Words.Denied + ")"); yield break; }
            var t = new AudioTest();
            ctx.Banner("microphone in use: level measured, nothing kept");
            try { yield return src.ReadTest(2.0, Math.Min(maxCh, 4) > 0 ? Math.Min(maxCh, 4) : 1, t); }
            finally { ctx.Banner(""); }
            test.Set("reads_count", t.Frames); test.Set("sample_rate_hz", t.SampleRate); test.Set("channels_count", t.Channels); test.Set("peak_level_fraction_of_full_scale", t.PeakLevel);
            if (t.Ok && t.Frames > 0) ctx.Finish(test, ProbeStatus.Pass, t.Channels + " channel(s) at " + t.SampleRate + " Hz for " + Words.Num(t.Seconds, "0.0") + " s; peak level " + Words.Num(t.PeakLevel) + " of full scale (0 to 1; level only, nothing stored)");
            else ctx.Finish(test, ProbeStatus.Fail, "no audio: " + (t.Error.Length > 0 ? t.Error : "the input opened but delivered nothing"));
            ctx.Pass(inputs.Count + " input(s); up to " + maxCh + " channel(s); rates " + (rates.Count > 0 ? string.Join("/", rates.Select(x => x.ToString()).ToArray()) : "unreported") + " Hz");
        }
    }

    // ------------------------------------------------------------------------------------------ thermal and battery over time

    public sealed class DeviceSample
    {
        public int ThermalStatus = -1;       // Android PowerManager.THERMAL_STATUS_* (0 none .. 6 shutdown), -1 unknown
        public double ThermalHeadroom = -1;  // ratio: 1.0 = at the throttling limit, NaN or negative when not reported
        public double BatteryPercent = -1;
        public double BatteryTempC = -1;
        public bool Charging;
    }

    public interface IDeviceSource
    {
        string Start();
        DeviceSample Read();
    }

    public static class DeviceSampling
    {
        public static void Record(ProbeReport report, double t, DeviceSample d)
        {
            var th = new ProbeSample { T = t };
            if (d.ThermalStatus >= 0) th.V["status_code"] = d.ThermalStatus;
            if (!double.IsNaN(d.ThermalHeadroom) && d.ThermalHeadroom >= 0) th.V["headroom_ratio"] = d.ThermalHeadroom;
            report.AddSample("thermal", th);
            var b = new ProbeSample { T = t };
            if (d.BatteryPercent >= 0) b.V["percent"] = d.BatteryPercent;
            if (d.BatteryTempC >= 0) b.V["temp_c"] = d.BatteryTempC;
            b.V["charging_0_or_1"] = d.Charging ? 1 : 0;
            report.AddSample("battery", b);
        }
    }

    public sealed class DeviceMonitorProbe : ProbeBase
    {
        readonly IDeviceSource src;
        readonly double seconds, every;
        public DeviceMonitorProbe(IDeviceSource src, double seconds = 60, double every = 5) { this.src = src; this.seconds = seconds; this.every = every; }
        public override string Id { get { return "device.thermal_battery"; } }
        public override string Group { get { return "device"; } }
        public override string Name { get { return "Thermal headroom, thermal status and battery over " + seconds.ToString("0") + " s"; } }
        public override double TimeoutSeconds { get { return seconds + 30; } }

        public override IEnumerator Run(ProbeContext ctx)
        {
            string why = src.Start();
            if (why != null) throw new ProbeUnsupportedException(why);
            double start = ctx.Clock.Now, next = start; DeviceSample first = null, last = null; double maxHead = -1; int maxStatus = -1, n = 0;
            while (ctx.Clock.Now - start < seconds)
            {
                if (ctx.Clock.Now >= next)
                {
                    next += every;
                    var d = src.Read();
                    if (d != null)
                    {
                        n++; if (first == null) first = d; last = d;
                        DeviceSampling.Record(ctx.Report, ctx.Clock.Now - start, d);
                        if (d.ThermalHeadroom > maxHead) maxHead = d.ThermalHeadroom;
                        if (d.ThermalStatus > maxStatus) maxStatus = d.ThermalStatus;
                    }
                }
                yield return null;
            }
            if (first == null) throw new ProbeUnsupportedException("neither thermal nor battery state could be read");
            var th = ctx.Sub("thermal", "Thermal status and headroom");
            th.Set("status_code_first", first.ThermalStatus); th.Set("status_code_last", last.ThermalStatus); th.Set("status_code_max", maxStatus); th.Set("headroom_ratio_max", maxHead);
            bool thermalOk = maxStatus >= 0 || maxHead >= 0;
            ctx.Finish(th, thermalOk ? ProbeStatus.Pass : ProbeStatus.Unsupported, thermalOk ? "status " + first.ThermalStatus + " to " + last.ThermalStatus + " (0 none, 1 light, 2 moderate, 3 severe), headroom up to " + Words.Num(maxHead, "0.##") + " as a ratio (1.0 = at the throttling limit)" : "the system reports no thermal status or headroom");
            var bat = ctx.Sub("battery", "Battery level");
            bat.Set("percent_first", first.BatteryPercent); bat.Set("percent_last", last.BatteryPercent); bat.Set("charging", last.Charging);
            bool batOk = first.BatteryPercent >= 0;
            ctx.Finish(bat, batOk ? ProbeStatus.Pass : ProbeStatus.Unsupported, batOk ? "the app can read the battery: " + Words.Num(first.BatteryPercent, "0") + " to " + Words.Num(last.BatteryPercent, "0") + " percent over " + seconds.ToString("0") + " s, charging: " + last.Charging + " (this is the puck's own battery; the glasses have none)" : "the app cannot read a battery level");
            ctx.Set("samples_count", n);
            ctx.Info("see thermal and battery sub-rows and the series in the JSON");
        }
    }

    // ------------------------------------------------------------------------------------------ the soak

    public interface ISoakSource
    {
        // Starts drawing the synthetic figure; null when it is, else why not. `Description` says what is drawn (triangles, bones, materials).
        string Start();
        void Stop();
        string Description { get; }
    }

    public static class FrameStats
    {
        // Percentile by the nearest-rank method on a sorted copy.
        public static double Percentile(IList<double> values, double p)
        {
            if (values.Count == 0) return 0;
            var s = values.OrderBy(x => x).ToArray();
            int i = (int)Math.Ceiling(p * s.Length) - 1;
            return s[Math.Max(0, Math.Min(s.Length - 1, i))];
        }
    }

    // Draws a synthetic figure (a generated mesh with bones, standing in for a character of similar weight) for a long time and records frame-time percentiles
    // with the thermal state and battery. It does not start on a unit that is already hot or low on battery, stops by itself if one limit is reached, and
    // stops early on a tap, pinch or key. Slow frames are a measurement, not a failure, unless the unit's own reported refresh rate is missed.
    public sealed class SoakProbe : ProbeBase
    {
        public const double StopIgnoredForS = 3, SafetyCheckEveryS = 2;
        public const double AssumedBudgetMs = 1000.0 / 72.0; // 13.89 ms; // the 72 Hz platform guideline: an assumption, not a reading of the unit
        readonly ISoakSource src;
        readonly IDeviceSource dev;
        readonly double seconds;
        readonly Func<bool> stopRequested;
        readonly SafetyLimits limits;
        public SoakProbe(ISoakSource src, IDeviceSource dev, double seconds = 600, Func<bool> stopRequested = null, SafetyLimits limits = null) { this.src = src; this.dev = dev; this.seconds = seconds; this.stopRequested = stopRequested; this.limits = limits ?? new SafetyLimits(); }
        public override string Id { get { return "soak"; } }
        public override string Group { get { return "soak"; } }
        public override string Name { get { return "Soak: a synthetic figure for " + (seconds / 60).ToString("0.#", CultureInfo.InvariantCulture) + " minutes"; } }
        public override double TimeoutSeconds { get { return seconds + 60; } }

        public override IEnumerator Run(ProbeContext ctx)
        {
            // The start check: no reading of thermal and battery, or one over a limit, and the soak does not begin.
            DeviceSample d0 = null;
            if (dev != null) { string dwhy = dev.Start(); if (dwhy == null) d0 = dev.Read(); }
            if (d0 == null) { ctx.Skip("not started: the thermal and battery state could not be read, and the soak does not run without it"); yield break; }
            string startWhy = limits.Check(d0);
            if (startWhy != null) { ctx.Skip("not started: " + startWhy); ctx.Set("not_started_reason", startWhy); yield break; }

            string why = src.Start();
            if (why != null) throw new ProbeUnsupportedException(why);
            string stopReason = null;
            var frames = new List<double>(); // milliseconds
            double start = ctx.Clock.Now, last = start, nextCheck = start + SafetyCheckEveryS, nextRow = start, nextDev = start;
            DeviceSample firstDev = d0, lastDev = d0; int maxStatus = d0.ThermalStatus; double maxHead = d0.ThermalHeadroom;
            var bucket = new List<double>();
            try
            {
                ctx.Prompt("Soak: a synthetic figure is drawn (" + src.Description + "). Tap, pinch or press a key to stop early (" + (seconds / 60).ToString("0.#", CultureInfo.InvariantCulture) + " min).");
                while (ctx.Clock.Now - start < seconds)
                {
                    yield return null;
                    double now = ctx.Clock.Now;
                    double ms = (now - last) * 1000.0; last = now;
                    frames.Add(ms); bucket.Add(ms);
                    if (now - start > StopIgnoredForS && stopRequested != null && stopRequested()) { stopReason = "stopped early by the wearer"; break; }
                    if (now >= nextRow)
                    {
                        nextRow += 10.0;
                        var s = new ProbeSample { T = now - start };
                        s.V["frame_ms_p50"] = FrameStats.Percentile(bucket, 0.5); s.V["frame_ms_p99"] = FrameStats.Percentile(bucket, 0.99); s.V["frame_ms_max"] = bucket.Count > 0 ? bucket.Max() : 0;
                        ctx.Report.AddSample("soak_frames", s);
                        bucket.Clear();
                    }
                    if (now >= nextCheck)
                    {
                        nextCheck += SafetyCheckEveryS;
                        DeviceSample d = null;
                        try { d = dev.Read(); } catch (Exception) { }
                        if (d != null)
                        {
                            lastDev = d;
                            if (d.ThermalStatus > maxStatus) maxStatus = d.ThermalStatus;
                            if (d.ThermalHeadroom > maxHead) maxHead = d.ThermalHeadroom;
                            if (now >= nextDev) { nextDev += 10.0; DeviceSampling.Record(ctx.Report, now - start, d); }
                            string w = limits.Check(d);
                            if (w != null) { stopReason = "stopped by a safety limit: " + w; break; }
                        }
                    }
                }
            }
            finally
            {
                src.Stop();
                ctx.Prompt("");
            }
            double elapsed = ctx.Clock.Now - start;
            double p50 = FrameStats.Percentile(frames, 0.5), p95 = FrameStats.Percentile(frames, 0.95), p99 = FrameStats.Percentile(frames, 0.99), p999 = FrameStats.Percentile(frames, 0.999), max = frames.Count > 0 ? frames.Max() : 0;
            double meanFps = elapsed > 0 ? frames.Count / elapsed : 0;
            double reportedHz = ctx.Shared.RefreshHz;
            double budget = reportedHz > 0 ? 1000.0 / reportedHz : AssumedBudgetMs;
            // A frame counts as over only when it is more than 5 percent past the budget, so rounding in the clock is not a miss.
            int over = frames.Count(f => f > budget * 1.05), over2 = frames.Count(f => f > 2 * budget);
            ctx.Set("what_is_drawn", src.Description);
            ctx.Set("measured_seconds", elapsed); ctx.Set("frames_count", frames.Count); ctx.Set("mean_fps", meanFps);
            ctx.Set("frame_ms_p50", p50); ctx.Set("frame_ms_p95", p95); ctx.Set("frame_ms_p99", p99); ctx.Set("frame_ms_p99_9", p999); ctx.Set("frame_ms_max", max);
            ctx.Set("budget_ms", budget); ctx.Set("budget_source", reportedHz > 0 ? "the unit's reported refresh rate (" + Words.Num(reportedHz, "0.#") + " Hz)" : "assumed 72 Hz platform guideline (13.8 ms), not read from the unit");
            ctx.Set("frames_over_budget_pct", frames.Count > 0 ? 100.0 * over / frames.Count : 0); ctx.Set("frames_over_double_budget_pct", frames.Count > 0 ? 100.0 * over2 / frames.Count : 0);
            ctx.Set("thermal_status_code_first", firstDev.ThermalStatus); ctx.Set("thermal_status_code_max", maxStatus); ctx.Set("thermal_headroom_ratio_max", maxHead);
            ctx.Set("battery_percent_first", firstDev.BatteryPercent); ctx.Set("battery_percent_last", lastDev.BatteryPercent);
            ctx.Set("safety_limits", "thermal status " + limits.MaxThermalStatus + ", battery temperature " + Words.Num(limits.MaxBatteryTempC, "0.0") + " C, battery " + Words.Num(limits.MinBatteryPercent, "0") + " percent while not charging (conservative defaults, not vendor limits)");
            if (firstDev.BatteryPercent >= 0 && lastDev.BatteryPercent >= 0 && elapsed > 60 && !lastDev.Charging) ctx.Set("battery_drop_per_hour_pct_extrapolated", (firstDev.BatteryPercent - lastDev.BatteryPercent) * 3600.0 / elapsed);
            if (stopReason != null) ctx.Set("stop_reason", stopReason);
            string budgetNote = reportedHz > 0 ? "budget " + Words.Num(budget, "0.0") + " ms from the unit's reported " + Words.Num(reportedHz, "0.#") + " Hz" : "budget " + Words.Num(budget, "0.0") + " ms is an ASSUMPTION (the 72 Hz platform guideline), not read from the unit";
            string detail = frames.Count + " frames in " + Words.Num(elapsed, "0") + " s (" + Words.Num(meanFps, "0.#") + " per second) of " + src.Description + "; frame time p50 " + Words.Num(p50, "0.0") + " ms, p95 " + Words.Num(p95, "0.0") + ", p99 " + Words.Num(p99, "0.0") + ", max " + Words.Num(max, "0.0") + "; " + (frames.Count > 0 ? Words.Num(100.0 * over / frames.Count, "0.0") : "0") + " % over the budget by more than 5 percent (" + budgetNote + ")";
            if (stopReason != null) { ctx.Info(stopReason + "; " + detail); yield break; }
            if (frames.Count < 10) { ctx.Fail("too few frames: " + detail); yield break; }
            // Judged only against the unit's own refresh rate: a mean rate under 90 percent of it is a fail. Otherwise a number to read.
            if (reportedHz > 0 && meanFps < 0.9 * reportedHz) ctx.Fail("the unit's own reported refresh rate was missed: " + detail);
            else if (reportedHz > 0 && p99 <= budget * 1.05) ctx.Pass(detail);
            else ctx.Info("a measurement, not a verdict: " + detail);
        }
    }
}
