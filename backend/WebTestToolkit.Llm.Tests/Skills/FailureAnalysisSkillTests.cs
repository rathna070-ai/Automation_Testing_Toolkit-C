using Microsoft.Extensions.Logging.Abstractions;
using WebTestToolkit.Contracts.Models;
using WebTestToolkit.Llm.Skills;
using WebTestToolkit.Llm.Tests.TestHelpers;
using WebTestToolkit.Llm.Transport;

namespace WebTestToolkit.Llm.Tests.Skills;

public class FailureAnalysisSkillTests
{
    private static ScenarioResult SampleFailure() => new()
    {
        FeatureName = "Login",
        ScenarioName = "Successful login with valid credentials",
        Outcome = ScenarioOutcome.Failed,
        ErrorMessage = "NoSuchElementException: Unable to locate element: {\"method\":\"id\",\"selector\":\"username\"}",
        StackTrace = "at WebTestToolkit.GeneratedTests.PageObjects.LoginPage.FindVisible(String locatorKey)"
    };

    private static FailureAnalysisSkill BuildSkill(IChatClient chatClient) =>
        new(chatClient, new PromptLibrary(), NullLogger<FailureAnalysisSkill>.Instance);

    [Test]
    public async Task NoApiKey_PropagatesUnavailable_WithoutThrowing()
    {
        var chatClient = new FakeChatClient(ChatResult.Unavailable("No Groq API key is configured. Add one on the Settings page."));
        var skill = BuildSkill(chatClient);

        var result = await skill.RunAsync(SampleFailure());

        Assert.That(result.Outcome, Is.EqualTo(SkillOutcome.Unavailable));
        Assert.That(result.Reason, Does.Contain("API key"));
    }

    [Test]
    public async Task ValidStructuredResponse_ParsesIntoFailureAnalysis()
    {
        var json = """
            {
              "category": "brokenLocator",
              "rootCause": "The username field's id changed, so the existing locator can no longer find it.",
              "suggestedFix": "Re-inspect the username field and use Auto-Heal to update its locator.",
              "suggestedLocator": { "page": "LoginPage", "key": "UsernameInput", "strategy": "id", "value": "user-name", "why": "Matches the new id attribute" },
              "isLikelyApplicationBug": false,
              "confidence": 0.86
            }
            """;
        var chatClient = new FakeChatClient(ChatResult.Success(json, "openai/gpt-oss-120b", 500, 120));
        var skill = BuildSkill(chatClient);

        var result = await skill.RunAsync(SampleFailure());

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Category, Is.EqualTo(FailureCategory.BrokenLocator));
        Assert.That(result.Value.IsLikelyApplicationBug, Is.False);
        Assert.That(result.Value.Confidence, Is.EqualTo(0.86));
        Assert.That(result.Value.SuggestedLocator!.Value, Is.EqualTo("user-name"));
        Assert.That(result.Model, Is.EqualTo("openai/gpt-oss-120b"));
        Assert.That(result.PromptTokens, Is.EqualTo(500));
    }

    [Test]
    public async Task ResponseWithNullSuggestedLocator_ParsesFine()
    {
        var json = """
            {
              "category": "timing",
              "rootCause": "The page had not finished loading before the assertion ran.",
              "suggestedFix": "Increase the wait, or check whether the app is slow to respond.",
              "suggestedLocator": null,
              "isLikelyApplicationBug": false,
              "confidence": 0.4
            }
            """;
        var chatClient = new FakeChatClient(ChatResult.Success(json, "openai/gpt-oss-120b", 300, 90));
        var skill = BuildSkill(chatClient);

        var result = await skill.RunAsync(SampleFailure());

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.SuggestedLocator, Is.Null);
        Assert.That(result.Value.Category, Is.EqualTo(FailureCategory.Timing));
    }

    [Test]
    public async Task MalformedJson_ReturnsSchemaMismatch_NotAnException()
    {
        var chatClient = new FakeChatClient(ChatResult.Success("not valid json at all", "openai/gpt-oss-120b", 10, 5));
        var skill = BuildSkill(chatClient);

        var result = await skill.RunAsync(SampleFailure());

        Assert.That(result.Outcome, Is.EqualTo(SkillOutcome.SchemaMismatch));
    }

    [Test]
    public async Task Truncated_PropagatesAsTruncatedOutcome()
    {
        var chatClient = new FakeChatClient(ChatResult.Truncated("Response was cut off."));
        var skill = BuildSkill(chatClient);

        var result = await skill.RunAsync(SampleFailure());

        Assert.That(result.Outcome, Is.EqualTo(SkillOutcome.Truncated));
    }

    [Test]
    public async Task BuildsRequest_WithFeatureScenarioAndErrorInUserMessage()
    {
        var chatClient = new FakeChatClient(ChatResult.Unavailable("no key"));
        var skill = BuildSkill(chatClient);

        await skill.RunAsync(SampleFailure());

        var userMessage = chatClient.LastRequest!.Messages.Single(m => m.Role == "user").Content;
        Assert.That(userMessage, Does.Contain("Login"));
        Assert.That(userMessage, Does.Contain("Successful login with valid credentials"));
        Assert.That(userMessage, Does.Contain("NoSuchElementException"));
    }
}
