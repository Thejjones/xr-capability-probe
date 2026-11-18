using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace XrProbe
{
    public interface IProbeClock { double Now { get; } }

    public sealed class StopwatchClock : IProbeClock
    {
        readonly Stopwatch sw = Stopwatch.StartNew();
        public double Now { get { return sw.Elapsed.TotalSeconds; } }
    }

    // A clock the tests move by hand.
    public sealed class ManualProbeClock : IProbeClock
    {
        public double Now { get; set; }
        public void Advance(double seconds) { Now += seconds; }
    }

    // Where small facts that must survive a restart go (the saved anchor of the last run). The app's own files folder in the app, memory in tests.
    public interface IProbeStore
    {
        string Read(string key);
        void Write(string key, string value);
    }

    public sealed class MemoryProbeStore : IProbeStore
    {
        public readonly Dictionary<string, string> Data = new Dictionary<string, string>();
        public string Read(string key) { string v; return Data.TryGetValue(key, out v) ? v : null; }
        public void Write(string key, string value) { Data[key] = value; }
    }

    // A probe that finds the unit does not offer what it asks for throws this (or returns Unsupported itself).
    public sealed class ProbeUnsupportedException : Exception
    {
        public ProbeUnsupportedException(string why) : base(why) { }
    }

    public interface IProbe
    {
        string Id { get; }
        string Group { get; }
        string Name { get; }
        // Longest the probe may take, seconds. A probe that is still going is ended as an error.
        double TimeoutSeconds { get; }
        IEnumerator Run(ProbeContext ctx);
    }

    public abstract class ProbeBase : IProbe
    {
        public abstract string Id { get; }
        public abstract string Group { get; }
        public abstract string Name { get; }
        public virtual double TimeoutSeconds { get { return 30; } }
        public abstract IEnumerator Run(ProbeContext ctx);
    }

    // What a running probe sees: its row, a way to add more rows, the clock, the prompt line and the log.
    public sealed class ProbeContext
    {
        public readonly ProbeReport Report;
        public readonly ProbeRow Row;
        public readonly IProbeClock Clock;
        public readonly IProbeStore Store;
        public readonly ProbeShared Shared;
        readonly Action<string> log, prompt, banner;
        readonly Action<ProbeRow> changed;
        readonly List<Action> restores;

        public ProbeContext(ProbeReport report, ProbeRow row, IProbeClock clock, IProbeStore store, Action<string> log, Action<string> prompt, Action<ProbeRow> changed,
            ProbeShared shared = null, Action<string> banner = null, List<Action> restores = null)
        {
            Report = report; Row = row; Clock = clock; Store = store; this.log = log; this.prompt = prompt; this.changed = changed;
            Shared = shared ?? new ProbeShared(); this.banner = banner; this.restores = restores ?? new List<Action>();
        }

        public void Log(string s) { if (log != null) log(ProbeSanitizer.Clean(s)); }

        // A line that says what is in use right now ("camera in use: frames counted and dropped"); empty clears it.
        public void Banner(string s) { if (banner != null) banner(ProbeSanitizer.Clean(s ?? "")); }

        // Something this probe changed on the unit and must put back. It runs when the probe ends, and on pause, quit and destroy.
        public void OnRestore(Action restore) { restores.Add(restore); }

        // A line shown on the panel for the wearer ("pinch now"); empty clears it.
        public void Prompt(string s) { if (prompt != null) prompt(ProbeSanitizer.Clean(s ?? "")); }

        public void Pass(string detail) { Finish(Row, ProbeStatus.Pass, detail); }
        public void Fail(string detail) { Finish(Row, ProbeStatus.Fail, detail); }
        public void Unsupported(string detail) { Finish(Row, ProbeStatus.Unsupported, detail); }
        public void Info(string detail) { Finish(Row, ProbeStatus.Info, detail); }
        public void Skip(string detail) { Finish(Row, ProbeStatus.Skipped, detail); }
        public void Set(string key, object value) { Row.Set(key, ProbeSanitizer.CleanValue(value, key)); }

        public void Finish(ProbeRow row, ProbeStatus status, string detail)
        {
            row.Status = status; row.Detail = ProbeSanitizer.Clean(detail ?? "");
            row.DurationS = Math.Max(0, Clock.Now - row.StartedS);
            if (changed != null) changed(row);
        }

        // Another row owned by this probe (a probe that answers several questions, one row each).
        public ProbeRow Sub(string idSuffix, string name)
        {
            var r = new ProbeRow { Id = ProbeSanitizer.CleanId(Row.Id + "." + idSuffix), Group = Row.Group, Name = ProbeSanitizer.Clean(name), StartedS = Clock.Now };
            Report.Add(r);
            if (changed != null) changed(r);
            return r;
        }

        // Wait this long, one frame at a time.
        public IEnumerator Wait(double seconds)
        {
            double end = Clock.Now + seconds;
            while (Clock.Now < end) yield return null;
        }
    }

    // Writes the report as one JSON and one text file in a folder, replacing the earlier ones in a single step so a crash leaves a whole file.
    public static class ProbeReportWriter
    {
        public const string JsonName = "aura-probe-report.json";
        public const string TextName = "aura-probe-report.txt";

        public static bool Write(string dir, ProbeReport report, out string error)
        {
            error = null;
            try
            {
                Directory.CreateDirectory(dir);
                Replace(Path.Combine(dir, JsonName), report.ToJson());
                Replace(Path.Combine(dir, TextName), report.ToText());
                return true;
            }
            catch (Exception e)
            {
                error = e.GetType().Name + ": " + e.Message;
                return false;
            }
        }

        static void Replace(string path, string text)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text, new System.Text.UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
    }

    // Saves the report and remembers whether that worked, so the panel and the log can say so when it did not (a full or unwritable folder, a write that
    // left an empty file). The rows stay on the panel either way.
    public sealed class ReportSaver
    {
        readonly string dir;
        readonly Action<string> log;
        public string LastError { get; private set; }

        public ReportSaver(string dir, Action<string> log) { this.dir = dir; this.log = log; }

        public bool Save(ProbeReport report)
        {
            string err;
            bool ok = ProbeReportWriter.Write(dir, report, out err);
            if (ok) ok = Intact(out err);
            if (ok) { LastError = null; return true; }
            string clean = ProbeSanitizer.Clean(err ?? "unknown error");
            if (clean != LastError && log != null) log("report could not be written: " + clean);
            LastError = clean;
            return false;
        }

        bool Intact(out string err)
        {
            err = null;
            try
            {
                foreach (var n in new[] { ProbeReportWriter.JsonName, ProbeReportWriter.TextName })
                {
                    var f = new FileInfo(Path.Combine(dir, n));
                    if (!f.Exists || f.Length == 0) { err = n + " is missing or empty after the write"; return false; }
                }
                return true;
            }
            catch (Exception e) { err = e.GetType().Name + ": " + e.Message; return false; }
        }

        // The panel line for a failed save ("" when the last save worked).
        public string WarningLine
        {
            get { return LastError == null ? "" : "report could not be written: " + LastError + ". The rows stay on this panel and are in the log (adb logcat -s Unity)."; }
        }

        // The whole text report as log lines, for a unit where the files cannot be pulled. Every line has already been through the sanitiser.
        public static string[] LogLines(ProbeReport report)
        {
            var lines = report.ToText().Replace("\r", "").Split('\n');
            var o = new List<string>();
            foreach (var l in lines) if (l.Length > 0) o.Add("report| " + l);
            return o.ToArray();
        }
    }

    // Runs probes one after another. Every probe is wrapped: a thing the unit does not offer, or any exception, becomes a row
    // that says so, and the run goes on. The report is saved after every row, so a crash still leaves what was learnt.
    public sealed class ProbeRunner
    {
        readonly ProbeReport report;
        readonly IProbeClock clock;
        readonly IProbeStore store;
        readonly Action<ProbeReport> save;
        readonly Action<string> log;
        public Action<string> PromptSink, BannerSink;
        public readonly ProbeShared Shared = new ProbeShared();
        readonly List<Action> restores = new List<Action>();
        Stack<IEnumerator> activeStack;
        public event Action<ProbeRow> RowChanged;
        public ProbeRow Current { get; private set; }

        public ProbeRunner(ProbeReport report, IProbeClock clock, IProbeStore store, Action<ProbeReport> save, Action<string> log)
        {
            this.report = report; this.clock = clock; this.store = store; this.save = save; this.log = log;
        }

        void Changed(ProbeRow r)
        {
            if (RowChanged != null) RowChanged(r);
            if (r.Status != ProbeStatus.Running && save != null)
            {
                try { save(report); } catch (Exception) { /* saving must never end the run */ }
            }
        }

        // How an exception is told in the report: a missing feature is "unsupported", anything else an error.
        public static void Classify(ProbeRow row, Exception e, IProbeClock clock)
        {
            if (row.Status != ProbeStatus.Running) return;
            ProbeStatus st = ProbeStatus.Error;
            if (e is ProbeUnsupportedException || e is NotSupportedException || e is NotImplementedException || e is MissingMethodException
                || e is MissingFieldException || e is EntryPointNotFoundException || e is DllNotFoundException || e is TypeLoadException) st = ProbeStatus.Unsupported;
            string msg = ProbeSanitizer.Clean(e.Message);
            if (msg.Length > 300) msg = msg.Substring(0, 300);
            row.Status = st;
            row.Detail = st == ProbeStatus.Unsupported ? "unsupported: " + msg : "error: " + e.GetType().Name + ": " + msg;
            row.DurationS = Math.Max(0, clock.Now - row.StartedS);
        }

        public IEnumerator RunAll(IEnumerable<IProbe> probes)
        {
            foreach (var p in probes)
            {
                var it = RunProbe(p);
                while (it.MoveNext()) yield return it.Current;
            }
        }

        public IEnumerator RunProbe(IProbe probe)
        {
            var row = new ProbeRow { Id = probe.Id, Group = probe.Group, Name = ProbeSanitizer.Clean(probe.Name), StartedS = clock.Now };
            report.Add(row);
            Current = row;
            if (RowChanged != null) RowChanged(row);
            var ctx = new ProbeContext(report, row, clock, store, log, PromptSink, Changed, Shared, BannerSink, restores);
            var stack = new Stack<IEnumerator>();
            activeStack = stack;
            try { stack.Push(probe.Run(ctx)); }
            catch (Exception e) { Classify(row, e, clock); }
            double deadline = clock.Now + probe.TimeoutSeconds;
            try
            {
            while (stack.Count > 0)
            {
                if (clock.Now > deadline)
                {
                    if (row.Status == ProbeStatus.Running) { row.Status = ProbeStatus.Error; row.Detail = "error: timed out after " + probe.TimeoutSeconds.ToString("0") + " s"; row.DurationS = clock.Now - row.StartedS; }
                    break;
                }
                bool moved = false; object cur = null; Exception ex = null;
                try
                {
                    var top = stack.Peek();
                    moved = top.MoveNext();
                    if (moved) cur = top.Current;
                }
                catch (Exception e) { ex = e; }
                if (ex != null) { Classify(row, ex, clock); break; }
                if (!moved) { stack.Pop(); continue; }
                var inner = cur as IEnumerator;
                if (inner != null) { stack.Push(inner); continue; }
                yield return cur;
            }
            }
            finally
            {
                // An abandoned probe (timeout, error, or the whole run stopped) has its finally blocks run, then what it changed is put back.
                DisposeAll(stack);
                activeStack = null;
                RunRestores();
                if (BannerSink != null) BannerSink("");
            }
            // Anything the probe left without an answer is an error, never a silent gap.
            for (int i = report.Rows.IndexOf(row); i >= 0 && i < report.Rows.Count; i++)
            {
                var r = report.Rows[i];
                if (r.Status == ProbeStatus.Running && (r == row || r.Id.StartsWith(row.Id + ".", StringComparison.Ordinal)))
                {
                    r.Status = ProbeStatus.Error; r.Detail = "error: the probe ended without an answer"; r.DurationS = Math.Max(0, clock.Now - r.StartedS);
                }
            }
            if (row.DurationS <= 0) row.DurationS = Math.Max(0, clock.Now - row.StartedS);
            Changed(row);
            if (log != null) log(ProbeReport.StatusName(row.Status) + " " + ProbeSanitizer.CleanId(row.Id) + ": " + ProbeSanitizer.Clean(row.Detail));
            Current = null;
        }

        static void DisposeAll(Stack<IEnumerator> stack)
        {
            while (stack.Count > 0)
            {
                var d = stack.Pop() as IDisposable;
                try { if (d != null) d.Dispose(); } catch (Exception) { /* a finally block that fails must not stop the others */ }
            }
        }

        void RunRestores()
        {
            var list = restores.ToArray();
            restores.Clear();
            foreach (var r in list)
            {
                try { r(); } catch (Exception) { /* put back what can be put back */ }
            }
        }

        // The app is pausing, quitting or being destroyed: put back everything a probe changed, now.
        public void RestoreAll() { RunRestores(); }

        // Ends the probe in progress: its enumerators are disposed so their finally blocks run, then the restores.
        public void Abort()
        {
            var st = activeStack;
            if (st != null) DisposeAll(st);
            RunRestores();
        }

        // Runs an enumerator to its end with no Unity behind it (tests): each yield advances the clock by one frame.
        public static void RunToEnd(IEnumerator it, ManualProbeClock clock, double frameSeconds = 1.0 / 60.0, int maxFrames = 2000000)
        {
            int n = 0;
            while (it.MoveNext())
            {
                clock.Advance(frameSeconds);
                if (++n > maxFrames) throw new InvalidOperationException("probe did not end");
            }
        }
    }
}
