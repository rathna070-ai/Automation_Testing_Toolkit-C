namespace WebTestToolkit.Contracts.Models;

// A candidate way to locate an element, ranked by how stable it's likely to be.
// Strategy values match GeneratedTests/Support/LocatorRepository.cs's ToBy() switch: "id" | "css" | "xpath" | "name".
public record LocatorCandidate(string Strategy, string Value, int Score);

// Mirrors WebTestToolkit.GeneratedTests/Support/LocatorRepository.cs's LocatorEntry/PageLocators exactly.
// Kept as a separate copy (not shared via project reference) so GeneratedTests stays a standalone,
// independently runnable test project with no dependency on the App-side tooling assemblies.
public record LocatorEntry(string Strategy, string Value);

public record PageLocators(string Url, Dictionary<string, LocatorEntry> Locators);
