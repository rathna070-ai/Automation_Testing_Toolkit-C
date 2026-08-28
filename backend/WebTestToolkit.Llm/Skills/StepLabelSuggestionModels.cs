namespace WebTestToolkit.Llm.Skills;

// Everything the model needs to improve on StepLabeler's deterministic label, and nothing
// more. No InputValue (never send a typed value, especially a password, to an LLM). No
// OuterHtmlSnippet/CssClasses either — third-party page markup the skill doesn't need is
// prompt-injection surface for zero benefit; VisibleText/AriaLabel/AssociatedLabelText
// already carry what a human would use to describe the element.
public record StepLabelSuggestionInput(
    string ActionType,
    string PageName,
    string DeterministicLabel,
    string TagName,
    string? ElementType,
    string? VisibleText,
    string? Placeholder,
    string? AriaLabel,
    string? AssociatedLabelText,
    string? AncestorContext);

public record StepLabelSuggestionResult(string Label);
