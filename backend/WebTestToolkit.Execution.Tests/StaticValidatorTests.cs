using WebTestToolkit.Contracts.Models;
using WebTestToolkit.Execution.Generation;
using WebTestToolkit.Llm.Skills;

namespace WebTestToolkit.Execution.Tests;

public class StaticValidatorTests
{
    private const string ValidPageObject = """
        using OpenQA.Selenium;
        using OpenQA.Selenium.Support.UI;
        using WebTestToolkit.GeneratedTests.Support;

        namespace WebTestToolkit.GeneratedTests.PageObjects;

        public class LoginPage
        {
            private readonly IWebDriver _driver;
            private readonly WebDriverWait _wait;
            private readonly PageLocators _locators;

            public LoginPage(DriverContext driverContext)
            {
                _driver = driverContext.Driver;
                _wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(10));
                _locators = LocatorRepository.Load("LoginPage");
            }

            public void EnterUsername(string value)
            {
                var element = FindVisible("UsernameInput");
                element.Clear();
                element.SendKeys(value);
            }

            private IWebElement FindVisible(string locatorKey)
            {
                var entry = _locators.Locators[locatorKey];
                var by = LocatorRepository.ToBy(entry);
                return _wait.Until(driver => driver.FindElement(by));
            }
        }
        """;

    private const string ValidFeature = """
        Feature: Login
          As a user
          I want to log in
          So that I can reach the secure area

          Scenario: Successful login with valid credentials
            Given I am on the login page
            When I enter the username "tomsmith"
            Then I should see a success message
        """;

    // A complete, valid set: feature + page object + the step bindings that cover it.
    // A feature without matching bindings is genuinely invalid (WTT150), so the baseline
    // has to include the Steps file to represent "nothing wrong here".
    private static GeneratedFileSet FileSet(
        List<GeneratedFileDto>? files = null,
        List<GeneratedLocatorDto>? locators = null) =>
        new(
            files ??
            [
                new GeneratedFileDto("Features/Login.feature", ValidFeature),
                new GeneratedFileDto("PageObjects/LoginPage.cs", ValidPageObject),
                new GeneratedFileDto("Steps/LoginSteps.cs", CoveredSteps)
            ],
            locators ??
            [
                new GeneratedLocatorDto("LoginPage", "UsernameInput", "id", "username", "https://example.com/login")
            ],
            "Generated the login flow.");

    [Test]
    public void ValidFileSet_ProducesNoIssues()
    {
        var issues = StaticValidator.Validate(FileSet(), []);
        Assert.That(issues, Is.Empty, string.Join("; ", issues.Select(i => $"{i.Code}: {i.Message}")));
    }

    [Test]
    public void HardcodedByLocator_IsRejected()
    {
        var withHardcodedLocator = ValidPageObject.Replace(
            """FindVisible("UsernameInput")""",
            """_driver.FindElement(By.Id("username"))""");

        var issues = StaticValidator.Validate(
            FileSet(files:
            [
                new GeneratedFileDto("Features/Login.feature", ValidFeature),
                new GeneratedFileDto("PageObjects/LoginPage.cs", withHardcodedLocator)
            ]), []);

        Assert.That(issues.Any(i => i.Code == "WTT100"), Is.True,
            "A hardcoded By must be rejected — it silently breaks auto-heal for that element.");
    }

    [TestCase("Support/Hooks.cs")]
    [TestCase("../../../evil.cs")]
    [TestCase("WebTestToolkit.GeneratedTests.csproj")]
    [TestCase("LocatorRepository/LoginPage.locators.json")]
    public void DisallowedPath_IsRejected(string path)
    {
        var issues = StaticValidator.Validate(
            FileSet(files: [new GeneratedFileDto(path, "// anything")]), []);

        Assert.That(issues.Any(i => i.Code == "WTT001"), Is.True);
    }

    [Test]
    public void UnknownLocatorStrategy_IsRejected()
    {
        var issues = StaticValidator.Validate(
            FileSet(locators: [new GeneratedLocatorDto("LoginPage", "UsernameInput", "dataTestId", "user", "https://x")]),
            []);

        Assert.That(issues.Any(i => i.Code == "WTT110"), Is.True,
            "ToBy throws at runtime on unknown strategies, so this must be caught statically.");
    }

    [Test]
    public void LocatorKeyUsedButNotReturned_IsRejected()
    {
        var issues = StaticValidator.Validate(
            FileSet(locators: [new GeneratedLocatorDto("LoginPage", "SomeOtherKey", "id", "x", "https://x")]),
            []);

        Assert.That(issues.Any(i => i.Code == "WTT121"), Is.True);
    }

