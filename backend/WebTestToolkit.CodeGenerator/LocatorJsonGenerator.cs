using System.Text.Encodings.Web;
using System.Text.Json;
using WebTestToolkit.Contracts.Models;

namespace WebTestToolkit.CodeGenerator;

public record GeneratedLocatorFile(string PageName, string Content);

// Produces the same JSON shape as the Phase 1 hand-written LoginPage.locators.json,
// picking each captured element's best-ranked locator candidate.
public static class LocatorJsonGenerator
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // These files are meant to be hand-edited during auto-heal, so keep values like
        // CSS selectors ("button[type='submit']") human-readable instead of '-escaped.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static List<GeneratedLocatorFile> Generate(TestFlow flow, IReadOnlyList<StepPlan> plans)
    {
        var results = new List<GeneratedLocatorFile>();

        foreach (var pageGroup in plans.GroupBy(p => p.PageName))
        {
            var locators = new Dictionary<string, LocatorEntry>();
            foreach (var plan in pageGroup)
            {
                if (plan.Step.Element is null)
                    continue;

                if (locators.ContainsKey(plan.LocatorKey))
                    continue;

                // An element captured without any locator candidates can't be written to the
                // repository. Skip it rather than crashing the whole generation; the missing key
                // surfaces as a clear KeyNotFoundException at test time instead.
                var best = plan.Step.Element.BestLocator;
                if (best is null)
                    continue;

                locators[plan.LocatorKey] = new LocatorEntry(best.Strategy, best.Value);
            }

            var pageLocators = new PageLocators(flow.StartUrl, locators);
            var json = JsonSerializer.Serialize(pageLocators, Options);
            results.Add(new GeneratedLocatorFile(pageGroup.Key, json));
        }

        return results;
    }
}
