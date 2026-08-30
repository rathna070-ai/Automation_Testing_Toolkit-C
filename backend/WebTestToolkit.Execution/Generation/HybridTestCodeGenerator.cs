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
    private readonly GenerationResultCache _cache;
    private readonly int _maxRequestTokens;

    public HybridTestCodeGenerator(
        ScriptGenerationSkill generationSkill,
        ScriptRepairSkill repairSkill,
        ReferenceBundleBuilder bundleBuilder,
        BuildSandbox sandbox,
        GeneratedProjectWriter writer,
        ILogger<HybridTestCodeGenerator> logger,
        GenerationResultCache cache,
        int maxRequestTokens = DefaultMaxRequestTokens)
    {
        _generationSkill = generationSkill;
        _repairSkill = repairSkill;
        _bundleBuilder = bundleBuilder;
        _sandbox = sandbox;
        _writer = writer;
        _logger = logger;
        _cache = cache;
        _maxRequestTokens = maxRequestTokens;
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

        // Scoped to Preview (WriteToProject:false) only — a Generate click must always
        // actually write files, and a cache hit that skipped GeneratedProjectWriter.Write
        // would silently do nothing on a repeat click despite the user asking for exactly
        // that. Preview-twice-unchanged is also the case this exists for in the first place:
        // reviewing a result, tweaking nothing, and re-running it just to see it again.
        var cacheKey = options.WriteToProject ? null : GenerationResultCache.ComputeKey(bundle, options);
        if (cacheKey is not null && _cache.TryGet(cacheKey, out var cached))
        {
            progress?.Report("Using the cached result for this exact flow — nothing changed since the last run.");
            return new CodeGenerationResult
            {
                Source = cached.Source,
                Files = cached.Files,
                DeterministicFiles = cached.DeterministicFiles,
                Attempts = cached.Attempts,
                FallbackReason = cached.FallbackReason,
                WrittenPaths = cached.WrittenPaths,
                Cached = true
            };
        }

        // Groq bills the prompt *and* the reserved completion budget against one per-minute
        // allowance, so both have to be counted here or the check passes a request the API
        // then rejects. A request bigger than the whole per-minute allowance can never
        // succeed — retrying or waiting does not help — so skip locally rather than spend a
        // round trip on a guaranteed 413.
        var estimatedRequestTokens = EstimatePromptTokens(bundle) + ScriptGenerationSkill.CompletionTokenBudget;
        if (estimatedRequestTokens > _maxRequestTokens)
        {
            var skipReason =
                $"This flow needs about {estimatedRequestTokens} tokens per AI request (prompt plus the " +
                $"{ScriptGenerationSkill.CompletionTokenBudget}-token response reservation), over the Groq plan's " +
                $"{_maxRequestTokens}-tokens-per-minute allowance — capture a shorter flow or upgrade the Groq tier to use AI here. " +
                "Used the deterministic generator instead.";

            progress?.Report(skipReason);
            _logger.LogInformation(
                "Skipping AI generation for '{Flow}': estimated request size {Estimated} exceeds the {Limit}-token-per-minute allowance",
                flow.Name, estimatedRequestTokens, _maxRequestTokens);

            var skipResult = await FinishWithDeterministicAsync(
                flow, deterministicSet, attempts, GenerationSource.DeterministicFallback,
                skipReason, options, progress, ct);
            if (cacheKey is not null)
                _cache.Set(cacheKey, skipResult);
            return skipResult;
        }

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

            // Preserve any PageObjects method an earlier, differently-named flow's already-
            // generated Steps.cs still calls, that this flow's own output doesn't redefine —
            // otherwise this flow's generation can silently break that other one.
            var candidate = PageObjectMerger.MergeWithExisting(
                BuildCandidateFiles(previousResponse), SolutionPaths.GeneratedTestsDirectory());

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

                var llmResult = new CodeGenerationResult
                {
                    Source = isRepair ? GenerationSource.LlmRepaired : GenerationSource.LlmVerified,
                    Files = files,
                    DeterministicFiles = deterministicSet,
                    Attempts = attempts,
                    WrittenPaths = written
                };
                if (cacheKey is not null)
                    _cache.Set(cacheKey, llmResult);
                return llmResult;
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
        var fallbackResult = await FinishWithDeterministicAsync(
            flow, deterministicSet, attempts, GenerationSource.DeterministicFallback, reason, options, progress, ct);
        if (cacheKey is not null)
            _cache.Set(cacheKey, fallbackResult);
        return fallbackResult;
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
        var candidate = PageObjectMerger.MergeWithExisting(
            deterministicSet.ToDictionary(f => f.RelativePath, f => f.Content, StringComparer.Ordinal),
            SolutionPaths.GeneratedTestsDirectory());

        // The deterministic output goes through the same static gate the LLM output does.
        // It used to be compiled and nothing more, which left the *only* always-taken path as
        // the least-checked one: the checks that matter most here — WTT130/WTT131, ambiguous
        // or colliding Reqnroll bindings — describe a **runtime** failure, so a suite where
        // two flows define the same step pattern compiles perfectly and is written anyway.
        // That is not hypothetical; it is how two real committed flows ended up unable to run.
        progress?.Report("Checking the deterministic output…");
        var validationIssues = StaticValidator.Validate(
            ToValidatableFileSet(candidate), _bundleBuilder.ExistingBindings(flow.Name));

        progress?.Report("Compiling the deterministic output…");
        var build = await _sandbox.TryBuildAsync(candidate, PathsToClear(flow, deterministicSet), ct);
        stopwatch.Stop();

        var allIssues = validationIssues.Concat(build.Issues).ToList();
        attempts.Add(new GenerationAttempt(
            attempts.Count + 1, GenerationAttemptKind.Deterministic, null,
            build.Succeeded && !HasBlockingIssues(validationIssues),
            (int)stopwatch.ElapsedMilliseconds, 0, 0, allIssues));

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

        // The sandbox just compiled the *merged* candidate — writing deterministicSet here
        // instead would silently drop whatever PageObjectMerger just preserved, undoing the
        // fix by writing the very content the compile step proved was wrong.
        var mergedFiles = ToGeneratedFiles(candidate);
        var written = options.WriteToProject ? _writer.Write(mergedFiles) : [];
        progress?.Report($"Compiled. {mergedFiles.Count} files {(options.WriteToProject ? "written" : "ready")}.");

        // A blocking issue cannot suppress this result the way it can an LLM attempt: there is
        // no further path to fall back to, and returning nothing would leave the user with no
        // output at all. Surface it loudly on the reason and the attempt instead — the issue
        // is almost always something in the *existing* project (a step pattern another flow
        // already claims), which the user has to resolve by editing or removing that flow.
        var blocking = validationIssues.Where(i => i.Severity == IssueSeverity.Blocking).ToList();
        if (blocking.Count > 0)
        {
            var warning =
                $"Warning: the generated suite has {blocking.Count} problem(s) that will not show up as a " +
                "compile error but will fail when the tests run — " +
                string.Join("; ", blocking.Take(3).Select(i => $"{i.Code} {i.Message}"));
            progress?.Report(warning);
            fallbackReason = string.IsNullOrWhiteSpace(fallbackReason) ? warning : $"{fallbackReason} {warning}";
        }

        return new CodeGenerationResult
        {
            Source = source,
            Files = mergedFiles,
            DeterministicFiles = deterministicSet,
            Attempts = attempts,
            FallbackReason = fallbackReason,
            WrittenPaths = written
        };
    }

    // The inverse of BuildCandidateFiles: a plain file dictionary back into the shape
    // StaticValidator reads. The split matters — the validator's allowed-path rule covers only
    // .feature/.cs, because in the LLM flow locators arrive as *data* rather than as files, so
    // handing it the locator JSON as a file would trip WTT001 on output that is perfectly fine.
    private static GeneratedFileSet ToValidatableFileSet(IReadOnlyDictionary<string, string> candidate)
    {
        var files = new List<GeneratedFileDto>();
        var locators = new List<GeneratedLocatorDto>();

        foreach (var (path, content) in candidate.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var normalized = path.Replace('\\', '/');
            if (!normalized.EndsWith(".locators.json", StringComparison.OrdinalIgnoreCase))
            {
                files.Add(new GeneratedFileDto(normalized, content));
                continue;
            }

            var page = Path.GetFileName(normalized);
            page = page[..^".locators.json".Length];

            PageLocators? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<PageLocators>(content, LocatorReadOptions);
            }
            catch (JsonException)
            {
                continue; // malformed locator JSON is the compile/runtime step's problem, not this one
            }

            if (parsed is null)
                continue;

            foreach (var (key, entry) in parsed.Locators)
                locators.Add(new GeneratedLocatorDto(page, key, entry.Strategy, entry.Value, parsed.Url));
        }

        return new GeneratedFileSet(files, locators, Summary: "");
    }

    private static readonly JsonSerializerOptions LocatorReadOptions = new() { PropertyNameCaseInsensitive = true };

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

    // Groq's on_demand tier meters *tokens per minute*, counting a request's prompt plus its
    // reserved max_completion_tokens together. Observed verbatim from a real 15-step flow:
    //
    //   HTTP 413: Request too large for model `openai/gpt-oss-120b` ... service tier
    //   `on_demand` on tokens per minute (TPM): Limit 8000, Requested 17296
    //
    // Note what this is *not*, because both were assumed at different points and both are
    // wrong: it is not the model's context window (gpt-oss-120b holds 131,072 tokens), and it
    // is not a per-request byte cap. It is an account allowance, so the ceiling moves with the
    // plan rather than the model — and a single request over the whole per-minute budget can
    // never succeed no matter how long we wait.
    //
    // Worth knowing when tuning: ReferenceBundleBuilder's fixed parts (support API + gold
    // sample + csproj + the deterministic reference implementation) already cost ~4,000 tokens
    // before a single captured step is added, and CompletionTokenBudget reserves 6,000 more —
    // so on this tier the headroom for an actual flow is very small, and shrinking the bundle
    // is the only lever that widens it.
    // A default, not a law: this is an account allowance, so it moves with the Groq plan
    // rather than with the code. Overridable through the constructor for exactly that reason
    // — and because a tighter budget than the assembled bundle needs makes the AI path
    // unreachable, which the tests need to be able to opt out of.
    public const int DefaultMaxRequestTokens = 8_000;

    // No tokenizer dependency: chars/4 is the standard rough heuristic for English text and
    // is good enough to gate against a request that's an order of magnitude too large,
    // which is the actual failure mode this guards — not a precise budget.
    private static int EstimatePromptTokens(ScriptGenerationInput input)
    {
        var totalChars = input.FlowName.Length + input.FlowJson.Length + input.ProjectFile.Length +
            input.SupportApi.Length + input.GoldSample.Length + input.ReferenceImplementation.Length +
            input.ExistingProjectIndex.Length + (input.UntrustedPageContent?.Length ?? 0);

        return totalChars / 4;
    }
}