    [Test]
    public void ThreadSleep_IsRejected()
    {
        var withSleep = ValidPageObject.Replace("element.Clear();", "Thread.Sleep(1000); element.Clear();");
        var issues = StaticValidator.Validate(
            FileSet(files:
            [
                new GeneratedFileDto("Features/Login.feature", ValidFeature),
                new GeneratedFileDto("PageObjects/LoginPage.cs", withSleep)
            ]), []);

        Assert.That(issues.Any(i => i.Code == "WTT102"), Is.True);
    }

    [Test]
    public void RedefiningScenarioHooks_IsRejected()
    {
        var steps = """
            using Reqnroll;
            namespace WebTestToolkit.GeneratedTests.Steps;

            [Binding]
            public class LoginSteps
            {
                [AfterScenario]
                public void Cleanup() { }
            }
            """;

        var issues = StaticValidator.Validate(
            FileSet(files: [new GeneratedFileDto("Steps/LoginSteps.cs", steps)]), []);

        Assert.That(issues.Any(i => i.Code == "WTT103"), Is.True);
    }

    [Test]
    public void BindingCollidingWithExistingStep_IsRejected()
    {
        var steps = """
            using Reqnroll;
            namespace WebTestToolkit.GeneratedTests.Steps;

            [Binding]
            public class LoginSteps
            {
                [Given(@"I am on the login page")]
                public void GivenIAmOnTheLoginPage() { }
            }
            """;

        var existing = new List<BindingPattern>
        {
            new("Given", @"I\ am\ on\ the\ login\ page", "Steps/SampleLoginSteps.cs")
        };

        var issues = StaticValidator.Validate(
            FileSet(files: [new GeneratedFileDto("Steps/LoginSteps.cs", steps)]), existing);

        Assert.That(issues.Any(i => i.Code == "WTT130"), Is.True,
            "Ambiguous Reqnroll bindings compile fine and fail at runtime, so they must be caught here.");
    }

    [Test]
    public void FeatureFileWithoutScenario_IsRejected()
    {
        var issues = StaticValidator.Validate(
            FileSet(files: [new GeneratedFileDto("Features/Login.feature", "Feature: Login\n  Just a description.")]),
            []);

        Assert.That(issues.Any(i => i.Code == "WTT141"), Is.True);
    }

    [Test]
    public void MarkdownFenceInContent_IsRejected()
    {
        var issues = StaticValidator.Validate(
            FileSet(files: [new GeneratedFileDto("PageObjects/LoginPage.cs", "```csharp\npublic class X {}\n```")]),
            []);

        Assert.That(issues.Any(i => i.Code == "WTT003"), Is.True);
    }

    // --- Step coverage (WTT150): a feature step with no matching binding compiles fine
    // --- and fails at runtime with "No matching step definition".

