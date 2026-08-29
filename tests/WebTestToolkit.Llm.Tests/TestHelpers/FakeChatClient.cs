using WebTestToolkit.Llm.Transport;

namespace WebTestToolkit.Llm.Tests.TestHelpers;

// Lets skill-level tests exercise LlmSkill's request building and response parsing without
// any network involved. Records the last request it was asked to complete so tests can
// assert on what the skill sent.
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
