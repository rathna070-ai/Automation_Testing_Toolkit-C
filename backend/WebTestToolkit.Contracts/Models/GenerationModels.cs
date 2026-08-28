namespace WebTestToolkit.Contracts.Models;

// Which path actually produced the files the user ended up with. Surfaced in the UI —
// silently shipping a fallback would hide a real signal about prompt quality.
public enum GenerationSource
{
    // LLM was off or unconfigured; deterministic output was the intent, not a fallback.
    Deterministic,
    // LLM's first attempt validated and compiled.
    LlmVerified,
    // LLM's output compiled only after one or more repair round-trips.
    LlmRepaired,
    // Every LLM attempt failed; we shipped the deterministic output instead.
    DeterministicFallback,
    // Even the deterministic output failed to compile — something is wrong with the project.
    Failed
}

public enum GenerationAttemptKind
{
    Deterministic,
    LlmInitial,
    LlmRepair
}

public enum IssueSource
{
    // Our own pre-flight checks (path whitelist, hardcoded-locator ban, binding conflicts).
    Static,
    // The C# compiler, via `dotnet build`.
    Compiler,
    // The model returned something that wasn't usable at all.
    Transport
}

public record ValidationIssue(
    IssueSource Source,
    string Code,
    string? File,
    int? Line,
    string Message);

public record GeneratedFile(string RelativePath, string Content);

public record GenerationAttempt(
    int Number,
    GenerationAttemptKind Kind,
    string? Model,
    bool Succeeded,
    int DurationMs,
    int PromptTokens,
    int CompletionTokens,
    IReadOnlyList<ValidationIssue> Issues);

public class CodeGenerationResult
{
    public required GenerationSource Source { get; init; }
    public required IReadOnlyList<GeneratedFile> Files { get; init; }

    // Always populated, whichever path won — this is what powers the
    // "compare with the deterministic version" view in the UI.
    public required IReadOnlyList<GeneratedFile> DeterministicFiles { get; init; }

    public required IReadOnlyList<GenerationAttempt> Attempts { get; init; }
    public string? FallbackReason { get; init; }
    public IReadOnlyList<string> WrittenPaths { get; init; } = [];

    public int TotalPromptTokens => Attempts.Sum(a => a.PromptTokens);
    public int TotalCompletionTokens => Attempts.Sum(a => a.CompletionTokens);
    public int TotalDurationMs => Attempts.Sum(a => a.DurationMs);
}
