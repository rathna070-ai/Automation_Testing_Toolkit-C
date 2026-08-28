using WebTestToolkit.Llm.Transport;

namespace WebTestToolkit.Llm.Skills;

// Domain-level mirror of ChatOutcome, plus the one failure mode that's specific to
// skills rather than transport: the model returned JSON that doesn't match the schema
// the caller asked for.
public enum SkillOutcome
{
    Success,
    Unavailable,
    Truncated,
    TransportError,
    SchemaMismatch
}

public class SkillResult<T>
{
    public required SkillOutcome Outcome { get; init; }
    public T? Value { get; init; }
    public string? Reason { get; init; }
    public string? Model { get; init; }
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }

    public bool IsSuccess => Outcome == SkillOutcome.Success;

    public static SkillResult<T> Success(T value, string model, int promptTokens, int completionTokens) => new()
    {
        Outcome = SkillOutcome.Success,
        Value = value,
        Model = model,
        PromptTokens = promptTokens,
        CompletionTokens = completionTokens
    };

    public static SkillResult<T> From(ChatResult chatResult)
    {
        var outcome = chatResult.Outcome switch
        {
            ChatOutcome.Unavailable => SkillOutcome.Unavailable,
            ChatOutcome.Truncated => SkillOutcome.Truncated,
            _ => SkillOutcome.TransportError
        };
        return new SkillResult<T> { Outcome = outcome, Reason = chatResult.Reason };
    }

    public static SkillResult<T> SchemaMismatch(string reason) => new()
    {
        Outcome = SkillOutcome.SchemaMismatch,
        Reason = reason
    };
}
