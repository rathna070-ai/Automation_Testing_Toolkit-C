using WebTestToolkit.Llm.Transport;

namespace WebTestToolkit.Execution.Tests;

// Returns a scripted sequence of responses, so a test can stage "first attempt returns
// code that won't compile, second attempt fixes it" and drive the repair loop offline.
public class SequencedChatClient : IChatClient
{
    private readonly Queue<ChatResult> _responses;

    public SequencedChatClient(params ChatResult[] responses)
    {
        _responses = new Queue<ChatResult>(responses);
    }

    public List<ChatRequest> Requests { get; } = [];

    public Task<ChatResult> CompleteAsync(ChatRequest request, CancellationToken ct = default)
    {
        Requests.Add(request);
        var result = _responses.Count > 0
            ? _responses.Dequeue()
            : ChatResult.Error("No more scripted responses.");
        return Task.FromResult(result);
    }
}
