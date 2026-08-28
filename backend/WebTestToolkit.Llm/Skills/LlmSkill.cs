using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using WebTestToolkit.Llm.Transport;

namespace WebTestToolkit.Llm.Skills;

// One prompt + one schema + one typed result. Concrete skills are small: they supply the
// prompt/schema names and turn an input into the user message; everything else (calling
// the transport, deserializing the structured response, mapping failures) lives here once.
public abstract class LlmSkill<TInput, TOutput>
{
    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly IChatClient _chatClient;
    private readonly PromptLibrary _prompts;
    private readonly ILogger _logger;

    protected LlmSkill(IChatClient chatClient, PromptLibrary prompts, ILogger logger)
    {
        _chatClient = chatClient;
        _prompts = prompts;
        _logger = logger;
    }

    protected abstract string PromptName { get; }
    protected abstract string SchemaName { get; }
    protected virtual string ReasoningEffort => "medium";
    protected virtual double Temperature => 0.2;
    protected virtual int MaxCompletionTokens => 1536;

    protected abstract string BuildUserMessage(TInput input);

    public async Task<SkillResult<TOutput>> RunAsync(TInput input, CancellationToken ct = default)
    {
        var systemPrompt = _prompts.GetPrompt(PromptName);
        var schema = _prompts.GetSchema(SchemaName);
        var userMessage = BuildUserMessage(input);

        var request = new ChatRequest(
            Messages: [ChatMessage.System(systemPrompt), ChatMessage.User(userMessage)],
            SchemaName: SchemaName,
            Schema: schema,
            ReasoningEffort: ReasoningEffort,
            Temperature: Temperature,
            MaxCompletionTokens: MaxCompletionTokens);

        var chatResult = await _chatClient.CompleteAsync(request, ct);

        if (chatResult.Outcome != ChatOutcome.Success)
            return SkillResult<TOutput>.From(chatResult);

        try
        {
            var value = JsonSerializer.Deserialize<TOutput>(chatResult.Content!, DeserializeOptions);
            if (value is null)
                return SkillResult<TOutput>.SchemaMismatch("Model returned JSON null.");

            return SkillResult<TOutput>.Success(value, chatResult.Model!, chatResult.PromptTokens, chatResult.CompletionTokens);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "{Skill}: model response did not match the expected schema", GetType().Name);
            return SkillResult<TOutput>.SchemaMismatch($"Model response did not match the expected schema: {ex.Message}");
        }
    }
}
