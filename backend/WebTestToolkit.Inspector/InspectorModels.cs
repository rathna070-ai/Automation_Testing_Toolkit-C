using WebTestToolkit.Contracts.Models;

namespace WebTestToolkit.Inspector;

public enum InspectorSessionState
{
    Starting,
    Running,
    // Browser still open, overlay told to stop recording. The user can keep clicking around
    // without polluting the flow — useful for getting the app into the right state first.
    Paused,
    // Session finished normally; the browser has been closed.
    Stopped,
    // Something went wrong — usually the user closed the Chrome window themselves.
    Faulted
}

// One captured action, already ranked and labeled, ready to be shown live in the UI and
// (with whatever the user edits) turned into a TestStep.
public sealed record InspectorEvent
{
    public required string SessionId { get; init; }
    public required int Sequence { get; init; }
    public required ActionType ActionType { get; init; }
    public required string Url { get; init; }
    public required string PageName { get; init; }
    public string LocatorKey { get; init; } = "";
    public string SuggestedLabel { get; init; } = "";
    public string? InputValue { get; init; }
    public string? ExpectedText { get; init; }
    public CapturedElement? Element { get; init; }
    public DateTimeOffset CapturedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    // Surfaced so the UI can warn before the user builds a flow on a locator that will not
    // survive the next deploy, rather than finding out weeks later.
    public int LocatorScore => Element?.BestLocator?.Score ?? 0;
}

public sealed record InspectorSessionInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string StartUrl { get; init; }
    public required InspectorSessionState State { get; init; }
    public required int StepCount { get; init; }
    public required DateTimeOffset StartedUtc { get; init; }
    public required DateTimeOffset LastActivityUtc { get; init; }
    public string? CurrentUrl { get; init; }
    public string? FaultReason { get; init; }
}

public sealed record InspectorStartRequest(string Name, string StartUrl, bool Headless = false);
