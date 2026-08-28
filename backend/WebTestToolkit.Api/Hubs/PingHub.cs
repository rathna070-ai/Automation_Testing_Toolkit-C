using Microsoft.AspNetCore.SignalR;

namespace WebTestToolkit.Api.Hubs;

// Minimal round-trip proof that SignalR is wired end to end before any real hub
// (inspector/run progress) exists. Safe to delete once those land.
public class PingHub : Hub
{
    public Task<string> Ping() => Task.FromResult("pong");
}
