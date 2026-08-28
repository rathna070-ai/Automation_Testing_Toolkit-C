using Microsoft.Extensions.Logging.Abstractions;
using WebTestToolkit.Contracts.Models;
using WebTestToolkit.Llm;
using WebTestToolkit.Llm.Skills;
using WebTestToolkit.Llm.Transport;

namespace WebTestToolkit.Export.Tests;

public class TestCaseSuiteBuilderTests
{
    private static TestFlow SampleFlow() => new()
    {
        Name = "Login",
        StartUrl = "https://the-internet.herokuapp.com/login",
        Steps =
        [
            new TestStep { Order = 1, ActionType = ActionType.Navigate, Label = "I open the login page", PageName = "LoginPage" },
            new TestStep
            {
                Order = 2, ActionType = ActionType.Type, Label = "I enter the password", PageName = "LoginPage",
                LocatorKey = "PasswordInput", InputValue = "SuperSecretPassword!"
            },
            new TestStep
            {
                Order = 3, ActionType = ActionType.Click, Label = "I click the login button", PageName = "LoginPage",
                LocatorKey = "LoginButton"
            },
            new TestStep
            {
                Order = 4, ActionType = ActionType.AssertText, Label = "I should see the flash message", PageName = "LoginPage",
                LocatorKey = "FlashMessage", ExpectedText = "You logged into a secure area!"
            }
        ]
    };

    private static TestCaseProseSkill BuildSkill(IChatClient chatClient) =>
        new(chatClient, new PromptLibrary(), NullLogger<TestCaseProseSkill>.Instance);

    [Test]
    public async Task BuildAsync_NoLlm_ProducesOneDeterministicTestCasePerStep()
    {
        var skill = BuildSkill(new FakeChatClient(ChatResult.Unavailable("unused")));

        var suite = await TestCaseSuiteBuilder.BuildAsync(SampleFlow(), skill, useLlm: false, CancellationToken.None);

        Assert.That(suite.FlowName, Is.EqualTo("Login"));
        Assert.That(suite.TestCases, Has.Count.EqualTo(1));

        var testCase = suite.TestCases[0];
        Assert.That(testCase.Steps, Has.Count.EqualTo(4));
        Assert.That(testCase.Source, Is.EqualTo(TestCaseSource.Recorded));
        Assert.That(testCase.LastRunStatus, Is.Null, "nothing has ever run this flow — must not fabricate a status");
    }

    // "I enter the password" -> "Enter the password." — the label read as an instruction,
    // not a first-person narration.
    [Test]
    public async Task BuildDeterministic_StripsLeadingIAndCapitalizes()
    {
        var skill = BuildSkill(new FakeChatClient(ChatResult.Unavailable("unused")));
        var suite = await TestCaseSuiteBuilder.BuildAsync(SampleFlow(), skill, useLlm: false, CancellationToken.None);

        var typeStep = suite.TestCases[0].Steps[1];
        Assert.That(typeStep.Action, Is.EqualTo("Enter the password."));
    }

    // TestData is mechanical, sourced straight from the flow, regardless of LLM involvement —
    // this is what makes it safe that the prose skill is never shown the real value.
    [Test]
    public async Task BuildDeterministic_TestDataIsTheRecordedInputValue()
    {
        var skill = BuildSkill(new FakeChatClient(ChatResult.Unavailable("unused")));
        var suite = await TestCaseSuiteBuilder.BuildAsync(SampleFlow(), skill, useLlm: false, CancellationToken.None);

        var typeStep = suite.TestCases[0].Steps[1];
        Assert.That(typeStep.TestData, Is.EqualTo("SuperSecretPassword!"));
    }

    // An assertion step's own ExpectedText is ground truth from the recording — the
    // deterministic writer must use it verbatim, never invent a different outcome.
    [Test]
    public async Task BuildDeterministic_UsesExpectedTextVerbatimForAssertions()
    {
        var skill = BuildSkill(new FakeChatClient(ChatResult.Unavailable("unused")));
        var suite = await TestCaseSuiteBuilder.BuildAsync(SampleFlow(), skill, useLlm: false, CancellationToken.None);

        var assertStep = suite.TestCases[0].Steps[3];
        Assert.That(assertStep.ExpectedResult, Is.EqualTo("You logged into a secure area!"));
    }

    [Test]
    public async Task BuildAsync_LlmUnavailable_FallsBackToDeterministicSilently()
    {
        var skill = BuildSkill(new FakeChatClient(ChatResult.Unavailable("No Groq API key is configured.")));

        var suite = await TestCaseSuiteBuilder.BuildAsync(SampleFlow(), skill, useLlm: true, CancellationToken.None);

        // Same output as the useLlm:false path — a missing key must never produce an error
        // or an empty suite, only the deterministic rendering.
        Assert.That(suite.TestCases[0].Title, Is.EqualTo("Login flow"));
        Assert.That(suite.TestCases[0].Steps, Has.Count.EqualTo(4));
    }

    [Test]
    public async Task BuildAsync_LlmSuccess_UsesProseButKeepsMechanicalTestData()
    {
        var json = """
            {
              "title": "Successful login with valid credentials",
              "precondition": "User is on the login page.",
              "steps": [
                { "number": 1, "action": "Navigate to the login page.", "expectedResult": "The login page loads." },
                { "number": 2, "action": "Enter a valid password.", "expectedResult": "The Password field contains the entered value." },
                { "number": 3, "action": "Click the Login button.", "expectedResult": "The form submits." },
                { "number": 4, "action": "Observe the flash message.", "expectedResult": "You logged into a secure area!" }
              ]
            }
            """;
        var skill = BuildSkill(new FakeChatClient(ChatResult.Success(json, "openai/gpt-oss-120b", 300, 100)));

        var suite = await TestCaseSuiteBuilder.BuildAsync(SampleFlow(), skill, useLlm: true, CancellationToken.None);

        var testCase = suite.TestCases[0];
        Assert.That(testCase.Title, Is.EqualTo("Successful login with valid credentials"));
        Assert.That(testCase.Steps[1].Action, Is.EqualTo("Enter a valid password."));

        // The model wrote the wording; it never received this value and could not have
        // produced it — proves TestCaseSuiteBuilder, not the model, is the source of TestData.
        Assert.That(testCase.Steps[1].TestData, Is.EqualTo("SuperSecretPassword!"));
    }

    [Test]
    public async Task BuildAsync_LlmReturnsUnparsableJson_FallsBackToDeterministic()
    {
        var skill = BuildSkill(new FakeChatClient(ChatResult.Success("not json", "openai/gpt-oss-120b", 10, 5)));

        var suite = await TestCaseSuiteBuilder.BuildAsync(SampleFlow(), skill, useLlm: true, CancellationToken.None);

        Assert.That(suite.TestCases[0].Title, Is.EqualTo("Login flow"));
    }
}
