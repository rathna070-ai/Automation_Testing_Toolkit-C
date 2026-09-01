using WebTestToolkit.Contracts.Models;
using WebTestToolkit.Inspector.Capture;

namespace WebTestToolkit.Inspector.Tests;

// This is the no-API-key path: everything the user sees in the step list before an LLM is
// involved. The plan requires the toolkit to be fully usable with no key configured, so
// these names and sentences have to stand on their own.
public class StepLabelerTests
{
    private static CapturedElement Element(
        string tag,
        string? type = null,
        string? name = null,
        string? label = null,
        string? text = null,
        string? id = null,
        string? placeholder = null,
        string? ariaLabel = null) => new()
    {
        TagName = tag,
        Type = type,
        Name = name,
        AssociatedLabelText = label,
        VisibleText = text,
        Id = id,
        Placeholder = placeholder,
        AriaLabel = ariaLabel,
        Candidates = { new LocatorCandidate("id", id ?? "x", 100) }
    };

    [TestCase("https://the-internet.herokuapp.com/login", "LoginPage")]
    [TestCase("https://the-internet.herokuapp.com/secure", "SecurePage")]
    [TestCase("https://example.com/", "HomePage")]
    [TestCase("https://example.com", "HomePage")]
    [TestCase("https://example.com/checkout/payment", "CheckoutPaymentPage")]
    [TestCase("https://example.com/account/settings.html", "AccountSettingsPage")]
    [TestCase("https://example.com/login?next=/home", "LoginPage")]
    public void PageNameFromUrl_ReadsThePageOutOfTheUrl(string url, string expected)
    {
        Assert.That(StepLabeler.PageNameFromUrl(url), Is.EqualTo(expected));
    }

    // /orders/48213/edit is the edit page for one order, not a page called "48213".
    [Test]
    public void PageNameFromUrl_SkipsIdentifiersInThePath()
    {
        Assert.That(StepLabeler.PageNameFromUrl("https://example.com/orders/48213/edit"),
            Is.EqualTo("OrdersEditPage"));
        Assert.That(StepLabeler.PageNameFromUrl("https://example.com/users/6f1c9b4e-2a11-4f0c-9d3e-77a1b2c3d4e5"),
            Is.EqualTo("UsersPage"));
    }

    [Test]
    public void PageNameFromUrl_FallsBackWhenTheUrlIsNotAUrl()
    {
        Assert.That(StepLabeler.PageNameFromUrl("not a url"), Is.EqualTo("HomePage"));
    }

    [Test]
    public void LocatorKeyFor_NamesElementsAfterWhatTheUserSees()
    {
        var labeler = new StepLabeler();

        Assert.Multiple(() =>
        {
            Assert.That(labeler.LocatorKeyFor("LoginPage", Element("input", type: "text", name: "username")),
                Is.EqualTo("UsernameInput"));
            Assert.That(labeler.LocatorKeyFor("LoginPage", Element("input", type: "password", name: "password")),
                Is.EqualTo("PasswordInput"));
            Assert.That(labeler.LocatorKeyFor("LoginPage", Element("button", type: "submit", text: "Login")),
                Is.EqualTo("LoginButton"));
            Assert.That(labeler.LocatorKeyFor("LoginPage", Element("a", text: "Forgot password")),
                Is.EqualTo("ForgotPasswordLink"));
            Assert.That(labeler.LocatorKeyFor("LoginPage", Element("select", name: "country")),
                Is.EqualTo("CountryDropdown"));
            Assert.That(labeler.LocatorKeyFor("LoginPage", Element("input", type: "checkbox", label: "Remember me")),
                Is.EqualTo("RememberMeCheckbox"));
        });
    }

    // Two "Submit" buttons on one page is completely ordinary markup, but two properties
    // called SubmitButton on one page object would not compile.
    [Test]
    public void LocatorKeyFor_MakesKeysUniqueWithinAPage()
    {
        var labeler = new StepLabeler();
        var first = labeler.LocatorKeyFor("CartPage", Element("button", type: "submit", text: "Remove"));
        var second = labeler.LocatorKeyFor("CartPage", Element("button", type: "submit", text: "Remove"));
        var third = labeler.LocatorKeyFor("CartPage", Element("button", type: "submit", text: "Remove"));

        Assert.That(new[] { first, second, third }, Is.EqualTo(new[] { "RemoveButton", "RemoveButton2", "RemoveButton3" }));
    }

    // Different page objects, so the same key on each is correct — collisions are per-page.
    [Test]
    public void LocatorKeyFor_AllowsTheSameKeyOnDifferentPages()
    {
        var labeler = new StepLabeler();

        Assert.That(labeler.LocatorKeyFor("LoginPage", Element("button", type: "submit", text: "Continue")),
            Is.EqualTo("ContinueButton"));
        Assert.That(labeler.LocatorKeyFor("CheckoutPage", Element("button", type: "submit", text: "Continue")),
            Is.EqualTo("ContinueButton"));
    }

