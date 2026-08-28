using System.Text.Encodings.Web;
using System.Text.Json;
using WebTestToolkit.Contracts.Models;
using WebTestToolkit.Llm.Skills;

namespace WebTestToolkit.Execution.Generation;

// The model returns locators as data; the toolkit serializes the files. That removes an
// entire failure surface for free — the JSON is byte-shape-identical to what
// LocatorRepository expects and to what the deterministic generator produces, so
// auto-heal's future LocatorJsonPatcher only ever has one format to deal with.
public static class LocatorFileBuilder
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Keep values like "button[type='submit']" readable — these files get hand-edited.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static Dictionary<string, string> Build(IEnumerable<GeneratedLocatorDto> locators)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var pageGroup in locators.GroupBy(l => l.Page, StringComparer.Ordinal))
        {
            var url = pageGroup.Select(l => l.Url).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u)) ?? "";

            var entries = new Dictionary<string, LocatorEntry>(StringComparer.Ordinal);
            foreach (var locator in pageGroup)
                entries[locator.Key] = new LocatorEntry(locator.Strategy, locator.Value);

            var pageLocators = new PageLocators(url, entries);
            files[$"LocatorRepository/{pageGroup.Key}.locators.json"] = JsonSerializer.Serialize(pageLocators, Options);
        }

        return files;
    }
}
