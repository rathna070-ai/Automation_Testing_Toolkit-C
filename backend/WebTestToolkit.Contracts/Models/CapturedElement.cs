namespace WebTestToolkit.Contracts.Models;

// Raw facts captured about one element during an Inspect session, plus the ranked
// locator candidates computed for it. BestLocator is what the code generator and
// auto-heal actually write into the locator JSON.
public class CapturedElement
{
    public string TagName { get; set; } = "";
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? VisibleText { get; set; }
    public List<LocatorCandidate> Candidates { get; set; } = new();

    // Extra DOM context. Not used for locating the element — this is what gives the
    // label-suggestion and assertion-inference prompts enough to propose sensible wording.
    public string? Type { get; set; }
    public string? Placeholder { get; set; }
    public string? AriaLabel { get; set; }
    public string? AssociatedLabelText { get; set; }
    public string? CssClasses { get; set; }
    public string? OuterHtmlSnippet { get; set; }
    public string? AncestorContext { get; set; }

    public bool HasLocator => Candidates.Count > 0;

    // Null when nothing was captured for this element. Callers that write locator JSON
    // must check HasLocator first — an element with no candidates cannot be located.
    public LocatorCandidate? BestLocator => Candidates
        .OrderByDescending(c => c.Score)
        .FirstOrDefault();
}
