using OpenQA.Selenium;
using WebTestToolkit.Contracts.Models;

namespace WebTestToolkit.Inspector.Tests;

// The only test here that launches a real browser. It is the one that proves the pieces
// actually fit: overlay injection, hover/click capture, sessionStorage surviving a form
// submit, ranking, labeling, and the TestFlow handoff.
//
// [Explicit] because it needs Chrome installed and (on a cold machine) Selenium Manager
// needs the network to fetch a driver. The rest of the suite must stay runnable anywhere:
//     dotnet test backend/WebTestToolkit.Inspector.Tests --filter "Category=Browser"
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

    private TinyWebServer _server = null!;

    [SetUp]
    public void SetUp() => _server = new TinyWebServer(new Dictionary<string, string>
    {
        ["/login"] = LoginPage,
        ["/secure"] = SecurePage
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
            await session.PollAsync(CancellationToken.None);

            username.Clear();
            username.SendKeys("tomsmith");
            session.Driver.FindElement(By.Id("password")).Click(); // blur -> second change event
            // The step count does not change here — the correction collapses into the
            // existing step — so wait on the value, not on a new step appearing.
            await PollUntilAsync(session, s =>
                s.Steps.Any(e => e.ActionType == ActionType.Type && e.InputValue == "tomsmith"));

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
}
