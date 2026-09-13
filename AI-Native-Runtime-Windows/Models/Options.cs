namespace AI_Native_Runtime_Windows.Models
{
    /// <summary>Bound from `appsettings.json`'s `Firebase` section.</summary>
    public sealed class FirebaseOptions
    {
        public const string SectionName = "Firebase";
        public string ApiKey { get; set; } = "";
    }

    /// <summary>Bound from `appsettings.json`'s `Runtime` section. `ApplicationId` must
    /// match one of CORE's `FIRST_PARTY_APPLICATION_IDS` (`api/applications.rs`) -
    /// fixed for this repository, not meant to be changed per-deployment.</summary>
    public sealed class RuntimeOptions
    {
        public const string SectionName = "Runtime";
        public string ApplicationId { get; set; } = "app_desktop_windows";
        public string PipeName { get; set; } = "ainativeruntime-runtime";
    }
}
