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
    }
}
