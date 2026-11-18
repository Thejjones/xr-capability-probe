package xrprobe.plugin;

import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.graphics.ImageFormat;
import android.hardware.camera2.CameraAccessException;
import android.hardware.camera2.CameraCaptureSession;
import android.hardware.camera2.CameraCharacteristics;
import android.hardware.camera2.CameraDevice;
import android.hardware.camera2.CameraManager;
import android.hardware.camera2.CaptureRequest;
import android.hardware.camera2.params.StreamConfigurationMap;
import android.media.AudioDeviceInfo;
import android.media.AudioFormat;
import android.media.AudioManager;
import android.media.AudioRecord;
import android.media.Image;
import android.media.ImageReader;
import android.media.MediaRecorder;
import android.os.BatteryManager;
import android.os.Build;
import android.os.Handler;
import android.os.HandlerThread;
import android.os.PowerManager;
import android.util.Range;
import android.util.Size;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.Comparator;
import java.util.List;

// Read-only probes of the device for the Aura Probe app: the Camera2 list and a frame count, the audio inputs and a short
// read, thermal and battery state. Everything is returned as JSON text. Camera frames and microphone samples are counted
// and measured and dropped at once: nothing is stored, kept or sent anywhere. Every method catches its own failures and
// reports them in an "error" field, so a missing feature never crashes the app.
public final class ProbeNative {
    private ProbeNative() {}

    // The app's current activity (or its application as a fallback), found without Unity types so this file stays free of them.
    private static Context ctx() {
        try {
            Class<?> c = Class.forName("com.unity3d.player.UnityPlayer");
            Object a = c.getField("currentActivity").get(null);
            if (a instanceof Context) return (Context) a;
        } catch (Throwable ignored) {}
        try {
            Class<?> c = Class.forName("android.app.ActivityThread");
            return (Context) c.getMethod("currentApplication").invoke(null);
        } catch (Throwable ignored) {}
        return null;
    }

    // ---------------------------------------------------------------- the launch intent

    public static String intentExtra(String key) {
        try {
            Context c = ctx();
            if (!(c instanceof Activity)) return "";
            Intent i = ((Activity) c).getIntent();
            if (i == null) return "";
            String v = i.getStringExtra(key);
            return v == null ? "" : v;
        } catch (Throwable t) {
            return "";
        }
    }

    // ---------------------------------------------------------------- who the device says it is

    public static String identityJson() {
        JSONObject o = new JSONObject();
        try {
            o.put("manufacturer", Build.MANUFACTURER);
            o.put("model", Build.MODEL);
            o.put("device", Build.DEVICE);
            o.put("product", Build.PRODUCT);
            o.put("android_release", Build.VERSION.RELEASE);
            o.put("sdk_int", Build.VERSION.SDK_INT);
            o.put("security_patch", Build.VERSION.SECURITY_PATCH);
            o.put("abis", new JSONArray(Arrays.asList(Build.SUPPORTED_ABIS)));
        } catch (Throwable t) {
            try { o.put("error", String.valueOf(t)); } catch (JSONException ignored) {}
        }
        return o.toString();
    }

    // ---------------------------------------------------------------- Camera2

