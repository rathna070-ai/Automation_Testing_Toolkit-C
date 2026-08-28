using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WebTestToolkit.Inspector;

public sealed class InspectorOptions
{
    // Fast enough that a click feels live in the UI, slow enough that we're not hammering
    // the WebDriver HTTP endpoint while the user reads the page.
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(400);

    // A forgotten session is a Chrome window sitting on the user's desktop forever
    // (plan risk #5). Measured from the last captured action, not from session start.
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(30);

    // Each session is a real browser. This is a guard against a runaway client, not a
    // feature — nobody hand-drives four windows at once.
    public int MaxConcurrentSessions { get; set; } = 3;

    // Stopped and faulted sessions stick around so the UI can still read their captured
    // steps after the browser closes; this is when they finally get dropped.
    public TimeSpan CompletedRetention { get; set; } = TimeSpan.FromHours(2);
}

// Owns every live browser the toolkit has opened. Singleton: sessions outlive the request
// that created them, which is the whole point — the user starts inspecting in one request
// and asks for the flow several requests later.
public sealed class InspectorSessionManager : IDisposable
{
    private readonly ConcurrentDictionary<string, InspectorSession> _sessions = new(StringComparer.Ordinal);
    private readonly ILogger<InspectorSessionManager> _logger;
    private readonly IOptions<InspectorOptions> _options;

    public InspectorSessionManager(IOptions<InspectorOptions> options, ILogger<InspectorSessionManager> logger)
    {
        _options = options;
        _logger = logger;
    }

    public InspectorOptions Options => _options.Value;

    public async Task<InspectorSession> StartAsync(InspectorStartRequest request, CancellationToken ct)
    {
        var live = _sessions.Values.Count(s =>
            s.State is InspectorSessionState.Running or InspectorSessionState.Paused);

        if (live >= Options.MaxConcurrentSessions)
        {
            throw new InvalidOperationException(
                $"{live} inspect sessions are already open (limit {Options.MaxConcurrentSessions}). " +
                "Stop one before starting another.");
        }

        var session = await InspectorSession.StartAsync(request, ct);
        _sessions[session.Id] = session;

        _logger.LogInformation("Inspect session {SessionId} started at {Url}", session.Id, request.StartUrl);
        return session;
    }

    public InspectorSession? Find(string id) =>
        _sessions.TryGetValue(id, out var session) ? session : null;

    public IReadOnlyList<InspectorSession> All() => _sessions.Values.ToList();

    // What the broadcast service iterates. Paused sessions are included deliberately: the
    // URL can still change while paused, and we want the UI to reflect that.
    public IReadOnlyList<InspectorSession> Pollable() => _sessions.Values
        .Where(s => s.State is InspectorSessionState.Running or InspectorSessionState.Paused)
        .ToList();

    public async Task<bool> StopAsync(string id, CancellationToken ct)
    {
        var session = Find(id);
        if (session is null)
            return false;

        await session.StopAsync(ct);
        _logger.LogInformation("Inspect session {SessionId} stopped with {Steps} steps", id, session.Steps.Count);
        return true;
    }

    // Closes the browser but keeps the captured steps readable; Sweep drops the record later.
    public async Task SweepAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var session in _sessions.Values)
        {
            if (session.State is InspectorSessionState.Running or InspectorSessionState.Paused)
            {
                if (now - session.LastActivityUtc > Options.IdleTimeout)
                {
                    _logger.LogInformation(
                        "Inspect session {SessionId} idle for over {Timeout}; closing its browser",
                        session.Id, Options.IdleTimeout);
                    await session.StopAsync(ct);
                }
                continue;
            }

            if (now - session.LastActivityUtc > Options.CompletedRetention &&
                _sessions.TryRemove(session.Id, out var removed))
            {
                removed.Dispose();
            }
        }
    }

    // Called on host shutdown. Without this, Ctrl+C on the API leaves orphaned Chrome and
    // chromedriver processes behind on the user's machine.
    public void Dispose()
    {
        foreach (var session in _sessions.Values)
        {
            try
            {
                session.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to dispose inspect session {SessionId}", session.Id);
            }
        }

        _sessions.Clear();
    }
}
