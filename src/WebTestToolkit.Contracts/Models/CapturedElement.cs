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

    public LocatorCandidate BestLocator => Candidates
        .OrderByDescending(c => c.Score)
        .First();
}