    [Test]
    public void LocatorKeyFor_DoesNotDoubleTheRoleSuffix()
    {
        var labeler = new StepLabeler();
        Assert.That(labeler.LocatorKeyFor("LoginPage", Element("button", type: "submit", text: "Login Button")),
            Is.EqualTo("LoginButton"));
    }

    [Test]
    public void LocatorKeyFor_TidiesLabelMarkupNoise()
    {
        var labeler = new StepLabeler();
        // "Email address:*" is what a required-field label actually contains.
        Assert.That(labeler.LocatorKeyFor("SignupPage", Element("input", type: "email", label: "Email address:*")),
            Is.EqualTo("EmailAddressInput"));
    }

    [Test]
    public void NavigateLabel_ReadsAsASentence()
    {
        Assert.That(StepLabeler.NavigateLabel("LoginPage"), Is.EqualTo("I open the login page"));
        Assert.That(StepLabeler.NavigateLabel("CheckoutPaymentPage"), Is.EqualTo("I open the checkout payment page"));
    }

    [Test]
    public void ActionLabel_DescribesTheActionInGherkinVoice()
    {
        Assert.Multiple(() =>
        {
            Assert.That(StepLabeler.ActionLabel(ActionType.Type, Element("input", type: "text", name: "username"), "tomsmith"),
                Is.EqualTo("I enter the username"));
            Assert.That(StepLabeler.ActionLabel(ActionType.Click, Element("button", type: "submit", text: "Login"), null),
                Is.EqualTo("I click the login button"));
            Assert.That(StepLabeler.ActionLabel(ActionType.Click, Element("a", text: "Sign up"), null),
                Is.EqualTo("I click the sign up link"));
            Assert.That(StepLabeler.ActionLabel(ActionType.Click, Element("input", type: "checkbox", label: "Remember me"), null),
                Is.EqualTo("I tick the remember me"));
        });
    }

    // The value is already carried on the step as test data; repeating it in the sentence
    // would put a real password into the .feature file for anyone to read.
    [Test]
    public void ActionLabel_NeverEchoesASecret()
    {
        var label = StepLabeler.ActionLabel(
            ActionType.Type,
            Element("input", type: "password", name: "password"),
            "SuperSecretPassword!");

        Assert.That(label, Is.EqualTo("I enter the password"));
        Assert.That(label, Does.Not.Contain("SuperSecret"));
    }

    // --- Label quality (P23) --------------------------------------------------------------
    //
    // Both defects below came out of a real recorded flow (Test445), not from theory.

    // The locator-key path has always deduped the element-kind suffix
    // (LocatorKeyFor's `baseName.EndsWith(suffix)` check); the label path appended it
    // unconditionally, so an element already named "...button" got it twice.
    [Test]
    public void ButtonWhoseNameAlreadyEndsInButton_DoesNotGetItTwice()
    {
        var label = StepLabeler.ActionLabel(
            ActionType.Click, Element("button", id: "login-button", text: "Login"), null);

        Assert.That(label, Is.EqualTo("I click the login button"));
        Assert.That(label, Does.Not.Contain("button button"));
    }

    [Test]
    public void ButtonWhoseNameDoesNotEndInButton_StillGetsTheNoun()
    {
        var label = StepLabeler.ActionLabel(
            ActionType.Click, Element("button", id: "submit", text: "Submit"), null);

        Assert.That(label, Is.EqualTo("I click the submit button"));
    }

    [Test]
    public void LinkWhoseNameAlreadyEndsInLink_DoesNotGetItTwice()
    {
        var label = StepLabeler.ActionLabel(
            ActionType.Click, Element("a", id: "help-link", text: "Help"), null);

        Assert.That(label, Does.Not.Contain("link link"));
    }

    // ToPascalCase keeps only [A-Za-z0-9], which is correct for an identifier and destructive
    // for prose: a "$29.99" price became "2999", giving "I click the 2999". The label is a
    // sentence, so it keeps the captured text; only the locator key needs sanitizing.
    [Test]
    public void PriceLikeText_KeepsItsRawFormInTheLabel()
    {
        var label = StepLabeler.ActionLabel(
            ActionType.Click, Element("span", text: "$29.99"), null);

        Assert.That(label, Is.EqualTo("I click the $29.99"));
    }

    // The sanitized form is still what a locator key uses — the two paths differ on purpose.
    [Test]
    public void PriceLikeText_StillProducesAnIdentifierSafeLocatorKey()
    {
        var key = new StepLabeler().LocatorKeyFor("CartPage", Element("span", text: "$29.99"));

        Assert.That(key, Does.Not.Contain("$"));
        Assert.That(key, Does.Not.Contain("."));
    }

    // Ordinary text is unaffected — the raw-text fallback only applies when PascalCasing has
    // left no letters at all.
    [Test]
    public void OrdinaryText_IsStillHumanizedNotEchoedRaw()
    {
        var label = StepLabeler.ActionLabel(
            ActionType.Click, Element("span", text: "Add To Cart"), null);

        Assert.That(label, Is.EqualTo("I click the add to cart"));
    }
}
