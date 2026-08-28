using WebTestToolkit.Contracts.Models;

namespace WebTestToolkit.Api.Dtos;

public record GenerateFlowRequest(TestFlow Flow, bool UseLlm = true, int MaxRepairAttempts = 2);

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
    int TotalDurationMs)
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
        result.TotalDurationMs);
}
