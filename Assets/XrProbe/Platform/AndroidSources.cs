using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace XrProbe
{
    // Calls into XrProbeNative.java (Assets/XrProbe/Plugins/Android): Camera2, audio inputs, thermal and battery. Off Android every call
    // says "unsupported" so the same suite runs in the editor and reports so.
    public static class AndroidBridge
    {
        public const string NativeClass = "xrprobe.plugin.ProbeNative";

#if UNITY_ANDROID && !UNITY_EDITOR
        public static string CallString(string method, params object[] args)
        {
            using (var c = new AndroidJavaClass(NativeClass)) return c.CallStatic<string>(method, args);
        }

        public static void CallVoid(string method, params object[] args)
        {
            using (var c = new AndroidJavaClass(NativeClass)) c.CallStatic(method, args);
        }
#else
        public static string CallString(string method, params object[] args) { throw new ProbeUnsupportedException("not running on an Android device"); }
        public static void CallVoid(string method, params object[] args) { throw new ProbeUnsupportedException("not running on an Android device"); }
#endif

        public static bool OnAndroid
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        // The extra a launch intent carried (adb shell am start ... --es key value); empty off Android or when absent.
        public static string IntentExtra(string key)
        {
            if (!OnAndroid) return "";
            try { return CallString("intentExtra", key); } catch (Exception) { return ""; }
        }

        public static string IdentityJson()
        {
            if (!OnAndroid) return "{}";
            try { return CallString("identityJson"); } catch (Exception) { return "{}"; }
        }

        // Asks for a runtime permission and waits for the answer. A permission already held answers at once.
        public static IEnumerator Request(string permission, Action<bool> granted)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Permission.HasUserAuthorizedPermission(permission)) { granted(true); yield break; }
            bool done = false, ok = false;
            var cb = new PermissionCallbacks();
            cb.PermissionGranted += _ => { ok = true; done = true; };
            cb.PermissionDenied += _ => { ok = false; done = true; };
            cb.PermissionDeniedAndDontAskAgain += _ => { ok = false; done = true; };
            Permission.RequestUserPermission(permission, cb);
            float end = Time.realtimeSinceStartup + 60f;
            while (!done && Time.realtimeSinceStartup < end) yield return null;
            granted(ok && Permission.HasUserAuthorizedPermission(permission));
#else
            granted(false);
            yield break;
#endif
        }

        public static bool Has(string permission)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return Permission.HasUserAuthorizedPermission(permission);
#else
            return false;
#endif
        }
    }

    public sealed class AndroidCameraSource : ICameraSource
    {
        public const string Permission = "android.permission.CAMERA";

        public string ListJson() { return AndroidBridge.CallString("cameraListJson"); }

        public IEnumerator RequestPermission(Action<bool> granted) { return AndroidBridge.Request(Permission, granted); }

        public IEnumerator CountFrames(string cameraId, double seconds, FrameTest result)
        {
            AndroidBridge.CallVoid("cameraCountFrames", cameraId, seconds);
            float end = Time.realtimeSinceStartup + (float)seconds + 12f;
            JObject st = null;
            while (Time.realtimeSinceStartup < end)
            {
                yield return null;
                try { st = JObject.Parse(AndroidBridge.CallString("cameraStatus")); } catch (Exception) { st = null; }
                if (st != null && (bool)st["done"]) break;
            }
            if (st == null || !(bool)st["done"]) { result.Error = "the frame count did not finish in time"; yield break; }
            result.Ok = (bool)st["ok"]; result.Frames = (int)st["frames"]; result.Seconds = (double)st["seconds"];
            result.Width = (int)st["width"]; result.Height = (int)st["height"]; result.Error = (string)st["error"] ?? "";
        }
    }

    public sealed class AndroidAudioSource : IAudioSource
    {
        public const string Permission = "android.permission.RECORD_AUDIO";

        public string ListJson() { return AndroidBridge.CallString("audioListJson"); }

        public IEnumerator RequestPermission(Action<bool> granted) { return AndroidBridge.Request(Permission, granted); }

        public IEnumerator ReadTest(double seconds, int channels, AudioTest result)
        {
            AndroidBridge.CallVoid("audioRead", seconds, channels);
            float end = Time.realtimeSinceStartup + (float)seconds + 10f;
            JObject st = null;
            while (Time.realtimeSinceStartup < end)
            {
                yield return null;
                try { st = JObject.Parse(AndroidBridge.CallString("audioStatus")); } catch (Exception) { st = null; }
                if (st != null && (bool)st["done"]) break;
            }
            if (st == null || !(bool)st["done"]) { result.Error = "the audio read did not finish in time"; yield break; }
            result.Ok = (bool)st["ok"]; result.Frames = (int)st["frames"]; result.Seconds = (double)st["seconds"]; result.SampleRate = (int)st["sample_rate"];
            result.Channels = (int)st["channels"]; result.PeakLevel = (double)st["peak"]; result.Error = (string)st["error"] ?? "";
        }
    }

    public sealed class AndroidDeviceSource : IDeviceSource
    {
        public string Start()
        {
            if (!AndroidBridge.OnAndroid) return "not running on an Android device";
            return null;
        }

        public DeviceSample Read()
        {
            var o = JObject.Parse(AndroidBridge.CallString("deviceJson"));
            var d = new DeviceSample();
            if (o["thermal_status"] != null) d.ThermalStatus = (int)o["thermal_status"];
            d.ThermalHeadroom = o["thermal_headroom"] != null ? (double)o["thermal_headroom"] : -1;
            if (o["battery_percent"] != null) d.BatteryPercent = (double)o["battery_percent"];
            if (o["battery_temp_c"] != null) d.BatteryTempC = (double)o["battery_temp_c"];
            d.Charging = o["charging"] != null && (bool)o["charging"];
            return d;
        }
    }
}
