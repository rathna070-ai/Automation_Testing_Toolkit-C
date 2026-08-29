using OpenQA.Selenium;
using WebTestToolkit.Contracts.Models;

namespace WebTestToolkit.Inspector.Tests;

// The only test here that launches a real browser. It is the one that proves the pieces
// actually fit: overlay injection, hover/click capture, sessionStorage surviving a form
// submit, ranking, labeling, and the TestFlow handoff.
//
// [Explicit] because it needs Chrome installed and (on a cold machine) Selenium Manager
// needs the network to fetch a driver. The rest of the suite must stay runnable anywhere:
//     dotnet test tests/WebTestToolkit.Inspector.Tests --filter "Category=Browser"
[Explicit("Requires a local Chrome installation.")]
[Category("Browser")]
public class InspectorSessionBrowserTests
{
    private const string LoginPage = """
    <!doctype html>
    <html><head><title>Login</title></head>
    <body>
      <form id="login" action="/secure" method="get">
        <h2>Login Page</h2>
        <label for="username">Username</label>
        <input type="text" id="username" name="username">
        <label for="password">Password</label>
        <input type="password" id="password" name="password">
        <button type="submit" id="submit">Login</button>
      </form>
    </body></html>
    """;

    private const string SecurePage = """
    <!doctype html>
    <html><head><title>Secure</title></head>
    <body><div id="flash">You logged into a secure area!</div></body></html>
    """;

    // Real element state (P13 item 4) is only observable against real form controls — a
    // required <select> with real options, a checkbox, and a maxlength'd text field.
    private const string FormPage = """
    <!doctype html>
    <html><head><title>Form</title></head>
    <body>
      <form>
        <label for="notes">Notes</label>
        <input type="text" id="notes" maxlength="10">
        <label for="country">Country</label>
        <select id="country" required>
          <option value="">Choose...</option>
          <option value="us">United States</option>
          <option value="ca">Canada</option>
        </select>
        <label for="agree">I agree</label>
        <input type="checkbox" id="agree">
      </form>
    </body></html>
    """;

    private TinyWebServer _server = null!;

    [SetUp]
    public void SetUp() => _server = new TinyWebServer(new Dictionary<string, string>
    {
        ["/login"] = LoginPage,
        ["/secure"] = SecurePage,
        ["/form"] = FormPage
    });

    [TearDown]
    public void TearDown() => _server.Dispose();

    [Test]
    public async Task CapturesAWholeLoginFlowAndTurnsItIntoATestFlow()
    {
        var session = await InspectorSession.StartAsync(
            new InspectorStartRequest("Login", $"{_server.BaseUrl}/login", Headless: true),
            CancellationToken.None);

        try
        {
            var driver = session.Driver;

            driver.FindElement(By.Id("username")).SendKeys("tomsmith");
            driver.FindElement(By.Id("password")).SendKeys("SuperSecretPassword!");
            // Clicking submit blurs the password field (firing its change event) and then
            // navigates away — the exact sequence the sessionStorage queue exists to survive.
            driver.FindElement(By.Id("submit")).Click();

            await PollUntilStepsAsync(session, expectedSteps: 4);

            var steps = session.Steps;

            Assert.That(steps.Select(s => s.ActionType), Is.EqualTo(new[]
            {
                ActionType.Navigate, // the start URL
                ActionType.Type,     // username
                ActionType.Type,     // password
                ActionType.Click     // Login
            }), "captured: " + string.Join(" | ", steps.Select(s => $"{s.ActionType}:{s.LocatorKey}")));

            Assert.Multiple(() =>
            {
                Assert.That(steps[0].PageName, Is.EqualTo("LoginPage"));
                Assert.That(steps[1].LocatorKey, Is.EqualTo("UsernameInput"));
                Assert.That(steps[1].InputValue, Is.EqualTo("tomsmith"));
                Assert.That(steps[2].LocatorKey, Is.EqualTo("PasswordInput"));
                Assert.That(steps[2].SuggestedLabel, Is.EqualTo("I enter the password"));
                // <button type="submit" id="submit">Login</button> — named for the word on
                // the button, not the id, because that is what the reader of the test sees.
                Assert.That(steps[3].LocatorKey, Is.EqualTo("LoginButton"));

                // The click is what caused the navigation, so recording a separate "I open
                // the secure page" step would make the generated test skip the login itself.
                Assert.That(steps.Count(s => s.ActionType == ActionType.Navigate), Is.EqualTo(1));

                // ids are present on every field here, so ranking should have chosen them.
                Assert.That(steps[1].Element!.BestLocator!.Strategy, Is.EqualTo("id"));
                Assert.That(steps[1].Element!.BestLocator!.Value, Is.EqualTo("username"));
            });

            var flow = session.ToFlow();
            Assert.That(flow.Name, Is.EqualTo("Login"));
            Assert.That(flow.Steps.Select(s => s.Order), Is.EqualTo(new[] { 1, 2, 3, 4 }));
        }
        finally
        {
            await session.StopAsync(CancellationToken.None);
        }
    }

