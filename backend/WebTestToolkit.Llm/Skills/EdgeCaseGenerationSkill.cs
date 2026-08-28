using Microsoft.Extensions.Logging;
using WebTestToolkit.Llm.Transport;

namespace WebTestToolkit.Llm.Skills;

public class EdgeCaseGenerationSkill : LlmSkill<EdgeCaseGenerationInput, EdgeCaseGenerationOutput>
{
    public EdgeCaseGenerationSkill(IChatClient chatClient, PromptLibrary prompts, ILogger<EdgeCaseGenerationSkill> logger)
        : base(chatClient, prompts, logger)
    {
    }

    protected override string PromptName => "edge-case-generation";
    protected override string SchemaName => "edge_case_generation";

    // Reasoning about which alternate paths are worth testing, from structure alone (no
    // real values to anchor on) — more than a label-suggestion glance, less than codegen.
    protected override string ReasoningEffort => "medium";
    protected override int MaxCompletionTokens => 2048;

    protected override string BuildUserMessage(EdgeCaseGenerationInput input)
    {
        var steps = string.Join("\n", input.Steps.Select(s =>
            $"  {s.Order}. [{s.ActionType}] {s.Label} (page: {s.PageName}" +
            $"{(s.HasInputValue ? ", has a value" : "")}{(s.HasExpectedText ? ", has expected text" : "")})"));

        return $"""
            <flow>
            Name: {input.FlowName}
            StartUrl: {input.StartUrl}
            Steps:
            {steps}
            </flow>
            """;
    }
}
