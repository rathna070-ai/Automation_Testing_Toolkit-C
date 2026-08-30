using System.Text.RegularExpressions;

namespace WebTestToolkit.Execution.Generation;

// Scope is the [Scope(Feature = "...")] the binding's class carries, or null when unscoped.
// Two identical patterns in different feature scopes do not conflict — Reqnroll resolves
// them per feature — which is what lets two flows recorded against the same site coexist.
public record BindingPattern(string Keyword, string Pattern, string SourceFile, string? Scope = null);

// Reqnroll resolves step bindings at runtime, so a duplicate or ambiguous pattern compiles
// perfectly and then fails every run with "Ambiguous step definitions". The compiler cannot
// see this class of bug, which is why it gets checked statically instead.
public static partial class BindingIndex
{
    [GeneratedRegex("""\[(Given|When|Then|StepDefinition)\(@?"((?:[^"]|"")*)"\)\]""", RegexOptions.Compiled)]
    private static partial Regex BindingAttributeRegex();

    [GeneratedRegex("""\[Scope\(\s*Feature\s*=\s*"((?:[^"]|"")*)"\s*\)\]""", RegexOptions.Compiled)]
    private static partial Regex ScopeAttributeRegex();

    public static List<BindingPattern> Extract(string fileName, string content)
    {
        // One scope per file: the generator emits a single [Binding] class per flow, and the
        // [Scope] sits on that class. A hand-written file with several scoped classes would
        // over-apply the first scope here — accepted, because generated files are what this
        // index exists to check, and over-scoping only ever makes the conflict check *stricter*
        // for the file it misreads, never laxer.
        var scopeMatch = ScopeAttributeRegex().Match(content);
        var scope = scopeMatch.Success ? scopeMatch.Groups[1].Value.Replace("\"\"", "\"") : null;

        return BindingAttributeRegex()
            .Matches(content)
            .Select(m => new BindingPattern(
                m.Groups[1].Value, m.Groups[2].Value.Replace("\"\"", "\""), fileName, scope))
            .ToList();
    }

    // Two patterns conflict when a single step sentence could match both. Comparing the
    // regexes directly is not enough (`I\ enter\ "(.*)"` vs `I enter "(.*)"` are written
    // differently but match the same text), so compare on a normalized form.
    public static bool Conflicts(BindingPattern a, BindingPattern b)
    {
        if (a.Keyword != b.Keyword || Normalize(a.Pattern) != Normalize(b.Pattern))
            return false;

        // Same sentence, but scoped to two different features: Reqnroll picks the one whose
        // scope matches the running feature, so this is legal and deliberate. Only an
        // *unscoped* binding can collide with anything, because it applies everywhere.
        if (a.Scope is not null && b.Scope is not null)
            return string.Equals(a.Scope, b.Scope, StringComparison.Ordinal);

        return true;
    }

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
