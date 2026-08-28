using System.Text.Json;
using Microsoft.Extensions.Logging;
using WebTestToolkit.Llm.Transport;

namespace WebTestToolkit.Llm.Skills;

public class TestCaseProseSkill : LlmSkill<TestCaseProseInput, TestCaseProseResult>
{
    public TestCaseProseSkill(IChatClient chatClient, PromptLibrary prompts, ILogger<TestCaseProseSkill> logger)
        : base(chatClient, prompts, logger)
    {
    }

    protected override string PromptName => "test-case-prose";
    protected override string SchemaName => "test_case_prose";

    // Wording, not correctness-critical code — cheap and fast is the right trade here.
    protected override string ReasoningEffort => "low";
    protected override int MaxCompletionTokens => 2048;

    protected override string BuildUserMessage(TestCaseProseInput input)
    {
        var steps = input.Steps.Select(s => new
        {
            number = s.Number,
            actionType = s.ActionType,
            label = s.Label,
            pageName = s.PageName,
            expectedText = s.ExpectedText
        });

        var stepsJson = JsonSerializer.Serialize(steps, new JsonSerializerOptions { WriteIndented = true });

        return $"""
            <flow>
            Name: {input.FlowName}
            Start URL: {input.StartUrl}
            </flow>

            <steps>
            {stepsJson}
            </steps>
            """;
    }
}
