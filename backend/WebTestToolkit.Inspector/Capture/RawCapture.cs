namespace WebTestToolkit.Inspector.Capture;

// The wire shape produced by Overlay/inspector-overlay.js. Deserialized straight from the
// JSON string the overlay's drain() returns, rather than walked as Selenium's
// ReadOnlyCollection<object>/Dictionary<string,object> soup — a typed record makes the
// JS/C# contract explicit and breaks loudly if the overlay changes shape.
//
// Every field is nullable because it comes from a third-party page: an element may have no
// name, no label, no text. Only Kind and TagName are guaranteed.
public sealed record RawCapture
{
    public string Kind { get; init; } = "";
    public string TagName { get; init; } = "";
    public string? Id { get; init; }
    public string? Name { get; init; }
    public string? Type { get; init; }
    public string? Placeholder { get; init; }
    public string? AriaLabel { get; init; }
    public string? LabelText { get; init; }
    public string? CssClasses { get; init; }
    public string? Text { get; init; }
    public string? Value { get; init; }
    public string? Html { get; init; }
    public string? Ancestors { get; init; }
    public string Url { get; init; } = "";
    public long At { get; init; }
    public List<RawCandidate> Candidates { get; init; } = new();

    // Real element state, not inferable from the outerHTML snippet alone — a <select>'s
    // real option list, a checkbox/radio's current state, whether a field is required, and
    // its max length. Null when the concept doesn't apply to this element (e.g. Checked is
    // null for a text input, Options is null for anything but a <select>).
    public bool? Checked { get; init; }
    public bool? Required { get; init; }
    public int? MaxLength { get; init; }
    public List<RawSelectOption>? Options { get; init; }
}

public sealed record RawSelectOption
{
    public string Value { get; init; } = "";
    public string Text { get; init; } = "";
    public bool Selected { get; init; }
}

// Strategy is one of "id" | "css" | "xpath" | "name" (the only four LocatorRepository.ToBy
// understands). Kind is the *reason* the overlay proposed it, and is what LocatorRanker
// scores — "id" and "volatileId" share a strategy but are worlds apart in stability.
public sealed record RawCandidate
{
    public string Strategy { get; init; } = "";
    public string Value { get; init; } = "";
    public string Kind { get; init; } = "";
}

// The overlay's status() response — used to decide whether re-injection is needed.
public sealed record OverlayStatus
{
    public int Version { get; init; }
    public bool Enabled { get; init; }
    public int Pending { get; init; }
    public string Url { get; init; } = "";
    public string Title { get; init; } = "";
}
