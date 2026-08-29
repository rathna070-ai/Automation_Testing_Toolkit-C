using System.Text.Json;
using System.Text.RegularExpressions;
using WebTestToolkit.Inspector.Capture;
using WebTestToolkit.Inspector.Overlay;

namespace WebTestToolkit.Inspector.Tests;

// The overlay is JavaScript in an embedded resource and the session is C#. Nothing checks
// that contract at compile time, so it is checked here.
public class OverlayContractTests
{
    [Test]
    public void OverlayScript_IsActuallyEmbeddedInTheAssembly()
    {
        // If the .csproj loses its EmbeddedResource item, this throws with an explanation.
        // The symptom otherwise is "inspect captures nothing", which is far worse to debug.
        Assert.That(OverlayScript.Value, Is.Not.Empty);
        Assert.That(OverlayScript.Value, Does.Contain("window.__wtt"));
    }

    // PollCore asks the page whether window.__wtt.version equals its own constant and
    // re-injects when it doesn't. A mismatch wouldn't break capture — it would silently
    // re-inject ~10KB of script on every 400ms poll, forever.
    [Test]
    public void OverlayVersion_MatchesTheConstantTheSessionChecks()
    {
        var declared = Regex.Match(OverlayScript.Value, @"var VERSION = (\d+);");

        Assert.That(declared.Success, Is.True, "inspector-overlay.js no longer declares `var VERSION = <n>;`");
        Assert.That(int.Parse(declared.Groups[1].Value), Is.EqualTo(InspectorSession.OverlayVersion));
    }

    [TestCase("drain")]
    [TestCase("enable")]
    [TestCase("disable")]
    [TestCase("status")]
    [TestCase("destroy")]
    public void OverlayScript_ExposesTheFunctionsTheSessionCalls(string function)
    {
        Assert.That(OverlayScript.Value, Does.Match($@"{function}:\s*"),
            $"InspectorSession calls window.__wtt.{function}(), but the overlay no longer exports it.");
    }

    // Capturing a click on a submit button destroys the JS context a moment later. If the
    // queue were an in-memory array, that event would be gone; sessionStorage survives the
    // unload. This is the single most load-bearing line in the overlay.
    [Test]
    public void OverlayScript_QueuesThroughSessionStorageSoClicksSurviveNavigation()
    {
        Assert.That(OverlayScript.Value, Does.Contain("sessionStorage"));
    }

    // Capturing must not change how the page behaves — the user has to be able to log in,
    // submit forms and navigate while we watch.
    [Test]
    public void OverlayScript_NeverCancelsThePagesOwnEvents()
    {
        // Matches a call, not the word — the overlay's own comments explain why it never
        // does this, and a bare Does.Not.Contain would fail on the explanation.
        Assert.That(OverlayScript.Value, Does.Not.Match(@"\.\s*preventDefault\s*\("));
        Assert.That(OverlayScript.Value, Does.Not.Match(@"\.\s*stopPropagation\s*\("));
    }

    // Exactly the shape drain() emits. If the overlay's `describe()` changes field names,
    // this breaks instead of quietly deserializing everything to null.
    [Test]
    public void RawCapture_DeserializesTheOverlayPayload()
    {
        const string payload = """
        [{
          "kind": "click",
          "tagName": "button",
          "id": "login",
          "name": null,
          "type": "submit",
          "placeholder": null,
          "ariaLabel": null,
          "labelText": null,
          "cssClasses": "radius",
          "text": "Login",
          "value": null,
          "html": "<button type=\"submit\" id=\"login\">Login</button>",
          "ancestors": "form#login \"Login Page\"",
          "url": "https://the-internet.herokuapp.com/login",
          "at": 1756339200000,
          "candidates": [
            { "strategy": "id", "value": "login", "kind": "id" },
            { "strategy": "css", "value": "form > button", "kind": "cssPath" }
          ],
          "checked": null,
          "required": false,
          "maxLength": null,
          "options": null
        }]
        """;

        var parsed = JsonSerializer.Deserialize<List<RawCapture>>(
            payload, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        Assert.That(parsed, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(parsed[0].Kind, Is.EqualTo("click"));
            Assert.That(parsed[0].TagName, Is.EqualTo("button"));
            Assert.That(parsed[0].Type, Is.EqualTo("submit"));
            Assert.That(parsed[0].Text, Is.EqualTo("Login"));
            Assert.That(parsed[0].Candidates, Has.Count.EqualTo(2));
            Assert.That(parsed[0].Candidates[0].Kind, Is.EqualTo("id"));
            Assert.That(parsed[0].Required, Is.False);
            Assert.That(parsed[0].Checked, Is.Null);
            Assert.That(parsed[0].Options, Is.Null);
        });

        var element = LocatorRanker.ToCapturedElement(parsed[0]);
        Assert.That(element.BestLocator!.Value, Is.EqualTo("login"));
    }
}
