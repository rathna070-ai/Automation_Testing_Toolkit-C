using WebTestToolkit.Contracts.Models;
using WebTestToolkit.Execution.Generation;

namespace WebTestToolkit.Execution.Tests;

// SerializeFlowForPrompt is a pure function over the flow, so these need no sandbox and no
// real project on disk — unlike ReferenceBundleBuilder.Build, which reads the live
// GeneratedTests directory.
public class ReferenceBundleBuilderTests
{
    private static TestFlow FlowWithCapturedDom() => new()
    {
        Name = "BundleProbe",
        StartUrl = "https://example.com/bundle-probe",
        Steps =
        [
            new TestStep
            {
                Order = 1,
                ActionType = ActionType.Type,
                Label = "I type the probe user name",
                InputValue = "tomsmith",
                PageName = "BundleProbePage",
                LocatorKey = "UsernameInput",
                Element = new CapturedElement
                {
                    TagName = "input",
                    Id = "username",
                    VisibleText = "Username",
                    Placeholder = "Enter your username",
                    Required = true,
                    Candidates = [new LocatorCandidate("id", "username", 100)],
                    OuterHtmlSnippet = "<input id=\"username\" class=\"form-control input-lg\" type=\"text\" />",
                    AncestorContext = "<form id=\"login\"><div class=\"row\">…</div></form>"
                }
            }
        ]
    };

    // The raw-DOM fields exist for the Inspect-time label/assertion prompts; by codegen the
    // label is already chosen, so carrying them costs prompt budget for nothing — and on a
    // real captured flow that surplus is what pushed a request into a Groq 413.
    [Test]
    public void SerializeFlowForPrompt_DropsTheRawDomFields()
    {
        var json = ReferenceBundleBuilder.SerializeFlowForPrompt(FlowWithCapturedDom());

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Not.Contain("OuterHtmlSnippet").IgnoreCase);
            Assert.That(json, Does.Not.Contain("AncestorContext").IgnoreCase);
            Assert.That(json, Does.Not.Contain("form-control input-lg"),
                "The snippet's contents must go with the field, not just its name.");
            Assert.That(json, Does.Not.Contain("<form id="));
        });
    }

    // The trim is a subtraction of exactly two fields — everything the model actually needs
    // to name a step, pick a locator and understand the element must survive it.
    [Test]
    public void SerializeFlowForPrompt_KeepsEverythingElse()
    {
        var json = ReferenceBundleBuilder.SerializeFlowForPrompt(FlowWithCapturedDom());

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("BundleProbe"));
            Assert.That(json, Does.Contain("https://example.com/bundle-probe"));
            Assert.That(json, Does.Contain("I type the probe user name"));
            Assert.That(json, Does.Contain("UsernameInput"));
            Assert.That(json, Does.Contain("tomsmith"));
            Assert.That(json, Does.Contain("username"), "The winning locator candidate must survive.");
            Assert.That(json, Does.Contain("Enter your username"));
        });
    }

    [Test]
    public void SerializeFlowForPrompt_IsSubstantiallySmallerThanTheUntrimmedFlow()
    {
        var flow = FlowWithCapturedDom();
        var trimmed = ReferenceBundleBuilder.SerializeFlowForPrompt(flow);

        var untrimmed = System.Text.Json.JsonSerializer.Serialize(flow);

        Assert.That(trimmed.Length, Is.LessThan(untrimmed.Length),
            "Dropping the raw DOM fields must actually reduce the prompt's flow section.");
    }

    // A step with no captured element at all (a Navigate step) must not throw on the way
    // through the pruner.
    [Test]
    public void SerializeFlowForPrompt_HandlesAStepWithNoElement()
    {
        var flow = new TestFlow
        {
            Name = "NavigateOnly",
            StartUrl = "https://example.com",
            Steps = [new TestStep { Order = 1, ActionType = ActionType.Navigate, Label = "I open the page" }]
        };

        Assert.That(
            ReferenceBundleBuilder.SerializeFlowForPrompt(flow),
            Does.Contain("I open the page"));
    }
}
