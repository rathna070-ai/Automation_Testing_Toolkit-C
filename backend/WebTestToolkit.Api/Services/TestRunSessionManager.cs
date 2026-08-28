using System.Collections.Concurrent;
using WebTestToolkit.Contracts.Models;

namespace WebTestToolkit.Api.Services;

public enum TestRunStatus { Running, Completed, Faulted }

// One triggered `dotnet test` run. A run takes tens of seconds (a two-scenario suite already
// takes 20-40s against a real site), so this exists purely so a client that subscribes to the
// SignalR group a moment late — or reconnects, or just refreshes the Report page — can still
// see every console line and the final result via GET, not only via the live push.
public sealed class TestRunSession
{
    private readonly List<string> _consoleLines = [];
    private readonly object _lock = new();

    public string Id { get; } = Guid.NewGuid().ToString("n");
    public DateTimeOffset StartedUtc { get; } = DateTimeOffset.UtcNow;
    public TestRunStatus Status { get; private set; } = TestRunStatus.Running;
    public RunSummary? Summary { get; private set; }
    public string? Error { get; private set; }

    public void AppendLine(string line)
    {
        lock (_lock) _consoleLines.Add(line);
    }

    public IReadOnlyList<string> ConsoleLines()
    {
        lock (_lock) return _consoleLines.ToList();
    }

    public void Complete(RunSummary summary)
    {
        Summary = summary;
        Status = TestRunStatus.Completed;
    }

    public void Fault(string error)
    {
        Error = error;
        Status = TestRunStatus.Faulted;
    }
}

// Singleton, in-memory, no retention sweep — unlike InspectorSessionManager's browser
// sessions, a TestRunSession holds nothing but strings and a summary, so there's no runaway
// resource (a Chrome window, a file handle) to bound the lifetime of.
public sealed class TestRunSessionManager
{
    private readonly ConcurrentDictionary<string, TestRunSession> _sessions = new(StringComparer.Ordinal);
    private volatile string? _latestId;

    public TestRunSession Create()
    {
        var session = new TestRunSession();
        _sessions[session.Id] = session;
        _latestId = session.Id;
        return session;
    }

    public TestRunSession? Find(string id) =>
        _sessions.TryGetValue(id, out var session) ? session : null;

    public TestRunSession? Latest() => _latestId is null ? null : Find(_latestId);
}
