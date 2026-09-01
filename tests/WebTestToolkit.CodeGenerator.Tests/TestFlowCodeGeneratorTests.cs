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
        // Clicks route through ClickSafely since P24, so an overlay or an open dialog fails
        // with an explanation naming the element rather than a bare Selenium exception type.
        Assert.That(page, Does.Contain("ClickSafely(FindVisible(\"LoginButton\"), \"LoginButton\");"));
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
        // Asserts the value under test and that a failure message accompanies it, without
        // pinning the exact emitted line — the message wording is expected to keep improving.
        Assert.That(steps, Does.Contain("Does.Contain(\"You logged into a secure area\")"));
        Assert.That(steps, Does.Contain("did not contain the expected text"),
            "A bare Assert with no message makes a failure report say nothing about what was expected.");
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

        // The raw name is illegal in an *identifier* but required in the [Scope] string, which
        // has to match the feature file's title verbatim or the bindings resolve nowhere. So
        // this checks the identifiers specifically rather than banning the raw name outright.
        Assert.That(steps, Does.Not.Contain("class flow new 1"));
        Assert.That(steps, Does.Not.Contain("flow new 1Steps"));
        Assert.That(steps, Does.Contain("[Scope(Feature = \"flow new 1\")]"));

        // The Gherkin title is prose, not an identifier — the raw name is fine, and reads
        // better, there. It is also what [Scope] above must agree with.
        var feature = files["Features/FlowNew1.feature"];
        Assert.That(feature, Does.Contain("Feature: flow new 1"));
    }

    // Regression: two different elements can capture with the exact same label text (e.g.
    // two identically-priced cart items, both labeled purely from their "$29.99" price with
    // nothing more specific to name them by). That used to produce two [When(...)] methods
    // in the Steps class with the identical name AND identical parameter list — CS0111 —
    // and, independent of the compile error, two byte-identical Gherkin lines, which
    // Reqnroll treats as an ambiguous binding at runtime.
    [Test]
    public void DuplicateStepLabels_AreDisambiguatedInsteadOfColliding()
    {
        var flow = BuildLoginFlow();
        // Push the assertion to the end and insert both duplicate-label steps as "When"s
        // ahead of it, so this test isn't also incidentally exercising DetermineSection.
        flow.Steps.Single(s => s.ActionType == ActionType.AssertText).Order = 7;
        flow.Steps.Add(new TestStep
        {
            Order = 5,
            ActionType = ActionType.Click,
            Label = "I click the item total",
            PageName = "LoginPage",
            LocatorKey = "ItemTotalA",
            Element = new CapturedElement
            {
                TagName = "span",
                Candidates = [new LocatorCandidate("css", "#item-a", 60)]
            }
        });
        flow.Steps.Add(new TestStep
        {
            Order = 6,
            ActionType = ActionType.Click,
            Label = "I click the item total",
            PageName = "LoginPage",
            LocatorKey = "ItemTotalB",
            Element = new CapturedElement
            {
                TagName = "span",
                Candidates = [new LocatorCandidate("css", "#item-b", 60)]
            }
        });

        var files = TestFlowCodeGenerator.Generate(flow);
        var steps = files["Steps/LoginSteps.cs"];
        var feature = files["Features/Login.feature"];

        // Distinct method names with the identical parameter list — this is exactly what
        // CS0111 fires on if they collide.
        Assert.That(steps, Does.Contain("WhenIClickTheItemTotal("));
        Assert.That(steps, Does.Contain("WhenIClickTheItemTotal2("));

        // The second Gherkin line is disambiguated too — not just the method name — which
        // is what keeps Reqnroll's binding resolution unambiguous at runtime.
        Assert.That(feature, Does.Contain("And I click the item total (2)"));

        // Each still drives its own, distinct locator — disambiguation didn't collapse them
        // into calling the same page-object method.
        var page = files["PageObjects/LoginPage.cs"];
        Assert.That(page, Does.Contain("FindVisible(\"ItemTotalA\")"));
        Assert.That(page, Does.Contain("FindVisible(\"ItemTotalB\")"));
    }

    // A recorded <select> used to arrive as ActionType.Type, which emits Clear()+SendKeys().
    // Clear() on a non-editable element is "invalid element state" per the WebDriver spec, so
    // every flow containing a dropdown generated a suite that threw the moment it ran — in the
    // *deterministic* generator, the path that is supposed to always be safe.
    private static TestFlow BuildDropdownFlow() => new()
    {
        Name = "Signup",
        StartUrl = "https://example.com/signup",
        Steps =
        [
            new TestStep
            {
                Order = 1, ActionType = ActionType.Navigate,
                Label = "I am on the signup page", PageName = "SignupPage"
            },
            new TestStep
            {
                Order = 2,
                ActionType = ActionType.Select,
                Label = "I choose the country dropdown",
                InputValue = "India",
                PageName = "SignupPage",
                LocatorKey = "CountryDropdown",
                Element = new CapturedElement
                {
                    TagName = "select",
                    Id = "country",
                    Candidates = [new LocatorCandidate("id", "country", 100)],
                    Options =
                    [
                        new SelectOption("in", "India", true),
                        new SelectOption("uk", "United Kingdom", false)
                    ]
                }
            }
        ]
    };

    [Test]
    public void SelectStep_UsesSelectElement_NotClearAndSendKeys()
    {
        var files = TestFlowCodeGenerator.Generate(BuildDropdownFlow());
        var page = files["PageObjects/SignupPage.cs"];

        Assert.Multiple(() =>
        {
            Assert.That(page, Does.Contain("new SelectElement(element).SelectByText(value)"));
            Assert.That(page, Does.Contain("public void IChooseTheCountryDropdown(string value)"));

            // The specific crash this fixes: Clear() must not be emitted for a <select>.
            Assert.That(page, Does.Not.Contain("element.Clear()"),
                "Clear() on a <select> throws invalid-element-state at runtime.");
            Assert.That(page, Does.Not.Contain("element.SendKeys(value)"));
        });
    }

    [Test]
    public void SelectStep_IsParameterizedLikeAType()
    {
        var files = TestFlowCodeGenerator.Generate(BuildDropdownFlow());

        Assert.Multiple(() =>
        {
            // The chosen option binds as a capture group, so re-recording with a different
            // option is a new Examples row rather than a whole new step definition.
            Assert.That(files["Features/Signup.feature"],
                Does.Contain("I choose the country dropdown \"India\""));
            Assert.That(files["Steps/SignupSteps.cs"],
                Does.Contain("public void WhenIChooseTheCountryDropdown(string value)"));
            Assert.That(files["Steps/SignupSteps.cs"], Does.Contain("(value);"));
        });
    }

    // --- Popup / overlay diagnostics (P24) -------------------------------------------------

    [Test]
    public void ClicksGoThroughClickSafely_NotRawClick()
    {
        var page = TestFlowCodeGenerator.Generate(BuildLoginFlow())["PageObjects/LoginPage.cs"];

        Assert.Multiple(() =>
        {
            Assert.That(page, Does.Contain("private void ClickSafely(IWebElement element, string locatorKey)"));
            Assert.That(page, Does.Contain("ClickSafely(FindVisible("));

            // A bare .Click() would bypass the diagnostics entirely. The only Click() left
            // should be the one inside ClickSafely itself.
            Assert.That(CountOccurrences(page, "element.Click();"), Is.EqualTo(1));
        });
    }

    // Selenium's own ElementClickInterceptedException names neither the step nor what covered
    // it, so a cookie banner reads as an unrelated mystery. Same for a stray alert(), which
    // surfaces on whatever command runs next.
    [Test]
    public void ClickSafely_ExplainsAnOverlayAndAnOpenDialog()
    {
        var page = TestFlowCodeGenerator.Generate(BuildLoginFlow())["PageObjects/LoginPage.cs"];

        Assert.Multiple(() =>
        {
            Assert.That(page, Does.Contain("catch (ElementClickInterceptedException"));
            Assert.That(page, Does.Contain("is covering it"));

            Assert.That(page, Does.Contain("catch (UnhandledAlertException"));
            Assert.That(page, Does.Contain("ex.AlertText"));

            // Caught, not handled: the overlay is never dismissed and the dialog never
            // answered, because either would let a step report success it did not earn.
            Assert.That(page, Does.Not.Contain("SwitchTo().Alert().Accept()"));
            Assert.That(page, Does.Not.Contain("SwitchTo().Alert().Dismiss()"));
        });
    }

    [Test]
    public void CheckboxClick_AlsoGoesThroughClickSafely()
    {
        var flow = BuildLoginFlow();
        flow.Steps[1].ActionType = ActionType.Click;
        flow.Steps[1].Label = "I tick the remember me";
        flow.Steps[1].Element = new CapturedElement
        {
            TagName = "input",
            Type = "checkbox",
            Id = "remember",
            Checked = true,
            Candidates = [new LocatorCandidate("id", "remember", 100)]
        };

        var page = TestFlowCodeGenerator.Generate(flow)["PageObjects/LoginPage.cs"];

        Assert.That(page, Does.Contain("ClickSafely(element,"),
            "The idempotent checkbox path clicks too, so it needs the same diagnostics.");
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
