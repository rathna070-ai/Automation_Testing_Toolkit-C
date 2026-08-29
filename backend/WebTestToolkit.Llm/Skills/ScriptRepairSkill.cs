using Microsoft.Extensions.Logging;
using WebTestToolkit.Llm.Transport;

namespace WebTestToolkit.Llm.Skills;

// A genuine multi-turn continuation rather than "here is broken code, fix it": the model
// sees the original request and its own previous answer, so it can reason from its own
// trail. That repairs meaningfully better than restating the task cold.
public class ScriptRepairSkill : LlmSkill<ScriptRepairInput, GeneratedFileSet>
{
    public ScriptRepairSkill(IChatClient chatClient, PromptLibrary prompts, ILogger<ScriptRepairSkill> logger)
        : base(chatClient, prompts, logger)
    {
    }

    protected override string PromptName => "script-repair";
    protected override string SchemaName => "generated_test_files";
    // Matches ScriptGenerationSkill's own medium/6000 pairing, for the same reason — and
    // this call is the more likely one to hit a Groq request-size limit in practice, since
    // BuildMessages below replays the *entire* original prompt plus the model's full prior
    // response on top of the new repair turn.
    protected override string ReasoningEffort => "medium";
    protected override int MaxCompletionTokens => 6000;

    // Slightly warmer than generation: at near-zero temperature a model that misreads an
    // error tends to return the identical wrong fix again, burning the retry budget.
    protected override double Temperature => 0.35;

    protected override string BuildUserMessage(ScriptRepairInput input) => BuildRepairTurn(input);

    protected override IReadOnlyList<ChatMessage> BuildMessages(ScriptRepairInput input, string systemPrompt) =>
    [
        ChatMessage.System(systemPrompt),
        ChatMessage.User(ScriptGenerationSkill.BuildPrompt(input.Original)),
        ChatMessage.Assistant(input.PreviousResponseJson),
        ChatMessage.User(BuildRepairTurn(input))
    ];

    private static string BuildRepairTurn(ScriptRepairInput input) => $"""
        The files you produced did not pass validation. Fix them and return the COMPLETE
        corrected file set again in the same schema — not a patch, not only the changed files.

        Problems found (paths are relative to the test project root):

        {input.IssuesReport}

        Reminders: you may only use the API surface shown in the original message, you may
        only write Features/*.feature, Steps/*Steps.cs and PageObjects/*.cs, and you must
        never construct a `By` in C#. Make the minimal change that fixes these problems.
        """;
}
