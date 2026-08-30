using WebTestToolkit.Contracts.Models;

namespace WebTestToolkit.Api.Dtos;

public record GenerateFlowRequest(TestFlow Flow, bool UseLlm = true, int MaxRepairAttempts = 2);

// Suggestions only — nothing here is written or compiled. Each option's Flow is already a
// complete, independently-generatable TestFlow (see EdgeCaseFlowBuilder); accepting one is
// just posting it to /api/flows/preview or /api/flows/generate like any other flow, so this
// review step never needs its own write/compile machinery.
public record EdgeCaseRequest(TestFlow Flow);

public record EdgeCaseOptionDto(string NameSuffix, string Title, string Rationale, TestFlow Flow);

public record EdgeCaseResponse(bool Available, IReadOnlyList<EdgeCaseOptionDto> EdgeCases, string? UnavailableReason);

public record GenerationAttemptDto(
    int Number,
    string Kind,
    string? Model,
    bool Succeeded,
    int DurationMs,
    int PromptTokens,
    int CompletionTokens,
    IReadOnlyList<ValidationIssue> Issues);

public record GenerateFlowResponse(
    string Source,
    IReadOnlyList<GeneratedFile> Files,
    IReadOnlyList<GeneratedFile> DeterministicFiles,
    IReadOnlyList<GenerationAttemptDto> Attempts,
    string? FallbackReason,
    IReadOnlyList<string> WrittenPaths,
    int TotalPromptTokens,
    int TotalCompletionTokens,
    int TotalDurationMs,
    bool Cached)
{
    public static GenerateFlowResponse From(CodeGenerationResult result) => new(
        result.Source.ToString(),
        result.Files,
        result.DeterministicFiles,
        result.Attempts.Select(a => new GenerationAttemptDto(
            a.Number, a.Kind.ToString(), a.Model, a.Succeeded, a.DurationMs,
            a.PromptTokens, a.CompletionTokens, a.Issues)).ToList(),
        result.FallbackReason,
        result.WrittenPaths,
        result.TotalPromptTokens,
        result.TotalCompletionTokens,
        result.TotalDurationMs,
        result.Cached);
}
