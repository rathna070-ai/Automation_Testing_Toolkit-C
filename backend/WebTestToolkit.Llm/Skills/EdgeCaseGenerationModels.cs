namespace WebTestToolkit.Llm.Skills;

// One captured step's shape, with no actual values — same "never show the model a real
// value" discipline as StepLabelSuggestionInput, extended to InputValue/ExpectedText:
// the model doesn't need to see what was typed (it could be a password) to reason about
// what a plausible edge case is, and it is asked to invent new values anyway.
public record EdgeCaseStepSummary(
    int Order,
    string ActionType,
    string Label,
    string PageName,
    bool HasInputValue,
    bool HasExpectedText);

public record EdgeCaseGenerationInput(
    string FlowName,
    string StartUrl,
    IReadOnlyList<EdgeCaseStepSummary> Steps);

// Only steps that need to change from the happy path are listed; every other step of the
// original flow is reused unmodified by EdgeCaseFlowBuilder. NewInputValue/NewExpectedText
// are the model's own invented values (e.g. "wrong-password"), never a real captured one.
public record EdgeCaseStepOverride(int StepOrder, string? NewInputValue, string? NewExpectedText);

public record EdgeCaseSuggestion(
    string NameSuffix,
    string Title,
    string Rationale,
    IReadOnlyList<EdgeCaseStepOverride> Overrides);

public record EdgeCaseGenerationOutput(IReadOnlyList<EdgeCaseSuggestion> EdgeCases);
