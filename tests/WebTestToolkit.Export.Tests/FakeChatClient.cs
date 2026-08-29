using WebTestToolkit.Llm.Transport;

namespace WebTestToolkit.Export.Tests;

// Mirrors WebTestToolkit.Llm.Tests/TestHelpers/FakeChatClient.cs. Kept as its own small copy
// rather than a cross-test-project reference — each test project stays self-contained, and
// this is the same ~15 lines either way.
public class FakeChatClient : IChatClient
{
    private readonly ChatResult _result;

    public FakeChatClient(ChatResult result)
    {
        _result = result;
    }

    public ChatRequest? LastRequest { get; private set; }

    public Task<ChatResult> CompleteAsync(ChatRequest request, CancellationToken ct = default)
    {
        LastRequest = request;
        return Task.FromResult(_result);
    }
}
