using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WebTestToolkit.CodeGenerator;
using WebTestToolkit.Contracts.Models;
using WebTestToolkit.Llm.Skills;

namespace WebTestToolkit.Execution.Generation;

public record GenerationOptions(bool UseLlm = true, int MaxRepairAttempts = 2, bool WriteToProject = true);

// The generate -> validate -> repair -> fall back loop.
//
// The deterministic generator runs first and always: it costs nothing, it is the
// reference implementation shown to the model, and it is the guaranteed-compiling output
// if every LLM attempt fails. That is what makes the LLM path safe to ship — the worst
// case is the quality we already had, never a broken project.
public class HybridTestCodeGenerator
{
    private readonly ScriptGenerationSkill _generationSkill;
    private readonly ScriptRepairSkill _repairSkill;
    private readonly ReferenceBundleBuilder _bundleBuilder;
    private readonly BuildSandbox _sandbox;
    private readonly GeneratedProjectWriter _writer;
    private readonly ILogger<HybridTestCodeGenerator> _logger;

    public HybridTestCodeGenerator(
        ScriptGenerationSkill generationSkill,
        ScriptRepairSkill repairSkill,
        ReferenceBundleBuilder bundleBuilder,
        BuildSandbox sandbox,
        GeneratedProjectWriter writer,
        ILogger<HybridTestCodeGenerator> logger)
    {
        _generationSkill = generationSkill;
        _repairSkill = repairSkill;
        _bundleBuilder = bundleBuilder;
        _sandbox = sandbox;
        _writer = writer;
        _logger = logger;
    }

    public async Task<CodeGenerationResult> GenerateAsync(
        TestFlow flow,
        GenerationOptions options,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var attempts = new List<GenerationAttempt>();

        // Step 0 — deterministic. Always runs, always succeeds, and is rendered in the UI
        // immediately so the user reads real code during the LLM round-trip.
        var deterministicFiles = TestFlowCodeGenerator.Generate(flow);
        var deterministicSet = ToGeneratedFiles(deterministicFiles);
        progress?.Report($"Deterministic baseline ready ({deterministicFiles.Count} files)");

        if (!options.UseLlm)
        {
            return await FinishWithDeterministicAsync(
                flow, deterministicSet, attempts, GenerationSource.Deterministic, fallbackReason: null, options, progress, ct);
        }

        var bundle = _bundleBuilder.Build(flow, deterministicFiles);
        var existingBindings = _bundleBuilder.ExistingBindings(flow.Name);

        GeneratedFileSet? previousResponse = null;
        string? previousResponseJson = null;
        IReadOnlyList<ValidationIssue> lastIssues = [];

        var totalAttempts = 1 + Math.Max(0, options.MaxRepairAttempts);
        for (var attemptNumber = 1; attemptNumber <= totalAttempts; attemptNumber++)
        {
            var isRepair = attemptNumber > 1;
            var stopwatch = Stopwatch.StartNew();

            progress?.Report(isRepair
                ? $"Asking the model to fix {lastIssues.Count} problem(s) (attempt {attemptNumber})…"
                : "Asking the model to write the tests…");

            SkillResult<GeneratedFileSet> skillResult;
            if (!isRepair)
            {
                skillResult = await _generationSkill.RunAsync(bundle, ct);
            }
            else
            {
                var issuesReport = MsBuildErrorParser.FormatForPrompt(lastIssues, ToDictionary(previousResponse!));
                skillResult = await _repairSkill.RunAsync(
                    new ScriptRepairInput(bundle, previousResponseJson!, issuesReport), ct);
            }

            if (!skillResult.IsSuccess)
            {
                stopwatch.Stop();
                attempts.Add(new GenerationAttempt(
                    attemptNumber,
                    isRepair ? GenerationAttemptKind.LlmRepair : GenerationAttemptKind.LlmInitial,
                    skillResult.Model, false, (int)stopwatch.ElapsedMilliseconds,
                    skillResult.PromptTokens, skillResult.CompletionTokens,
                    [new ValidationIssue(IssueSource.Transport, skillResult.Outcome.ToString(), null, null, skillResult.Reason ?? "The model call did not succeed.")]));

                // Transport failures (no key, rate limit, network) won't fix themselves on
                // a retry of the same shape — stop and fall back rather than burning attempts.
                break;
            }

            previousResponse = skillResult.Value!;
            previousResponseJson = JsonSerializer.Serialize(previousResponse);

            var candidate = BuildCandidateFiles(previousResponse);

            progress?.Report("Checking the generated code…");
            var issues = StaticValidator.Validate(previousResponse, existingBindings).ToList();

            // Advisory issues (a style nit like a duplicated interaction block) ride along
            // for the UI but must never gate the build or burn a repair attempt arguing with
            // the model over something that isn't actually broken.
            if (!HasBlockingIssues(issues))
            {
                progress?.Report($"Compiling (attempt {attemptNumber})…");
                var build = await _sandbox.TryBuildAsync(candidate, PathsToClear(flow, previousResponse), ct);
                if (!build.Succeeded)
                    issues.AddRange(build.Issues);
            }

            stopwatch.Stop();
            var succeeded = !HasBlockingIssues(issues);

            attempts.Add(new GenerationAttempt(
                attemptNumber,
                isRepair ? GenerationAttemptKind.LlmRepair : GenerationAttemptKind.LlmInitial,
                skillResult.Model, succeeded, (int)stopwatch.ElapsedMilliseconds,
                skillResult.PromptTokens, skillResult.CompletionTokens, issues));

            if (succeeded)
            {
                var files = ToGeneratedFiles(candidate);
                var written = options.WriteToProject ? _writer.Write(files) : [];
                progress?.Report($"Compiled. {files.Count} files {(options.WriteToProject ? "written" : "ready")}.");

                return new CodeGenerationResult
                {
                    Source = isRepair ? GenerationSource.LlmRepaired : GenerationSource.LlmVerified,
                    Files = files,
                    DeterministicFiles = deterministicSet,
                    Attempts = attempts,
                    WrittenPaths = written
                };
            }

            lastIssues = issues;
            _logger.LogInformation("Generation attempt {Attempt} for '{Flow}' failed with {Count} issue(s)",
                attemptNumber, flow.Name, issues.Count);
        }

        var reason = lastIssues.Count > 0
            ? $"The AI-generated code did not pass after {attempts.Count} attempt(s). Last problems: " +
              string.Join("; ", lastIssues.Take(3).Select(i => $"{i.Code} {i.Message}"))
            : attempts.LastOrDefault()?.Issues.FirstOrDefault()?.Message
              ?? "The model call did not succeed.";

        progress?.Report("Falling back to the deterministic generator…");
        return await FinishWithDeterministicAsync(
            flow, deterministicSet, attempts, GenerationSource.DeterministicFallback, reason, options, progress, ct);
    }

