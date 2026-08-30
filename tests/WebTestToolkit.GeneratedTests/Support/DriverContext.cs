using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;

namespace WebTestToolkit.GeneratedTests.Support;

// Reqnroll context-injection: one instance of this class is shared across all
// bindings within a single scenario, so the browser session survives across steps.
public class DriverContext : IDisposable
{
    private IWebDriver? _driver;

    public IWebDriver Driver => _driver ??= CreateDriver();

    private static IWebDriver CreateDriver()
    {
        var options = new ChromeOptions();
        options.AddArgument("--start-maximized");

        try
        {
            return new ChromeDriver(options);
        }
        catch (Exception ex)
        {
            // Almost always Chrome not being installed, or Selenium Manager unable to reach
            // the network to fetch a matching driver on first run — the same failure mode
            // InspectController.Start already gives an actionable message for. Left as the
            // raw WebDriverException/Win32Exception here, this surfaces in a test run as an
            // opaque stack trace with no hint that the fix is "install Chrome" or "check your
            // network", not "the generated test is broken".
            throw new InvalidOperationException(
                "Could not start Chrome for this test run — Chrome may not be installed, or " +
                "Selenium Manager could not reach the network to fetch a matching driver. " +
                $"Original error: {ex.Message}", ex);
        }
    }

    public void Dispose()
    {
        _driver?.Quit();
        _driver?.Dispose();
        _driver = null;
    }
}
