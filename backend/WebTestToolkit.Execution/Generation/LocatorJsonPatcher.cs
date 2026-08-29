using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using WebTestToolkit.Contracts.Models;

namespace WebTestToolkit.Execution.Generation;

// Auto-heal's whole reason to exist: rewriting one locator entry in an existing
// *.locators.json file without touching a single generated .cs file. Reuses
// LocatorFileBuilder's JSON shape/options exactly, so a healed file stays byte-style-
// identical to one this toolkit generated in the first place — and the atomic
// write-then-move trick GeneratedProjectWriter uses, so an interrupted write can never
// leave a half-written locator file behind.
public static class LocatorJsonPatcher
{
    // Locator strategies LocatorRepository.ToBy() actually knows how to turn into a
    // Selenium By. Rejecting anything else here means it can never throw a NotSupportedException
    // at test-run time from a locator this patcher wrote.
    private static readonly HashSet<string> AllowedStrategies = new(StringComparer.Ordinal) { "id", "css", "xpath", "name" };

    // Page/key names only ever come from StepLabeler's PascalCase output or from a value the
    // caller already confirmed exists in a real locator file — but this is still
    // client-controlled input reaching a file path, so it gets the same defence-in-depth
    // GeneratedProjectWriter applies to generated file paths.
    private static readonly Regex ValidName = new("^[A-Za-z0-9_]+$", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    // Every page with a *.locators.json file, so the UI can offer a picker without the
    // caller needing to already know what exists.
    public static IReadOnlyList<string> ListPages(string? baseDir = null)
    {
        var dir = RepositoryDir(baseDir);
        if (!Directory.Exists(dir))
            return Array.Empty<string>();

        const string suffix = ".locators.json";
        return Directory.GetFiles(dir, "*" + suffix)
            .Select(f => Path.GetFileName(f)[..^suffix.Length])
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }

    public static PageLocators Load(string page, string? baseDir = null)
    {
        var path = FilePath(page, baseDir);
        if (!File.Exists(path))
            throw new FileNotFoundException($"No locator file for page '{page}'.", path);

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<PageLocators>(json, ReadOptions)
            ?? throw new InvalidOperationException($"Could not parse locator file for '{page}'.");
    }

    // Rewrites exactly one key's entry, leaving the page's URL and every other key
    // untouched. The key must already exist — auto-heal re-locates a broken element, it
    // never invents a new one (that's what a fresh Inspect session is for).
    public static PageLocators Patch(string page, string key, LocatorEntry entry, string? baseDir = null)
    {
        if (!ValidName.IsMatch(key))
            throw new ArgumentException($"'{key}' is not a valid locator key.", nameof(key));

        if (!AllowedStrategies.Contains(entry.Strategy))
        {
            throw new ArgumentException(
                $"'{entry.Strategy}' is not a supported locator strategy (expected one of: {string.Join(", ", AllowedStrategies)}).",
                nameof(entry));
        }

        if (string.IsNullOrWhiteSpace(entry.Value))
            throw new ArgumentException("A locator value is required.", nameof(entry));

        var current = Load(page, baseDir);
        if (!current.Locators.ContainsKey(key))
            throw new InvalidOperationException($"Page '{page}' has no locator key '{key}'.");

        var updated = new Dictionary<string, LocatorEntry>(current.Locators, StringComparer.Ordinal)
        {
            [key] = entry
        };
        var patched = current with { Locators = updated };

        var path = FilePath(page, baseDir);
        var json = JsonSerializer.Serialize(patched, WriteOptions);

        // Write to a temp file then move, same as GeneratedProjectWriter — an interrupted
        // write can never leave a half-written locator file in the repo.
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, path, overwrite: true);

        return patched;
    }

    private static string RepositoryDir(string? baseDir) =>
        Path.Combine(baseDir ?? SolutionPaths.GeneratedTestsDirectory(), "LocatorRepository");

    private static string FilePath(string page, string? baseDir)
    {
        if (!ValidName.IsMatch(page))
            throw new ArgumentException($"'{page}' is not a valid page name.", nameof(page));

        return Path.Combine(RepositoryDir(baseDir), $"{page}.locators.json");
    }
}
