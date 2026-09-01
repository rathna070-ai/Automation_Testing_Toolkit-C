using System.Runtime.Versioning;

// WebTestToolkit.Api is [SupportedOSPlatform("windows")] (DPAPI key storage, local Chrome), so
// every call into it from an unmarked assembly raises CA1416. These tests only ever run on
// Windows for the same reasons the API does — saying so removes ~39 warnings that were
// otherwise noise obscuring real ones.
[assembly: SupportedOSPlatform("windows")]
