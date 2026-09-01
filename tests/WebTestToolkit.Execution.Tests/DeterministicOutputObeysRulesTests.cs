using WebTestToolkit.CodeGenerator;
using WebTestToolkit.Contracts.Models;
using WebTestToolkit.Execution.Generation;
using WebTestToolkit.Llm.Skills;

namespace WebTestToolkit.Execution.Tests;

// The deterministic generator's output is embedded in the generation prompt as a
// known-correct reference implementation, and it is the fallback when the model fails.
// If it violated a rule we state in the prompt, we would be sending contradictory signals
// and could ship a fallback that our own validator would reject. This guards that seam.
public class DeterministicOutputObeysRulesTests
{
    private static TestFlow BuildFlow() => new()
    {
        Name = "RulesProbe",
        StartUrl = "https://example.com/login",
        Steps =
        [
            new TestStep { Order = 1, ActionType = ActionType.Navigate, Label = "I open the rules probe page", PageName = "RulesProbePage" },
            new TestStep
            {
                Order = 2, ActionType = ActionType.Type, Label = "I type the probe name", InputValue = "tomsmith",
                PageName = "RulesProbePage", LocatorKey = "UsernameInput",
                Element = new CapturedElement { Candidates = [new LocatorCandidate("id", "username", 100)] }
            },
            new TestStep
            {
                Order = 3, ActionType = ActionType.Click, Label = "I press the probe button",
                PageName = "RulesProbePage", LocatorKey = "SubmitButton",
                Element = new CapturedElement { Candidates = [new LocatorCandidate("css", "button[type='submit']", 70)] }
            },
            new TestStep
            {
                Order = 4, ActionType = ActionType.AssertText, Label = "I should see the probe confirmation",
                ExpectedText = "Welcome", PageName = "RulesProbePage", LocatorKey = "FlashMessage",
                Element = new CapturedElement { Candidates = [new LocatorCandidate("id", "flash", 100)] }
            }
        ]
    };

    // Mirrors how TestCodeGenerator turns generator output into a candidate set,
    // minus the locator JSON (which the validator inspects via the locators array).
    private static GeneratedFileSet ToFileSet(IReadOnlyDictionary<string, string> files, TestFlow flow)
    {
        var codeFiles = files
            .Where(kv => !kv.Key.EndsWith(".locators.json", StringComparison.OrdinalIgnoreCase))
            .Select(kv => new GeneratedFileDto(kv.Key, kv.Value))
            .ToList();

        var locators = flow.Steps
            .Where(s => s.Element?.BestLocator is not null && !string.IsNullOrEmpty(s.LocatorKey))
            .Select(s => new GeneratedLocatorDto(
                s.PageName, s.LocatorKey, s.Element!.BestLocator!.Strategy, s.Element.BestLocator.Value, flow.StartUrl))
            .ToList();

        return new GeneratedFileSet(codeFiles, locators, "deterministic");
    }

    [Test]
    public void DeterministicOutput_PassesEveryStaticRule()
    {
        var flow = BuildFlow();
        var files = TestFlowCodeGenerator.Generate(flow);

        var issues = StaticValidator.Validate(ToFileSet(files, flow), existingBindings: []);

        Assert.That(issues, Is.Empty,
            "The reference implementation shown to the model must obey the rules we give it: " +
            string.Join("; ", issues.Select(i => $"{i.Code} {i.File} {i.Message}")));
    }

    [Test]
    public void DeterministicOutput_BindsEveryGherkinStep()
    {
        var flow = BuildFlow();
        var files = TestFlowCodeGenerator.Generate(flow);

        var issues = StaticValidator.Validate(ToFileSet(files, flow), existingBindings: []);

        Assert.That(issues.Any(i => i.Code == "WTT150"), Is.False,
            "Every generated feature step must have a binding whose pattern matches it.");
    }

    [Test]
    public void DeterministicOutput_AssertsInThenSteps()
    {
        var flow = BuildFlow();
        var files = TestFlowCodeGenerator.Generate(flow);

        var issues = StaticValidator.Validate(ToFileSet(files, flow), existingBindings: []);

        Assert.That(issues.Any(i => i.Code == "WTT151"), Is.False,
            "A Then step that verifies nothing passes silently forever.");
    }
}