    public static String cameraListJson() {
        Context ctx = ctx();
        JSONObject root = new JSONObject();
        JSONArray cams = new JSONArray();
        try {
            CameraManager cm = (CameraManager) ctx.getSystemService(Context.CAMERA_SERVICE);
            if (cm == null) { root.put("error", "no camera service"); root.put("cameras", cams); return root.toString(); }
            String[] ids = cm.getCameraIdList();
            for (String id : ids) {
                JSONObject c = new JSONObject();
                c.put("id", id);
                try {
                    CameraCharacteristics ch = cm.getCameraCharacteristics(id);
                    Integer facing = ch.get(CameraCharacteristics.LENS_FACING);
                    c.put("facing", facing == null ? "unknown" : (facing == CameraCharacteristics.LENS_FACING_BACK ? "back (world-facing)" : facing == CameraCharacteristics.LENS_FACING_FRONT ? "front (user-facing)" : "external"));
                    StreamConfigurationMap map = ch.get(CameraCharacteristics.SCALER_STREAM_CONFIGURATION_MAP);
                    JSONArray sizes = new JSONArray();
                    if (map != null) {
                        Size[] s = map.getOutputSizes(ImageFormat.YUV_420_888);
                        if (s != null) {
                            List<Size> l = new ArrayList<>(Arrays.asList(s));
                            Collections.sort(l, new Comparator<Size>() {
                                @Override public int compare(Size a, Size b) { return Long.compare((long) b.getWidth() * b.getHeight(), (long) a.getWidth() * a.getHeight()); }
                            });
                            for (int i = 0; i < l.size() && i < 8; i++) sizes.put(l.get(i).getWidth() + "x" + l.get(i).getHeight());
                        }
                    }
                    c.put("sizes", sizes);
                    JSONArray fps = new JSONArray();
                    Range<Integer>[] r = ch.get(CameraCharacteristics.CONTROL_AE_AVAILABLE_TARGET_FPS_RANGES);
                    if (r != null) for (Range<Integer> x : r) fps.put(x.getLower() + "-" + x.getUpper());
                    c.put("fps_ranges", fps);
                    JSONArray caps = new JSONArray();
                    int[] cp = ch.get(CameraCharacteristics.REQUEST_AVAILABLE_CAPABILITIES);
                    if (cp != null) for (int x : cp) caps.put(x);
                    c.put("capabilities", caps);
                    JSONArray phys = new JSONArray();
                    if (Build.VERSION.SDK_INT >= 28) for (String p : ch.getPhysicalCameraIds()) phys.put(p);
                    c.put("physical_ids", phys);
                } catch (Throwable t) {
                    c.put("facing", "unreadable");
                    c.put("error", String.valueOf(t));
                }
                cams.put(c);
            }
        } catch (Throwable t) {
            try { root.put("error", String.valueOf(t)); } catch (JSONException ignored) {}
        }
        try { root.put("cameras", cams); } catch (JSONException ignored) {}
        return root.toString();
    }

    private static final Object camLock = new Object();
    private static volatile String camStatus = "{\"done\":true,\"ok\":false,\"frames\":0,\"seconds\":0,\"width\":0,\"height\":0,\"error\":\"not started\"}";
    private static HandlerThread camThread;

    private static void setCamStatus(boolean done, boolean ok, int frames, double seconds, int w, int h, String error) {
        JSONObject o = new JSONObject();
        try {
            o.put("done", done); o.put("ok", ok); o.put("frames", frames); o.put("seconds", seconds);
            o.put("width", w); o.put("height", h); o.put("error", error == null ? "" : error);
        } catch (JSONException ignored) {}
        camStatus = o.toString();
    }

    public static String cameraStatus() { return camStatus; }

