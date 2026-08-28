using Microsoft.AspNetCore.SignalR;

namespace WebTestToolkit.Api.Hubs;

// Live feed of an inspect session. Clients join the group for the session they are watching,
// so opening two sessions in two browser tabs doesn't cross the streams.
//
// The hub is deliberately push-only — commands (start/stop/pause) go through the REST
// controller, because they need request semantics (validation, status codes, errors) that a
// fire-and-forget hub invocation doesn't give us.
public class InspectHub : Hub
{
    // Client -> server. Group names are the session id, which the client only knows because
    // POST /api/inspect/start returned it.
    public Task Subscribe(string sessionId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(sessionId));

    public Task Unsubscribe(string sessionId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupFor(sessionId));

    public static string GroupFor(string sessionId) => $"inspect:{sessionId}";

    // Server -> client method names, kept here so the C# broadcaster and the TypeScript
    // client have one place to disagree with each other loudly.
    public const string StepCapturedMethod = "stepCaptured";
    public const string SessionStateMethod = "sessionState";
}
