namespace WebTestToolkit.Contracts.Models;

// Where a test case came from. Only Recorded is ever produced today — EdgeCase and Outline
// exist so P9 (which is where the LLM edge-case skill and Scenario Outline support actually
// land) can add cases to a suite without a schema change here.
public enum TestCaseSource
{
    Recorded,
    EdgeCase,
    Outline
}

public enum TestCasePriority
{
    Low,
    Medium,
    High
}

public record TestCaseStep(int Number, string Action, string? TestData, string ExpectedResult);

// One manual-test-case rendering of a flow (or, later, of one Scenario Outline row / one
// edge case). Deliberately reuses ScenarioOutcome rather than inventing a parallel
// "not run yet" status — null means exactly that, and a non-null value already carries
// the same three states a real run reports.
public class TestCaseDocument
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Precondition { get; set; } = "";
    public TestCasePriority Priority { get; set; } = TestCasePriority.Medium;
    public TestCaseSource Source { get; set; } = TestCaseSource.Recorded;
    public ScenarioOutcome? LastRunStatus { get; set; }
    public List<TestCaseStep> Steps { get; set; } = new();
}

public class TestCaseSuite
{
    public string FlowName { get; set; } = "";
    public string StartUrl { get; set; } = "";
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    public List<TestCaseDocument> TestCases { get; set; } = new();
}
