using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace XrProbe
{
    // Keeps identifying text out of the report, the panel and the log. Everything a probe writes as a string passes through Clean; the
    // sources also never read the things that would identify a person (device names, accounts, networks), so this is the second layer.
    public static class ProbeSanitizer
    {
        static readonly Regex[] Rules =
        {
            new Regex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.Compiled),                       // e-mail
            new Regex(@"(?<![0-9A-Fa-f])[0-9A-Fa-f]{2}([:\-][0-9A-Fa-f]{2}){5}(?![0-9A-Fa-f])", RegexOptions.Compiled),                              // MAC
            new Regex(@"(?<!\b(?i:version|ver|v|api|openxr|xr|runtime|plug-?in|driver|build|release|android|unity)[ :=]*)(?<![0-9])\d{1,3}(\.\d{1,3}){3}(?![0-9])", RegexOptions.Compiled), // IPv4 (not a four-part version after a cue word)
            new Regex(@"\b([0-9A-Fa-f]{1,4}:){3,7}[0-9A-Fa-f]{1,4}\b", RegexOptions.Compiled),                            // IPv6
            new Regex(@"(?i)\bssid\b\s*[:=]?\s*(""[^""]*""|'[^']*'|\S+)", RegexOptions.Compiled),                         // network name
            new Regex(@"\b[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\b", RegexOptions.Compiled), // GUID
            new Regex(@"\b[0-9A-Fa-f]{16}-[0-9A-Fa-f]{16}\b", RegexOptions.Compiled),                                     // Unity trackable / saved id
            new Regex(@"\b[A-Za-z]:[\\/][^\s""']*", RegexOptions.Compiled),                                               // Windows path
            new Regex(@"(?<![\w.])/(?:[\w.@+\-]+/)+[\w.@+\-]*", RegexOptions.Compiled),                                   // Unix path
            new Regex(@"(?i)\b(uid|pid|userid|user)\s*[=:]\s*\S+", RegexOptions.Compiled),                                // uid and the like
            new Regex(@"\bu\d+_a\d+\b", RegexOptions.Compiled),                                                           // Android app uid
            new Regex(@"\b(?:com|org|net|io|dev|app|co|me|edu|gov|android|androidx|google|samsung|huawei|xiaomi|oppo|vivo|meta|oculus|qualcomm|unity|java|javax|kotlin)(\.[a-z][a-z0-9_]*){2,}\b", RegexOptions.Compiled),   // package names
            new Regex(@"-?\d{1,3}\.\d{4,}\s*,\s*-?\d{1,3}\.\d{4,}", RegexOptions.Compiled),                               // coordinate pair
            new Regex(@"(?i)\b(lat|lon|lng|latitude|longitude)\b\s*[:=]?\s*-?\d+(\.\d+)?", RegexOptions.Compiled),        // labelled coordinate
            new Regex(@"\b[A-Z][a-z]{1,}['’]s\s+[A-Za-z0-9\-]+", RegexOptions.Compiled),                             // a person's device name
            new Regex(@"(?<![A-Za-z0-9])(?=[A-Za-z0-9]*\d)(?=[A-Za-z0-9]*[A-Za-z])[A-Za-z0-9]{10,}(?![A-Za-z0-9])", RegexOptions.Compiled),          // serial-like token
        };

        static readonly string[] Labels =
        {
            "<email>", "<mac>", "<ip>", "<ip>", "ssid <redacted>", "<id>", "<id>", "<path>", "<path>", "<redacted>", "<uid>", "<name>", "<coordinates>", "<coordinates>", "<device name>", "<token>",
        };

        static readonly Regex PlainVersion = new Regex(@"^\d+(\.\d+){2,3}([-+][\w.\-]+)?$", RegexOptions.Compiled);

        public static string Clean(string s) { return Clean(s, false); }

        // versionField: the value belongs to a key that names a version (runtime_version, api_version ...). If the whole value is a plain version number, a
        // four-part one is not taken for an address; anything else in such a field is cleaned as usual.
        public static string Clean(string s, bool versionField)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            bool plainVersion = versionField && PlainVersion.IsMatch(s);
            for (int i = 0; i < Rules.Length; i++) { if (plainVersion && i == 2) continue; s = Rules[i].Replace(s, Labels[i]); }
            return s.Length > 400 ? s.Substring(0, 400) : s;
        }

        public static bool IsVersionKey(string key) { return key != null && key.EndsWith("version", StringComparison.OrdinalIgnoreCase); }

        // A row id is built from fixed words and sometimes a label or a camera id. It keeps its dots (the package-name and token rules would mangle it), loses
        // anything personal by the other rules, and is cut down to a plain character set.
        public static string CleanId(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            for (int i = 0; i < Rules.Length; i++) { if (i == 11) continue; s = Rules[i].Replace(s, Labels[i]); }
            s = Regex.Replace(s, @"[^A-Za-z0-9._-]", "_");
            return s.Length > 64 ? s.Substring(0, 64) : s;
        }

        // A value that goes into a row: strings are cleaned, arrays of strings too, everything else (numbers, booleans) is left alone.
        public static object CleanValue(object v) { return CleanValue(v, null); }

        public static object CleanValue(object v, string key)
        {
            bool ver = IsVersionKey(key);
            var s = v as string;
            if (s != null) return Clean(s, ver);
            var arr = v as string[];
            if (arr != null) { var o = new string[arr.Length]; for (int i = 0; i < arr.Length; i++) o[i] = Clean(arr[i], ver); return o; }
            return v;
        }
    }

    // A label for something the platform identifies (a saved anchor): a short hash that changes every run, so a label means "the same one within
    // this report" and two reports can never be linked through it.
    public sealed class ProbeShared
    {
        readonly byte[] salt = new byte[16];
        readonly Dictionary<string, bool> permissions = new Dictionary<string, bool>();
        public double RefreshHz;
        public double? FloorY;    // metres, from the biggest floor plane the plane survey saw

        public ProbeShared()
        {
            using (var r = RandomNumberGenerator.Create()) r.GetBytes(salt);
        }

        public string Label(string id)
        {
            if (string.IsNullOrEmpty(id)) return "none";
            using (var h = SHA256.Create())
            {
                var data = new byte[salt.Length + Encoding.UTF8.GetByteCount(id)];
                Buffer.BlockCopy(salt, 0, data, 0, salt.Length);
                Encoding.UTF8.GetBytes(id, 0, id.Length, data, salt.Length);
                var d = h.ComputeHash(data);
                return "#" + d[0].ToString("x2") + d[1].ToString("x2") + d[2].ToString("x2");
            }
        }

        public void SetPermission(string permission, bool granted) { permissions[permission] = granted; }

        // True only when the wearer was asked and said no. Not asked yet, or no permission system (the editor), is not a denial.
        public bool Denied(string permission) { bool g; return permission != null && permissions.TryGetValue(permission, out g) && !g; }
    }

    // The device facts the report may carry. Anything else the platform offers (serial, account, name, network) is dropped by key.
    public static class DeviceFacts
    {
        public static readonly HashSet<string> Allowed = new HashSet<string>
        {
            "manufacturer", "model", "device", "product", "android_release", "sdk_int", "security_patch", "abis",
        };

        public static bool Accept(string key) { return Allowed.Contains(key); }

        // Copies only the allowed keys of a JSON object of device facts (anything else the platform offered, such as a serial or a name, is dropped).
        public static void Collect(string json, IDictionary<string, string> into)
        {
            var o = Newtonsoft.Json.Linq.JObject.Parse(json);
            foreach (var kv in o) if (Accept(kv.Key)) into[kv.Key] = ProbeSanitizer.Clean(kv.Value.ToString());
        }
    }

    // Limits for the soak. Conservative defaults chosen without a unit to read them from; the operating range of the Aura's puck is published as 0 to 35 degrees C
    // (ambient), not as a battery temperature, so these are not vendor limits.
    public sealed class SafetyLimits
    {
        public int MaxThermalStatus = 3;        // Android thermal status 3 is "severe"
        public double MaxBatteryTempC = 42.0;
        public double MinBatteryPercent = 20.0;

        // The reason to stop (or not to start), or null when all readings are inside the limits. An unreadable value is not a reason.
        public string Check(DeviceSample d)
        {
            if (d == null) return null;
            if (d.ThermalStatus >= MaxThermalStatus) return "thermal status " + d.ThermalStatus + " reached (limit " + MaxThermalStatus + ", conservative default)";
            if (d.BatteryTempC >= MaxBatteryTempC) return "battery temperature " + d.BatteryTempC.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " C reached (limit " + MaxBatteryTempC.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " C, conservative default)";
            if (d.BatteryPercent >= 0 && d.BatteryPercent < MinBatteryPercent && !d.Charging) return "battery at " + d.BatteryPercent.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " percent and not charging (limit " + MinBatteryPercent.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " percent, conservative default)";
            return null;
        }
    }

    // The first screen: nothing runs until the wearer starts it on purpose.
    public static class ProbeTexts
    {
        public const string NotAffiliated = "Not affiliated with XREAL, Google or Unity.";

        public static readonly string[] Intro =
        {
            "<b>XR GLASSES CAPABILITY PROBE</b> (for XREAL AURA)",
            "",
            "This app asks the glasses what they can do (tracking, planes,",
            "anchors, hands, display, camera and microphone lists, heat).",
            "It has never been run on a retail unit: expect surprises.",
            "",
            "<b>Sit down and stay still. The lenses will dim and colours will",
            "flash for a few seconds.</b> It takes about 5 minutes, then an",
            "optional 10-minute soak that stops by itself if the unit heats up.",
            "",
            "Recorded: a report on this device only (no photos, no audio).",
            "Camera frames are counted and dropped. The microphone level is",
            "measured and nothing is kept. There is no network access at all.",
            "You decide whether to share the report.",
            "",
            NotAffiliated,
        };
    }

    // Decides when the probe starts. Only a deliberate start (a click, tap, key or pinch) starts it, never a timer: a wearer who cannot stop a run must not
    // be put through one. If no input of any kind has been seen for a minute the screen says so and goes on waiting.
    public sealed class StartGate
    {
        public enum State { Waiting, Started }
        public const double NoInputSeconds = 60;

        readonly double shownAt;
        bool anySignal;
        public State Current { get; private set; }

        public StartGate(double now) { shownAt = now; }

        public void Tick(double now, bool inputSignal, bool startPressed)
        {
            if (Current == State.Started) return;
            if (startPressed) { Current = State.Started; return; }
            if (inputSignal) anySignal = true;
            noInput = !anySignal && now - shownAt >= NoInputSeconds;
        }

        bool noInput;
        public bool NoInputDetected { get { return noInput; } }

        public string StatusLine(double now)
        {
            if (Current == State.Started) return "Starting...";
            if (noInput) return "No input detected: nothing was run. Nothing will run until an input works and you start it (click the trackpad, pinch, tap or press a key).";
            return "To start: click the puck's trackpad (or pinch, tap or press a key). Nothing runs until you do.";
        }
    }
}
