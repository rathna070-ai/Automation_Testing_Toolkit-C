using Microsoft.Extensions.Logging;
using WebTestToolkit.Llm.Transport;

namespace WebTestToolkit.Llm.Skills;

public class ScriptGenerationSkill : LlmSkill<ScriptGenerationInput, GeneratedFileSet>
{
    public ScriptGenerationSkill(IChatClient chatClient, PromptLibrary prompts, ILogger<ScriptGenerationSkill> logger)
        : base(chatClient, prompts, logger)
    {
    }

    protected override string PromptName => "script-generation";
    protected override string SchemaName => "generated_test_files";

    // Open-ended authoring, and the user has consciously accepted a wait — worth the
    // reasoning budget. Codegen is the one place where correctness dominates latency.
    protected override string ReasoningEffort => "high";
    protected override double Temperature => 0.2;

    // Reasoning tokens count against the completion budget on gpt-oss. Too small a cap and
    // "high" effort burns it thinking, returning JSON cut in half.
    protected override int MaxCompletionTokens => 8192;

    protected override string BuildUserMessage(ScriptGenerationInput input) => BuildPrompt(input);

    // Shared with the repair skill so the repair turn replays a byte-identical original request.
    internal static string BuildPrompt(ScriptGenerationInput input)
    {
        var untrusted = string.IsNullOrWhiteSpace(input.UntrustedPageContent)
            ? ""
            : $"""

              <untrusted_page_content>
              {input.UntrustedPageContent}
              </untrusted_page_content>
              """;

        return $"""
            <task>
            Generate the Selenium + Reqnroll test files for the flow below.
            The flow is named "{input.FlowName}".
            </task>

            <project_file>
            {input.ProjectFile}
            </project_file>

            <support_api>
            {input.SupportApi}
            </support_api>

            <gold_sample>
            {input.GoldSample}
            </gold_sample>

            <existing_project_index>
            {input.ExistingProjectIndex}
            </existing_project_index>

            <flow>
            {input.FlowJson}
            </flow>
            {untrusted}

            <reference_implementation>
            {input.ReferenceImplementation}
            </reference_implementation>
            """;
    }
}
