using System.Text.RegularExpressions;
using WebTestToolkit.Contracts.Models;

namespace WebTestToolkit.Execution.Generation;

// The cheap gate that runs before a build is ever spent. Catches most model slips in
// milliseconds, and — more importantly — catches the two classes of bug a successful
// compile would happily let through: hardcoded locators (which silently break auto-heal)
// and ambiguous Reqnroll bindings (which fail at runtime, not compile time).
public static partial class StaticValidator
{
    private static readonly string[] AllowedStrategies = ["id", "css", "xpath", "name"];

    [GeneratedRegex(@"^(Features/[A-Za-z0-9_]+\.feature|Steps/[A-Za-z0-9_]+Steps\.cs|PageObjects/[A-Za-z0-9_]+\.cs)$", RegexOptions.Compiled)]
    private static partial Regex AllowedPathRegex();

    // The auto-heal invariant. Any By construction or direct FindElement in generated code
    // means that element can no longer be repaired by editing JSON.
    [GeneratedRegex(@"\bBy\s*\.\s*(Id|CssSelector|XPath|Name|ClassName|TagName|LinkText|PartialLinkText)\s*\(|\bFindElement\s*\(\s*By\b", RegexOptions.Compiled)]
    private static partial Regex HardcodedLocatorRegex();

    [GeneratedRegex(@"FindVisible\(\s*""([^""]+)""\s*\)", RegexOptions.Compiled)]
    private static partial Regex FindVisibleKeyRegex();

    [GeneratedRegex(@"LocatorRepository\s*\.\s*Load\(\s*""([^""]+)""\s*\)", RegexOptions.Compiled)]
    private static partial Regex LocatorLoadRegex();

    [GeneratedRegex(@"public\s+class\s+(\w+)", RegexOptions.Compiled)]
    private static partial Regex ClassNameRegex();

    [GeneratedRegex(@"public\s+(?:async\s+)?[\w<>\[\],\.]+\s+(\w+)\s*\([^)]*\)\s*(?=\{|=>)", RegexOptions.Compiled)]
    private static partial Regex MethodSignatureRegex();

    // An assertion whose subject is a literal rather than anything read from the page:
    // Assert.That(true), Assert.IsTrue(true), Assert.AreEqual(1, 1), Assert.That(1, Is.EqualTo(1)).
    //
    // Deliberately narrow, and deliberately free of backreferences. Requiring the two literals
    // to be equal would need a  pointing into a different alternation branch (which silently
    // never matches), and equality is the wrong test anyway — AreEqual(1, 2) asserts a literal
    // instead of the page just as much as AreEqual(1, 1) does. Every alternative here requires
    // a *literal* operand, so an assertion over a real variable or a page-object call can never
    // match: a false positive would block a valid generation.
    [GeneratedRegex(
        @"Assert\s*\.\s*(?:That|IsTrue|IsFalse)\s*\(\s*(?:true|false)\s*[,)]"
        + @"|Assert\s*\.\s*(?:AreEqual|AreNotEqual)\s*\(\s*(?:""[^""]*""|-?\d+(?:\.\d+)?|true|false)\s*,\s*(?:""[^""]*""|-?\d+(?:\.\d+)?|true|false)\s*\)"
        + @"|Assert\s*\.\s*That\s*\(\s*(?:""[^""]*""|-?\d+(?:\.\d+)?|true|false)\s*,\s*Is\s*\.\s*EqualTo\s*\(\s*(?:""[^""]*""|-?\d+(?:\.\d+)?|true|false)\s*\)",
        RegexOptions.Compiled)]
    private static partial Regex TautologicalAssertRegex();

