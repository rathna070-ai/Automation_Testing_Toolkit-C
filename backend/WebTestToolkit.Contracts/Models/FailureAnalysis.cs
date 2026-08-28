namespace WebTestToolkit.Contracts.Models;

public enum FailureCategory
{
    BrokenLocator,
    Timing,
    AssertionMismatch,
    Navigation,
    TestData,
    Environment,
    ApplicationBug,
    Unknown
}

// A candidate locator fix, offered only when the failure looks like a broken locator and
// the model was confident enough to name a specific replacement.
public class SuggestedLocatorFix
{
    public string Page { get; set; } = "";
    public string Key { get; set; } = "";
    public string Strategy { get; set; } = "";
    public string Value { get; set; } = "";
    public string Why { get; set; } = "";
}

// Result of asking Groq to explain a ScenarioResult's failure.
public class FailureAnalysis
{
    public FailureCategory Category { get; set; } = FailureCategory.Unknown;
    public string RootCause { get; set; } = "";
    public string SuggestedFix { get; set; } = "";
    public SuggestedLocatorFix? SuggestedLocator { get; set; }
    public bool IsLikelyApplicationBug { get; set; }
    public double Confidence { get; set; }
    public string? Model { get; set; }
}
