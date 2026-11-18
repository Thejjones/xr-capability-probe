using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace XrProbe
{
    // A simple world-locked panel: text only (the Aura adds light, so dark is see-through). Pass, fail and unsupported per row, in colour.
    public class ProbePanel : MonoBehaviour
    {
        public TextMesh text;
        public int visibleRows = 16;
        public string Headline = "";
        public string Prompt = "";
        public string Banner = "";
        public string Footer = "";
        // A problem the wearer should see (a report that could not be written). Shown in red under the headline.
        public string Warning = "";
        // When set, the whole panel shows this text (the first screen) instead of the rows.
        public string Intro;

        ProbeReport lastReport;
        ProbeRow lastRunning;
        float nextBuild;

        public static string Colour(ProbeStatus s)
        {
            switch (s)
            {
                case ProbeStatus.Pass: return "#7CFF9B";
                case ProbeStatus.Fail: return "#FF7A7A";
                case ProbeStatus.Unsupported: return "#FFD27C";
                case ProbeStatus.Error: return "#FF7AE0";
                case ProbeStatus.Info: return "#9CCBFF";
                case ProbeStatus.Skipped: return "#AAAAAA";
                default: return "#FFFFFF";
            }
        }

        public static string Tag(ProbeStatus s)
        {
            switch (s)
            {
                case ProbeStatus.Pass: return "PASS ";
                case ProbeStatus.Fail: return "FAIL ";
                case ProbeStatus.Unsupported: return "UNSUP";
                case ProbeStatus.Error: return "ERROR";
                case ProbeStatus.Info: return "INFO ";
                case ProbeStatus.Skipped: return "SKIP ";
                default: return "...  ";
            }
        }

        static string Trim(string s, int n) { s = (s ?? "").Replace('\n', ' '); return s.Length <= n ? s : s.Substring(0, n - 1) + "~"; }

        public void Show(ProbeReport report, ProbeRow running) { lastReport = report; lastRunning = running; Build(); }

        public void Tick()
        {
            if (Time.unscaledTime >= nextBuild) { nextBuild = Time.unscaledTime + 0.5f; Build(); }
        }

        // Where the panel stands: 1.8 m ahead of where the wearer is looking, level with the eyes, facing him.
        public void PlaceInFrontOf(Camera head)
        {
            Vector3 f = Vector3.ProjectOnPlane(head.transform.forward, Vector3.up);
            if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            f.Normalize();
            transform.position = head.transform.position + f * 1.8f;
            transform.rotation = Quaternion.LookRotation(f, Vector3.up);
        }

        // The text: headline, the running row and its prompt, then the latest finished rows, then the counts.
        public string Compose(ProbeReport report, ProbeRow running)
        {
            if (Intro != null) return Intro;
            var sb = new StringBuilder();
            sb.Append("<b>XR GLASSES CAPABILITY PROBE</b>  ").Append(Headline).Append('\n');
            if (report == null) return sb.ToString();
            if (!string.IsNullOrEmpty(Banner)) sb.Append("<color=#9CCBFF><b>").Append(Trim(Banner, 78)).Append("</b></color>\n");
            if (!string.IsNullOrEmpty(Warning)) foreach (var l in Wrap(Warning, 78)) sb.Append("<color=#FF8A8A>").Append(l).Append("</color>\n");
            if (report.Complete) return ComposeSummary(report, sb);
            if (running != null && running.Status == ProbeStatus.Running) sb.Append("<color=#FFFFFF>running: ").Append(Trim(running.Name, 62)).Append("</color>\n");
            if (!string.IsNullOrEmpty(Prompt)) sb.Append("<color=#FFF1A8>").Append(Trim(Prompt, 78)).Append("</color>\n");
            var done = new List<ProbeRow>();
            foreach (var r in report.Rows) if (r.Status != ProbeStatus.Running) done.Add(r);
            int from = Mathf.Max(0, done.Count - visibleRows);
            for (int i = from; i < done.Count; i++)
            {
                var r = done[i];
                sb.Append("<color=").Append(Colour(r.Status)).Append('>').Append(Tag(r.Status)).Append("</color> ").Append(Trim(r.Name, 38));
                if (!string.IsNullOrEmpty(r.Detail)) sb.Append("  <color=#AAAAAA>").Append(Trim(r.Detail, 40)).Append("</color>");
                sb.Append('\n');
            }
            sb.Append(report.Count(ProbeStatus.Pass)).Append(" pass   ").Append(report.Count(ProbeStatus.Fail)).Append(" fail   ")
              .Append(report.Count(ProbeStatus.Unsupported)).Append(" unsupported   ").Append(report.Count(ProbeStatus.Error)).Append(" error");
            if (report.Complete) sb.Append("\nDONE: the reports are in the app's files folder (see the README)");
            if (!string.IsNullOrEmpty(Footer)) sb.Append("\n<color=#FFF1A8>").Append(Footer).Append("</color>");
            return sb.ToString();
        }

        static List<string> Wrap(string s, int width)
        {
            var o = new List<string>(); var words = s.Split(' '); var line = "";
            foreach (var w in words)
            {
                if (line.Length > 0 && line.Length + 1 + w.Length > width) { o.Add(line); line = w; }
                else line = line.Length == 0 ? w : line + " " + w;
            }
            if (line.Length > 0) o.Add(line);
            return o;
        }

        // The end screen: what the wearer can read off if the files cannot be pulled. Failures and errors first, then what was unsupported, then how the
        // lenses and the blend mode were left.
        string ComposeSummary(ProbeReport report, StringBuilder sb)
        {
            sb.Append("<b>DONE</b> - summary (the full report is in the app's files folder; the log has it too)\n");
            int shown = 0;
            foreach (var r in report.Rows)
            {
                if (r.Status != ProbeStatus.Fail && r.Status != ProbeStatus.Error) continue;
                if (shown++ >= 8) break;
                sb.Append("<color=").Append(Colour(r.Status)).Append('>').Append(Tag(r.Status)).Append("</color> ").Append(Trim(r.Name, 38));
                if (!string.IsNullOrEmpty(r.Detail)) sb.Append("  <color=#AAAAAA>").Append(Trim(r.Detail, 40)).Append("</color>");
                sb.Append('\n');
            }
            if (shown == 0) sb.Append("no failures or errors\n");
            var unsupported = new List<string>();
            foreach (var r in report.Rows) if (r.Status == ProbeStatus.Unsupported) unsupported.Add(r.Id);
            if (unsupported.Count > 0) sb.Append("unsupported: ").Append(Trim(string.Join(", ", unsupported.ToArray()), 66)).Append('\n');
            foreach (var id in new[] { "display.dimming", "display.blend" })
            {
                var r = report.Find(id);
                if (r == null) continue;
                object restored;
                string word = r.Values.TryGetValue("original_restored", out restored) && restored is bool ? ((bool)restored ? "restored" : "NOT CONFIRMED RESTORED") : Tag(r.Status).Trim();
                sb.Append(id).Append(": original ").Append(word).Append('\n');
            }
            sb.Append(report.Count(ProbeStatus.Pass)).Append(" pass   ").Append(report.Count(ProbeStatus.Fail)).Append(" fail   ")
              .Append(report.Count(ProbeStatus.Unsupported)).Append(" unsupported   ").Append(report.Count(ProbeStatus.Error)).Append(" error   ")
              .Append(report.Count(ProbeStatus.Info)).Append(" info   ").Append(report.Count(ProbeStatus.Skipped)).Append(" skipped");
            if (!string.IsNullOrEmpty(Footer)) sb.Append("\n<color=#FFF1A8>").Append(Footer).Append("</color>");
            return sb.ToString();
        }

        void Build()
        {
            if (text == null) return;
            text.text = Compose(lastReport, lastRunning);
        }

        // A TextMesh panel made in code (the scene builder and the tests use this).
        public static ProbePanel Create(Transform parent)
        {
            var go = new GameObject("ProbePanel");
            if (parent != null) go.transform.SetParent(parent, false);
            var panel = go.AddComponent<ProbePanel>();
            var tgo = new GameObject("Text");
            tgo.transform.SetParent(go.transform, false);
            var tm = tgo.AddComponent<TextMesh>();
            tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.GetComponent<MeshRenderer>().sharedMaterial = tm.font.material;
            tm.fontSize = 64;
            tm.characterSize = 0.0058f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Left;
            tm.richText = true;
            tm.color = Color.white;
            panel.text = tm;
            return panel;
        }
    }
}
