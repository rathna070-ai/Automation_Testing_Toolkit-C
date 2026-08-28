using WebTestToolkit.Contracts.Models;
using WebTestToolkit.Inspector;

namespace WebTestToolkit.Api.Dtos;

public record StartInspectRequest(string Name, string StartUrl, bool Headless = false);

public record SetCaptureRequest(bool Enabled);

public record UpdateStepRequest(
    ActionType? ActionType = null,
    string? Label = null,
    string? LocatorKey = null,
    string? InputValue = null,
    string? ExpectedText = null,
    string? LocatorStrategy = null,
    string? LocatorValue = null)
{
    public InspectorStepEdit ToEdit() => new(
        ActionType, Label, LocatorKey, InputValue, ExpectedText, LocatorStrategy, LocatorValue);
}

// Session plus the steps captured so far, so the UI can render everything from a single
// call after a reload — the SignalR feed only carries what happens from now on.
public record InspectSessionResponse(
    InspectorSessionInfo Session,
    IReadOnlyList<InspectorEvent> Steps)
{
    public static InspectSessionResponse From(InspectorSession session) =>
        new(session.Describe(), session.Steps);
}

// Never a non-2xx for "no key configured" or "Groq had a bad day" — same convention as
// AnalyzeFailureResponse. A missing suggestion is an ordinary outcome the caller shows by
// just leaving the deterministic label in place, not an error to surface.
public record SuggestLabelResponse(bool Available, string? Label, string? UnavailableReason);
