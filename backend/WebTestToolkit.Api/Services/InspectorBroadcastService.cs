using Microsoft.AspNetCore.SignalR;
using WebTestToolkit.Api.Hubs;
using WebTestToolkit.Inspector;

namespace WebTestToolkit.Api.Services;

// Bridges the Inspector (which knows nothing about ASP.NET) to SignalR.
//
// Polling rather than push, because WebDriver has no event channel — the only way to learn
// that the user clicked something is to ask the page. The overlay queues events in
// sessionStorage between polls, so nothing is lost in the gaps; the poll interval only
// affects how quickly a step appears in the UI, not whether it is captured.
public sealed class InspectorBroadcastService : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    private readonly InspectorSessionManager _sessions;
    private readonly IHubContext<InspectHub> _hub;
    private readonly ILogger<InspectorBroadcastService> _logger;

    private readonly Dictionary<string, InspectorSessionState> _lastBroadcastState = new(StringComparer.Ordinal);
    private DateTimeOffset _nextSweep = DateTimeOffset.UtcNow;

    public InspectorBroadcastService(
        InspectorSessionManager sessions,
        IHubContext<InspectHub> hub,
        ILogger<InspectorBroadcastService> logger)
    {
        _sessions = sessions;
        _hub = hub;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_sessions.Options.PollInterval);

        while (await SafeWaitAsync(timer, stoppingToken))
        {
            try
            {
                await PumpAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // One bad session must not take the pump down for every other session.
                _logger.LogError(ex, "Inspect broadcast pump failed; continuing");
            }
        }
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task PumpAsync(CancellationToken ct)
    {
        foreach (var session in _sessions.Pollable())
        {
            var events = await session.PollAsync(ct);

            foreach (var captured in events)
            {
                await _hub.Clients
                    .Group(InspectHub.GroupFor(session.Id))
                    .SendAsync(InspectHub.StepCapturedMethod, captured, ct);
            }
        }

        // State changes originate here too: PollAsync is where we discover the user closed
        // the Chrome window, and the UI has no other way to find that out.
        var all = _sessions.All();

        // Sessions the manager has swept away can never change state again; without this the
        // dictionary would grow for the lifetime of the process.
        foreach (var stale in _lastBroadcastState.Keys.Except(all.Select(s => s.Id)).ToList())
            _lastBroadcastState.Remove(stale);

        foreach (var session in all)
        {
            var state = session.State;
            if (_lastBroadcastState.TryGetValue(session.Id, out var previous) && previous == state)
                continue;

            _lastBroadcastState[session.Id] = state;
            await _hub.Clients
                .Group(InspectHub.GroupFor(session.Id))
                .SendAsync(InspectHub.SessionStateMethod, session.Describe(), ct);
        }

        if (DateTimeOffset.UtcNow >= _nextSweep)
        {
            _nextSweep = DateTimeOffset.UtcNow + SweepInterval;
            await _sessions.SweepAsync(ct);
        }
    }
}
