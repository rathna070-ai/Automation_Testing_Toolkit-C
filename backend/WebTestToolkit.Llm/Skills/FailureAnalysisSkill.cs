using Microsoft.Extensions.Logging;
using WebTestToolkit.Contracts.Models;
using WebTestToolkit.Llm.Transport;

namespace WebTestToolkit.Llm.Skills;

public class FailureAnalysisSkill : LlmSkill<ScenarioResult, FailureAnalysis>
{
    public FailureAnalysisSkill(IChatClient chatClient, PromptLibrary prompts, ILogger<FailureAnalysisSkill> logger)
        : base(chatClient, prompts, logger)
    {
    }

    protected override string PromptName => "failure-analysis";
    protected override string SchemaName => "failure_analysis";
    protected override string ReasoningEffort => "medium";
    protected override int MaxCompletionTokens => 1024;

    protected override string BuildUserMessage(ScenarioResult input)
    {
        var stackTrace = Truncate(input.StackTrace, 2000);
        return $"""
            <scenario>
            Feature: {input.FeatureName}
            Scenario: {input.ScenarioName}
            </scenario>
            <error>
            {input.ErrorMessage ?? "(no error message captured)"}
            </error>
            <stack_trace>
            {stackTrace ?? "(no stack trace captured)"}
            </stack_trace>
            """;
    }

    private static string? Truncate(string? text, int maxLength)
    {
        if (text is null || text.Length <= maxLength)
            return text;
        return text[..maxLength] + "\n… (truncated)";
    }
}
