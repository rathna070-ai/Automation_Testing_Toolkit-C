namespace WebTestToolkit.Contracts.Models;

public enum ScenarioOutcome
{
    Passed,
    Failed,
    Skipped
}

// One scenario's result from a test run, parsed out of the .trx file dotnet test produces.
public class ScenarioResult
{
    public string FeatureName { get; set; } = "";
    public string ScenarioName { get; set; } = "";
    public ScenarioOutcome Outcome { get; set; }
    public TimeSpan Duration { get; set; }
    public string? ErrorMessage { get; set; }
    public string? StackTrace { get; set; }
    public string? ScreenshotPath { get; set; }
}

// The full result of one "Run & Report" execution, shown in the Report window.
public class RunSummary
{
    public DateTime RunAtUtc { get; set; }
    public int Total { get; set; }
    public int Passed { get; set; }
    public int Failed { get; set; }
    public TimeSpan Duration { get; set; }
    public List<ScenarioResult> Scenarios { get; set; } = new();
}
