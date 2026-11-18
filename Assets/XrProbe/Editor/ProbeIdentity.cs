namespace XrProbe
{
    // The names the app and the build go by, in ONE place. Change them here and nowhere else (the Java package name in
    // Plugins/Android/ProbeNative.java is internal and does not have to match, and implies no domain).
    public static class ProbeIdentity
    {
        public const string ProductName = "XR Glasses Capability Probe (for XREAL AURA)";
        public const string CompanyName = "xr-probe contributors";

        // The account the project will live under. The account is named (2026-11-18). "OWNER" is the placeholder value a fork must replace; a build with the placeholder is refused unless
        // it is asked for as a test build, because an application id is permanent once an app is installed from it.
        public const string Owner = "thejjones";
        public const string Placeholder = "OWNER";
        public const string ApplicationId = "io.github." + Owner + ".xrcapabilityprobe";
        public static bool OwnerIsPlaceholder { get { return Owner == Placeholder; } }
    }
}
