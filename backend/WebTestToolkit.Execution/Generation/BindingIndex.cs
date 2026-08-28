using System.Text.RegularExpressions;

namespace WebTestToolkit.Execution.Generation;

public record BindingPattern(string Keyword, string Pattern, string SourceFile);

// Reqnroll resolves step bindings at runtime, so a duplicate or ambiguous pattern compiles
// perfectly and then fails every run with "Ambiguous step definitions". The compiler cannot
// see this class of bug, which is why it gets checked statically instead.
public static partial class BindingIndex
{
    [GeneratedRegex("""\[(Given|When|Then|StepDefinition)\(@?"((?:[^"]|"")*)"\)\]""", RegexOptions.Compiled)]
    private static partial Regex BindingAttributeRegex();

    public static List<BindingPattern> Extract(string fileName, string content) =>
        BindingAttributeRegex()
            .Matches(content)
            .Select(m => new BindingPattern(m.Groups[1].Value, m.Groups[2].Value.Replace("\"\"", "\""), fileName))
            .ToList();

    // Two patterns conflict when a single step sentence could match both. Comparing the
    // regexes directly is not enough (`I\ enter\ "(.*)"` vs `I enter "(.*)"` are written
    // differently but match the same text), so compare on a normalized form.
    public static bool Conflicts(BindingPattern a, BindingPattern b) =>
        a.Keyword == b.Keyword && Normalize(a.Pattern) == Normalize(b.Pattern);

    private static string Normalize(string pattern)
    {
        // Drop regex escaping of characters that only ever appear literally in step text,
        // and collapse any capture group to a single placeholder so patterns that differ
        // only in how they capture are treated as the same sentence.
        var unescaped = Regex.Replace(pattern, @"\\(?=[ '""!,.\-:;()])", "");
        var placeholders = Regex.Replace(unescaped, @"\((?:\?<[^>]+>)?[^)]*\)", "{}");
        return Regex.Replace(placeholders, @"\s+", " ").Trim();
    }
}
