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
        return new ChromeDriver(options);
    }

    public void Dispose()
    {
        _driver?.Quit();
        _driver?.Dispose();
        _driver = null;
    }
}
