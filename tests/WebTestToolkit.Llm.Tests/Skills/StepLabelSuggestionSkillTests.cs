using Microsoft.Extensions.Logging.Abstractions;
using WebTestToolkit.Llm.Skills;
using WebTestToolkit.Llm.Tests.TestHelpers;
using WebTestToolkit.Llm.Transport;

namespace WebTestToolkit.Llm.Tests.Skills;

public class StepLabelSuggestionSkillTests
{
    private static StepLabelSuggestionInput SampleInput() => new(
        ActionType: "type",
        PageName: "LoginPage",
        DeterministicLabel: "I enter the username",
        TagName: "input",
        ElementType: "text",
        VisibleText: null,
        Placeholder: null,
        AriaLabel: null,
        AssociatedLabelText: "Username",
        AncestorContext: "form#login \"Login Page\"");

    private static StepLabelSuggestionSkill BuildSkill(IChatClient chatClient) =>
        new(chatClient, new PromptLibrary(), NullLogger<StepLabelSuggestionSkill>.Instance);

    [Test]
    public async Task NoApiKey_PropagatesUnavailable_WithoutThrowing()
    {
        var chatClient = new FakeChatClient(ChatResult.Unavailable("No Groq API key is configured."));
        var skill = BuildSkill(chatClient);

        var result = await skill.RunAsync(SampleInput());

        Assert.That(result.Outcome, Is.EqualTo(SkillOutcome.Unavailable));
    }

    [Test]
    public async Task ValidStructuredResponse_ParsesIntoLabel()
    {
        var json = """{ "label": "I enter my username" }""";
        var chatClient = new FakeChatClient(ChatResult.Success(json, "openai/gpt-oss-120b", 120, 12));
        var skill = BuildSkill(chatClient);

        var result = await skill.RunAsync(SampleInput());

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Label, Is.EqualTo("I enter my username"));
    }

    [Test]
    public async Task MalformedJson_ReturnsSchemaMismatch_NotAnException()
    {
        var chatClient = new FakeChatClient(ChatResult.Success("not json", "openai/gpt-oss-120b", 10, 5));
        var skill = BuildSkill(chatClient);

        var result = await skill.RunAsync(SampleInput());

        Assert.That(result.Outcome, Is.EqualTo(SkillOutcome.SchemaMismatch));
    }

    // No InputValue field exists on this DTO at all — this test documents that omission
    // rather than merely re-proving it, since a future refactor that widened the input
    // record to carry a raw value would otherwise sail through unnoticed.
    [Test]
    public async Task BuildsRequest_WithElementContext_NeverAValue()
    {
        var chatClient = new FakeChatClient(ChatResult.Unavailable("no key"));
        var skill = BuildSkill(chatClient);

        await skill.RunAsync(SampleInput());

        var userMessage = chatClient.LastRequest!.Messages.Single(m => m.Role == "user").Content;
        Assert.That(userMessage, Does.Contain("LoginPage"));
        Assert.That(userMessage, Does.Contain("Username"));
        Assert.That(userMessage, Does.Contain("I enter the username"));
    }
}
