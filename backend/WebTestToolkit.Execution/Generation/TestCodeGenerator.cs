using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WebTestToolkit.CodeGenerator;
using WebTestToolkit.Contracts.Models;

namespace WebTestToolkit.Execution.Generation;

public record GenerationOptions(bool WriteToProject = true);

// generate -> merge -> validate -> compile -> write.
//
// This was HybridTestCodeGenerator: a deterministic baseline, then an LLM attempt, then repair
// turns, then a fall back to the baseline. The LLM half is gone (see docs/ARCHITECTURE.md) —
// the toolkit's rule is now that the LLM produces data a human reviews or a deterministic
// generator consumes, and never code that ships. What remains is the path that was already
// producing every file this project has ever written.
//
// The gate did not go with it. StaticValidator still runs here, on deterministic output,
// because the checks that matter most describe *runtime* failures a compiler cannot see: a
// suite where two flows claim the same Reqnroll step pattern compiles perfectly and then
// cannot run. The template emitter needs policing for the same reason a model did.
public class TestCodeGenerator
{
    private readonly BuildSandbox _sandbox;
    private readonly GeneratedProjectWriter _writer;
    private readonly ILogger<TestCodeGenerator> _logger;

    public TestCodeGenerator(
        BuildSandbox sandbox,
        GeneratedProjectWriter writer,
        ILogger<TestCodeGenerator> logger)
    {
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
        var stopwatch = Stopwatch.StartNew();

        var generated = TestFlowCodeGenerator.Generate(flow);
        var generatedSet = ToGeneratedFiles(generated);
        progress?.Report($"Generated {generated.Count} files.");

        // Preserve any PageObjects method or locator entry an earlier, differently-named flow's
        // already-written Steps.cs still calls but this flow's own output does not redefine —
        // otherwise generating one flow silently breaks another that shares a page.
        var candidate = PageObjectMerger.MergeWithExisting(
            generatedSet.ToDictionary(f => f.RelativePath, f => f.Content, StringComparer.Ordinal),
            SolutionPaths.GeneratedTestsDirectory());

        progress?.Report("Checking the generated code…");
        var validationIssues = StaticValidator.Validate(
            ToValidatableFileSet(candidate), BindingIndex.ExistingBindings(flow.Name));

        progress?.Report("Compiling…");
        var build = await _sandbox.TryBuildAsync(candidate, PathsToClear(flow, generatedSet), ct);
        stopwatch.Stop();

        var allIssues = validationIssues.Concat(build.Issues).ToList();
        var attempts = new List<GenerationAttempt>
        {
            new(1, GenerationAttemptKind.Deterministic, null,
                build.Succeeded && !HasBlockingIssues(validationIssues),
                (int)stopwatch.ElapsedMilliseconds, 0, 0, allIssues)
        };

        if (!build.Succeeded)
        {
            _logger.LogWarning("Generated output for '{Flow}' did not compile", flow.Name);
            return new CodeGenerationResult
            {
                Source = GenerationSource.Failed,
                Files = [],
                DeterministicFiles = generatedSet,
                Attempts = attempts,
                FallbackReason = "The generated output did not compile — this usually means something in the existing test project is broken, not this flow."
            };
        }

        // The sandbox compiled the *merged* candidate, so that is what gets written. Writing
        // the unmerged set here would silently drop whatever PageObjectMerger preserved.
        var mergedFiles = ToGeneratedFiles(candidate);
        var written = options.WriteToProject ? _writer.Write(mergedFiles) : [];
        progress?.Report($"Compiled. {mergedFiles.Count} files {(options.WriteToProject ? "written" : "ready")}.");

        // A blocking issue cannot suppress the result: there is no other path to fall back to,
        // and returning nothing would leave the user with no output at all. Surface it loudly
        // instead — it is almost always something in the *existing* project (a step pattern
        // another flow already claims) that has to be resolved there.
        string? warning = null;
        var blocking = validationIssues.Where(i => i.Severity == IssueSeverity.Blocking).ToList();
        if (blocking.Count > 0)
        {
            warning =
                $"Warning: the generated suite has {blocking.Count} problem(s) that will not show up as a " +
                "compile error but will fail when the tests run — " +
                string.Join("; ", blocking.Take(3).Select(i => $"{i.Code} {i.Message}"));
            progress?.Report(warning);
        }

        return new CodeGenerationResult
        {
            Source = GenerationSource.Deterministic,
            Files = mergedFiles,
            DeterministicFiles = generatedSet,
            Attempts = attempts,
            FallbackReason = warning,
            WrittenPaths = written
        };
    }

    // A plain file dictionary in the shape StaticValidator reads. The split matters: the
    // validator's allowed-path rule (WTT001) covers only .feature/.cs, because locators travel
    // as *data* rather than as files — handing it the locator JSON as a file would reject
    // output that is perfectly correct.
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

    private static IReadOnlyList<GeneratedFile> ToGeneratedFiles(IReadOnlyDictionary<string, string> files) =>
        files.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new GeneratedFile(kv.Key, kv.Value))
            .ToList();

    // Stale files from a previous generation of the same flow would otherwise linger in the
    // sandbox and surface as phantom duplicate-binding errors.
    private static List<string> PathsToClear(TestFlow flow, IReadOnlyList<GeneratedFile> candidate)
    {
        // Same sanitized identifier TestFlowCodeGenerator uses for its own file names —
        // flow.Name is free text a user typed, not a path.
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
