using Microsoft.AspNetCore.Mvc;
using WebTestToolkit.Api.Dtos;
using WebTestToolkit.Contracts.Models;
using WebTestToolkit.Inspector;

namespace WebTestToolkit.Api.Controllers;

// Commands for an inspect session. The live feed of captured steps goes over SignalR
// (/hubs/inspect); everything that needs validation and a status code lives here.
[ApiController]
[Route("api/inspect")]
public class InspectController : ControllerBase
{
    private readonly InspectorSessionManager _sessions;

    public InspectController(InspectorSessionManager sessions)
    {
        _sessions = sessions;
    }

    [HttpGet("sessions")]
    public ActionResult<IReadOnlyList<InspectorSessionInfo>> List() =>
        Ok(_sessions.All().Select(s => s.Describe()).ToList());

    [HttpPost("start")]
    public async Task<ActionResult<InspectSessionResponse>> Start(
        [FromBody] StartInspectRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "A flow name is required — it becomes the feature and file name." });

        if (string.IsNullOrWhiteSpace(request.StartUrl))
            return BadRequest(new { error = "A start URL is required." });

        try
        {
            var session = await _sessions.StartAsync(
                new InspectorStartRequest(request.Name.Trim(), request.StartUrl.Trim(), request.Headless), ct);

            return Ok(InspectSessionResponse.From(session));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            // Concurrent-session limit.
            return Conflict(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            // Almost always "Chrome could not be launched" — a missing browser, or Selenium
            // Manager unable to reach the network to fetch a driver on first run. Say so
            // plainly rather than returning an opaque 500.
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = "Could not start Chrome for the inspect session.",
                detail = ex.Message
            });
        }
    }

    [HttpGet("{id}")]
    public ActionResult<InspectSessionResponse> Get(string id)
    {
        var session = _sessions.Find(id);
        return session is null ? SessionNotFound(id) : Ok(InspectSessionResponse.From(session));
    }

    // Pause/resume. Lets the user get the app into the right state (dismiss a cookie banner,
    // scroll to the right place) without those clicks landing in the flow.
    [HttpPost("{id}/capture")]
    public async Task<ActionResult<InspectorSessionInfo>> SetCapture(
        string id, [FromBody] SetCaptureRequest request, CancellationToken ct)
    {
        var session = _sessions.Find(id);
        if (session is null)
            return SessionNotFound(id);

        await session.SetCaptureEnabledAsync(request.Enabled, ct);
        return Ok(session.Describe());
    }

    [HttpPost("{id}/stop")]
    public async Task<ActionResult<InspectSessionResponse>> Stop(string id, CancellationToken ct)
    {
        var session = _sessions.Find(id);
        if (session is null)
            return SessionNotFound(id);

        await session.StopAsync(ct);
        // Returns the captured steps, not just an ack — "Stop Inspect" is immediately
        // followed by "Generate", and the steps are what that needs.
        return Ok(InspectSessionResponse.From(session));
    }

    [HttpPatch("{id}/steps/{sequence:int}")]
    public ActionResult<InspectSessionResponse> UpdateStep(
        string id, int sequence, [FromBody] UpdateStepRequest request)
    {
        var session = _sessions.Find(id);
        if (session is null)
            return SessionNotFound(id);

        if (!session.UpdateStep(sequence, request.ToEdit()))
            return NotFound(new { error = $"Step {sequence} is not part of session '{id}'." });

        return Ok(InspectSessionResponse.From(session));
    }

    [HttpDelete("{id}/steps/{sequence:int}")]
    public ActionResult<InspectSessionResponse> DeleteStep(string id, int sequence)
    {
        var session = _sessions.Find(id);
        if (session is null)
            return SessionNotFound(id);

        if (!session.RemoveStep(sequence))
            return NotFound(new { error = $"Step {sequence} is not part of session '{id}'." });

        return Ok(InspectSessionResponse.From(session));
    }

    // The handoff to the rest of the toolkit: the result posts straight to
    // /api/flows/preview or /api/flows/generate.
    [HttpGet("{id}/flow")]
    public ActionResult<TestFlow> Flow(string id)
    {
        var session = _sessions.Find(id);
        return session is null ? SessionNotFound(id) : Ok(session.ToFlow());
    }

    private NotFoundObjectResult SessionNotFound(string id) =>
        NotFound(new { error = $"No inspect session '{id}'. It may have been closed or timed out." });
}
