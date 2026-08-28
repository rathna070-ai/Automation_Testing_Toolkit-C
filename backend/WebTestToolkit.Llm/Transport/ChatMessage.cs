namespace WebTestToolkit.Llm.Transport;

public record ChatMessage(string Role, string Content)
{
    public static ChatMessage System(string content) => new("system", content);
    public static ChatMessage User(string content) => new("user", content);

    // Used to replay the model's own prior output back to it during a repair turn, so it
    // can see its reasoning trail rather than being handed broken code cold.
    public static ChatMessage Assistant(string content) => new("assistant", content);
}
