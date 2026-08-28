using System.Text.Json;
using OpenQA.Selenium;

namespace WebTestToolkit.GeneratedTests.Support;

public record LocatorEntry(string Strategy, string Value);

public record PageLocators(string Url, Dictionary<string, LocatorEntry> Locators);

// Reads locator JSON files under LocatorRepository/*.locators.json so that
// re-inspecting a changed element (auto-heal) is a JSON edit, never a code edit.
public static class LocatorRepository
{
    private static readonly Dictionary<string, PageLocators> Cache = new();

    public static PageLocators Load(string pageName)
    {
        if (Cache.TryGetValue(pageName, out var cached))
            return cached;

        var path = Path.Combine(AppContext.BaseDirectory, "LocatorRepository", $"{pageName}.locators.json");
        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var parsed = JsonSerializer.Deserialize<PageLocators>(json, options)
            ?? throw new InvalidOperationException($"Could not parse locator file for '{pageName}'.");

        Cache[pageName] = parsed;
        return parsed;
    }

    public static By ToBy(LocatorEntry entry) => entry.Strategy switch
    {
        "id" => By.Id(entry.Value),
        "css" => By.CssSelector(entry.Value),
        "xpath" => By.XPath(entry.Value),
        "name" => By.Name(entry.Value),
        _ => throw new NotSupportedException($"Unknown locator strategy '{entry.Strategy}'.")
    };
}
