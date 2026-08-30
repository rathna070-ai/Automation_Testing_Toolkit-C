using System.Text.Json;
using OpenQA.Selenium;
using Reqnroll;

namespace WebTestToolkit.GeneratedTests.Support;

[Binding]
public class Hooks
{
    private readonly DriverContext _driverContext;

    public Hooks(DriverContext driverContext)
    {
        _driverContext = driverContext;
    }

    // Hosts the sample suite's login-page fixture locally for the whole run rather than
    // pointing LoginPage at the third-party the-internet.herokuapp.com/login it originally
    // targeted — this is the project's own reference/gold sample, so its correctness
    // shouldn't depend on that site's uptime the way a real captured flow's does.
    //
    // TinyWebServer picks an OS-assigned port (avoids ever colliding with something already
    // listening), so LoginPage.locators.json's checked-in "url" can't be a real address ahead
    // of time. Patched here, once, before any scenario's DriverContext/LocatorRepository is
    // touched — LocatorRepository.Load caches on first read per pageName, so the patch has to
    // land on disk before that first read, which [BeforeTestRun] guarantees.
    private static TinyWebServer? _server;

    private const string LoginPageHtml = """
        <!doctype html>
        <html>
        <head><meta charset="utf-8"><title>Login</title></head>
        <body>
          <form id="login" onsubmit="return false;">
            <input id="username" name="username" type="text" />
            <input id="password" name="password" type="password" />
            <button type="submit" onclick="tryLogin()">Login</button>
          </form>
          <div id="flash" style="display:none;"></div>
          <script>
            function tryLogin() {
              var u = document.getElementById('username').value;
              var p = document.getElementById('password').value;
              var flash = document.getElementById('flash');
              if (u === 'tomsmith' && p === 'SuperSecretPassword!') {
                flash.textContent = 'You logged into a secure area!';
              } else {
                flash.textContent = 'Your password is invalid!';
              }
              flash.style.display = 'block';
            }
          </script>
        </body>
        </html>
        """;

    // Serves the <select> the Signup scenario drives. That scenario exists to prove a
    // generated dropdown step actually runs: the generator used to emit Clear()+SendKeys()
    // for a <select>, and Clear() on a non-editable element throws invalid-element-state, so
    // every recorded flow with a dropdown produced a suite that crashed. A unit test can show
    // the emitted *text* is right; only running it against a real browser shows it works.
    private const string SignupPageHtml = """
        <!doctype html>
        <html>
        <head><meta charset="utf-8"><title>Signup</title></head>
        <body>
          <form id="signup" onsubmit="return false;">
            <select id="country" name="country">
              <option value="">-- choose --</option>
              <option value="in">India</option>
              <option value="uk">United Kingdom</option>
            </select>
          </form>
          <div id="chosen"></div>
          <script>
            document.getElementById('country').addEventListener('change', function () {
              var sel = this;
              document.getElementById('chosen').textContent =
                'Selected: ' + sel.options[sel.selectedIndex].text;
            });
          </script>
        </body>
        </html>
        """;

    [BeforeTestRun]
    public static void BeforeTestRun()
    {
        _server = new TinyWebServer(new Dictionary<string, string>
        {
            ["/"] = LoginPageHtml,
            ["/signup"] = SignupPageHtml
        });
        PatchLoginPageUrl(_server.BaseUrl + "/");
        PatchPageUrl("SignupPage", _server.BaseUrl + "/signup");
    }

    [AfterTestRun]
    public static void AfterTestRun()
    {
        _server?.Dispose();
        _server = null;
    }

    private static void PatchLoginPageUrl(string url) => PatchPageUrl("LoginPage", url);

    private static void PatchPageUrl(string pageName, string url)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "LocatorRepository", $"{pageName}.locators.json");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var current = JsonSerializer.Deserialize<PageLocators>(File.ReadAllText(path), options)
            ?? throw new InvalidOperationException($"Could not parse {pageName}.locators.json to patch its fixture URL.");

        var patched = current with { Url = url };
        File.WriteAllText(path, JsonSerializer.Serialize(patched, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }

    [AfterScenario]
    public void AfterScenario(ScenarioContext scenarioContext)
    {
        if (scenarioContext.TestError != null)
        {
            TakeFailureScreenshot(scenarioContext);
        }

        _driverContext.Dispose();
    }

    private void TakeFailureScreenshot(ScenarioContext scenarioContext)
    {
        if (_driverContext.Driver is not ITakesScreenshot screenshotDriver)
            return;

        var screenshotsDir = Path.Combine(AppContext.BaseDirectory, "Screenshots");
        Directory.CreateDirectory(screenshotsDir);

        var invalidChars = Path.GetInvalidFileNameChars();
        var safeName = new string(scenarioContext.ScenarioInfo.Title
            .Select(c => invalidChars.Contains(c) ? '_' : c)
            .ToArray());

        var fileName = $"{safeName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.png";
        var path = Path.Combine(screenshotsDir, fileName);

        screenshotDriver.GetScreenshot().SaveAsFile(path);
        scenarioContext["ScreenshotPath"] = path;

        // The .trx file has no field for arbitrary per-test metadata, but VSTest does capture
        // each test's Console output into Output/StdOut — so that's how TrxParser recovers
        // this path after the run, without needing its own out-of-band file.
        Console.WriteLine($"[WTT_SCREENSHOT]{path}");
    }
}