    // Opens the camera, lets frames arrive for the given time and counts them. The Image is closed at once: nothing is read out of it.
    public static void cameraCountFrames(final String id, final double seconds) {
        final Context ctx = ctx();
        setCamStatus(false, false, 0, 0, 0, 0, "");
        if (ctx == null) { setCamStatus(true, false, 0, 0, 0, 0, "no application context"); return; }
        synchronized (camLock) {
            if (camThread != null) { camThread.quitSafely(); camThread = null; }
            camThread = new HandlerThread("aura-probe-camera");
            camThread.start();
        }
        final Handler handler = new Handler(camThread.getLooper());
        final int[] frames = {0};
        final long[] firstNs = {0};
        final long[] lastNs = {0};
        final boolean[] finished = {false};
        final CameraDevice[] device = {null};
        final CameraCaptureSession[] session = {null};
        final ImageReader[] reader = {null};
        final int[] size = {0, 0};

        final Runnable finish = new Runnable() {
            @Override public void run() {
                synchronized (finished) {
                    if (finished[0]) return;
                    finished[0] = true;
                }
                try { if (session[0] != null) session[0].close(); } catch (Throwable ignored) {}
                try { if (device[0] != null) device[0].close(); } catch (Throwable ignored) {}
                try { if (reader[0] != null) reader[0].close(); } catch (Throwable ignored) {}
                double secs = lastNs[0] > firstNs[0] ? (lastNs[0] - firstNs[0]) / 1e9 : 0;
                setCamStatus(true, frames[0] > 0, frames[0], secs > 0 ? secs : seconds, size[0], size[1], frames[0] > 0 ? "" : "the camera opened but delivered no frame");
                synchronized (camLock) { if (camThread != null) { camThread.quitSafely(); camThread = null; } }
            }
        };
        final Runnable fail = null;

        try {
            CameraManager cm = (CameraManager) ctx.getSystemService(Context.CAMERA_SERVICE);
            CameraCharacteristics ch = cm.getCameraCharacteristics(id);
            StreamConfigurationMap map = ch.get(CameraCharacteristics.SCALER_STREAM_CONFIGURATION_MAP);
            Size[] sizes = map == null ? null : map.getOutputSizes(ImageFormat.YUV_420_888);
            // The smallest size at least 640x480 keeps the cost low; fall back to whatever is smallest.
            Size pick = null;
            if (sizes != null) for (Size s : sizes) {
                if (s.getWidth() >= 640 && s.getHeight() >= 480 && (pick == null || (long) s.getWidth() * s.getHeight() < (long) pick.getWidth() * pick.getHeight())) pick = s;
            }
            if (pick == null && sizes != null && sizes.length > 0) pick = sizes[sizes.length - 1];
            if (pick == null) { setCamStatus(true, false, 0, 0, 0, 0, "no YUV output size"); return; }
            size[0] = pick.getWidth(); size[1] = pick.getHeight();
            reader[0] = ImageReader.newInstance(pick.getWidth(), pick.getHeight(), ImageFormat.YUV_420_888, 2);
            reader[0].setOnImageAvailableListener(new ImageReader.OnImageAvailableListener() {
                @Override public void onImageAvailable(ImageReader r) {
                    Image img = null;
                    try {
                        img = r.acquireLatestImage();
                        if (img != null) {
                            long now = System.nanoTime();
                            if (frames[0] == 0) firstNs[0] = now;
                            lastNs[0] = now;
                            frames[0]++;
                        }
                    } catch (Throwable ignored) {
                    } finally {
                        if (img != null) img.close();
                    }
                }
            }, handler);
            cm.openCamera(id, new CameraDevice.StateCallback() {
                @Override public void onOpened(CameraDevice cd) {
                    device[0] = cd;
                    try {
                        final CaptureRequest.Builder b = cd.createCaptureRequest(CameraDevice.TEMPLATE_PREVIEW);
                        b.addTarget(reader[0].getSurface());
                        cd.createCaptureSession(Collections.singletonList(reader[0].getSurface()), new CameraCaptureSession.StateCallback() {
                            @Override public void onConfigured(CameraCaptureSession s) {
                                session[0] = s;
                                try { s.setRepeatingRequest(b.build(), null, handler); }
                                catch (Throwable t) { setCamStatus(true, false, 0, 0, size[0], size[1], "capture request failed: " + t); finish.run(); return; }
                                handler.postDelayed(finish, (long) (seconds * 1000));
                            }
                            @Override public void onConfigureFailed(CameraCaptureSession s) {
                                setCamStatus(true, false, 0, 0, size[0], size[1], "the capture session could not be configured");
                                finish.run();
                            }
                        }, handler);
                    } catch (Throwable t) {
                        setCamStatus(true, false, 0, 0, size[0], size[1], "session failed: " + t);
                        finish.run();
                    }
                }
                @Override public void onDisconnected(CameraDevice cd) {
                    device[0] = cd;
                    if (frames[0] == 0) setCamStatus(true, false, 0, 0, size[0], size[1], "the camera was disconnected (in use by the system?)");
                    finish.run();
                }
                @Override public void onError(CameraDevice cd, int error) {
                    device[0] = cd;
                    setCamStatus(true, false, frames[0], 0, size[0], size[1], "camera error code " + error);
                    finish.run();
                }
            }, handler);
        } catch (SecurityException se) {
            setCamStatus(true, false, 0, 0, 0, 0, "permission denied: " + se.getMessage());
        } catch (CameraAccessException ce) {
            setCamStatus(true, false, 0, 0, 0, 0, "camera access: " + ce.getMessage());
        } catch (Throwable t) {
            setCamStatus(true, false, 0, 0, 0, 0, String.valueOf(t));
        }
    }