    // Retyping a field fires `change` twice. Two "I enter the username" steps with different
    // values would generate a test that types the typo and then corrects it.
    [Test]
    public async Task CollapsesARetypedFieldIntoASingleStep()
    {
        var session = await InspectorSession.StartAsync(
            new InspectorStartRequest("Retype", $"{_server.BaseUrl}/login", Headless: true),
            CancellationToken.None);

        try
        {
            var username = session.Driver.FindElement(By.Id("username"));
            username.SendKeys("wrong");
            session.Driver.FindElement(By.Id("password")).Click(); // blur -> first change event
            var firstPoll = await session.PollAsync(CancellationToken.None);
            var originalSequence = firstPoll.Single(e => e.ActionType == ActionType.Type).Sequence;

            username.Clear();
            username.SendKeys("tomsmith");
            session.Driver.FindElement(By.Id("password")).Click(); // blur -> second change event

            // The correction must come back from PollAsync itself, at the SAME Sequence —
            // that's what InspectorBroadcastService pushes to SignalR. A UI only listening
            // to the live feed (never re-fetching) has to see the fix, not just eventual
            // internal consistency; asserting on session.Steps alone wouldn't catch a
            // regression where the correction updates state but is never broadcast.
            var correction = await PollUntilReturnsAsync(session,
                s => s.SingleOrDefault(e => e.ActionType == ActionType.Type && e.InputValue == "tomsmith"));

            Assert.That(correction, Is.Not.Null, "the retype correction was never returned from PollAsync");
            Assert.That(correction!.Sequence, Is.EqualTo(originalSequence),
                "a correction must reuse the original step's Sequence so a live listener can upsert it, not append a duplicate");

            var typeSteps = session.Steps.Where(s => s.ActionType == ActionType.Type).ToList();
            Assert.That(typeSteps, Has.Count.EqualTo(1));
            Assert.That(typeSteps[0].InputValue, Is.EqualTo("tomsmith"));
        }
        finally
        {
            await session.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task PausingStopsCaptureWithoutClosingTheBrowser()
    {
        var session = await InspectorSession.StartAsync(
            new InspectorStartRequest("Paused", $"{_server.BaseUrl}/login", Headless: true),
            CancellationToken.None);

        try
        {
            await session.SetCaptureEnabledAsync(false, CancellationToken.None);
            Assert.That(session.State, Is.EqualTo(InspectorSessionState.Paused));

            session.Driver.FindElement(By.Id("username")).SendKeys("ignored");
            session.Driver.FindElement(By.Id("password")).Click();
            await session.PollAsync(CancellationToken.None);

            Assert.That(session.Steps.Count(s => s.ActionType == ActionType.Type), Is.Zero,
                "clicks and edits made while paused must not land in the flow");

            await session.SetCaptureEnabledAsync(true, CancellationToken.None);
            Assert.That(session.State, Is.EqualTo(InspectorSessionState.Running));

            session.Driver.FindElement(By.Id("username")).SendKeys("captured");
            session.Driver.FindElement(By.Id("password")).Click();
            await PollUntilStepsAsync(session, expectedSteps: 2);

            Assert.That(session.Steps.Count(s => s.ActionType == ActionType.Type), Is.EqualTo(1));
        }
        finally
        {
            await session.StopAsync(CancellationToken.None);
        }
    }

    // Navigation the user performed themselves (address bar, a link we didn't see) is a
    // real step and does need recording.
    [Test]
    public async Task RecordsANavigationThatNoClickExplains()
    {
        var session = await InspectorSession.StartAsync(
            new InspectorStartRequest("Nav", $"{_server.BaseUrl}/login", Headless: true),
            CancellationToken.None);

        try
        {
            session.Driver.Navigate().GoToUrl($"{_server.BaseUrl}/secure");
            await PollUntilStepsAsync(session, expectedSteps: 2);

            var steps = session.Steps;
            Assert.That(steps, Has.Count.EqualTo(2));
            Assert.That(steps[1].ActionType, Is.EqualTo(ActionType.Navigate));
            Assert.That(steps[1].PageName, Is.EqualTo("SecurePage"));
            Assert.That(steps[1].SuggestedLabel, Is.EqualTo("I open the secure page"));
        }
        finally
        {
            await session.StopAsync(CancellationToken.None);
        }
    }

    // Real element state (P13 item 4): without this, the model only has the raw outerHTML
    // snippet to infer a <select>'s real options or a checkbox's state from — a real bug in
    // a sibling project came from exactly that gap. Proves the whole pipeline against a real
    // browser: overlay capture -> RawCapture -> LocatorRanker.ToCapturedElement.
    [Test]
    public async Task CapturesRealElementStateForSelectCheckboxAndMaxLength()
    {
        var session = await InspectorSession.StartAsync(
            new InspectorStartRequest("Form", $"{_server.BaseUrl}/form", Headless: true),
            CancellationToken.None);

        try
        {
            var driver = session.Driver;

            driver.FindElement(By.Id("notes")).SendKeys("hello" + Keys.Tab);

            // WebDriver's click on an <option> is special-cased to select it directly,
            // firing `change` on the <select> — no separate "open the dropdown" click needed
            // (and one would otherwise show up as its own, distinct capture).
            driver.FindElement(By.CssSelector("#country option[value='ca']")).Click();

            driver.FindElement(By.Id("agree")).Click();

            // navigate, notes, country (change), a stray click the option-click also fires
            // against the select itself in headless mode, agree.
            await PollUntilStepsAsync(session, expectedSteps: 5);

            var steps = session.Steps;
            var notes = steps.Single(s => s.Element?.Id == "notes");
            // WebDriver's click on an <option> in headless Chrome also dispatches a separate
            // click captured against the <select> itself (target resolution quirk of a
            // native dropdown with no real open/close UI in headless mode) — that stray
            // click carries no value or options and isn't what this test is about; the
            // `change`-driven capture (ActionType.Type) is the one carrying real state.
            var country = steps.Single(s => s.Element?.TagName == "select" && s.ActionType == ActionType.Type);
            var agree = steps.Single(s => s.Element?.Id == "agree");

            Assert.Multiple(() =>
            {
                Assert.That(notes.Element!.MaxLength, Is.EqualTo(10));

                Assert.That(country.Element!.Required, Is.True);
                Assert.That(country.Element!.Options, Is.Not.Null);
                Assert.That(country.Element!.Options!.Select(o => o.Value),
                    Is.EqualTo(new[] { "", "us", "ca" }));
                Assert.That(country.Element!.Options!.Single(o => o.Value == "ca").Selected, Is.True);
                Assert.That(country.Element!.Options!.Single(o => o.Value == "us").Selected, Is.False);

                Assert.That(agree.Element!.Checked, Is.True);
                Assert.That(agree.Element!.Options, Is.Null,
                    "Options must only be populated for a <select>.");
            });
        }
        finally
        {
            await session.StopAsync(CancellationToken.None);
        }
    }

    // The broadcast service polls on a timer; here we poll by hand until the flow looks the
    // way the test expects, so the test isn't racing page load.
    //
    // Always polls at least once. Checking the condition first would make this a no-op for
    // any test whose assertion is about a step *changing* rather than a step appearing.
    private static async Task PollUntilAsync(InspectorSession session, Func<InspectorSession, bool> settled)
    {
        for (var attempt = 0; attempt < 25; attempt++)
        {
            await session.PollAsync(CancellationToken.None);
            if (settled(session))
                return;
            await Task.Delay(200);
        }
    }

    private static Task PollUntilStepsAsync(InspectorSession session, int expectedSteps) =>
        PollUntilAsync(session, s => s.Steps.Count >= expectedSteps);

    // Like PollUntilAsync, but returns whatever a single PollAsync call itself handed back —
    // the exact list InspectorBroadcastService would push to SignalR — rather than the
    // session's converged internal state. Needed to prove a value actually got broadcast,
    // not just that it eventually became true internally.
    private static async Task<InspectorEvent?> PollUntilReturnsAsync(
        InspectorSession session, Func<IReadOnlyList<InspectorEvent>, InspectorEvent?> select)
    {
        for (var attempt = 0; attempt < 25; attempt++)
        {
            var polled = await session.PollAsync(CancellationToken.None);
            var found = select(polled);
            if (found is not null)
                return found;
            await Task.Delay(200);
        }
        return null;
    }
}