    private static readonly (string Pattern, string Code, string Message)[] ForbiddenPatterns =
    [
        (@"\bnew\s+ChromeDriver\b", "WTT101", "Generated code must not create a WebDriver; DriverContext already owns the browser session."),
        (@"\bnew\s+FirefoxDriver\b", "WTT101", "Generated code must not create a WebDriver; DriverContext already owns the browser session."),
        (@"\bThread\s*\.\s*Sleep\b", "WTT102", "Thread.Sleep is not allowed; waiting is FindVisible's job via WebDriverWait."),
        (@"\[\s*(Before|After)Scenario", "WTT103", "Scenario hooks already exist in Support/Hooks.cs and must not be redefined."),
        (@"\[\s*(Before|After)TestRun", "WTT103", "Test-run hooks are owned by Support/Hooks.cs and must not be redefined."),
        (@"\bProcess\s*\.\s*Start\b", "WTT104", "Generated test code must not start processes."),
        (@"\bFile\s*\.\s*Delete\b", "WTT104", "Generated test code must not delete files."),
        (@"\bEnvironment\s*\.\s*Exit\b", "WTT104", "Generated test code must not exit the process.")
    ];

    public static List<ValidationIssue> Validate(GeneratedFileSet fileSet, IReadOnlyList<BindingPattern> existingBindings)
    {
        var issues = new List<ValidationIssue>();

        ValidatePaths(fileSet, issues);
        ValidateForbiddenPatterns(fileSet, issues);
        ValidateLocatorStrategies(fileSet, issues);
        ValidateLocatorClosure(fileSet, issues);
        ValidateBindings(fileSet, existingBindings, issues);
        ValidateGherkin(fileSet, issues);
        ValidateStepCoverage(fileSet, existingBindings, issues);
        ValidateThenStepsAssert(fileSet, issues);
        ValidateGivenWhenStepsAct(fileSet, issues);
        ValidateDuplicatedInteractionBlocks(fileSet, issues);

        return issues;
    }

    private static void ValidatePaths(GeneratedFileSet fileSet, List<ValidationIssue> issues)
    {
        if (fileSet.Files.Count == 0)
            issues.Add(new ValidationIssue(IssueSource.Static, "WTT001", null, null, "No files were returned."));

        foreach (var file in fileSet.Files)
        {
            var normalized = file.Path.Replace('\\', '/');
            if (!AllowedPathRegex().IsMatch(normalized))
            {
                issues.Add(new ValidationIssue(IssueSource.Static, "WTT001", file.Path, null,
                    $"'{file.Path}' is not an allowed output path. Only Features/<Name>.feature, Steps/<Name>Steps.cs and PageObjects/<Name>.cs may be written."));
            }

            if (string.IsNullOrWhiteSpace(file.Content))
            {
                issues.Add(new ValidationIssue(IssueSource.Static, "WTT002", file.Path, null, "File content is empty."));
            }
            else if (file.Content.Contains("```"))
            {
                issues.Add(new ValidationIssue(IssueSource.Static, "WTT003", file.Path, null,
                    "File content contains a markdown code fence; return raw file content only."));
            }
        }
    }

