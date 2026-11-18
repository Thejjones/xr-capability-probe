using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace XrProbe
{
    // What one probe concluded. Unsupported is an answer, not a failure: the unit does not offer the thing.
    public enum ProbeStatus { Running, Pass, Fail, Unsupported, Info, Error, Skipped }

    public sealed class ProbeRow
    {
        public string Id, Group, Name;
        public ProbeStatus Status = ProbeStatus.Running;
        public string Detail = "";
        public readonly SortedDictionary<string, object> Values = new SortedDictionary<string, object>(StringComparer.Ordinal);
        public double StartedS, DurationS;

        public void Set(string key, object value) { Values[key] = value; }
    }

    public sealed class ProbeSample
    {
        public double T;
        public readonly SortedDictionary<string, double> V = new SortedDictionary<string, double>(StringComparer.Ordinal);
    }

    // The whole report: rows in the order they ran, series of samples over time, and the facts about the device the app could read.
    public sealed class ProbeReport
    {
        public const int Format = 1;
        public string App = "XR Glasses Capability Probe";
        public string Version = "dev";
        public string StartedUtc = "", FinishedUtc = "";
        public bool Complete;
        // A line that says what this report is (a sample made from fakes, for instance). Empty for a real run.
        public string Notice = "";
        public readonly SortedDictionary<string, string> Device = new SortedDictionary<string, string>(StringComparer.Ordinal);
        public readonly List<ProbeRow> Rows = new List<ProbeRow>();
        public readonly SortedDictionary<string, List<ProbeSample>> Series = new SortedDictionary<string, List<ProbeSample>>(StringComparer.Ordinal);

        public ProbeRow Add(ProbeRow r) { Rows.Add(r); return r; }

        public ProbeRow Find(string id) { return Rows.Find(r => r.Id == id); }

        public int Count(ProbeStatus s)
        {
            int n = 0;
            foreach (var r in Rows) if (r.Status == s) n++;
            return n;
        }

        public void AddSample(string series, ProbeSample s)
        {
            List<ProbeSample> l;
            if (!Series.TryGetValue(series, out l)) { l = new List<ProbeSample>(); Series[series] = l; }
            l.Add(s);
        }

        static string Name(ProbeStatus s)
        {
            switch (s)
            {
                case ProbeStatus.Pass: return "pass";
                case ProbeStatus.Fail: return "fail";
                case ProbeStatus.Unsupported: return "unsupported";
                case ProbeStatus.Info: return "info";
                case ProbeStatus.Error: return "error";
                case ProbeStatus.Skipped: return "skipped";
                default: return "running";
            }
        }

        public static string StatusName(ProbeStatus s) { return Name(s); }

        static JToken Token(string key, object v)
        {
            if (v == null) return JValue.CreateNull();
            v = ProbeSanitizer.CleanValue(v, key);
            if (v is float f) return new JValue(Math.Round((double)f, 4));
            if (v is double d) return new JValue(double.IsNaN(d) || double.IsInfinity(d) ? 0.0 : Math.Round(d, 4));
            return JToken.FromObject(v);
        }

        public string ToJson()
        {
            var rows = new JArray();
            foreach (var r in Rows)
            {
                var values = new JObject();
                foreach (var kv in r.Values) values[ProbeSanitizer.Clean(kv.Key)] = Token(kv.Key, kv.Value);
                rows.Add(new JObject
                {
                    { "id", ProbeSanitizer.CleanId(r.Id) }, { "group", r.Group }, { "name", ProbeSanitizer.Clean(r.Name) }, { "status", Name(r.Status) }, { "detail", ProbeSanitizer.Clean(r.Detail) },
                    { "started_s", Math.Round(r.StartedS, 2) }, { "duration_s", Math.Round(r.DurationS, 2) }, { "values", values },
                });
            }
            var series = new JObject();
            foreach (var kv in Series)
            {
                var arr = new JArray();
                foreach (var s in kv.Value)
                {
                    var o = new JObject { { "t", Math.Round(s.T, 2) } };
                    foreach (var v in s.V) o[ProbeSanitizer.Clean(v.Key)] = Token(v.Key, v.Value);
                    arr.Add(o);
                }
                series[ProbeSanitizer.Clean(kv.Key)] = arr;
            }
            var device = new JObject();
            foreach (var kv in Device) device[kv.Key] = ProbeSanitizer.Clean(kv.Value);
            var summary = new JObject();
            foreach (ProbeStatus st in Enum.GetValues(typeof(ProbeStatus))) summary[Name(st)] = Count(st);
            var root = new JObject
            {
                { "format", Format }, { "app", App }, { "version", Version },
                { "notice", ProbeSanitizer.Clean(Notice) }, { "started_utc", StartedUtc }, { "finished_utc", FinishedUtc }, { "complete", Complete },
                { "device", device }, { "summary", summary }, { "rows", rows }, { "series", series },
            };
            return root.ToString(Formatting.Indented);
        }

        static string Upper(ProbeStatus s)
        {
            switch (s)
            {
                case ProbeStatus.Pass: return "PASS";
                case ProbeStatus.Fail: return "FAIL";
                case ProbeStatus.Unsupported: return "UNSUPPORTED";
                case ProbeStatus.Info: return "INFO";
                case ProbeStatus.Error: return "ERROR";
                case ProbeStatus.Skipped: return "SKIPPED";
                default: return "RUNNING";
            }
        }

        static string Fmt(string key, object v)
        {
            if (v == null) return "null";
            v = ProbeSanitizer.CleanValue(v, key);
            if (v is double d) return d.ToString("0.###", CultureInfo.InvariantCulture);
            if (v is float f) return f.ToString("0.###", CultureInfo.InvariantCulture);
            if (v is System.Collections.IEnumerable e && !(v is string))
            {
                var parts = new List<string>();
                foreach (var x in e) parts.Add(Fmt(key, x));
                return "[" + string.Join(", ", parts.ToArray()) + "]";
            }
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        // The human-readable report: a header, the counts, one line per row, the series' ranges.
        public string ToText()
        {
            var sb = new StringBuilder();
            sb.AppendLine(App + " report (format " + Format + ", app version " + Version + ")");
            if (Notice.Length > 0) sb.AppendLine("NOTICE: " + ProbeSanitizer.Clean(Notice));
            sb.AppendLine("started " + StartedUtc + "   finished " + (string.IsNullOrEmpty(FinishedUtc) ? "(not yet)" : FinishedUtc) + "   complete: " + (Complete ? "yes" : "no"));
            foreach (var kv in Device) sb.AppendLine("device " + kv.Key + ": " + ProbeSanitizer.Clean(kv.Value));
            var counts = new List<string>();
            foreach (ProbeStatus st in Enum.GetValues(typeof(ProbeStatus))) if (st != ProbeStatus.Running || Count(st) > 0) counts.Add(Count(st) + " " + Name(st));
            sb.AppendLine("summary: " + string.Join(", ", counts.ToArray()));
            sb.AppendLine();
            string group = null;
            foreach (var r in Rows)
            {
                if (r.Group != group) { group = r.Group; sb.AppendLine("== " + group); }
                sb.AppendLine("  [" + Upper(r.Status) + "] " + ProbeSanitizer.Clean(r.Name) + (r.Detail.Length > 0 ? ": " + ProbeSanitizer.Clean(r.Detail) : "") + "   (" + ProbeSanitizer.CleanId(r.Id) + ", " + r.DurationS.ToString("0.0", CultureInfo.InvariantCulture) + " s)");
                foreach (var kv in r.Values) sb.AppendLine("        " + ProbeSanitizer.Clean(kv.Key) + " = " + Fmt(kv.Key, kv.Value));
            }
            if (Series.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("== series (full samples are in the JSON)");
                foreach (var kv in Series)
                {
                    var names = new SortedSet<string>(StringComparer.Ordinal);
                    foreach (var s in kv.Value) foreach (var k in s.V.Keys) names.Add(k);
                    foreach (var n in names)
                    {
                        double min = double.MaxValue, max = double.MinValue, first = 0, last = 0; int c = 0;
                        foreach (var s in kv.Value)
                        {
                            double v;
                            if (!s.V.TryGetValue(n, out v)) continue;
                            if (c == 0) first = v;
                            last = v; c++;
                            if (v < min) min = v;
                            if (v > max) max = v;
                        }
                        if (c > 0) sb.AppendLine("  " + ProbeSanitizer.Clean(kv.Key + "." + n) + ": " + c + " samples, first " + first.ToString("0.###", CultureInfo.InvariantCulture) + ", last " + last.ToString("0.###", CultureInfo.InvariantCulture) + ", min " + min.ToString("0.###", CultureInfo.InvariantCulture) + ", max " + max.ToString("0.###", CultureInfo.InvariantCulture));
                    }
                }
            }
            return sb.ToString();
        }
    }
}
