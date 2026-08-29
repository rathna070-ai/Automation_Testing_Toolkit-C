using System.Net;
using System.Text;

namespace WebTestToolkit.Llm.Tests.TestHelpers;

public class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }

    public static StubHttpMessageHandler ThatThrowsIfCalled() =>
        new(_ => throw new InvalidOperationException("HTTP call made when none was expected."));

    public static StubHttpMessageHandler Returning(HttpStatusCode statusCode, string jsonBody) =>
        new(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
        });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        LastRequest = request;
        LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        return _responder(request);
    }
}