    private static void ValidateForbiddenPatterns(GeneratedFileSet fileSet, List<ValidationIssue> issues)
    {
        foreach (var file in fileSet.Files.Where(f => f.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            var hardcoded = HardcodedLocatorRegex().Match(file.Content);
            if (hardcoded.Success)
            {
                issues.Add(new ValidationIssue(IssueSource.Static, "WTT100", file.Path,
                    LineOf(file.Content, hardcoded.Index),
                    $"Hardcoded locator '{hardcoded.Value.Trim()}' found. Locators must be keys resolved through LocatorRepository, with the selector returned in the 'locators' array — otherwise auto-heal cannot repair this element."));
            }

            foreach (var (pattern, code, message) in ForbiddenPatterns)
            {
                var match = Regex.Match(file.Content, pattern);
                if (match.Success)
                    issues.Add(new ValidationIssue(IssueSource.Static, code, file.Path, LineOf(file.Content, match.Index), message));
            }
        }
    }

    private static void ValidateLocatorStrategies(GeneratedFileSet fileSet, List<ValidationIssue> issues)
    {
        foreach (var locator in fileSet.Locators)
        {
            if (!AllowedStrategies.Contains(locator.Strategy, StringComparer.Ordinal))
            {
                issues.Add(new ValidationIssue(IssueSource.Static, "WTT110", null, null,
                    $"Locator '{locator.Page}.{locator.Key}' uses strategy '{locator.Strategy}'. Only id, css, xpath and name are supported — LocatorRepository.ToBy throws on anything else at runtime."));
            }

            if (string.IsNullOrWhiteSpace(locator.Value))
            {
                issues.Add(new ValidationIssue(IssueSource.Static, "WTT111", null, null,
                    $"Locator '{locator.Page}.{locator.Key}' has an empty value."));
            }
        }
    }

    // Every key a page object asks for must exist, and every page it loads must have locators.
    // A missing key is a runtime KeyNotFoundException the compiler cannot see.
    private static void ValidateLocatorClosure(GeneratedFileSet fileSet, List<ValidationIssue> issues)
    {
        foreach (var file in fileSet.Files.Where(f => f.Path.Replace('\\', '/').StartsWith("PageObjects/", StringComparison.OrdinalIgnoreCase)))
        {
            var loadedPages = LocatorLoadRegex().Matches(file.Content).Select(m => m.Groups[1].Value).Distinct().ToList();
            foreach (var page in loadedPages)
            {
                if (!fileSet.Locators.Any(l => string.Equals(l.Page, page, StringComparison.Ordinal)))
                {
                    issues.Add(new ValidationIssue(IssueSource.Static, "WTT120", file.Path, null,
                        $"Page object loads locators for '{page}' but no locator with page='{page}' was returned."));
                }
            }

            foreach (Match match in FindVisibleKeyRegex().Matches(file.Content))
            {
                var key = match.Groups[1].Value;
                var known = fileSet.Locators.Any(l =>
                    string.Equals(l.Key, key, StringComparison.Ordinal) &&
                    (loadedPages.Count == 0 || loadedPages.Contains(l.Page, StringComparer.Ordinal)));

                if (!known)
                {
                    issues.Add(new ValidationIssue(IssueSource.Static, "WTT121", file.Path, LineOf(file.Content, match.Index),
                        $"FindVisible(\"{key}\") refers to a locator key that was not returned in the 'locators' array."));
                }
            }
        }
    }

    private static void ValidateBindings(GeneratedFileSet fileSet, IReadOnlyList<BindingPattern> existingBindings, List<ValidationIssue> issues)
    {
        var generated = new List<BindingPattern>();
        foreach (var file in fileSet.Files.Where(f => f.Path.Replace('\\', '/').StartsWith("Steps/", StringComparison.OrdinalIgnoreCase)))
            generated.AddRange(BindingIndex.Extract(file.Path, file.Content));

        foreach (var candidate in generated)
        {
            var clashWithExisting = existingBindings.FirstOrDefault(e => BindingIndex.Conflicts(e, candidate));
            if (clashWithExisting is not null)
            {
                issues.Add(new ValidationIssue(IssueSource.Static, "WTT130", candidate.SourceFile, null,
                    $"Step [{candidate.Keyword}(@\"{candidate.Pattern}\")] collides with an existing binding in {clashWithExisting.SourceFile}. Reqnroll would fail at runtime with an ambiguous step definition."));
            }
        }

        for (var i = 0; i < generated.Count; i++)
        {
            for (var j = i + 1; j < generated.Count; j++)
            {
                if (BindingIndex.Conflicts(generated[i], generated[j]))
                {
                    issues.Add(new ValidationIssue(IssueSource.Static, "WTT131", generated[i].SourceFile, null,
                        $"Two generated steps share the pattern [{generated[i].Keyword}(@\"{generated[i].Pattern}\")]."));
                }
            }
        }
    }

    private static void ValidateGherkin(GeneratedFileSet fileSet, List<ValidationIssue> issues)
    {
        foreach (var file in fileSet.Files.Where(f => f.Path.EndsWith(".feature", StringComparison.OrdinalIgnoreCase)))
        {
            var lines = file.Content.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

            if (!lines.Any(l => l.StartsWith("Feature:", StringComparison.Ordinal)))
                issues.Add(new ValidationIssue(IssueSource.Static, "WTT140", file.Path, null, "Feature file has no 'Feature:' line."));

            if (!lines.Any(l => l.StartsWith("Scenario:", StringComparison.Ordinal) || l.StartsWith("Scenario Outline:", StringComparison.Ordinal)))
                issues.Add(new ValidationIssue(IssueSource.Static, "WTT141", file.Path, null, "Feature file has no 'Scenario:' or 'Scenario Outline:'."));
        }
    }

    // A step in the .feature with no binding that matches it compiles perfectly and then
    // fails at runtime with "No matching step definition". Same family as the ambiguous-
    // binding check: invisible to the compiler, so it has to be caught here.
    private static void ValidateStepCoverage(
        GeneratedFileSet fileSet,
        IReadOnlyList<BindingPattern> existingBindings,
        List<ValidationIssue> issues)
    {
        var bindings = new List<BindingPattern>(existingBindings);
        foreach (var file in fileSet.Files.Where(f => f.Path.Replace('\\', '/').StartsWith("Steps/", StringComparison.OrdinalIgnoreCase)))
            bindings.AddRange(BindingIndex.Extract(file.Path, file.Content));

        foreach (var feature in fileSet.Files.Where(f => f.Path.EndsWith(".feature", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var (keyword, text, lineNumber) in EnumerateGherkinSteps(feature.Content))
            {
                if (bindings.Any(b => BindingMatches(b, keyword, text)))
                    continue;

                // A step that matches only once case is ignored is a different, more
                // actionable problem than a genuinely missing binding: the wording is
                // right, only the casing is off, and Reqnroll's step matching is
                // case-sensitive — so this fails at runtime despite looking correct.
                // Repair feedback that names the exact casing difference fixes it in one
                // turn instead of the model guessing whether to write a new step.
                var caseInsensitiveMatch = bindings.FirstOrDefault(b => BindingMatches(b, keyword, text, ignoreCase: true));
                if (caseInsensitiveMatch is not null)
                {
                    issues.Add(new ValidationIssue(IssueSource.Static, "WTT150", feature.Path, lineNumber,
                        $"Step '{keyword} {text}' matches [{caseInsensitiveMatch.Keyword}(@\"{caseInsensitiveMatch.Pattern}\")] in {caseInsensitiveMatch.SourceFile} only when case is ignored. Reqnroll's step matching is case-sensitive, so this fails at runtime despite looking correct — fix the casing so it matches exactly."));
                    continue;
                }

                issues.Add(new ValidationIssue(IssueSource.Static, "WTT150", feature.Path, lineNumber,
                    $"Step '{keyword} {text}' has no matching step definition. The binding attribute text must match the wording in the feature file exactly."));
            }
        }
    }

    // Yields each step with its effective keyword — And/But inherit whatever came before,
    // which is how Reqnroll resolves them.
    private static IEnumerable<(string Keyword, string Text, int Line)> EnumerateGherkinSteps(string featureContent)
    {
        var lines = featureContent.Split('\n');
        string? currentKeyword = null;
        var inExamples = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim().TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith('@'))
                continue;

            if (line.StartsWith("Examples:", StringComparison.Ordinal))
            {
                inExamples = true;
                continue;
            }

            if (line.StartsWith("Scenario", StringComparison.Ordinal) || line.StartsWith("Feature:", StringComparison.Ordinal))
            {
                inExamples = false;
                currentKeyword = null;
                continue;
            }

            // Examples rows and data tables are arguments, not steps.
            if (inExamples || line.StartsWith('|'))
                continue;

            var match = Regex.Match(line, @"^(Given|When|Then|And|But)\s+(.*)$");
            if (!match.Success)
                continue;

            var keyword = match.Groups[1].Value;
            var text = match.Groups[2].Value.Trim();

            if (keyword is "And" or "But")
            {
                if (currentKeyword is null)
                    continue;
                keyword = currentKeyword;
            }
            else
            {
                currentKeyword = keyword;
            }

            yield return (keyword, text, i + 1);
        }
    }

