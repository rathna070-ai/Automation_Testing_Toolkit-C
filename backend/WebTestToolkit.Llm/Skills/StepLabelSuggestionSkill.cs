using Microsoft.Extensions.Logging;
using WebTestToolkit.Llm.Transport;

namespace WebTestToolkit.Llm.Skills;

public class StepLabelSuggestionSkill : LlmSkill<StepLabelSuggestionInput, StepLabelSuggestionResult>
{
    public StepLabelSuggestionSkill(IChatClient chatClient, PromptLibrary prompts, ILogger<StepLabelSuggestionSkill> logger)
        : base(chatClient, prompts, logger)
    {
    }

    protected override string PromptName => "step-label-suggestion";
    protected override string SchemaName => "step_label_suggestion";

    // Called live, per step, potentially several times per inspect session — the one skill
    // where response latency is directly in the user's way while they're mid-flow.
    protected override string ReasoningEffort => "low";
    protected override int MaxCompletionTokens => 128;

    protected override string BuildUserMessage(StepLabelSuggestionInput input) => $"""
        <step>
        ActionType: {input.ActionType}
        Page: {input.PageName}
        DeterministicLabel: {input.DeterministicLabel}
        </step>

        <element>
        Tag: {input.TagName}
        Type: {input.ElementType ?? "(none)"}
        VisibleText: {input.VisibleText ?? "(none)"}
        Placeholder: {input.Placeholder ?? "(none)"}
        AriaLabel: {input.AriaLabel ?? "(none)"}
        AssociatedLabelText: {input.AssociatedLabelText ?? "(none)"}
        AncestorContext: {input.AncestorContext ?? "(none)"}
        </element>
        """;
}
