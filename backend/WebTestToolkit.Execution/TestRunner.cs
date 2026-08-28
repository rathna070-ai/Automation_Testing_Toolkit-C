using WebTestToolkit.Contracts.Models;

namespace WebTestToolkit.Execution;

// Whether a RunSummary came back at all — never the dotnet exit code. `dotnet test` exits
// non-zero whenever any scenario fails, which is a normal, useful outcome (the run itself
// succeeded at telling you that), not a failure of this operation. The only real failure
// case is "no .trx was produced at all" - almost always the test project not building.
public record TestRunResult(bool Succeeded, string RawOutput, RunSummary? Summary, string? Error);

public static class TestRunner
{
    public static async Task<TestRunResult> RunAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        string projectPath;
        try
        {
            projectPath = SolutionPaths.GeneratedTestsProject();
        }
        catch (InvalidOperationException ex)
        {
            return new TestRunResult(false, "", null, ex.Message);
        }

        var resultsDir = Path.Combine(Path.GetTempPath(), "WebTestToolkit", "TestResults");
        Directory.CreateDirectory(resultsDir);
        var trxFileName = $"results-{Guid.NewGuid():N}.trx";
        var runAtUtc = DateTime.UtcNow;

        var arguments = $"test \"{projectPath}\" --logger \"trx;LogFileName={trxFileName}\" --results-directory \"{resultsDir}\"";
        var workingDirectory = Path.GetDirectoryName(projectPath);

        var result = await DotnetCli.RunAsync(arguments, workingDirectory, progress, ct);

        var trxPath = Path.Combine(resultsDir, trxFileName);
        if (!File.Exists(trxPath))
        {
            return new TestRunResult(false, result.Output, null,
                "No .trx file was produced - the test project most likely failed to build. See the console output.");
        }

        var trxXml = await File.ReadAllTextAsync(trxPath, ct);
        var summary = TrxParser.Parse(trxXml, runAtUtc);

        return new TestRunResult(true, result.Output, summary, null);
    }
}
