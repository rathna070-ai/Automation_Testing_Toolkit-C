using Microsoft.Extensions.Logging.Abstractions;
using WebTestToolkit.Llm.Skills;
using WebTestToolkit.Llm.Tests.TestHelpers;
using WebTestToolkit.Llm.Transport;

namespace WebTestToolkit.Llm.Tests.Skills;

public class RunFailureAnalysisSkillTests
{
    private static RunFailureAnalysisInput SampleRun() => new(
        [
            new RunFailureScenarioInput("Login", "Valid login",
                "NoSuchElementException: Unable to locate element: {\"method\":\"id\",\"selector\":\"username\"}",
                "at LoginPage.FindVisible(String locatorKey)"),
            new RunFailureScenarioInput("Checkout", "Guest checkout",
                "NoSuchElementException: Unable to locate element: {\"method\":\"id\",\"selector\":\"username\"}",
                "at LoginPage.FindVisible(String locatorKey)"),
            new RunFailureScenarioInput("Search", "Search returns results",
                "TimeoutException: Timed out after 10 seconds",
                "at SearchPage.FindVisible(String locatorKey)")
        ],
        [new RunFailureLocatorInput("LoginPage", "UsernameInput", "id", "username")]);

    private static RunFailureAnalysisSkill BuildSkill(IChatClient chatClient) =>
        new(chatClient, new PromptLibrary(), NullLogger<RunFailureAnalysisSkill>.Instance);

    [Test]
    public async Task NoApiKey_PropagatesUnavailable_WithoutThrowing()
    {
        var skill = BuildSkill(new FakeChatClient(
            ChatResult.Unavailable("No Groq API key is configured. Add one on the Settings page.")));

        var result = await skill.RunAsync(SampleRun());

        Assert.That(result.Outcome, Is.EqualTo(SkillOutcome.Unavailable));
        Assert.That(result.Reason, Does.Contain("API key"));
    }

    [Test]
    public async Task ValidStructuredResponse_ParsesIntoGroups()
    {
        var json = """
            {
              "summary": "Three failures, two distinct problems.",
              "groups": [
                {
                  "title": "UsernameInput locator is stale",
                  "category": "brokenLocator",
                  "rootCause": "The username field can no longer be found by its id.",
                  "suggestedFix": "Re-inspect the username field and apply the new locator in Auto-heal.",
                  "scenarioNames": ["Valid login", "Guest checkout"],
                  "suggestedLocator": {
                    "page": "LoginPage",
                    "key": "UsernameInput",
                    "strategy": "css",
                    "value": "[data-test='username']",
                    "why": "The id changed but the data-test attribute is stable."
                  },
                  "isLikelyApplicationBug": false,
                  "confidence": 0.9
                },
                {
                  "title": "Search results never appear",
                  "category": "timing",
                  "rootCause": "The results list did not render within the wait.",
                  "suggestedFix": "Check whether the search endpoint is responding.",
                  "scenarioNames": ["Search returns results"],
                  "suggestedLocator": null,
                  "isLikelyApplicationBug": true,
                  "confidence": 0.6
                }
              ]
            }
            """;

        var result = await BuildSkill(new FakeChatClient(ChatResult.Success(json, "openai/gpt-oss-120b", 400, 200)))
            .RunAsync(SampleRun());

        Assert.That(result.IsSuccess, Is.True, result.Reason);
        var analysis = result.Value!;

        Assert.Multiple(() =>
        {
            // The whole point: three failures collapse to two causes, and the group covering
            // the most scenarios comes first.
            Assert.That(analysis.Groups, Has.Count.EqualTo(2));
            Assert.That(analysis.Groups[0].ScenarioNames, Has.Count.EqualTo(2));
            Assert.That(analysis.Groups[0].SuggestedLocator!.Key, Is.EqualTo("UsernameInput"));
            Assert.That(analysis.Groups[1].SuggestedLocator, Is.Null,
                "A group with no confident locator fix must leave it null rather than invent one.");
            Assert.That(analysis.Summary, Does.Contain("two distinct problems"));
        });
    }

    // The locator entries are the context the per-scenario skill never had — "the element was
    // not found" restates the error, whereas naming what the locator currently points at is a
    // diagnosis. They have to actually reach the prompt.
    [Test]
    public async Task UserMessage_CarriesEveryFailureAndTheKnownLocators()
    {
        var chatClient = new FakeChatClient(ChatResult.Unavailable("not needed"));
        await BuildSkill(chatClient).RunAsync(SampleRun());

        var sent = string.Concat(chatClient.LastRequest!.Messages.Select(m => m.Content));

        Assert.Multiple(() =>
        {
            Assert.That(sent, Does.Contain("Valid login"));
            Assert.That(sent, Does.Contain("Guest checkout"));
            Assert.That(sent, Does.Contain("Search returns results"));
            Assert.That(sent, Does.Contain("LoginPage.UsernameInput = id:username"));
        });
    }
}
