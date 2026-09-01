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

// One root cause, and every scenario it explains. A run with six failures is usually not six
// problems — it is one broken locator hit by six scenarios, or one environment issue. Analysing
// scenarios one at a time (the single-scenario FailureAnalysis above) cannot say that, because
// each call only ever sees one failure and has no way to notice the repetition.
public class FailureGroup
{
    public string Title { get; set; } = "";
    public FailureCategory Category { get; set; }
    public string RootCause { get; set; } = "";
    public string SuggestedFix { get; set; } = "";

    // Scenario names this group explains, so the UI can show "3 of 6 failures" against it and
    // the reader knows how much of the run one fix would clear.
    public List<string> ScenarioNames { get; set; } = new();

    public SuggestedLocatorFix? SuggestedLocator { get; set; }
    public bool IsLikelyApplicationBug { get; set; }
    public double Confidence { get; set; }
}

public class RunFailureAnalysis
{
    // Ordered most-explanatory first: the group covering the most scenarios is the one worth
    // fixing before the others.
    public List<FailureGroup> Groups { get; set; } = new();

    public string Summary { get; set; } = "";
}
