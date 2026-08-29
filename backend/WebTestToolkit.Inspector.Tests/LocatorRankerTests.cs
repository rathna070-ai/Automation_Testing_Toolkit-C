using WebTestToolkit.Contracts.Models;
using WebTestToolkit.Inspector.Capture;

namespace WebTestToolkit.Inspector.Tests;

// The ranking policy decides which selector gets written into the locator JSON, which
// decides whether the generated test still passes after the next front-end deploy. It is
// pure, so it is testable without launching a browser — that separation is the reason the
// overlay only *proposes* candidates and never picks one.
public class LocatorRankerTests
{
    private static RawCandidate Candidate(string strategy, string value, string kind) =>
        new() { Strategy = strategy, Value = value, Kind = kind };

    [Test]
    public void Rank_PrefersIdOverEverythingElse()
    {
        var ranked = LocatorRanker.Rank(new[]
        {
            Candidate("xpath", "/html[1]/body[1]/div[2]/input[1]", "absoluteXPath"),
            Candidate("css", "form > input:nth-of-type(1)", "cssPath"),
            Candidate("name", "username", "name"),
            Candidate("id", "username", "id")
        });

        Assert.That(ranked[0].Strategy, Is.EqualTo("id"));
        Assert.That(ranked[^1].Strategy, Is.EqualTo("xpath"));
    }

    [Test]
    public void Rank_OrdersByStabilityNotByInputOrder()
    {
        var ranked = LocatorRanker.Rank(new[]
        {
            Candidate("xpath", "/html[1]/body[1]", "absoluteXPath"),
            Candidate("css", "[data-testid=\"login\"]", "testId"),
            Candidate("css", "button[aria-label=\"Log in\"]", "ariaLabel")
        });

        Assert.That(ranked.Select(c => c.Value), Is.EqualTo(new[]
        {
            "[data-testid=\"login\"]",
            "button[aria-label=\"Log in\"]",
            "/html[1]/body[1]"
        }));
    }

    // A React ":r3:" or an "ember512" id is unique right now and gone after the next build.
    // Treating it as a real id is how you get a suite that passes today and fails next week.
    [Test]
    public void Rank_ScoresGeneratedIdsBelowRealAttributes()
    {
        Assert.That(LocatorRanker.ScoreFor("volatileId"), Is.LessThan(LocatorRanker.ScoreFor("name")));
        Assert.That(LocatorRanker.ScoreFor("volatileId"), Is.LessThan(LocatorRanker.ScoreFor("id")));
        // ...but still better than a structural path, which breaks on any markup refactor.
        Assert.That(LocatorRanker.ScoreFor("volatileId"), Is.GreaterThan(LocatorRanker.ScoreFor("cssPath")));
    }

    // LocatorRepository.ToBy throws at runtime on an unknown strategy, where the compiler
    // cannot see it. Filtering here means one can never reach generated code.
    [Test]
    public void Rank_DropsStrategiesLocatorRepositoryCannotResolve()
    {
        var ranked = LocatorRanker.Rank(new[]
        {
            Candidate("linkText", "Sign in", "text"),
            Candidate("className", "btn-primary", "cssPath"),
            Candidate("tagName", "button", "cssPath"),
            Candidate("id", "login", "id")
        });

        Assert.That(ranked, Has.Count.EqualTo(1));
        Assert.That(ranked[0].Strategy, Is.EqualTo("id"));
    }

    [Test]
    public void Rank_DropsEmptyValues()
    {
        var ranked = LocatorRanker.Rank(new[]
        {
            Candidate("id", "", "id"),
            Candidate("css", "   ", "cssPath"),
            Candidate("name", "email", "name")
        });

        Assert.That(ranked, Has.Count.EqualTo(1));
        Assert.That(ranked[0].Value, Is.EqualTo("email"));
    }

    [Test]
    public void Rank_DeduplicatesIdenticalSelectors()
    {
        var ranked = LocatorRanker.Rank(new[]
        {
            Candidate("css", "#login", "cssPath"),
            Candidate("css", "#login", "cssPath")
        });

        Assert.That(ranked, Has.Count.EqualTo(1));
    }

