using WebTestToolkit.Contracts.Models;
using WebTestToolkit.Execution.Generation;
using WebTestToolkit.Llm.Skills;

namespace WebTestToolkit.Execution.Tests;

public class EdgeCaseFlowBuilderTests
{
    private static TestFlow BuildLoginFlow() => new()
    {
        Name = "Login",
        StartUrl = "https://the-internet.herokuapp.com/login",
        Steps =
        [
            new TestStep { Order = 1, ActionType = ActionType.Navigate, Label = "I am on the login page", PageName = "LoginPage" },
            new TestStep
            {
                Order = 2, ActionType = ActionType.Type, Label = "I enter username", InputValue = "tomsmith",
                PageName = "LoginPage", LocatorKey = "UsernameInput",
                Element = new CapturedElement { TagName = "input", Id = "username", Candidates = [new LocatorCandidate("id", "username", 100)] }
            },
            new TestStep
            {
                Order = 3, ActionType = ActionType.Type, Label = "I enter password", InputValue = "SuperSecretPassword!",
                PageName = "LoginPage", LocatorKey = "PasswordInput",
                Element = new CapturedElement { TagName = "input", Id = "password", Candidates = [new LocatorCandidate("id", "password", 100)] }
            },
            new TestStep { Order = 4, ActionType = ActionType.Click, Label = "I click login", PageName = "LoginPage", LocatorKey = "LoginButton" },
            new TestStep
            {
                Order = 5, ActionType = ActionType.AssertText, Label = "I see the secure area", ExpectedText = "You logged into a secure area!",
                PageName = "LoginPage", LocatorKey = "FlashMessage"
            }
        ]
    };

    [Test]
    public void Build_RenamesFlowAndEveryStepsPageName_WithTheSuffix()
    {
        var original = BuildLoginFlow();
        var suggestion = new EdgeCaseSuggestion("InvalidPassword", "title", "rationale", []);

        var edgeCase = EdgeCaseFlowBuilder.Build(original, suggestion);

        Assert.That(edgeCase.Name, Is.EqualTo("LoginInvalidPassword"));
        // Every step gets its own page namespace, never the original's — reusing "LoginPage"
        // would make a later generation of this edge case overwrite PageObjects/LoginPage.cs.
        Assert.That(edgeCase.Steps, Has.All.Matches<TestStep>(s => s.PageName == "LoginPageInvalidPassword"));
    }

    [Test]
    public void Build_AppliesOverrides_OnlyToMatchingSteps_LeavesOthersUnchanged()
    {
        var original = BuildLoginFlow();
        var suggestion = new EdgeCaseSuggestion("InvalidPassword", "title", "rationale",
        [
            new EdgeCaseStepOverride(3, "wrong-password", null),
            new EdgeCaseStepOverride(5, null, "Your username is invalid!")
        ]);

        var edgeCase = EdgeCaseFlowBuilder.Build(original, suggestion);

        Assert.That(edgeCase.Steps.Single(s => s.Order == 3).InputValue, Is.EqualTo("wrong-password"));
        Assert.That(edgeCase.Steps.Single(s => s.Order == 5).ExpectedText, Is.EqualTo("Your username is invalid!"));
        // Untouched steps keep the original's values exactly.
        Assert.That(edgeCase.Steps.Single(s => s.Order == 2).InputValue, Is.EqualTo("tomsmith"));
    }

    [Test]
    public void Build_ReusesTheOriginalLocatorAndElement_NeverInventsOne()
    {
        var original = BuildLoginFlow();
        var suggestion = new EdgeCaseSuggestion("InvalidPassword", "title", "rationale",
            [new EdgeCaseStepOverride(3, "wrong-password", null)]);

        var edgeCase = EdgeCaseFlowBuilder.Build(original, suggestion);

        var overriddenStep = edgeCase.Steps.Single(s => s.Order == 3);
        Assert.That(overriddenStep.LocatorKey, Is.EqualTo("PasswordInput"));
        Assert.That(overriddenStep.Element!.Candidates[0].Value, Is.EqualTo("password"));
    }

    [Test]
    public void Build_SanitizesASuffixWithSpacesOrPunctuation_IntoAPascalCaseIdentifier()
    {
        var original = BuildLoginFlow();
        var suggestion = new EdgeCaseSuggestion("empty username!!", "title", "rationale", []);

        var edgeCase = EdgeCaseFlowBuilder.Build(original, suggestion);

        Assert.That(edgeCase.Name, Is.EqualTo("LoginEmptyUsername"));
    }

    [Test]
    public void Build_PreservesStepOrderAndCount()
    {
        var original = BuildLoginFlow();
        var suggestion = new EdgeCaseSuggestion("InvalidPassword", "title", "rationale", []);

        var edgeCase = EdgeCaseFlowBuilder.Build(original, suggestion);

        Assert.That(edgeCase.Steps, Has.Count.EqualTo(original.Steps.Count));
        Assert.That(edgeCase.Steps.Select(s => s.Order), Is.EqualTo(original.Steps.Select(s => s.Order)));
    }
}
