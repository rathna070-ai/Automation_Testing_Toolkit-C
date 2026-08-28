using System.Text.Json.Nodes;

namespace WebTestToolkit.Llm.Transport;

// One call to the model. SchemaName/Schema drive Groq's strict json_schema structured
// output — see https://console.groq.com/docs/structured-outputs. Schema must satisfy
// strict mode: every property required, additionalProperties:false on every object.
public record ChatRequest(
    IReadOnlyList<ChatMessage> Messages,
    string SchemaName,
    JsonNode Schema,
    string ReasoningEffort = "medium",
    double Temperature = 0.2,
    int MaxCompletionTokens = 1536);
