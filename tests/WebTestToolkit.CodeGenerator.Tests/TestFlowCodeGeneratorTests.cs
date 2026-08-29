using WebTestToolkit.CodeGenerator;
using WebTestToolkit.Contracts.Models;

namespace WebTestToolkit.CodeGenerator.Tests;

public class TestFlowCodeGeneratorTests
{
    // Mirrors the Phase 1 hand-written LoginPage/LoginSteps/LoginPage.locators.json sample,
    // as if it had been captured through Inspect instead of hand-written.
    private static TestFlow BuildLoginFlow() => new()
    {
        Name = "Login",
        StartUrl = "https://the-internet.herokuapp.com/login",
        Steps =
        [
            new TestStep
            {
                Order = 1,
                ActionType = ActionType.Navigate,
                Label = "I am on the login page",
                PageName = "LoginPage"
            },
            new TestStep
            {
                Order = 2,
                ActionType = ActionType.Type,
                Label = "I enter username",
                InputValue = "tomsmith",
                PageName = "LoginPage",
                LocatorKey = "UsernameInput",
                Element = new CapturedElement
                {
                    TagName = "input",
                    Id = "username",
                    Candidates = [new LocatorCandidate("id", "username", 100)]
                }
            },
            new TestStep
            {
                Order = 3,
                ActionType = ActionType.Type,
                Label = "I enter password",
                InputValue = "SuperSecretPassword!",
                PageName = "LoginPage",
                LocatorKey = "PasswordInput",
                Element = new CapturedElement
                {
                    TagName = "input",
                    Id = "password",
                    Candidates = [new LocatorCandidate("id", "password", 100)]
                }
            },
            new TestStep
            {
                Order = 4,
                ActionType = ActionType.Click,
                Label = "I click the login button",
                PageName = "LoginPage",
                LocatorKey = "LoginButton",
                Element = new CapturedElement
                {
                    TagName = "button",
                    Candidates = [new LocatorCandidate("css", "button[type='submit']", 70)]
                }
            },
            new TestStep
            {
                Order = 5,
                ActionType = ActionType.AssertText,
                Label = "I should see a success message",
                ExpectedText = "You logged into a secure area",
                PageName = "LoginPage",
                LocatorKey = "FlashMessage",
                Element = new CapturedElement
                {
                    TagName = "div",
                    Id = "flash",
                    Candidates = [new LocatorCandidate("id", "flash", 100)]
                }
            }
        ]
    };

    [Test]
    public void Generate_ProducesTheFourExpectedFiles()
    {
        var files = TestFlowCodeGenerator.Generate(BuildLoginFlow());

        Assert.That(files.Keys, Is.EquivalentTo(new[]
        {
            "Features/Login.feature",
            "Steps/LoginSteps.cs",
            "PageObjects/LoginPage.cs",
            "LocatorRepository/LoginPage.locators.json"
        }));
    }

    [Test]
    public void FeatureFile_HasCorrectKeywordsAndParameterizedTypeSteps()
    {
        var files = TestFlowCodeGenerator.Generate(BuildLoginFlow());
        var feature = files["Features/Login.feature"];

        Assert.That(feature, Does.Contain("Feature: Login"));
        Assert.That(feature, Does.Contain("Given I am on the login page"));
        Assert.That(feature, Does.Contain("When I enter username \"tomsmith\""));
        Assert.That(feature, Does.Contain("And I enter password \"SuperSecretPassword!\""));
        Assert.That(feature, Does.Contain("And I click the login button"));
        Assert.That(feature, Does.Contain("Then I should see a success message"));
    }

    [Test]
    public void PageObject_HasNavigateAndOneMethodPerCapturedElement()
    {
        var files = TestFlowCodeGenerator.Generate(BuildLoginFlow());
        var page = files["PageObjects/LoginPage.cs"];

        Assert.That(page, Does.Contain("public class LoginPage"));
        Assert.That(page, Does.Contain("public LoginPage(DriverContext driverContext)"));
        Assert.That(page, Does.Contain("LocatorRepository.Load(\"LoginPage\")"));
        Assert.That(page, Does.Contain("public void NavigateTo()"));
        Assert.That(page, Does.Contain("FindVisible(\"UsernameInput\")"));
        Assert.That(page, Does.Contain("FindVisible(\"PasswordInput\")"));
        Assert.That(page, Does.Contain("FindVisible(\"LoginButton\").Click();"));
        Assert.That(page, Does.Contain("FindVisible(\"FlashMessage\").Text;"));
        Assert.That(page, Does.Contain("private IWebElement FindVisible(string locatorKey)"));
    }

    [Test]
    public void StepsClass_BindsEachStepAndInjectsThePageObject()
    {
        var files = TestFlowCodeGenerator.Generate(BuildLoginFlow());
        var steps = files["Steps/LoginSteps.cs"];

        Assert.That(steps, Does.Contain("[Binding]"));
        Assert.That(steps, Does.Contain("public class LoginSteps"));
        Assert.That(steps, Does.Contain("public LoginSteps(LoginPage loginPage)"));
        Assert.That(steps, Does.Contain("[Given(@\"I\\ am\\ on\\ the\\ login\\ page\")]").Or.Contain("[Given(@\"I am on the login page\")]"));
        Assert.That(steps, Does.Contain("Assert.That(actual, Does.Contain(\"You logged into a secure area\"));"));
    }

    [Test]
    public void LocatorJson_ContainsBestLocatorForEachCapturedElement()
    {
        var files = TestFlowCodeGenerator.Generate(BuildLoginFlow());
        var json = files["LocatorRepository/LoginPage.locators.json"];

        Assert.That(json, Does.Contain("\"url\": \"https://the-internet.herokuapp.com/login\""));
        Assert.That(json, Does.Contain("\"UsernameInput\""));
        Assert.That(json, Does.Contain("\"strategy\": \"id\""));
        Assert.That(json, Does.Contain("\"value\": \"username\""));
        Assert.That(json, Does.Contain("\"value\": \"button[type='submit']\""));
    }

    // Regression: flow.Name is free text a user types into the "Flow name" box on the
    // Inspect page — nothing stops them typing "flow new 1". That used to land verbatim in
    // the class name and file path ("public class flow new 1Steps"), producing CS1514/
    // CS1513/CS0116 instead of a compiling file.
    [Test]
    public void FlowNameWithSpaces_ProducesAValidClassNameAndFilePath()
    {
        var flow = BuildLoginFlow();
        flow.Name = "flow new 1";
        var files = TestFlowCodeGenerator.Generate(flow);

        Assert.That(files.Keys, Does.Contain("Features/FlowNew1.feature"));
        Assert.That(files.Keys, Does.Contain("Steps/FlowNew1Steps.cs"));

        var steps = files["Steps/FlowNew1Steps.cs"];
        Assert.That(steps, Does.Contain("public class FlowNew1Steps"));
        Assert.That(steps, Does.Contain("public FlowNew1Steps(LoginPage loginPage)"));
        Assert.That(steps, Does.Not.Contain("flow new 1"));

        // The Gherkin title is prose, not an identifier — the raw name is fine, and reads
        // better, there.
        var feature = files["Features/FlowNew1.feature"];
        Assert.That(feature, Does.Contain("Feature: flow new 1"));
    }
}