    // Even the fallback gets compiled before it's written — if this fails, the problem is
    // the project itself (a pre-existing duplicate class, say), not the candidate, and
    // saying so plainly is more useful than writing files that don't build.
    private async Task<CodeGenerationResult> FinishWithDeterministicAsync(
        TestFlow flow,
        IReadOnlyList<GeneratedFile> deterministicSet,
        List<GenerationAttempt> attempts,
        GenerationSource source,
        string? fallbackReason,
        GenerationOptions options,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var candidate = deterministicSet.ToDictionary(f => f.RelativePath, f => f.Content, StringComparer.Ordinal);

        progress?.Report("Compiling the deterministic output…");
        var build = await _sandbox.TryBuildAsync(candidate, PathsToClear(flow, deterministicSet), ct);
        stopwatch.Stop();

        attempts.Add(new GenerationAttempt(
            attempts.Count + 1, GenerationAttemptKind.Deterministic, null,
            build.Succeeded, (int)stopwatch.ElapsedMilliseconds, 0, 0, build.Issues));

        if (!build.Succeeded)
        {
            return new CodeGenerationResult
            {
                Source = GenerationSource.Failed,
                Files = [],
                DeterministicFiles = deterministicSet,
                Attempts = attempts,
                FallbackReason = "The deterministic output did not compile either — this usually means something in the existing test project is broken, not the generated flow."
            };
        }

        var written = options.WriteToProject ? _writer.Write(deterministicSet) : [];
        progress?.Report($"Compiled. {deterministicSet.Count} files {(options.WriteToProject ? "written" : "ready")}.");

        return new CodeGenerationResult
        {
            Source = source,
            Files = deterministicSet,
            DeterministicFiles = deterministicSet,
            Attempts = attempts,
            FallbackReason = fallbackReason,
            WrittenPaths = written
        };
    }

    // The model's .cs/.feature files, plus locator JSON we serialize ourselves from its
    // `locators` array. Any .locators.json the model tried to author is discarded.
    private static Dictionary<string, string> BuildCandidateFiles(GeneratedFileSet fileSet)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in fileSet.Files)
        {
            var normalized = file.Path.Replace('\\', '/');
            if (normalized.EndsWith(".locators.json", StringComparison.OrdinalIgnoreCase))
                continue;
            files[normalized] = file.Content;
        }

        foreach (var (path, content) in LocatorFileBuilder.Build(fileSet.Locators))
            files[path] = content;

        return files;
    }

    private static IReadOnlyList<GeneratedFile> ToGeneratedFiles(IReadOnlyDictionary<string, string> files) =>
        files.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new GeneratedFile(kv.Key, kv.Value))
            .ToList();

    private static Dictionary<string, string> ToDictionary(GeneratedFileSet fileSet) =>
        fileSet.Files.ToDictionary(f => f.Path.Replace('\\', '/'), f => f.Content, StringComparer.Ordinal);

    // Stale files from a previous generation of the same flow would otherwise linger in the
    // sandbox and surface as phantom duplicate-binding errors.
    private static List<string> PathsToClear(TestFlow flow, GeneratedFileSet candidate) =>
        PathsToClear(flow, candidate.Files.Select(f => new GeneratedFile(f.Path.Replace('\\', '/'), f.Content)).ToList());

    private static List<string> PathsToClear(TestFlow flow, IReadOnlyList<GeneratedFile> candidate)
    {
        // Same sanitized identifier TestFlowCodeGenerator uses for the deterministic
        // baseline's own file names — flow.Name is free text a user typed, not a path.
        var className = Naming.ToPascalCaseIdentifier(flow.Name);

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            $"Features/{className}.feature",
            $"Steps/{className}Steps.cs"
        };

        foreach (var file in candidate)
            paths.Add(file.RelativePath.Replace('\\', '/'));

        return paths.ToList();
    }

    private static bool HasBlockingIssues(IReadOnlyList<ValidationIssue> issues) =>
        issues.Any(i => i.Severity == IssueSeverity.Blocking);
}
