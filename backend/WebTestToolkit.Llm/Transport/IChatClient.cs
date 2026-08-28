namespace WebTestToolkit.Llm.Transport;

// The seam between skills and the real Groq HTTP call. Swapping in a fake here is what
// makes skill logic (and the whole no-API-key path) testable without a network or a key.
public interface IChatClient
{
    Task<ChatResult> CompleteAsync(ChatRequest request, CancellationToken ct = default);
}