    // Four-quote delimiter: the capture group below contains a run of three quotes.
    private const string CoveredSteps = """"
        using Reqnroll;
        namespace WebTestToolkit.GeneratedTests.Steps;

        [Binding]
        public class LoginSteps
        {
            [Given(@"I am on the login page")]
            public void GivenIAmOnTheLoginPage() { }

            [When(@"I enter the username ""(.*)""")]
            public void WhenIEnterTheUsername(string value) { }

            [Then(@"I should see a success message")]
            public void ThenIShouldSeeASuccessMessage()
            {
                Assert.That(true, Is.True);
            }
        }
        """";

    [Test]
    public void EveryFeatureStepHasABinding_ProducesNoIssue()
    {
        var issues = StaticValidator.Validate(
            FileSet(files:
            [
                new GeneratedFileDto("Features/Login.feature", ValidFeature),
                new GeneratedFileDto("Steps/LoginSteps.cs", CoveredSteps)
            ]), []);

        Assert.That(issues.Any(i => i.Code == "WTT150"), Is.False,
            "False positives here would trigger pointless repair loops: " +
            string.Join("; ", issues.Select(i => $"{i.Code} {i.Message}")));
    }

    [Test]
    public void FeatureStepWithNoMatchingBinding_IsRejected()
    {
        var featureWithExtraStep = ValidFeature.Replace(
            "Then I should see a success message",
            "Then I should see a totally unbound outcome");

        var issues = StaticValidator.Validate(
            FileSet(files:
            [
                new GeneratedFileDto("Features/Login.feature", featureWithExtraStep),
                new GeneratedFileDto("Steps/LoginSteps.cs", CoveredSteps)
            ]), []);

        Assert.That(issues.Any(i => i.Code == "WTT150"), Is.True);
    }

    [Test]
    public void StepMatchingAnExistingBindingElsewhere_IsAccepted()
    {
        // Reusing a step defined by another flow is legitimate, not a missing binding.
        var featureOnly = FileSet(files: [new GeneratedFileDto("Features/Login.feature", ValidFeature)]);
        var existing = new List<BindingPattern>
        {
            new("Given", @"I\ am\ on\ the\ login\ page", "Steps/OtherSteps.cs"),
            new("When", @"I\ enter\ the\ username\ ""(.*)""", "Steps/OtherSteps.cs"),
            new("Then", @"I\ should\ see\ a\ success\ message", "Steps/OtherSteps.cs")
        };

        var issues = StaticValidator.Validate(featureOnly, existing);

        Assert.That(issues.Any(i => i.Code == "WTT150"), Is.False,
            string.Join("; ", issues.Select(i => $"{i.Code} {i.Message}")));
    }

    [Test]
    public void ScenarioOutlinePlaceholders_DoNotCauseFalsePositives()
    {
        var outline = """
            Feature: Login

              Scenario Outline: Login with several accounts
                Given I am on the login page
                When I enter the username "<username>"
                Then I should see a success message

              Examples:
                | username |
                | tomsmith |
                | admin    |
            """;

        var issues = StaticValidator.Validate(
            FileSet(files:
            [
                new GeneratedFileDto("Features/Login.feature", outline),
                new GeneratedFileDto("Steps/LoginSteps.cs", CoveredSteps)
            ]), []);

        Assert.That(issues.Any(i => i.Code == "WTT150"), Is.False,
            "Examples rows and <placeholders> must not be mistaken for unbound steps: " +
            string.Join("; ", issues.Select(i => $"{i.Code} {i.Message}")));
    }

    [Test]
    public void AndStepsInheritThePrecedingKeyword()
    {
        var feature = """
            Feature: Login

              Scenario: Login
                Given I am on the login page
                When I enter the username "tomsmith"
                And I enter the username "admin"
                Then I should see a success message
            """;

        var issues = StaticValidator.Validate(
            FileSet(files:
            [
                new GeneratedFileDto("Features/Login.feature", feature),
                new GeneratedFileDto("Steps/LoginSteps.cs", CoveredSteps)
            ]), []);

        Assert.That(issues.Any(i => i.Code == "WTT150"), Is.False,
            "An 'And' after a 'When' binds as a When: " +
            string.Join("; ", issues.Select(i => $"{i.Code} {i.Message}")));
    }

    // --- Then steps must verify (WTT151): an empty Then passes silently forever.

    [TestCase("{ }", true, TestName = "EmptyThenBody_IsRejected")]
    [TestCase("{ // TODO: verify something }", true, TestName = "ThenBodyWithOnlyAComment_IsRejected")]
    [TestCase("{ _page.DoSomething(); }", true, TestName = "ThenBodyWithNoAssertion_IsRejected")]
    [TestCase("{ Assert.That(true, Is.True); }", false, TestName = "ThenBodyWithAssert_IsAccepted")]
    [TestCase("{ if (!ok) throw new Exception(\"nope\"); }", false, TestName = "ThenBodyThatThrows_IsAccepted")]
    public void ThenStepVerificationIsChecked(string body, bool expectIssue)
    {
        var steps = $$"""
            using Reqnroll;
            namespace WebTestToolkit.GeneratedTests.Steps;

            [Binding]
            public class LoginSteps
            {
                [Then(@"I should see a success message")]
                public void ThenIShouldSeeASuccessMessage()
                {{body}}
            }
            """;

        var issues = StaticValidator.Validate(
            FileSet(files: [new GeneratedFileDto("Steps/LoginSteps.cs", steps)]), []);

        Assert.That(issues.Any(i => i.Code == "WTT151"), Is.EqualTo(expectIssue),
            string.Join("; ", issues.Select(i => $"{i.Code} {i.Message}")));
    }

    [Test]
    public void ExpressionBodiedThenWithoutAssertion_IsRejected()
    {
        var steps = """
            using Reqnroll;
            namespace WebTestToolkit.GeneratedTests.Steps;

            [Binding]
            public class LoginSteps
            {
                [Then(@"I should see a success message")]
                public void ThenIShouldSeeASuccessMessage() => _page.GetFlashMessage();
            }
            """;

        var issues = StaticValidator.Validate(
            FileSet(files: [new GeneratedFileDto("Steps/LoginSteps.cs", steps)]), []);

        Assert.That(issues.Any(i => i.Code == "WTT151"), Is.True);
    }

    [Test]
    public void GivenAndWhenSteps_AreNotRequiredToAssert()
    {
        var steps = """"
            using Reqnroll;
            namespace WebTestToolkit.GeneratedTests.Steps;

            [Binding]
            public class LoginSteps
            {
                [Given(@"I am on the login page")]
                public void GivenIAmOnTheLoginPage() { _page.NavigateTo(); }

                [When(@"I enter the username ""(.*)""")]
                public void WhenIEnterTheUsername(string value) { _page.EnterUsername(value); }
            }
            """";

        var issues = StaticValidator.Validate(
            FileSet(files: [new GeneratedFileDto("Steps/LoginSteps.cs", steps)]), []);

        Assert.That(issues.Any(i => i.Code == "WTT151"), Is.False,
            "Only Then steps carry a verification obligation.");
    }
}
