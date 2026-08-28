using Microsoft.AspNetCore.SignalR;

namespace WebTestToolkit.Api.Hubs;

// Live console output for one dotnet-test run. Same shape as InspectHub: push-only, clients
// join a group named after the run id they're watching. Unlike Inspect there's no polling
// service behind this — ExecutionController pushes each line directly as DotnetCli's
// IProgress<string> callback fires, since the console output is already arriving
// synchronously; there's nothing to poll.
public class RunHub : Hub
{
    public Task Subscribe(string runId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(runId));

    public Task Unsubscribe(string runId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupFor(runId));

    public static string GroupFor(string runId) => $"run:{runId}";

    public const string ConsoleLineMethod = "consoleLine";
    public const string RunCompletedMethod = "runCompleted";
}
