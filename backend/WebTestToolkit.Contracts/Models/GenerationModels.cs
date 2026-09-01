namespace WebTestToolkit.Contracts.Models;

// Which path actually produced the files the user ended up with. Surfaced in the UI —
// silently shipping a fallback would hide a real signal about prompt quality.
public enum GenerationSource
{
    // The deterministic generator's output, validated and compiled. The only success value
    // since the LLM codegen path was retired — LlmVerified/LlmRepaired/DeterministicFallback
    // described a hybrid loop that no longer exists.
    Deterministic,
    // Even the deterministic output failed to compile — something is wrong with the project.
    Failed
}

public enum GenerationAttemptKind
{
    // One kind, kept as an enum rather than collapsed away: an attempt list is still the shape
    // the UI renders, and a future non-LLM attempt kind (a retry after clearing the sandbox,
    // say) would slot in here.
    Deterministic
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

// Blocking gates the build and the repair loop — the default, and what every issue before
// this enum existed already behaved as. Advisory rides along for the UI (a style nit, e.g.
// a duplicated interaction block) without spending a repair attempt arguing over something
// that isn't actually broken.
public enum IssueSeverity
{
    Blocking,
    Advisory
}

public record ValidationIssue(
    IssueSource Source,
    string Code,
    string? File,
    int? Line,
    string Message,
    IssueSeverity Severity = IssueSeverity.Blocking);

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

    public int TotalDurationMs => Attempts.Sum(a => a.DurationMs);
}