    private static bool BindingMatches(BindingPattern binding, string keyword, string stepText, bool ignoreCase = false)
    {
        // StepDefinition binds to any keyword.
        if (binding.Keyword != "StepDefinition" && binding.Keyword != keyword)
            return false;

        // In a Scenario Outline the <placeholders> are substituted at runtime; swap in a
        // stand-in value so they can match a capture group during this static check.
        var probeText = Regex.Replace(stepText, @"<[^>]+>", "x");

        try
        {
            var pattern = binding.Pattern;
            if (!pattern.StartsWith('^')) pattern = "^" + pattern;
            if (!pattern.EndsWith('$')) pattern += "$";
            return Regex.IsMatch(probeText, pattern, ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
        }
        catch (ArgumentException)
        {
            // An invalid regex in the binding isn't this check's business — the
            // binding-conflict and compile stages will surface it.
            return true;
        }
    }

    // An empty Then step passes silently forever, which is worse than a failing test —
    // it reports success while verifying nothing.
    private static void ValidateThenStepsAssert(GeneratedFileSet fileSet, List<ValidationIssue> issues)
    {
        foreach (var file in fileSet.Files.Where(f => f.Path.Replace('\\', '/').StartsWith("Steps/", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (Match attribute in Regex.Matches(file.Content, """\[Then\(@?"(?:[^"]|"")*"\)\]"""))
            {
                var body = ExtractMethodBody(file.Content, attribute.Index + attribute.Length);
                if (body is null)
                    continue;

                var stripped = Regex.Replace(body, @"//.*?$|/\*.*?\*/", "", RegexOptions.Multiline | RegexOptions.Singleline);

                var asserts = stripped.Contains("Assert", StringComparison.Ordinal)
                    || stripped.Contains("throw", StringComparison.Ordinal)
                    || stripped.Contains(".Should(", StringComparison.Ordinal);

                if (!asserts)
                {
                    issues.Add(new ValidationIssue(IssueSource.Static, "WTT151", file.Path,
                        LineOf(file.Content, attribute.Index),
                        "This Then step performs no verification. An empty or non-asserting Then step passes silently and reports success without checking anything — it must call Assert or throw."));
                    continue;
                }

                // Presence of the word "Assert" was the whole check until now, which a
                // tautology satisfies: Assert.That(true) or Assert.IsTrue(true) reads like
                // verification, compiles, passes forever, and checks nothing. Industry
                // reviews of AI-written tests name this — an assertion that pins down the
                // absence of an obvious failure rather than the behaviour — as the single
                // most common defect, so the gate has to see through it rather than count
                // tokens.
                var tautology = TautologicalAssertRegex().Match(stripped);
                if (tautology.Success)
                {
                    issues.Add(new ValidationIssue(IssueSource.Static, "WTT153", file.Path,
                        LineOf(file.Content, attribute.Index),
                        $"'{tautology.Value.Trim()}' asserts a literal, not the page. It passes whatever the "
                        + "application does, so this step reports success without testing anything — assert on a "
                        + "value read back from the page object instead."));
                }
            }
        }
    }

    // Mirrors ValidateThenStepsAssert: a no-op Given/When body compiles and passes silently,
    // and a later Then step ends up checking whatever page state was already there instead
    // of the state the action was supposed to produce — just as dangerous as an unverified
    // Then, and just as invisible to the compiler.
    private static void ValidateGivenWhenStepsAct(GeneratedFileSet fileSet, List<ValidationIssue> issues)
    {
        foreach (var file in fileSet.Files.Where(f => f.Path.Replace('\\', '/').StartsWith("Steps/", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (Match attribute in Regex.Matches(file.Content, """\[(Given|When)\(@?"(?:[^"]|"")*"\)\]"""))
            {
                var body = ExtractMethodBody(file.Content, attribute.Index + attribute.Length);
                if (body is null)
                    continue;

                var stripped = Regex.Replace(body, @"//.*?$|/\*.*?\*/", "", RegexOptions.Multiline | RegexOptions.Singleline).Trim();

                if (stripped.Length == 0)
                {
                    var keyword = attribute.Groups[1].Value;
                    issues.Add(new ValidationIssue(IssueSource.Static, "WTT152", file.Path,
                        LineOf(file.Content, attribute.Index),
                        $"This {keyword} step body is empty. A no-op action step passes silently and a later Then step ends up checking the wrong page state — it must call a page object method."));
                }
            }
        }
    }

    // A maintainability smell, not a correctness bug: two page-object methods that repeat
    // the same multi-statement wait-then-interact shape, differing only in which locator key
    // they resolve, are a copy-paste that should have been a shared private helper. Advisory
    // severity — this must never burn a repair attempt arguing with the model over something
    // that isn't actually broken.
    private static void ValidateDuplicatedInteractionBlocks(GeneratedFileSet fileSet, List<ValidationIssue> issues)
    {
        foreach (var file in fileSet.Files.Where(f => f.Path.Replace('\\', '/').StartsWith("PageObjects/", StringComparison.OrdinalIgnoreCase)))
        {
            var classMatch = ClassNameRegex().Match(file.Content);
            var className = classMatch.Success ? classMatch.Groups[1].Value : null;

            var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            foreach (Match signature in MethodSignatureRegex().Matches(file.Content))
            {
                var methodName = signature.Groups[1].Value;
                if (methodName == className) // the constructor matches the same shape as a method
                    continue;

                var body = ExtractMethodBody(file.Content, signature.Index + signature.Length);
                if (body is null)
                    continue;

                var stripped = Regex.Replace(body, @"//.*?$|/\*.*?\*/", "", RegexOptions.Multiline | RegexOptions.Singleline);

                // Only a multi-statement body is the shape worth flagging — a duplicated
                // one-liner isn't the maintainability problem this check exists for.
                if (Regex.Matches(stripped, ";").Count < 2)
                    continue;

                // Normalize away the one thing expected to differ (the locator key string
                // literal) so structurally identical bodies compare equal.
                var normalized = Regex.Replace(stripped, "\"[^\"]*\"", "\"_\"");
                normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
                if (normalized.Length == 0)
                    continue;

                if (!groups.TryGetValue(normalized, out var names))
                    groups[normalized] = names = new List<string>();
                names.Add(methodName);
            }

            foreach (var names in groups.Values)
            {
                if (names.Count < 2)
                    continue;

                issues.Add(new ValidationIssue(IssueSource.Static, "WTT160", file.Path, null,
                    $"{string.Join(", ", names)} share the same wait-then-interact shape, differing only by locator key. Consider a shared private helper instead of repeating the block.",
                    IssueSeverity.Advisory));
            }
        }
    }

    // Handles both block bodies and expression-bodied members. Internal (not private) so
    // PageObjectMerger — same project, same "parse a generated method out of C# source
    // text" need — can reuse it rather than duplicating the brace-counting logic.
    internal static string? ExtractMethodBody(string content, int searchFrom)
    {
        var arrow = content.IndexOf("=>", searchFrom, StringComparison.Ordinal);
        var brace = content.IndexOf('{', searchFrom);

        if (arrow >= 0 && (brace < 0 || arrow < brace))
        {
            var end = content.IndexOf(';', arrow);
            return end < 0 ? content[arrow..] : content[arrow..end];
        }

        if (brace < 0)
            return null;

        var depth = 0;
        for (var i = brace; i < content.Length; i++)
        {
            if (content[i] == '{') depth++;
            else if (content[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return content[(brace + 1)..i];
            }
        }

        return null;
    }

    private static int LineOf(string content, int charIndex) =>
        content.Take(charIndex).Count(c => c == '\n') + 1;
}
