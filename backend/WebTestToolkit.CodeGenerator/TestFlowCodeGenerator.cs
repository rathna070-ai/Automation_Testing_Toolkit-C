using WebTestToolkit.Contracts.Models;

namespace WebTestToolkit.CodeGenerator;

// Facade: turns one captured TestFlow into the full set of files that would live under
// WebTestToolkit.GeneratedTests, keyed by relative path (e.g. "Features/Login.feature").
public static class TestFlowCodeGenerator
{
    public static Dictionary<string, string> Generate(TestFlow flow)
    {
        var plans = GherkinStepPlanner.Plan(flow);

        // Sanitize once for file names too — flow.Name is free text a user typed, and an
        // unsanitized "flow new 1" would also produce a path with a raw space in it.
        var className = Naming.ToPascalCaseIdentifier(flow.Name);

        var files = new Dictionary<string, string>
        {
            [$"Features/{className}.feature"] = FeatureFileGenerator.Generate(flow, plans),
            [$"Steps/{className}Steps.cs"] = StepsGenerator.Generate(flow, plans)
        };

        foreach (var page in PageObjectGenerator.Generate(plans))
            files[$"PageObjects/{page.PageName}.cs"] = page.Content;

        foreach (var locatorFile in LocatorJsonGenerator.Generate(flow, plans))
            files[$"LocatorRepository/{locatorFile.PageName}.locators.json"] = locatorFile.Content;

        return files;
    }
}
