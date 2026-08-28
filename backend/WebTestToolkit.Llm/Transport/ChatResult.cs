namespace WebTestToolkit.Llm.Transport;

public enum ChatOutcome
{
    Success,
    Unavailable,
    Truncated,
    TransportError
}

// The transport layer's result. Deliberately never throws for "no key configured" or
// "Groq returned an error" — those are ordinary outcomes a caller checks Outcome for,
// not exceptions. Reserve exceptions for programmer errors.
public class ChatResult
{
    public required ChatOutcome Outcome { get; init; }
    public string? Content { get; init; }
    public string? Reason { get; init; }
    public string? Model { get; init; }
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }

    public static ChatResult Success(string content, string model, int promptTokens, int completionTokens) => new()
    {
        Outcome = ChatOutcome.Success,
        Content = content,
        Model = model,
        PromptTokens = promptTokens,
        CompletionTokens = completionTokens
    };

    public static ChatResult Unavailable(string reason) => new()
    {
        Outcome = ChatOutcome.Unavailable,
        Reason = reason
    };

    public static ChatResult Truncated(string reason) => new()
    {
        Outcome = ChatOutcome.Truncated,
        Reason = reason
    };

    public static ChatResult Error(string reason) => new()
    {
        Outcome = ChatOutcome.TransportError,
        Reason = reason
    };
}
