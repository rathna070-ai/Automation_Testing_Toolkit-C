using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace WebTestToolkit.Llm.Transport;

// Talks to https://api.groq.com/openai/v1/chat/completions directly rather than through
// an SDK — Groq's whole surface here is one POST, and hand-rolling it means every byte on
// the wire is visible, which matters when something comes back a 400 you don't understand.
public class GroqClient : IChatClient
{
    private const string ChatCompletionsPath = "chat/completions";

    private readonly HttpClient _httpClient;
    private readonly IGroqSettingsProvider _settingsProvider;
    private readonly ILogger<GroqClient> _logger;

    public GroqClient(HttpClient httpClient, IGroqSettingsProvider settingsProvider, ILogger<GroqClient> logger)
    {
        _httpClient = httpClient;
        if (_httpClient.BaseAddress is null)
            _httpClient.BaseAddress = new Uri("https://api.groq.com/openai/v1/");
        _settingsProvider = settingsProvider;
        _logger = logger;
    }

    public async Task<ChatResult> CompleteAsync(ChatRequest request, CancellationToken ct = default)
    {
        var settings = await _settingsProvider.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            return ChatResult.Unavailable("No Groq API key is configured. Add one on the Settings page.");

        var wireRequest = new WireChatRequest(
            Model: settings.Model,
            Messages: request.Messages.Select(m => new WireMessage(m.Role, m.Content)).ToList(),
            Temperature: request.Temperature,
            MaxCompletionTokens: request.MaxCompletionTokens,
            ReasoningEffort: request.ReasoningEffort,
            ResponseFormat: new WireResponseFormat(
                "json_schema",
                new WireJsonSchema(request.SchemaName, Strict: true, request.Schema)));

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, ChatCompletionsPath)
        {
            Content = JsonContent.Create(wireRequest)
        };
        // Set per-request rather than on the shared HttpClient's default headers — the
        // client is pooled via IHttpClientFactory, and a key changed mid-flight (via the
        // Settings page) must never leak onto a request built with a different one.
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(httpRequest, ct);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return ChatResult.Error("Request to Groq timed out.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Groq request failed (network)");
            return ChatResult.Error($"Could not reach Groq: {ex.Message}");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                return await BuildErrorResultAsync(response, ct);

            WireChatResponse? parsed;
            try
            {
                parsed = await response.Content.ReadFromJsonAsync<WireChatResponse>(ct);
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
            {
                _logger.LogWarning(ex, "Could not parse Groq's response body");
                return ChatResult.Error("Could not parse Groq's response.");
            }

            var choice = parsed?.Choices?.FirstOrDefault();
            if (choice is null)
                return ChatResult.Error("Groq's response had no choices.");

            var promptTokens = parsed?.Usage?.PromptTokens ?? 0;
            var completionTokens = parsed?.Usage?.CompletionTokens ?? 0;

            if (choice.FinishReason == "length")
                return ChatResult.Truncated(
                    "The model's response was cut off before it finished — max_completion_tokens was too low for the requested reasoning effort.");

            return ChatResult.Success(choice.Message.Content, settings.Model, promptTokens, completionTokens);
        }
    }

    private async Task<ChatResult> BuildErrorResultAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string? message = null;
        try
        {
            var error = await response.Content.ReadFromJsonAsync<WireErrorResponse>(ct);
            message = error?.Error?.Message;
        }
        catch
        {
            // Body wasn't the expected error shape; fall through and report the status code alone.
        }

        var statusCode = (int)response.StatusCode;
        return response.StatusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized =>
                ChatResult.Unavailable("Groq rejected the API key (401). Check the key on the Settings page."),
            (System.Net.HttpStatusCode)429 =>
                ChatResult.Error("Groq rate limit exceeded (429). Try again shortly."),
            _ => ChatResult.Error(message is null
                ? $"Groq returned HTTP {statusCode}."
                : $"Groq returned HTTP {statusCode}: {message}")
        };
    }
}
