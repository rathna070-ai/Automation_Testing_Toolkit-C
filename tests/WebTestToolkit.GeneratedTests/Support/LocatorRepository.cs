using System.Collections.Concurrent;
using System.Text.Json;
using OpenQA.Selenium;

namespace WebTestToolkit.GeneratedTests.Support;

public record LocatorEntry(string Strategy, string Value);

// Locators is read-only because the cache below hands the *same* instance to every scenario
// that loads the page. A mutable dictionary there would be shared mutable state across
// parallel scenarios — one scenario could alter a locator another is mid-way through using.
public record PageLocators(string Url, IReadOnlyDictionary<string, LocatorEntry> Locators);

// Reads locator JSON files under LocatorRepository/*.locators.json so that
// re-inspecting a changed element (auto-heal) is a JSON edit, never a code edit.
public static class LocatorRepository
{
    // ConcurrentDictionary, not Dictionary: this cache is static and every scenario's page
    // objects read it, so under the parallel execution enabled in ParallelExecution.cs two
    // scenarios can call Load at the same moment. Concurrent writes to a plain Dictionary can
    // corrupt its internal buckets or spin forever — a failure that would surface as an
    // unrelated-looking hang, not a clean exception.
    private static readonly ConcurrentDictionary<string, PageLocators> Cache = new();

    public static PageLocators Load(string pageName) =>
        // GetOrAdd's factory can run more than once under contention. That is harmless here —
        // it re-reads the same file and produces an equal value, and only one result is ever
        // stored — so it is not worth a lock to prevent.
        Cache.GetOrAdd(pageName, static name =>
        {
            var path = Path.Combine(AppContext.BaseDirectory, "LocatorRepository", $"{name}.locators.json");
            var json = File.ReadAllText(path);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<PageLocators>(json, options)
                ?? throw new InvalidOperationException($"Could not parse locator file for '{name}'.");
        });

    public static By ToBy(LocatorEntry entry) => entry.Strategy switch
    {
        "id" => By.Id(entry.Value),
        "css" => By.CssSelector(entry.Value),
        "xpath" => By.XPath(entry.Value),
        "name" => By.Name(entry.Value),
        _ => throw new NotSupportedException($"Unknown locator strategy '{entry.Strategy}'.")
    };
}
