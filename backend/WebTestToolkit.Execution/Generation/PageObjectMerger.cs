using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using WebTestToolkit.Contracts.Models;

namespace WebTestToolkit.Execution.Generation;

// PageObjects/{PageName}.cs and LocatorRepository/{PageName}.locators.json are both keyed by
// page name only, not by flow — deliberately, so two flows that touch the same page share
// one page object and one locator file instead of duplicating them. But nothing merges:
// every generation wholesale-*replaces* both files with only what *this flow's own* steps
// need. Generate flow A (writes ClickX/ClickY on "HomePage", plus their locators), then
// later generate a differently-named flow B that also touches "HomePage" but only needs
// ClickX — B's generation silently deletes ClickY and its locator entry, breaking flow A's
// already-generated Steps.cs, which still calls it. Flow A was never touched; a completely
// different flow's generation broke it. This splices any still-needed existing method or
// locator entry back in before the merged content is compiled or written, so generating one
// flow can never silently break another.
//
// Applies identically to both generation paths (deterministic and LLM) — both write these
// paths through the same candidate-file dictionary, so both call through here.
public static partial class PageObjectMerger
{
    [GeneratedRegex(@"public\s+class\s+(\w+)", RegexOptions.Compiled)]
    private static partial Regex ClassNameRegex();

    // Same shape StaticValidator's own WTT160 check already uses to find action methods —
    // "public" rules out the constructor (no return-type token before the name, so it never
    // matches this pattern) and the private FindVisible helper.
    [GeneratedRegex(@"public\s+(?:async\s+)?[\w<>\[\],\.]+\s+(\w+)\s*\([^)]*\)\s*(?=\{|=>)", RegexOptions.Compiled)]
    private static partial Regex MethodSignatureRegex();

    private static readonly JsonSerializerOptions JsonReadOptions = new() { PropertyNameCaseInsensitive = true };

    // Same options LocatorFileBuilder/LocatorJsonGenerator already write with — a merged
    // file must stay byte-style-identical to one either generator produced on its own.
    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static Dictionary<string, string> MergeWithExisting(
        IReadOnlyDictionary<string, string> candidate, string projectDir)
    {
        var merged = new Dictionary<string, string>(candidate, StringComparer.Ordinal);

        foreach (var path in candidate.Keys)
        {
            var normalized = path.Replace('\\', '/');
            var isPageObject = normalized.StartsWith("PageObjects/", StringComparison.OrdinalIgnoreCase);
            var isLocatorFile = normalized.StartsWith("LocatorRepository/", StringComparison.OrdinalIgnoreCase)
                && normalized.EndsWith(".locators.json", StringComparison.OrdinalIgnoreCase);
            if (!isPageObject && !isLocatorFile)
                continue;

            var existingPath = Path.Combine(projectDir, normalized.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(existingPath))
                continue; // nothing to preserve — this page is new.

            var existingContent = File.ReadAllText(existingPath);
            var mergedContent = isPageObject
                ? MergePageObject(freshContent: merged[path], existingContent)
                : MergeLocatorFile(freshContent: merged[path], existingContent);
            if (mergedContent is not null)
                merged[path] = mergedContent;
        }

        return merged;
    }

    // Every locator key the existing file has that the fresh generation doesn't redefine
    // survives — a pure dictionary union, no C# parsing needed. The fresh file's own entries
    // always win on a key collision, and its Url wins outright (the two should always agree
    // for the same page in practice, so there's no real conflict to resolve there).
    private static string? MergeLocatorFile(string freshContent, string existingContent)
    {
        PageLocators? fresh, existing;
        try
        {
            fresh = JsonSerializer.Deserialize<PageLocators>(freshContent, JsonReadOptions);
            existing = JsonSerializer.Deserialize<PageLocators>(existingContent, JsonReadOptions);
        }
        catch (JsonException)
        {
            return null; // not recognizable as a locator file — leave it alone.
        }

        if (fresh is null || existing is null)
            return null;

        var mergedLocators = new Dictionary<string, LocatorEntry>(fresh.Locators, StringComparer.Ordinal);
        foreach (var (key, entry) in existing.Locators)
        {
            if (!mergedLocators.ContainsKey(key))
                mergedLocators[key] = entry;
        }

        if (mergedLocators.Count == fresh.Locators.Count)
            return freshContent; // nothing new to preserve.

        return JsonSerializer.Serialize(fresh with { Locators = mergedLocators }, JsonWriteOptions);
    }

    private static string? MergePageObject(string freshContent, string existingContent)
    {
        var classMatch = ClassNameRegex().Match(freshContent);
        if (!classMatch.Success)
            return null; // not recognizable as a page object — leave it alone.
        var className = classMatch.Groups[1].Value;

        var freshMethodNames = new HashSet<string>(
            MethodSignatureRegex().Matches(freshContent).Select(m => m.Groups[1].Value),
            StringComparer.Ordinal);

        var toPreserve = new List<string>();
        foreach (Match signature in MethodSignatureRegex().Matches(existingContent))
        {
            var methodName = signature.Groups[1].Value;

            // The constructor and FindVisible are always regenerated fresh — FindVisible
            // reads _locators, which is rebuilt from *this* generation's own locator keys,
            // so an old copy of it must never survive a merge.
            if (methodName == className || methodName == "FindVisible")
                continue;

            // The current flow's own fresh version of this method wins outright — only a
            // method the fresh content doesn't define at all gets preserved.
            if (freshMethodNames.Contains(methodName))
                continue;

            var fullMethod = ExtractFullMethod(existingContent, signature);
            if (fullMethod is not null)
                toPreserve.Add(fullMethod);
        }

        if (toPreserve.Count == 0)
            return freshContent;

        // Splice preserved methods in just before the class's closing brace — a pure
        // textual append, with no dependency on where FindVisible happens to sit.
        var lastBrace = freshContent.LastIndexOf('}');
        if (lastBrace < 0)
            return freshContent;

        // The extracted text starts at "public", not at the original line's leading
        // whitespace (the signature regex match begins there) — restore the 4-space class-
        // member indent on the way back in; the body's own interior lines already carry
        // their original indentation verbatim.
        var insertion = string.Concat(toPreserve.Select(m => "\n    " + m + "\n"));
        return freshContent[..lastBrace] + insertion + freshContent[lastBrace..];
    }

    // The full method text — signature line through the closing brace — built from
    // StaticValidator.ExtractMethodBody's own brace-matching rather than re-implementing
    // it: that returns the body's *interior*, so the body's end index is derivable from
    // where its search for the opening brace started plus the interior's length.
    private static string? ExtractFullMethod(string content, Match signatureMatch)
    {
        var searchFrom = signatureMatch.Index + signatureMatch.Length;
        var body = StaticValidator.ExtractMethodBody(content, searchFrom);
        if (body is null)
            return null;

        var braceStart = content.IndexOf('{', searchFrom);
        if (braceStart < 0)
            return null;

        var closeBraceIndex = braceStart + 1 + body.Length;
        if (closeBraceIndex >= content.Length)
            return null;

        return content[signatureMatch.Index..(closeBraceIndex + 1)];
    }
}