    // ---------------------------------------------------------------- audio

    private static String typeName(int t) {
        switch (t) {
            case AudioDeviceInfo.TYPE_BUILTIN_MIC: return "builtin mic";
            case AudioDeviceInfo.TYPE_BLUETOOTH_SCO: return "bluetooth sco";
            case AudioDeviceInfo.TYPE_BLUETOOTH_A2DP: return "bluetooth a2dp";
            case AudioDeviceInfo.TYPE_USB_DEVICE: return "usb device";
            case AudioDeviceInfo.TYPE_USB_HEADSET: return "usb headset";
            case AudioDeviceInfo.TYPE_WIRED_HEADSET: return "wired headset";
            case AudioDeviceInfo.TYPE_TELEPHONY: return "telephony";
            case AudioDeviceInfo.TYPE_REMOTE_SUBMIX: return "remote submix";
            default: return "type " + t;
        }
    }

    public static String audioListJson() {
        Context ctx = ctx();
        JSONObject root = new JSONObject();
        JSONArray inputs = new JSONArray();
        try {
            AudioManager am = (AudioManager) ctx.getSystemService(Context.AUDIO_SERVICE);
            if (am == null) { root.put("error", "no audio service"); root.put("inputs", inputs); return root.toString(); }
            for (AudioDeviceInfo d : am.getDevices(AudioManager.GET_DEVICES_INPUTS)) {
                JSONObject o = new JSONObject();
                o.put("id", d.getId());
                o.put("type", typeName(d.getType()));
                // A name only for the built-in microphone. Another input's name can be a person's own (a headset they named): it is never read into the report.
                if (d.getType() == AudioDeviceInfo.TYPE_BUILTIN_MIC) o.put("name", String.valueOf(d.getProductName()));
                o.put("channel_counts", new JSONArray(toList(d.getChannelCounts())));
                o.put("sample_rates", new JSONArray(toList(d.getSampleRates())));
                o.put("encodings", new JSONArray(toList(d.getEncodings())));
                inputs.put(o);
            }
            String rate = am.getProperty(AudioManager.PROPERTY_OUTPUT_SAMPLE_RATE);
            if (rate != null) root.put("native_sample_rate", Integer.parseInt(rate));
        } catch (Throwable t) {
            try { root.put("error", String.valueOf(t)); } catch (JSONException ignored) {}
        }
        try { root.put("inputs", inputs); } catch (JSONException ignored) {}
        return root.toString();
    }

    private static List<Integer> toList(int[] a) {
        List<Integer> l = new ArrayList<>();
        if (a != null) for (int x : a) l.add(x);
        return l;
    }

    private static volatile String audioStatus = "{\"done\":true,\"ok\":false,\"frames\":0,\"seconds\":0,\"sample_rate\":0,\"channels\":0,\"peak\":0,\"error\":\"not started\"}";

    public static String audioStatus() { return audioStatus; }

    private static void setAudioStatus(boolean done, boolean ok, int frames, double seconds, int rate, int channels, double peak, String error) {
        JSONObject o = new JSONObject();
        try {
            o.put("done", done); o.put("ok", ok); o.put("frames", frames); o.put("seconds", seconds); o.put("sample_rate", rate);
            o.put("channels", channels); o.put("peak", peak); o.put("error", error == null ? "" : error);
        } catch (JSONException ignored) {}
        audioStatus = o.toString();
    }

