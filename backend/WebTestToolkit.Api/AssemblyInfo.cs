using System.Runtime.Versioning;

// This tool drives a local Chrome instance and (via FileSettingsStore) Windows DPAPI —
// it targets Windows only. Declaring it here lets platform-compat warnings surface
// genuine cross-platform mistakes instead of every call into Windows-only code.
[assembly: SupportedOSPlatform("windows")]
