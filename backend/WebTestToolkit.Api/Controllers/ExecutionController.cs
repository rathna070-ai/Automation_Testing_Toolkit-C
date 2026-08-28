using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using WebTestToolkit.Api.Dtos;
using WebTestToolkit.Api.Hubs;
using WebTestToolkit.Api.Services;
using WebTestToolkit.Execution;

namespace WebTestToolkit.Api.Controllers;

// Triggers `dotnet test` against tests/WebTestToolkit.GeneratedTests and reports the result.
// A run takes tens of seconds, so this is fire-and-forget-and-poll: POST returns a run id
// immediately (202), console output streams live over /hubs/run, and GET fetches the current
// state at any time — the same shape the client would need anyway to recover from a missed
// SignalR event or a page refresh mid-run.
[ApiController]
[Route("api/execution")]
public class ExecutionController : ControllerBase
{
    private readonly TestRunSessionManager _runs;
    private readonly IHubContext<RunHub> _hub;
    private readonly ILogger<ExecutionController> _logger;

    public ExecutionController(TestRunSessionManager runs, IHubContext<RunHub> hub, ILogger<ExecutionController> logger)
    {
        _runs = runs;
        _hub = hub;
        _logger = logger;
    }

    [HttpPost("run")]
    public ActionResult<StartRunResponse> Start()
    {
        var session = _runs.Create();
        _ = ExecuteAsync(session);
        return Accepted(new StartRunResponse(session.Id));
    }

    [HttpGet("runs/{id}")]
    public ActionResult<RunResponse> Get(string id)
    {
        var session = _runs.Find(id);
        return session is null ? NotFound(new { error = $"No test run '{id}'." }) : Ok(RunResponse.From(session));
    }

    // Lets the Report page work from a direct visit or a refresh, not only right after a Run
    // page kicked off a run in the same browser session.
    [HttpGet("runs/latest")]
    public ActionResult<RunResponse> Latest()
    {
        var session = _runs.Latest();
        return session is null
            ? NotFound(new { error = "No test run has been started yet." })
            : Ok(RunResponse.From(session));
    }

    private async Task ExecuteAsync(TestRunSession session)
    {
        var group = RunHub.GroupFor(session.Id);

        // DotnetCli's output arrives synchronously via this callback as the process writes
        // each line — there's nothing to poll, so it's pushed straight to the hub group.
        var progress = new Progress<string>(line =>
        {
            session.AppendLine(line);
            _ = SafeSendAsync(group, RunHub.ConsoleLineMethod, line);
        });

        try
        {
            var result = await TestRunner.RunAsync(progress, CancellationToken.None);
            if (result.Summary is not null)
                session.Complete(result.Summary);
            else
                session.Fault(result.Error ?? "The test run did not produce a result.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Test run {RunId} threw unexpectedly", session.Id);
            session.Fault(ex.Message);
        }

        await SafeSendAsync(group, RunHub.RunCompletedMethod, RunResponse.From(session));
    }

    // A dropped SignalR broadcast must never take the actual test run down with it — the
    // client can always recover the result via GET.
    private async Task SafeSendAsync(string group, string method, object arg)
    {
        try
        {
            await _hub.Clients.Group(group).SendAsync(method, arg);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast {Method} to run group {Group}", method, group);
        }
    }
}
