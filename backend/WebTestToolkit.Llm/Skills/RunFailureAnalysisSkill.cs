using Microsoft.Extensions.Logging;
using WebTestToolkit.Contracts.Models;
using WebTestToolkit.Llm.Transport;

namespace WebTestToolkit.Llm.Skills;

// One failing scenario, plus the locator entries it depends on when they are known. The
// locator half is the context the single-scenario skill never had: "the element wasn't found"
// is a restatement of the error, whereas "UsernameInput points at #username, which the page no
// longer has" is a diagnosis. The toolkit already has that JSON on disk, so withholding it was
// only ever an omission.
public record RunFailureScenarioInput(
    string FeatureName,
    string ScenarioName,
    string? ErrorMessage,
    string? StackTrace);

public record RunFailureLocatorInput(string Page, string Key, string Strategy, string Value);

public record RunFailureAnalysisInput(
    IReadOnlyList<RunFailureScenarioInput> Scenarios,
    IReadOnlyList<RunFailureLocatorInput> Locators);

// Analyses a whole run's failures together rather than one at a time.
//
// The per-scenario FailureAnalysisSkill answers "why did this fail?" for each failure
// independently, which structurally cannot notice that five of six failures share one cause —
// each call only ever sees one. That is the question someone staring at a wall of red actually
// has, so it needs a call that sees them all at once.
public class RunFailureAnalysisSkill : LlmSkill<RunFailureAnalysisInput, RunFailureAnalysis>
{
    public RunFailureAnalysisSkill(IChatClient chatClient, PromptLibrary prompts, ILogger<RunFailureAnalysisSkill> logger)
        : base(chatClient, prompts, logger)
    {
    }

    protected override string PromptName => "run-failure-analysis";
    protected override string SchemaName => "run_failure_analysis";
    protected override string ReasoningEffort => "medium";

    // Larger than the single-scenario skill's 1024: the response carries one group per distinct
    // cause, each with its own prose and scenario list. Still small — this reads errors and
    // stack traces, never source files.
    protected override int MaxCompletionTokens => 3000;

    protected override string BuildUserMessage(RunFailureAnalysisInput input)
    {
        var scenarios = string.Join("\n", input.Scenarios.Select(FormatScenario));

        var locators = input.Locators.Count == 0
            ? "(none available)"
            : string.Join("\n", input.Locators.Select(l => $"  {l.Page}.{l.Key} = {l.Strategy}:{l.Value}"));

        return $"""
            <failing_scenarios count="{input.Scenarios.Count}">
            {scenarios}
            </failing_scenarios>
            <known_locators>
            {locators}
            </known_locators>
            """;
    }

    private static string FormatScenario(RunFailureScenarioInput s) =>
        $"""
        <scenario name="{s.ScenarioName}" feature="{s.FeatureName}">
        error: {s.ErrorMessage ?? "(no error message captured)"}
        stack: {Truncate(s.StackTrace, PerScenarioStackTraceLimit) ?? "(no stack trace captured)"}
        </scenario>
        """;

    // Per scenario, not for the whole prompt: a run with ten failures would otherwise be ten
    // full stack traces, and the top frames are where the cause is anyway. Tighter than the
    // single-scenario skill's 2000 for exactly that reason — this prompt holds many of them.
    private const int PerScenarioStackTraceLimit = 600;

    private static string? Truncate(string? text, int maxLength)
    {
        if (text is null || text.Length <= maxLength)
            return text;
        return text[..maxLength] + "\n… (truncated)";
    }
}
