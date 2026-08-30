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

    // "medium", not "high": high-effort reasoning burns a large share of the completion
    // budget thinking before it writes a single file, which pushes the combined
    // prompt+completion size closer to Groq's on_demand-tier request cap — a real 413 on a
    // large captured flow, not a hypothetical one. Medium is enough for this task; codegen
    // isn't open-ended research, it's "reproduce this reference implementation, written
    // better" (see the prompt's own framing).
    protected override string ReasoningEffort => "medium";
    protected override double Temperature => 0.2;

    // Lowered from 8192 alongside the effort drop, for the same reason: reasoning tokens
    // count against this budget on gpt-oss, so the two settings move together.
    //
    // Public because Groq meters a request as prompt + this reservation together, so
    // HybridTestCodeGenerator's pre-flight size check has to add the same number rather than
    // keep a second copy of it that can drift.
    public const int CompletionTokenBudget = 6000;

    protected override int MaxCompletionTokens => CompletionTokenBudget;

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
