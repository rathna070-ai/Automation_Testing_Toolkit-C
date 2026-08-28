using WebTestToolkit.Contracts.Models;
using WebTestToolkit.Llm.Skills;

namespace WebTestToolkit.Export;

// Turns a captured TestFlow into a TestCaseSuite — a second *renderer* of the same flow the
// code generator turns into a Reqnroll suite, not a separate capture path.
//
// Deterministic prose is always built first and is the return value whenever the LLM path is
// off, unavailable, or returns something that doesn't parse — mirrors HybridTestCodeGenerator's
// deterministic-first, LLM-enhances design from P5, just without that pattern's sandbox-compile
// machinery: there is no way for wording to fail to "compile", so there is nothing to retry or
// repair, only a plain success-or-fall-back.
public static class TestCaseSuiteBuilder
{
    public static async Task<TestCaseSuite> BuildAsync(
        TestFlow flow, TestCaseProseSkill skill, bool useLlm, CancellationToken ct)
    {
        var testCase = BuildDeterministic(flow);

        if (useLlm)
        {
            var enhanced = await TryEnhanceAsync(flow, testCase, skill, ct);
            if (enhanced is not null)
                testCase = enhanced;
        }

        return new TestCaseSuite
        {
            FlowName = flow.Name,
            StartUrl = flow.StartUrl,
            TestCases = [testCase]
        };
    }

    private static async Task<TestCaseDocument?> TryEnhanceAsync(
        TestFlow flow, TestCaseDocument deterministic, TestCaseProseSkill skill, CancellationToken ct)
    {
        var input = new TestCaseProseInput(
            flow.Name,
            flow.StartUrl,
            flow.Steps
                .Select(s => new TestCaseProseStepInput(s.Order, s.ActionType.ToString(), s.Label, s.PageName, s.ExpectedText))
                .ToList());

        var result = await skill.RunAsync(input, ct);
        if (!result.IsSuccess)
            return null;

        // TestData is never touched here — it stays whatever BuildDeterministic already put
        // there (TestStep.InputValue, mechanically). The model was never shown a real value
        // (see TestCaseProseStepInput's doc comment), so there is nothing for it to overwrite.
        var proseByNumber = result.Value!.Steps.ToDictionary(s => s.Number);

        return new TestCaseDocument
        {
            Id = deterministic.Id,
            Title = result.Value.Title,
            Precondition = result.Value.Precondition,
            Priority = deterministic.Priority,
            Source = deterministic.Source,
            LastRunStatus = deterministic.LastRunStatus,
            Steps = deterministic.Steps
                .Select(step => proseByNumber.TryGetValue(step.Number, out var prose)
                    ? step with { Action = prose.Action, ExpectedResult = prose.ExpectedResult }
                    : step)
                .ToList()
        };
    }

    // The no-API-key path. Every field here has to stand on its own — this is what the
    // export looks like for a tool with nothing configured, not a degraded preview of it.
    public static TestCaseDocument BuildDeterministic(TestFlow flow)
    {
        return new TestCaseDocument
        {
            // Suites only ever hold one document today (the recorded happy path); once P9
            // adds edge cases/outline rows, this becomes TC-002, TC-003, ...
            Id = "TC-001",
            Title = $"{flow.Name} flow",
            Precondition = $"User starts at {flow.StartUrl}",
            Priority = TestCasePriority.Medium,
            Source = TestCaseSource.Recorded,
            LastRunStatus = null,
            Steps = flow.Steps
                .Select(s => new TestCaseStep(s.Order, DeterministicAction(s), s.InputValue, DeterministicExpectedResult(s)))
                .ToList()
        };
    }

    // StepLabeler's Gherkin-voice label ("I enter the username") read backwards as an
    // imperative instruction ("Enter the username.") for a manual tester to follow.
    private static string DeterministicAction(TestStep step)
    {
        var label = step.Label.Trim();
        if (label.StartsWith("I ", StringComparison.Ordinal))
            label = label[2..];

        if (label.Length == 0)
            return "Perform the recorded action.";

        return char.ToUpperInvariant(label[0]) + label[1..] + ".";
    }

    private static string DeterministicExpectedResult(TestStep step)
    {
        if (!string.IsNullOrWhiteSpace(step.ExpectedText))
            return step.ExpectedText;

        return step.ActionType switch
        {
            ActionType.Navigate => $"The {Humanize(step.PageName)} page loads.",
            ActionType.Type => "The field contains the entered value.",
            ActionType.Click => "The click completes and the page responds accordingly.",
            ActionType.AssertText => "The expected text is visible on the page.",
            ActionType.AssertVisible => "The expected element is visible on the page.",
            _ => "The action completes as expected."
        };
    }

    // Small enough, and specific enough to this project's own output, that sharing it with
    // Inspector's near-identical StepLabeler.Humanize isn't worth a cross-project dependency
    // Export otherwise has no reason to take.
    private static string Humanize(string pageName)
    {
        if (string.IsNullOrEmpty(pageName))
            return pageName;

        var name = pageName.EndsWith("Page", StringComparison.Ordinal) ? pageName[..^4] : pageName;
        var spaced = System.Text.RegularExpressions.Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])", " ");
        return spaced.Length == 0 ? "start" : spaced.ToLowerInvariant();
    }
}
