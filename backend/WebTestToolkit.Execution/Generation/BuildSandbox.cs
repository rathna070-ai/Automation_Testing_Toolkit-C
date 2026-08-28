using Microsoft.Extensions.Logging;
using WebTestToolkit.Contracts.Models;

namespace WebTestToolkit.Execution.Generation;

public record BuildOutcome(bool Succeeded, IReadOnlyList<ValidationIssue> Issues, string RawOutput);

// A persistent mirror of the real test project, living outside the repo, where candidate
// files are compiled before anything touches tests/.
//
// Why a persistent mirror rather than the two obvious alternatives:
//   * Write-in-place-and-revert looks simplest and is the most dangerous — one bad
//     candidate fails the *whole* project build, so a crash mid-loop leaves the user's real
//     suite broken. "I clicked Generate and now nothing compiles" is the worst outcome for
//     a tool whose promise is that you always get compiling code.
//   * A throwaway scratch project can't compile realistically: without Support/*, the
//     package graph, and the other flows' files, name collisions and missing members are
//     invisible. Once you've copied all that in you've built this, just slower.
// Living outside the repo also means it can never be committed and never gets indexed by
// an IDE watching the working tree.
public class BuildSandbox
{
    private readonly ILogger<BuildSandbox> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _sandboxProjectDir;
    private bool _restored;

    private static readonly string[] SkipDirectories = ["bin", "obj", "Screenshots", "TestResults", "Results"];

    public BuildSandbox(ILogger<BuildSandbox> logger)
    {
        _logger = logger;
        _sandboxProjectDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WebTestToolkit", "sandbox", "GeneratedTests");
    }

    public string ProjectDirectory => _sandboxProjectDir;

    public async Task<BuildOutcome> TryBuildAsync(
        IReadOnlyDictionary<string, string> candidateFiles,
        IReadOnlyCollection<string> pathsToClear,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var sourceDir = SolutionPaths.GeneratedTestsDirectory();
            await EnsureReadyAsync(sourceDir, ct);

            ClearFlowSlot(pathsToClear);
            WriteCandidates(candidateFiles);

            // --no-restore because EnsureReadyAsync already restored; incremental builds
            // after the first are 1-4s, which is negligible against an LLM round-trip.
            var result = await DotnetCli.RunAsync(
                "build WebTestToolkit.GeneratedTests.csproj --no-restore -nologo -v:q -p:GenerateFullPaths=true -clp:NoSummary",
                _sandboxProjectDir,
                progress: null,
                ct);

            var issues = result.Succeeded
                ? []
                : MsBuildErrorParser.Parse(result.Output, _sandboxProjectDir);

            return new BuildOutcome(result.Succeeded, issues, result.Output);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureReadyAsync(string sourceDir, CancellationToken ct)
    {
        Directory.CreateDirectory(_sandboxProjectDir);

        var csprojChanged = MirrorSourceProject(sourceDir);

        if (!_restored || csprojChanged)
        {
            var restore = await DotnetCli.RunAsync(
                "restore WebTestToolkit.GeneratedTests.csproj -nologo", _sandboxProjectDir, progress: null, ct);

            if (!restore.Succeeded)
                _logger.LogWarning("Sandbox restore failed: {Output}", restore.Output);

            _restored = true;
        }
    }

    // Copies the real project over the sandbox, but only files whose size or timestamp
    // differ — preserving MSBuild's incremental build. Returns whether the csproj changed,
    // since that's the one edit that invalidates the restore.
    private bool MirrorSourceProject(string sourceDir)
    {
        var csprojChanged = false;

        foreach (var sourcePath in EnumerateSourceFiles(sourceDir))
        {
            var relative = Path.GetRelativePath(sourceDir, sourcePath);
            var targetPath = Path.Combine(_sandboxProjectDir, relative);

            if (!NeedsCopy(sourcePath, targetPath))
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            CopyWithRetry(sourcePath, targetPath);

            if (relative.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                csprojChanged = true;
        }

        return csprojChanged;
    }

    private static IEnumerable<string> EnumerateSourceFiles(string sourceDir)
    {
        foreach (var path in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, path);
            var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (segments.Any(s => SkipDirectories.Contains(s, StringComparer.OrdinalIgnoreCase)))
                continue;

            // Reqnroll regenerates these from the .feature files on every build; copying
            // stale ones causes duplicate-class errors that have nothing to do with the candidate.
            if (relative.EndsWith(".feature.cs", StringComparison.OrdinalIgnoreCase))
                continue;

            yield return path;
        }
    }

    private static bool NeedsCopy(string sourcePath, string targetPath)
    {
        if (!File.Exists(targetPath))
            return true;

        var source = new FileInfo(sourcePath);
        var target = new FileInfo(targetPath);
        return source.Length != target.Length || source.LastWriteTimeUtc > target.LastWriteTimeUtc;
    }

    // Without this, renaming a step leaves the previous generation's file behind in the
    // sandbox and you chase a phantom duplicate-binding error.
    private void ClearFlowSlot(IReadOnlyCollection<string> pathsToClear)
    {
        foreach (var relative in pathsToClear)
        {
            var path = Path.Combine(_sandboxProjectDir, relative.Replace('/', Path.DirectorySeparatorChar));
            DeleteWithRetry(path);

            if (path.EndsWith(".feature", StringComparison.OrdinalIgnoreCase))
                DeleteWithRetry(path + ".cs");
        }
    }

    private void WriteCandidates(IReadOnlyDictionary<string, string> candidateFiles)
    {
        foreach (var (relative, content) in candidateFiles)
        {
            var path = Path.Combine(_sandboxProjectDir, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            WriteWithRetry(path, content);
        }
    }

    // Windows Defender opens files as they're created, so a transient sharing violation on
    // a file we just wrote is normal rather than a bug. Retry briefly before giving up.
    private static void CopyWithRetry(string source, string target) =>
        WithRetry(() => File.Copy(source, target, overwrite: true));

    private static void WriteWithRetry(string path, string content) =>
        WithRetry(() => File.WriteAllText(path, content));

    private static void DeleteWithRetry(string path) =>
        WithRetry(() => { if (File.Exists(path)) File.Delete(path); });

    private static void WithRetry(Action action, int attempts = 3)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (IOException) when (attempt < attempts)
            {
                Thread.Sleep(50 * attempt);
            }
        }
    }
}
