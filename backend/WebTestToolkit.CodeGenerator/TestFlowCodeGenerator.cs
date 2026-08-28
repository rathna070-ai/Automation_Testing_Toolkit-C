using WebTestToolkit.Contracts.Models;

namespace WebTestToolkit.CodeGenerator;

// Facade: turns one captured TestFlow into the full set of files that would live under
// WebTestToolkit.GeneratedTests, keyed by relative path (e.g. "Features/Login.feature").
public static class TestFlowCodeGenerator
{
    public static Dictionary<string, string> Generate(TestFlow flow)
    {
        var plans = GherkinStepPlanner.Plan(flow);

        var files = new Dictionary<string, string>
        {
            [$"Features/{flow.Name}.feature"] = FeatureFileGenerator.Generate(flow, plans),
            [$"Steps/{flow.Name}Steps.cs"] = StepsGenerator.Generate(flow, plans)
        };

        foreach (var page in PageObjectGenerator.Generate(plans))
            files[$"PageObjects/{page.PageName}.cs"] = page.Content;

        foreach (var locatorFile in LocatorJsonGenerator.Generate(flow, plans))
            files[$"LocatorRepository/{locatorFile.PageName}.locators.json"] = locatorFile.Content;

        return files;
    }
}
