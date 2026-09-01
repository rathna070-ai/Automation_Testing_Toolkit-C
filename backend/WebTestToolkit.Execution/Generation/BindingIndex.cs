using System.Text.RegularExpressions;
using WebTestToolkit.CodeGenerator;

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

    // Every binding already declared by *other* flows in the generated project. Moved here from
    // ReferenceBundleBuilder when the LLM codegen path was retired: that class existed to
    // assemble a prompt, but this method has nothing to do with prompting — it is a binding
    // index over the project, which is exactly what this class is. StaticValidator's
    // WTT130/WTT131 conflict checks are its only caller, on both the deterministic and (while
    // it existed) the LLM path.
    //
    // Files belonging to the flow being regenerated are excluded: they are about to be
    // replaced, so listing them would report a flow as conflicting with its own previous self.
    public static List<BindingPattern> ExistingBindings(string flowName)
    {
        var projectDir = SolutionPaths.GeneratedTestsDirectory();
        var bindings = new List<BindingPattern>();

        foreach (var relative in EnumerateStepSources(projectDir))
        {
            if (BelongsToFlow(relative, flowName))
                continue;

            var path = Path.Combine(projectDir, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path))
                bindings.AddRange(Extract(relative, File.ReadAllText(path)));
        }

        return bindings;
    }

    private static IEnumerable<string> EnumerateStepSources(string projectDir)
    {
        var dir = Path.Combine(projectDir, "Steps");
        if (!Directory.Exists(dir))
            yield break;

        foreach (var path in Directory.EnumerateFiles(dir, "*.cs", SearchOption.TopDirectoryOnly).OrderBy(p => p, StringComparer.Ordinal))
        {
            // Reqnroll's generated code-behind duplicates every binding attribute it can see;
            // indexing it would report every step as colliding with itself.
            if (path.EndsWith(".feature.cs", StringComparison.OrdinalIgnoreCase))
                continue;

            yield return $"Steps/{Path.GetFileName(path)}";
        }
    }

    private static bool BelongsToFlow(string relativePath, string flowName)
    {
        // flowName is free text a user typed; TestFlowCodeGenerator writes its files under the
        // sanitized identifier, so the comparison has to use the same one or a flow named
        // "flow new 1" would never recognize its own about-to-be-replaced files.
        var className = Naming.ToPascalCaseIdentifier(flowName);
        return Path.GetFileName(relativePath).Equals($"{className}Steps.cs", StringComparison.OrdinalIgnoreCase);
    }
}
