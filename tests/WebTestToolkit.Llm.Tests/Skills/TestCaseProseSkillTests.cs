using Microsoft.Extensions.Logging.Abstractions;
using WebTestToolkit.Llm.Skills;
using WebTestToolkit.Llm.Tests.TestHelpers;
using WebTestToolkit.Llm.Transport;

namespace WebTestToolkit.Llm.Tests.Skills;

public class TestCaseProseSkillTests
{
    private static TestCaseProseInput SampleInput() => new(
        "Login",
        "https://the-internet.herokuapp.com/login",
        [
            new TestCaseProseStepInput(1, "navigate", "I open the login page", "LoginPage", null),
            new TestCaseProseStepInput(2, "type", "I enter the username", "LoginPage", null),
            new TestCaseProseStepInput(3, "click", "I click the login button", "LoginPage", null),
            new TestCaseProseStepInput(4, "assertText", "I should see the flash message", "LoginPage", "You logged into a secure area!")
        ]);

    private static TestCaseProseSkill BuildSkill(IChatClient chatClient) =>
        new(chatClient, new PromptLibrary(), NullLogger<TestCaseProseSkill>.Instance);

    [Test]
    public async Task NoApiKey_PropagatesUnavailable_WithoutThrowing()
    {
        var chatClient = new FakeChatClient(ChatResult.Unavailable("No Groq API key is configured."));
        var skill = BuildSkill(chatClient);

        var result = await skill.RunAsync(SampleInput());

        Assert.That(result.Outcome, Is.EqualTo(SkillOutcome.Unavailable));
    }

    [Test]
    public async Task ValidStructuredResponse_ParsesIntoOneEntryPerStep()
    {
        var json = """
            {
              "title": "Successful login with valid credentials",
              "precondition": "User is on the login page and not already signed in.",
              "steps": [
                { "number": 1, "action": "Navigate to the login page.", "expectedResult": "The login page loads." },
                { "number": 2, "action": "Enter a valid username in the Username field.", "expectedResult": "The Username field contains the entered value." },
                { "number": 3, "action": "Click the Login button.", "expectedResult": "The form submits." },
                { "number": 4, "action": "Observe the flash message.", "expectedResult": "You logged into a secure area!" }
              ]
            }
            """;
        var chatClient = new FakeChatClient(ChatResult.Success(json, "openai/gpt-oss-120b", 400, 150));
        var skill = BuildSkill(chatClient);

        var result = await skill.RunAsync(SampleInput());

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Title, Is.EqualTo("Successful login with valid credentials"));
        Assert.That(result.Value.Steps, Has.Count.EqualTo(4));
        Assert.That(result.Value.Steps[3].ExpectedResult, Is.EqualTo("You logged into a secure area!"));
    }

    [Test]
    public async Task MalformedJson_ReturnsSchemaMismatch_NotAnException()
    {
        var chatClient = new FakeChatClient(ChatResult.Success("not json", "openai/gpt-oss-120b", 10, 5));
        var skill = BuildSkill(chatClient);

        var result = await skill.RunAsync(SampleInput());

        Assert.That(result.Outcome, Is.EqualTo(SkillOutcome.SchemaMismatch));
    }

    // The prompt tells the model never to invent test data; this proves we never even give
    // it the chance by putting a real value in front of it in the first place — the prompt
    // only carries labels/action types/expected text, never TestStep.InputValue.
    [Test]
    public async Task BuildsRequest_NeverIncludesAnyStepValue()
    {
        var chatClient = new FakeChatClient(ChatResult.Unavailable("no key"));
        var skill = BuildSkill(chatClient);

        var input = SampleInput() with
        {
            Steps =
            [
                new TestCaseProseStepInput(1, "type", "I enter the password", "LoginPage", null)
            ]
        };

        await skill.RunAsync(input);

        var userMessage = chatClient.LastRequest!.Messages.Single(m => m.Role == "user").Content;
        Assert.That(userMessage, Does.Contain("Login"));
        Assert.That(userMessage, Does.Not.Contain("inputValue").IgnoreCase);
    }
}
