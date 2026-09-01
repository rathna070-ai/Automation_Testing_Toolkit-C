using WebTestToolkit.Contracts.Models;

namespace WebTestToolkit.Api.Dtos;

public record LlmStatusResponse(bool ApiKeyConfigured, string Model);

// Available=false is an ordinary response, not an error — callers should check it rather
// than treat a non-2xx as the only failure signal.
public record AnalyzeFailureResponse(bool Available, FailureAnalysis? Analysis, string? UnavailableReason);

// Same Available/UnavailableReason shape as the single-scenario response: "no key configured",
// "no run yet" and "the last run passed" are all ordinary outcomes the caller renders, not
// HTTP errors.
public record AnalyzeRunResponse(bool Available, RunFailureAnalysis? Analysis, string? UnavailableReason);
