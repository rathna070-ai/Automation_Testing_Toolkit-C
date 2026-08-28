using System.Text.RegularExpressions;
using WebTestToolkit.Contracts.Models;

namespace WebTestToolkit.Execution.Generation;

public static partial class MsBuildErrorParser
{
    // "Steps/LoginSteps.cs(23,9): error CS1061: 'LoginPage' does not contain ... [C:\...csproj]"
    [GeneratedRegex(@"^(?<file>.+?)\((?<line>\d+),(?<col>\d+)\):\s+error\s+(?<code>[A-Za-z]+\d+):\s+(?<msg>.+?)(?:\s+\[(?<proj>.+)\])?$", RegexOptions.Compiled)]
    private static partial Regex PositionalErrorRegex();

    // MSB/NETSDK errors that carry no file position.
    [GeneratedRegex(@"^\s*error\s+(?<code>[A-Za-z]+\d+):\s+(?<msg>.+?)(?:\s+\[(?<proj>.+)\])?$", RegexOptions.Compiled)]
    private static partial Regex GlobalErrorRegex();

    // One missing using can produce hundreds of errors; sending them all wastes the token
    // budget and buries the signal. Dedupe by (code, message) and cap.
    public static List<ValidationIssue> Parse(string buildOutput, string projectRoot, int maxIssues = 25)
    {
        var issues = new List<ValidationIssue>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rawLine in buildOutput.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0)
                continue;

            var match = PositionalErrorRegex().Match(line);
            if (match.Success)
            {
                var file = ToRelativePath(match.Groups["file"].Value.Trim(), projectRoot);
                var code = match.Groups["code"].Value;
                var message = match.Groups["msg"].Value.Trim();
                if (seen.Add($"{code}|{file}|{message}"))
                {
                    issues.Add(new ValidationIssue(IssueSource.Compiler, code, file,
                        int.TryParse(match.Groups["line"].Value, out var n) ? n : null, message));
                }
                continue;
            }

            var global = GlobalErrorRegex().Match(line);
            if (global.Success)
            {
                var code = global.Groups["code"].Value;
                var message = global.Groups["msg"].Value.Trim();
                if (seen.Add($"{code}||{message}"))
                    issues.Add(new ValidationIssue(IssueSource.Compiler, code, null, null, message));
            }
        }

        // Positional errors first — they're the actionable ones.
        return issues
            .OrderByDescending(i => i.File is not null)
            .Take(maxIssues)
            .ToList();
    }

    // Absolute sandbox paths confuse the model and leak machine details into the prompt.
    private static string ToRelativePath(string path, string projectRoot)
    {
        try
        {
            if (Path.IsPathRooted(path) && !string.IsNullOrEmpty(projectRoot))
            {
                var relative = Path.GetRelativePath(projectRoot, path);
                if (!relative.StartsWith("..", StringComparison.Ordinal))
                    return relative.Replace('\\', '/');
            }
        }
        catch (ArgumentException)
        {
            // Not a usable path; fall through and return it unchanged.
        }

        return path.Replace('\\', '/');
    }

    // Compiler errors alone are terse. Attaching the surrounding source lines from the
    // candidate we already hold in memory costs nothing and markedly improves repair rate.
    public static string FormatForPrompt(IReadOnlyList<ValidationIssue> issues, IReadOnlyDictionary<string, string> candidateFiles)
    {
        var blocks = new List<string>();

        foreach (var issue in issues)
        {
            var header = issue.File is null
                ? $"error {issue.Code}: {issue.Message}"
                : $"{issue.File}({issue.Line}): error {issue.Code}: {issue.Message}";

            var context = BuildContext(issue, candidateFiles);
            blocks.Add(context is null ? header : $"{header}\n{context}");
        }

        return string.Join("\n\n", blocks);
    }

    private static string? BuildContext(ValidationIssue issue, IReadOnlyDictionary<string, string> candidateFiles)
    {
        if (issue.File is null || issue.Line is null)
            return null;

        var content = candidateFiles.FirstOrDefault(kv =>
            string.Equals(kv.Key.Replace('\\', '/'), issue.File.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)).Value;

        if (content is null)
            return null;

        var lines = content.Split('\n');
        var target = issue.Line.Value;
        var start = Math.Max(1, target - 2);
        var end = Math.Min(lines.Length, target + 2);

        var rendered = new List<string>();
        for (var n = start; n <= end; n++)
        {
            var marker = n == target ? ">" : " ";
            rendered.Add($"{marker} {n,4} | {lines[n - 1].TrimEnd('\r')}");
        }

        return string.Join("\n", rendered);
    }
}
