using System;
using System.IO;
using System.Linq;
using XrProbe;
using NUnit.Framework;
using UnityEngine;

// Writes the sample report the README shows. Every value comes from the fake sources of these tests (invented), and the
// report says so in its notice. It is a picture of the report's shape, not a measurement of any unit.
public partial class XrProbeTests
{
    public const string SampleNotice = "SAMPLE FROM SIMULATED DATA, NOT A REAL DEVICE. Every value was made up by the probe's own tests to show the shape of a report.";

    static string SampleDir
    {
        get
        {
            var o = Environment.GetEnvironmentVariable("PROBE_SAMPLE_OUT");
            return string.IsNullOrEmpty(o) ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "XrProbeSample")) : o;
        }
    }

    [Test]
    public void SampleReport_IsWrittenFromFakes_AndSaysPlainlyThatItIsNotARealDevice()
    {
        var report = new ProbeReport { Version = "sample", StartedUtc = "2000-01-01T00:00:00Z", FinishedUtc = "2000-01-01T00:09:30Z", Complete = true, Notice = SampleNotice };
        // The keys the app writes (ProbeApp.FillDevice and DeviceFacts), with invented values.
        report.Device["unity_version"] = "6000.6.9f1";
        report.Device["platform"] = "Android";
        report.Device["device_model"] = "Sample unit (invented)";
        report.Device["operating_system"] = "Android OS 14 / API-34 (invented build)";
        report.Device["processor"] = "ARM64 FP ASIMD (invented) x8";
        report.Device["memory_mb"] = "12000";
        report.Device["graphics_device"] = "Sample GPU (Vulkan 1.3.0)";
        report.Device["xr_loader"] = "OpenXR Loader (invented)";
        report.Device["manufacturer"] = "Sample maker";
        report.Device["model"] = "Sample unit";
        report.Device["device"] = "sample";
        report.Device["product"] = "sample";
        report.Device["android_release"] = "14";
        report.Device["sdk_int"] = "34";
        report.Device["security_patch"] = "2000-01-01";
        report.Device["abis"] = "arm64-v8a";
        var clock = new ManualProbeClock();
        var runner = new ProbeRunner(report, clock, new MemoryProbeStore(), null, null);
        runner.Shared.RefreshHz = 72;
        Action<IProbe> run = p => ProbeRunner.RunToEnd(runner.RunProbe(p), clock);

        run(new OpenXrRuntimeProbe(new FakeXr { Name = "Sample runtime", Version = "1.0", Api = "1.1.0", Plugin = "1.19.0", Enabled = new[] { "XR_SAMPLE_a" }, Available = new[] { "XR_SAMPLE_a", "XR_SAMPLE_b", "XR_SAMPLE_c" } }));
        run(new ExtensionListProbe(new FakeXr { Enabled = new[] { "XR_ANDROID_depth_texture" }, Available = new[] { "XR_ANDROID_depth_texture", "XR_KHR_sample" } }));
        run(new FeatureProbe("depth", "Environment depth", () => new FeatureAnswer { Supported = false, Detail = "no provider in this sample" }));
        var planes = new FakePlanes { Clock = clock };
        planes.At = t =>
        {
            var l = new System.Collections.Generic.List<PlaneInfo>();
            if (t >= 3) l.Add(new PlaneInfo { Id = "a", Label = "Floor", AreaM2 = 8 });
            if (t >= 10) { l.Add(new PlaneInfo { Id = "b", Label = "Wall", AreaM2 = 5 }); l.Add(new PlaneInfo { Id = "c", Label = "Table", AreaM2 = 1.2 }); }
            return l;
        };
        run(new PlaneSurveyProbe(planes, 20));
        run(new LightEstimationProbe(new FakeLight { GiveAfter = 2, Clock = clock }));
        run(new RefreshRateProbe(new FakeDisplay { Rates = new[] { 72f, 90f }, Current = 72 }));
        var e = new EyeInfo { Width = 1920, Height = 1080, FovLeftDeg = 26, FovRightDeg = 26, FovUpDeg = 15, FovDownDeg = 15 };
        run(new EyeProbe(new FakeDisplay { EyeData = new[] { e, e }, Scale = 1.0 }));
        run(new CameraProbe(new FakeCamera { Json = TwoCameras, Frames = 120 }, 4));
        run(new MicrophoneProbe(new FakeAudio { Json = FourMics }));
        var dev = new FakeDevice { At = n => new DeviceSample { ThermalStatus = n < 20 ? 0 : 1, ThermalHeadroom = 0.35 + 0.002 * n, BatteryPercent = 85 - n * 0.05, BatteryTempC = 31 + n * 0.02, Charging = false } };
        run(new DeviceMonitorProbe(dev, 60, 5));
        ProbeRunner.RunToEnd(runner.RunProbe(new SoakProbe(new FakeSoak(), dev, 60, null, null)), clock, 1.0 / 72.0);

        // The device block carries only keys the app itself writes.
        var appKeys = new System.Collections.Generic.HashSet<string> { "unity_version", "platform", "device_model", "operating_system", "processor", "memory_mb", "graphics_device", "xr_loader" };
        foreach (var k in report.Device.Keys) Assert.IsTrue(appKeys.Contains(k) || DeviceFacts.Accept(k), "the sample device block has a key the app does not write: " + k);
        string err;
        Assert.IsTrue(ProbeReportWriter.Write(SampleDir, report, out err), err);
        var text = File.ReadAllText(Path.Combine(SampleDir, ProbeReportWriter.TextName));
        var json = File.ReadAllText(Path.Combine(SampleDir, ProbeReportWriter.JsonName));
        StringAssert.Contains("SAMPLE FROM SIMULATED DATA, NOT A REAL DEVICE", text);
        StringAssert.Contains("SAMPLE FROM SIMULATED DATA, NOT A REAL DEVICE", json);
        Assert.IsTrue(report.Rows.Count >= 12, "rows: " + report.Rows.Count);
        Assert.AreEqual(0, report.Rows.Count(r => r.Status == ProbeStatus.Error));
    }
}
