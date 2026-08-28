using WebTestToolkit.Contracts.Models;

namespace WebTestToolkit.Api.Dtos;

public record LlmStatusResponse(bool ApiKeyConfigured, string Model);

// Available=false is an ordinary response, not an error — callers should check it rather
// than treat a non-2xx as the only failure signal.
public record AnalyzeFailureResponse(bool Available, FailureAnalysis? Analysis, string? UnavailableReason);
