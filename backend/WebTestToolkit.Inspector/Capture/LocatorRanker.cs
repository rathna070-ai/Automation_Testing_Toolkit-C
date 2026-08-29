using WebTestToolkit.Contracts.Models;

namespace WebTestToolkit.Inspector.Capture;

// Turns the overlay's raw proposals into scored LocatorCandidates.
//
// The split matters: only the injected JS can check whether a selector is unique on the
// live page, so *proposing* happens in the browser. Deciding which proposal wins is pure
// policy, so it lives here — where it can be unit-tested without launching Chrome, and
// where changing our mind about (say) text-based xpaths is a one-line edit.
//
// The score ends up in CapturedElement.BestLocator, which is what gets written into the
// locator JSON, so this ordering is effectively "how likely is this test to still pass
// after the next front-end deploy".
public static class LocatorRanker
{
    // LocatorRepository.ToBy() throws at *runtime* on anything else, where the compiler
    // cannot help. Filtering here means a bad strategy can never reach generated code.
    private static readonly HashSet<string> SupportedStrategies =
        new(StringComparer.Ordinal) { "id", "css", "xpath", "name" };

    // Keeps the payload (and later, the prompt) small. Nobody picks the 9th-best locator.
    private const int MaxCandidates = 8;

    public static int ScoreFor(string kind) => kind switch
    {
        // A developer-authored id is the most stable thing on a page.
        "id" => 100,
        // Explicitly added for testing, so nobody restyles it away.
        "testId" => 95,
        // Survives restyling; usually part of the form contract with the server.
        "name" => 85,
        // Accessibility attributes are semantic and rarely churn.
        "ariaLabel" => 78,
        "placeholder" => 72,
        // Readable, but the first casualty of localisation or a copy change.
        "text" => 60,
        // Unique right now, gone after the next deploy — better than nothing, worse than
        // any real attribute, which is why it is not simply scored as "id".
        "volatileId" => 45,
        // Structural: breaks whenever the markup is refactored.
        "cssPath" => 35,
        "absoluteXPath" => 10,
        _ => 5
    };

    // Same reasoning ScoreFor's switch is built on, exposed as data so the Inspect UI can
    // show *why* a candidate ranks where it does — useful when the best score is low and a
    // manual override is genuinely a judgment call, not a guess.
    public static string RationaleFor(string kind) => kind switch
    {
        "id" => "A developer-authored id — the most stable thing on a page.",
        "testId" => "Explicitly added for testing, so nobody restyles it away.",
        "name" => "Survives restyling; usually part of the form contract with the server.",
        "ariaLabel" => "An accessibility attribute — semantic and rarely churns.",
        "placeholder" => "An accessibility-adjacent attribute; can change with copy edits.",
        "text" => "Readable, but the first casualty of localisation or a copy change.",
        "volatileId" => "Unique right now, but framework-generated — likely gone after the next deploy.",
        "cssPath" => "Structural: breaks whenever the markup is refactored.",
        "absoluteXPath" => "The most fragile option — breaks on almost any markup change. Always available as a last resort.",
        _ => "Unrecognized candidate kind."
    };

    public static List<LocatorCandidate> Rank(IEnumerable<RawCandidate> raw)
    {
        var seen = new HashSet<(string, string)>();
        var ranked = new List<LocatorCandidate>();

        foreach (var candidate in raw)
        {
            if (!SupportedStrategies.Contains(candidate.Strategy))
                continue;
            if (string.IsNullOrWhiteSpace(candidate.Value))
                continue;
            // The overlay can legitimately propose the same selector twice (e.g. a css path
            // that collapses to the same string as the aria-label selector).
            if (!seen.Add((candidate.Strategy, candidate.Value)))
                continue;

            ranked.Add(new LocatorCandidate(candidate.Strategy, candidate.Value, ScoreFor(candidate.Kind), candidate.Kind));
        }

        return ranked
            .OrderByDescending(c => c.Score)
            .Take(MaxCandidates)
            .ToList();
    }

    public static CapturedElement ToCapturedElement(RawCapture capture) => new()
    {
        TagName = capture.TagName,
        Id = capture.Id,
        Name = capture.Name,
        VisibleText = capture.Text,
        Candidates = Rank(capture.Candidates),
        Type = capture.Type,
        Placeholder = capture.Placeholder,
        AriaLabel = capture.AriaLabel,
        AssociatedLabelText = capture.LabelText,
        CssClasses = capture.CssClasses,
        OuterHtmlSnippet = capture.Html,
        AncestorContext = capture.Ancestors,
        Checked = capture.Checked,
        Required = capture.Required,
        MaxLength = capture.MaxLength,
        Options = capture.Options?.Select(o => new SelectOption(o.Value, o.Text, o.Selected)).ToList()
    };
}
