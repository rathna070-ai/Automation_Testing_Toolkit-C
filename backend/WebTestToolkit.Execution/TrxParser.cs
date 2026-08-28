using System.Text.RegularExpressions;
using System.Xml.Linq;
using WebTestToolkit.Contracts.Models;

namespace WebTestToolkit.Execution;

// Parses the .trx file NUnit3TestAdapter writes for `dotnet test --logger trx`. The schema
// below was captured from a real run of tests/WebTestToolkit.GeneratedTests (both a passing
// and a deliberately-failed scenario) against this exact Reqnroll 3.3.4 / NUnit 3.14.0 /
// NUnit3TestAdapter 4.5.0 / .NET 8 combination — not guessed from documentation. See
// docs/ARCHITECTURE.md for the risk this closes out.
public static partial class TrxParser
{
    private static readonly XNamespace Ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    [GeneratedRegex(@"\[WTT_SCREENSHOT\](.+)", RegexOptions.Compiled)]
    private static partial Regex ScreenshotMarkerRegex();

    public static RunSummary Parse(string trxXml, DateTime runAtUtc)
    {
        var root = XDocument.Parse(trxXml).Root
            ?? throw new InvalidOperationException("The .trx file has no root element.");

        // UnitTestResult only carries a testId; the human-readable feature/class name lives
        // in TestDefinitions, keyed by that same id.
        var classNameByTestId = root.Element(Ns + "TestDefinitions")?
            .Elements(Ns + "UnitTest")
            .Where(e => e.Attribute("id") is not null)
            .ToDictionary(
                e => e.Attribute("id")!.Value,
                e => e.Element(Ns + "TestMethod")?.Attribute("className")?.Value ?? "")
            ?? [];

        var scenarios = new List<ScenarioResult>();

        foreach (var result in root.Element(Ns + "Results")?.Elements(Ns + "UnitTestResult") ?? [])
        {
            var testId = result.Attribute("testId")?.Value ?? "";
            var output = result.Element(Ns + "Output");
            var stdOut = output?.Element(Ns + "StdOut")?.Value;
            var errorInfo = output?.Element(Ns + "ErrorInfo");

            scenarios.Add(new ScenarioResult
            {
                FeatureName = FeatureNameFromClassName(classNameByTestId.GetValueOrDefault(testId, "")),
                ScenarioName = result.Attribute("testName")?.Value ?? "(unnamed)",
                Outcome = ParseOutcome(result.Attribute("outcome")?.Value),
                Duration = TimeSpan.TryParse(result.Attribute("duration")?.Value, out var d) ? d : TimeSpan.Zero,
                ErrorMessage = errorInfo?.Element(Ns + "Message")?.Value,
                StackTrace = errorInfo?.Element(Ns + "StackTrace")?.Value,
                ScreenshotPath = ExtractScreenshotPath(stdOut)
            });
        }

        return new RunSummary
        {
            RunAtUtc = runAtUtc,
            Total = scenarios.Count,
            Passed = scenarios.Count(s => s.Outcome == ScenarioOutcome.Passed),
            Failed = scenarios.Count(s => s.Outcome == ScenarioOutcome.Failed),
            Duration = scenarios.Aggregate(TimeSpan.Zero, static (sum, s) => sum + s.Duration),
            Scenarios = scenarios
        };
    }

    // Confirmed values from a real run: "Passed", "Failed". Everything else (NotExecuted for
    // an [Ignore]d test, Inconclusive, Timeout, Aborted) is treated as Skipped — none of them
    // are a pass and none represent a scenario that actually ran and failed.
    private static ScenarioOutcome ParseOutcome(string? raw) => raw switch
    {
        "Passed" => ScenarioOutcome.Passed,
        "Failed" => ScenarioOutcome.Failed,
        _ => ScenarioOutcome.Skipped
    };

    // TestDefinitions gives a compiled class name like
    // "WebTestToolkit.GeneratedTests.Features.LoginFeature" — Reqnroll's generated partial
    // class, always "<PascalCaseFeatureName>Feature". The .trx has no other record of the
    // Gherkin "Feature:" line, so this is the best available source for it.
    private static string FeatureNameFromClassName(string className)
    {
        var last = className.Length == 0 ? "" : (className.Split('.').LastOrDefault() ?? className);
        if (last.Length == 0)
            return "(unknown)";

        return last.EndsWith("Feature", StringComparison.Ordinal) && last.Length > "Feature".Length
            ? last[..^"Feature".Length]
            : last;
    }

    // See the matching Console.WriteLine in Support/Hooks.cs — VSTest captures each test's
    // Console output into Output/StdOut, which is the only place left to smuggle this path
    // through since the .trx schema has no field for arbitrary per-test metadata.
    private static string? ExtractScreenshotPath(string? stdOut)
    {
        if (string.IsNullOrEmpty(stdOut))
            return null;

        var matches = ScreenshotMarkerRegex().Matches(stdOut);
        return matches.Count == 0 ? null : matches[^1].Groups[1].Value.TrimEnd('\r', '\n');
    }
}
