namespace WebTestToolkit.Contracts.Models;

// A candidate way to locate an element, ranked by how stable it's likely to be.
// Strategy values match GeneratedTests/Support/LocatorRepository.cs's ToBy() switch: "id" | "css" | "xpath" | "name".
// Kind is the *reason* this candidate was proposed (e.g. "id", "testId", "cssPath") — not a
// resolvable locator strategy itself, just what LocatorRanker.RationaleFor keys off of so
// the Inspect UI can show why a candidate ranks where it does. Defaulted so every existing
// 3-arg call site (tests, hand-authored flows) keeps compiling unchanged.
public record LocatorCandidate(string Strategy, string Value, int Score, string Kind = "");

// Mirrors WebTestToolkit.GeneratedTests/Support/LocatorRepository.cs's LocatorEntry/PageLocators exactly.
// Kept as a separate copy (not shared via project reference) so GeneratedTests stays a standalone,
// independently runnable test project with no dependency on the App-side tooling assemblies.
public record LocatorEntry(string Strategy, string Value);

public record PageLocators(string Url, Dictionary<string, LocatorEntry> Locators);
