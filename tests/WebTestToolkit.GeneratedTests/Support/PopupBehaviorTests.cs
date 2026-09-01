using OpenQA.Selenium;

namespace WebTestToolkit.GeneratedTests.Support;

// Pins real Chrome's behaviour for the two situations P24's generated ClickSafely helper
// translates: a click swallowed by an overlay, and a click attempted while a JS dialog is open.
//
// ClickSafely catches ElementClickInterceptedException and UnhandledAlertException by name. Unit
// tests in WebTestToolkit.CodeGenerator.Tests prove the generator emits those catches and that
// both click paths route through them — but no unit test can show the names are the *right* ones.
// If Chrome or the .NET bindings surfaced either case as some other type, those catch blocks
// would be dead code and the diagnostics would silently never fire. That is what this checks.
//
// It also checks the dialog case does not simply hang, which is the behaviour
// UnhandledPromptBehavior.Ignore buys: without it the driver would answer the dialog itself, and
// a scenario would sail past a confirm() it never really responded to.
//
// [Explicit] and Category=Browser, matching the Inspector's real-browser tests: this needs a
// Chrome on the machine, so it is opt-in rather than part of an unattended run.
//   dotnet test tests/WebTestToolkit.GeneratedTests --filter "Category=Browser"
[TestFixture]
[Explicit("Drives a real Chrome window.")]
[Category("Browser")]
public class PopupBehaviorTests
{
    private const string OverlayPageHtml = """
        <!doctype html>
        <html>
        <head><meta charset="utf-8"><title>Overlay</title></head>
        <body>
          <button id="target">Target</button>
          <div id="banner" style="position:fixed;inset:0;background:rgba(0,0,0,0.5);z-index:9999;">
            We use cookies.
          </div>
        </body>
        </html>
        """;

    private const string DialogPageHtml = """
        <!doctype html>
        <html>
        <head><meta charset="utf-8"><title>Dialog</title></head>
        <body>
          <button id="target">Target</button>
          <script>alert('Session expired');</script>
        </body>
        </html>
        """;

    private TinyWebServer _server = null!;
    private DriverContext _driverContext = null!;

    [SetUp]
    public void SetUp()
    {
        _server = new TinyWebServer(new Dictionary<string, string>
        {
            ["/overlay"] = OverlayPageHtml,
            ["/dialog"] = DialogPageHtml
        });
        _driverContext = new DriverContext();
    }

    [TearDown]
    public void TearDown()
    {
        _driverContext.Dispose();
        _server.Dispose();
    }

    [Test]
    public void OverlayCoveringAnElement_ThrowsElementClickIntercepted()
    {
        var driver = _driverContext.Driver;
        driver.Navigate().GoToUrl(_server.BaseUrl + "/overlay");

        // Displayed and enabled, so FindVisible finds it happily — the interception only shows
        // up at the click. That is why the diagnostic has to live on the click, not the lookup.
        var target = driver.FindElement(By.Id("target"));
        Assert.That(target.Displayed, Is.True);

        Assert.Throws<ElementClickInterceptedException>(() => target.Click(),
            "ClickSafely catches this type by name; a different type would make it dead code.");
    }

    [Test]
    public void OpenJsDialog_ThrowsUnhandledAlertCarryingTheDialogText()
    {
        var driver = _driverContext.Driver;
        driver.Navigate().GoToUrl(_server.BaseUrl + "/dialog");

        var ex = Assert.Throws<UnhandledAlertException>(() => driver.FindElement(By.Id("target")).Click());

        // ClickSafely puts this text in its message. Without it the failure says only that some
        // command hit an unexpected dialog, which does not identify which dialog.
        Assert.That(ex!.AlertText, Is.EqualTo("Session expired"));
    }
}