    // Reads the default input for the given time, widest format first (48 kHz with the asked channels, then 16 kHz mono).
    // Only the format and the peak level are kept; the samples are dropped as they are read.
    public static void audioRead(final double seconds, final int wantChannels) {
        setAudioStatus(false, false, 0, 0, 0, 0, 0, "");
        new Thread(new Runnable() {
            @Override public void run() {
                int[][] tries = { {48000, wantChannels >= 2 ? AudioFormat.CHANNEL_IN_STEREO : AudioFormat.CHANNEL_IN_MONO, wantChannels >= 2 ? 2 : 1}, {16000, AudioFormat.CHANNEL_IN_MONO, 1} };
                String lastError = "";
                for (int[] t : tries) {
                    AudioRecord rec = null;
                    try {
                        int min = AudioRecord.getMinBufferSize(t[0], t[1], AudioFormat.ENCODING_PCM_16BIT);
                        if (min <= 0) { lastError = "format " + t[0] + " Hz x " + t[2] + " is not supported (" + min + ")"; continue; }
                        rec = new AudioRecord(MediaRecorder.AudioSource.MIC, t[0], t[1], AudioFormat.ENCODING_PCM_16BIT, min * 2);
                        if (rec.getState() != AudioRecord.STATE_INITIALIZED) { lastError = "the input would not initialise at " + t[0] + " Hz x " + t[2]; continue; }
                        short[] buf = new short[Math.max(1024, min / 2)];
                        rec.startRecording();
                        long end = System.nanoTime() + (long) (seconds * 1e9);
                        long samples = 0; int peak = 0; int reads = 0;
                        while (System.nanoTime() < end) {
                            int n = rec.read(buf, 0, buf.length);
                            if (n <= 0) { if (n < 0) { lastError = "read error " + n; break; } continue; }
                            reads++; samples += n;
                            for (int i = 0; i < n; i++) { int a = Math.abs((int) buf[i]); if (a > peak) peak = a; }
                        }
                        rec.stop();
                        double secs = samples / (double) (t[0] * t[2]);
                        if (reads > 0) { setAudioStatus(true, true, reads, secs, t[0], t[2], peak / 32768.0, ""); return; }
                        lastError = "no audio was delivered at " + t[0] + " Hz x " + t[2];
                    } catch (SecurityException se) {
                        setAudioStatus(true, false, 0, 0, 0, 0, 0, "permission denied: " + se.getMessage());
                        return;
                    } catch (Throwable th) {
                        lastError = String.valueOf(th);
                    } finally {
                        if (rec != null) { try { rec.release(); } catch (Throwable ignored) {} }
                    }
                }
                setAudioStatus(true, false, 0, 0, 0, 0, 0, lastError);
            }
        }, "aura-probe-audio").start();
    }

    // ---------------------------------------------------------------- thermal and battery

    public static String deviceJson() {
        Context ctx = ctx();
        JSONObject o = new JSONObject();
        try {
            Intent b = ctx.registerReceiver(null, new IntentFilter(Intent.ACTION_BATTERY_CHANGED));
            if (b != null) {
                int level = b.getIntExtra(BatteryManager.EXTRA_LEVEL, -1), scale = b.getIntExtra(BatteryManager.EXTRA_SCALE, -1);
                if (level >= 0 && scale > 0) o.put("battery_percent", 100.0 * level / scale);
                int temp = b.getIntExtra(BatteryManager.EXTRA_TEMPERATURE, Integer.MIN_VALUE);
                if (temp != Integer.MIN_VALUE) o.put("battery_temp_c", temp / 10.0);
                int status = b.getIntExtra(BatteryManager.EXTRA_STATUS, -1);
                o.put("charging", status == BatteryManager.BATTERY_STATUS_CHARGING || status == BatteryManager.BATTERY_STATUS_FULL);
            }
        } catch (Throwable t) {
            try { o.put("battery_error", String.valueOf(t)); } catch (JSONException ignored) {}
        }
        try {
            PowerManager pm = (PowerManager) ctx.getSystemService(Context.POWER_SERVICE);
            if (pm != null) {
                if (Build.VERSION.SDK_INT >= 29) o.put("thermal_status", pm.getCurrentThermalStatus());
                if (Build.VERSION.SDK_INT >= 30) {
                    float h = pm.getThermalHeadroom(10);
                    if (!Float.isNaN(h)) o.put("thermal_headroom", h);
                }
            }
        } catch (Throwable t) {
            try { o.put("thermal_error", String.valueOf(t)); } catch (JSONException ignored) {}
        }
        return o.toString();
    }
}
