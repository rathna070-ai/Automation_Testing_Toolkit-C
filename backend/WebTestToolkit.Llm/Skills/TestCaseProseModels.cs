namespace WebTestToolkit.Llm.Skills;

// One step's context for prose generation — deliberately not the raw TestStep/CapturedElement.
// Two things are left out on purpose: InputValue (a typed password has no business reaching
// an LLM prompt — TestData in the rendered TestCaseStep is filled in mechanically, never by
// the model) and the DOM noise (OuterHtmlSnippet/CssClasses) that CapturedElement carries for
// nothing this skill needs, which would otherwise be untrusted third-party content sitting in
// the prompt for no benefit.
public record TestCaseProseStepInput(
    int Number,
    string ActionType,
    string Label,
    string PageName,
    string? ExpectedText);

public record TestCaseProseInput(string FlowName, string StartUrl, IReadOnlyList<TestCaseProseStepInput> Steps);

public record TestCaseProseStepResult(int Number, string Action, string ExpectedResult);

public record TestCaseProseResult(string Title, string Precondition, List<TestCaseProseStepResult> Steps);
