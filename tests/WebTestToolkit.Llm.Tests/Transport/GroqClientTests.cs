using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using WebTestToolkit.Llm.Tests.TestHelpers;
using WebTestToolkit.Llm.Transport;

namespace WebTestToolkit.Llm.Tests.Transport;

public class GroqClientTests
{
    private static ChatRequest SampleRequest() => new(
        Messages: [ChatMessage.System("system prompt"), ChatMessage.User("user message")],
        SchemaName: "sample_schema",
        Schema: JsonNode.Parse("""{"type":"object","additionalProperties":false,"required":[],"properties":{}}""")!,
        ReasoningEffort: "low",
        Temperature: 0.1,
        MaxCompletionTokens: 256);

    private static GroqClient BuildClient(StubHttpMessageHandler handler, string? apiKey)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.groq.com/openai/v1/") };
        var settingsProvider = apiKey is null
            ? StaticGroqSettingsProvider.NoKey()
            : StaticGroqSettingsProvider.WithKey(apiKey);
        return new GroqClient(httpClient, settingsProvider, NullLogger<GroqClient>.Instance);
    }

    [Test]
    public async Task NoApiKey_ReturnsUnavailable_WithoutMakingHttpCall()
    {
        var handler = StubHttpMessageHandler.ThatThrowsIfCalled();
        var client = BuildClient(handler, apiKey: null);

        var result = await client.CompleteAsync(SampleRequest());

        Assert.That(result.Outcome, Is.EqualTo(ChatOutcome.Unavailable));
        Assert.That(result.Reason, Does.Contain("API key"));
    }

    [Test]
    public async Task SuccessResponse_ParsesContentModelAndTokenUsage()
    {
        var body = """
            {
              "choices": [{"message":{"role":"assistant","content":"{\"ok\":true}"},"finish_reason":"stop"}],
              "usage": {"prompt_tokens": 42, "completion_tokens": 7}
            }
            """;
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, body);
        var client = BuildClient(handler, apiKey: "test-key");

        var result = await client.CompleteAsync(SampleRequest());

        Assert.That(result.Outcome, Is.EqualTo(ChatOutcome.Success));
        Assert.That(result.Content, Is.EqualTo("{\"ok\":true}"));
        Assert.That(result.Model, Is.EqualTo("openai/gpt-oss-120b"));
        Assert.That(result.PromptTokens, Is.EqualTo(42));
        Assert.That(result.CompletionTokens, Is.EqualTo(7));
    }

    [Test]
    public async Task RequestBody_SendsStrictJsonSchemaModelAndAuth()
    {
        var body = """{"choices":[{"message":{"role":"assistant","content":"{}"},"finish_reason":"stop"}],"usage":{"prompt_tokens":1,"completion_tokens":1}}""";
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, body);
        var client = BuildClient(handler, apiKey: "secret-key");

        await client.CompleteAsync(SampleRequest());

        Assert.That(handler.LastRequest!.Headers.Authorization!.ToString(), Is.EqualTo("Bearer secret-key"));

        var sentJson = JsonNode.Parse(handler.LastRequestBody!)!;
        Assert.That(sentJson["model"]!.GetValue<string>(), Is.EqualTo("openai/gpt-oss-120b"));
        Assert.That(sentJson["reasoning_effort"]!.GetValue<string>(), Is.EqualTo("low"));
        Assert.That(sentJson["response_format"]!["type"]!.GetValue<string>(), Is.EqualTo("json_schema"));
        Assert.That(sentJson["response_format"]!["json_schema"]!["strict"]!.GetValue<bool>(), Is.True);
        Assert.That(sentJson["response_format"]!["json_schema"]!["name"]!.GetValue<string>(), Is.EqualTo("sample_schema"));
    }

    [Test]
    public async Task Unauthorized401_ReturnsUnavailableNotAnException()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.Unauthorized, """{"error":{"message":"Invalid API Key"}}""");
        var client = BuildClient(handler, apiKey: "bad-key");

        var result = await client.CompleteAsync(SampleRequest());

        Assert.That(result.Outcome, Is.EqualTo(ChatOutcome.Unavailable));
    }

    [Test]
    public async Task RateLimited429_ReturnsTransportError()
    {
        var handler = StubHttpMessageHandler.Returning((HttpStatusCode)429, """{"error":{"message":"rate limit"}}""");
        var client = BuildClient(handler, apiKey: "test-key");

        var result = await client.CompleteAsync(SampleRequest());

        Assert.That(result.Outcome, Is.EqualTo(ChatOutcome.TransportError));
        Assert.That(result.Reason, Does.Contain("rate limit").IgnoreCase);
    }

    [Test]
    public async Task FinishReasonLength_ReturnsTruncated()
    {
        var body = """{"choices":[{"message":{"role":"assistant","content":"{\"partial"},"finish_reason":"length"}],"usage":{"prompt_tokens":1,"completion_tokens":256}}""";
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, body);
        var client = BuildClient(handler, apiKey: "test-key");

        var result = await client.CompleteAsync(SampleRequest());

        Assert.That(result.Outcome, Is.EqualTo(ChatOutcome.Truncated));
    }

    [Test]
    public async Task MalformedResponseBody_ReturnsTransportErrorNotAnException()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, "this is not json");
        var client = BuildClient(handler, apiKey: "test-key");

        var result = await client.CompleteAsync(SampleRequest());

        Assert.That(result.Outcome, Is.EqualTo(ChatOutcome.TransportError));
    }
}
