using Microsoft.Extensions.Logging.Abstractions;
using WebTestToolkit.Llm.Skills;
using WebTestToolkit.Llm.Tests.TestHelpers;
using WebTestToolkit.Llm.Transport;

namespace WebTestToolkit.Llm.Tests.Skills;

public class EdgeCaseGenerationSkillTests
{
    private static EdgeCaseGenerationInput SampleInput() => new(
        "Login",
        "https://the-internet.herokuapp.com/login",
        [
            new EdgeCaseStepSummary(1, "Navigate", "I browse to the login page", "LoginPage", false, false),
            new EdgeCaseStepSummary(2, "Type", "I enter my username", "LoginPage", true, false),
            new EdgeCaseStepSummary(3, "Type", "I enter my password", "LoginPage", true, false),
            new EdgeCaseStepSummary(4, "Click", "I press the login button", "LoginPage", false, false),
            new EdgeCaseStepSummary(5, "AssertText", "I should see the secure area", "LoginPage", false, true)
        ]);

    private static EdgeCaseGenerationSkill BuildSkill(IChatClient chatClient) =>
        new(chatClient, new PromptLibrary(), NullLogger<EdgeCaseGenerationSkill>.Instance);

    [Test]
    public async Task NoApiKey_PropagatesUnavailable_WithoutThrowing()
    {
        var chatClient = new FakeChatClient(ChatResult.Unavailable("No Groq API key is configured."));
        var skill = BuildSkill(chatClient);

        var result = await skill.RunAsync(SampleInput());

        Assert.That(result.Outcome, Is.EqualTo(SkillOutcome.Unavailable));
    }

    [Test]
    public async Task ValidStructuredResponse_ParsesIntoEdgeCases()
    {
        var json = """
            {
              "edgeCases": [
                {
                  "nameSuffix": "InvalidPassword",
                  "title": "Login fails with an invalid password",
                  "rationale": "Verifies the error path when credentials are wrong.",
                  "overrides": [
                    { "stepOrder": 3, "newInputValue": "wrong-password", "newExpectedText": null },
                    { "stepOrder": 5, "newInputValue": null, "newExpectedText": "Your username is invalid!" }
                  ]
                }
              ]
            }
            """;
        var chatClient = new FakeChatClient(ChatResult.Success(json, "openai/gpt-oss-120b", 400, 200));
        var skill = BuildSkill(chatClient);

        var result = await skill.RunAsync(SampleInput());

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.EdgeCases, Has.Count.EqualTo(1));
        var edgeCase = result.Value.EdgeCases[0];
        Assert.That(edgeCase.NameSuffix, Is.EqualTo("InvalidPassword"));
        Assert.That(edgeCase.Overrides, Has.Count.EqualTo(2));
        Assert.That(edgeCase.Overrides[0].NewInputValue, Is.EqualTo("wrong-password"));
        Assert.That(edgeCase.Overrides[1].NewExpectedText, Is.EqualTo("Your username is invalid!"));
    }

    [Test]
    public async Task EmptyEdgeCasesList_ParsesFine()
    {
        var json = """{ "edgeCases": [] }""";
        var chatClient = new FakeChatClient(ChatResult.Success(json, "openai/gpt-oss-120b", 200, 20));
        var skill = BuildSkill(chatClient);

        var result = await skill.RunAsync(SampleInput());

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.EdgeCases, Is.Empty);
    }

    [Test]
    public async Task MalformedJson_ReturnsSchemaMismatch_NotAnException()
    {
        var chatClient = new FakeChatClient(ChatResult.Success("not json", "openai/gpt-oss-120b", 10, 5));
        var skill = BuildSkill(chatClient);

        var result = await skill.RunAsync(SampleInput());

        Assert.That(result.Outcome, Is.EqualTo(SkillOutcome.SchemaMismatch));
    }

    [Test]
    public async Task BuildsRequest_NeverIncludesRealValues_OnlyStructure()
    {
        var chatClient = new FakeChatClient(ChatResult.Unavailable("no key"));
        var skill = BuildSkill(chatClient);

        await skill.RunAsync(SampleInput());

        var userMessage = chatClient.LastRequest!.Messages.Single(m => m.Role == "user").Content;
        Assert.That(userMessage, Does.Contain("Login"));
        Assert.That(userMessage, Does.Contain("I enter my username"));
        // Structural flags only, never a real captured value (there is none in the input to leak).
        Assert.That(userMessage, Does.Contain("has a value"));
    }
}