    [Test]
    public void ToCapturedElement_CarriesDomContextThroughForThePromptLayer()
    {
        var element = LocatorRanker.ToCapturedElement(new RawCapture
        {
            Kind = "click",
            TagName = "button",
            Id = "submit",
            Type = "submit",
            AriaLabel = "Log in",
            LabelText = "Log in",
            Text = "Login",
            CssClasses = "btn btn-primary",
            Html = "<button id=\"submit\">Login</button>",
            Ancestors = "form#login \"Login Page\"",
            Url = "https://example.com/login",
            Candidates = { Candidate("id", "submit", "id") }
        });

        Assert.Multiple(() =>
        {
            Assert.That(element.HasLocator, Is.True);
            Assert.That(element.BestLocator!.Strategy, Is.EqualTo("id"));
            Assert.That(element.VisibleText, Is.EqualTo("Login"));
            Assert.That(element.AssociatedLabelText, Is.EqualTo("Log in"));
            Assert.That(element.AncestorContext, Is.EqualTo("form#login \"Login Page\""));
        });
    }

    // Without this, the model only has the raw outerHTML snippet to infer a <select>'s real
    // options or a checkbox's state from — real element state removes the guessing.
    [Test]
    public void ToCapturedElement_CarriesRealElementStateThrough()
    {
        var element = LocatorRanker.ToCapturedElement(new RawCapture
        {
            Kind = "input",
            TagName = "select",
            Id = "country",
            Checked = null,
            Required = true,
            MaxLength = null,
            Options =
            [
                new RawSelectOption { Value = "us", Text = "United States", Selected = true },
                new RawSelectOption { Value = "ca", Text = "Canada", Selected = false }
            ],
            Candidates = { Candidate("id", "country", "id") }
        });

        Assert.Multiple(() =>
        {
            Assert.That(element.Required, Is.True);
            Assert.That(element.Checked, Is.Null);
            Assert.That(element.Options, Has.Count.EqualTo(2));
            Assert.That(element.Options![0], Is.EqualTo(new SelectOption("us", "United States", true)));
            Assert.That(element.Options![1], Is.EqualTo(new SelectOption("ca", "Canada", false)));
        });
    }

    [Test]
    public void ToCapturedElement_ChecksAndMaxLengthCarryThrough()
    {
        var element = LocatorRanker.ToCapturedElement(new RawCapture
        {
            Kind = "click",
            TagName = "input",
            Type = "checkbox",
            Id = "agree",
            Checked = true,
            Required = false,
            MaxLength = null,
            Candidates = { Candidate("id", "agree", "id") }
        });

        Assert.That(element.Checked, Is.True);
        Assert.That(element.Required, Is.False);
        Assert.That(element.Options, Is.Null);
    }

    [Test]
    public void ToCapturedElement_WithNoElementState_LeavesFieldsNull()
    {
        var element = LocatorRanker.ToCapturedElement(new RawCapture
        {
            Kind = "click",
            TagName = "button",
            Id = "submit",
            Candidates = { Candidate("id", "submit", "id") }
        });

        Assert.That(element.Checked, Is.Null);
        Assert.That(element.Required, Is.Null);
        Assert.That(element.MaxLength, Is.Null);
        Assert.That(element.Options, Is.Null);
    }

    // An element with no usable locator would produce a page object method that can never
    // find anything. InspectorSession drops these rather than emitting a broken step.
    [Test]
    public void ToCapturedElement_ReportsNoLocatorWhenNothingSurvivesRanking()
    {
        var element = LocatorRanker.ToCapturedElement(new RawCapture
        {
            Kind = "click",
            TagName = "div",
            Candidates = { Candidate("linkText", "whatever", "text") }
        });

        Assert.That(element.HasLocator, Is.False);
        Assert.That(element.BestLocator, Is.Null);
    }
}
