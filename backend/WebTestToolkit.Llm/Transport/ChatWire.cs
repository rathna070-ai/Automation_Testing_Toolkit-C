using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace WebTestToolkit.Llm.Transport;

// Wire shapes for Groq's OpenAI-compatible chat/completions endpoint. Internal — callers
// only ever see ChatRequest/ChatResult.

internal record WireMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);

internal record WireResponseFormat(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("json_schema")] WireJsonSchema JsonSchema);

internal record WireJsonSchema(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("strict")] bool Strict,
    [property: JsonPropertyName("schema")] JsonNode Schema);

internal record WireChatRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] IReadOnlyList<WireMessage> Messages,
    [property: JsonPropertyName("temperature")] double Temperature,
    [property: JsonPropertyName("max_completion_tokens")] int MaxCompletionTokens,
    [property: JsonPropertyName("reasoning_effort")] string ReasoningEffort,
    [property: JsonPropertyName("response_format")] WireResponseFormat ResponseFormat);

internal record WireChoice(
    [property: JsonPropertyName("message")] WireMessage Message,
    [property: JsonPropertyName("finish_reason")] string? FinishReason);

internal record WireUsage(
    [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
    [property: JsonPropertyName("completion_tokens")] int CompletionTokens);

internal record WireChatResponse(
    [property: JsonPropertyName("choices")] IReadOnlyList<WireChoice>? Choices,
    [property: JsonPropertyName("usage")] WireUsage? Usage);

internal record WireErrorDetail(
    [property: JsonPropertyName("message")] string? Message);

internal record WireErrorResponse(
    [property: JsonPropertyName("error")] WireErrorDetail? Error);
