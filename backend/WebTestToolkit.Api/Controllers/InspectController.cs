using Microsoft.AspNetCore.Mvc;
using WebTestToolkit.Api.Dtos;
using WebTestToolkit.Api.Services;
using WebTestToolkit.Contracts.Models;
using WebTestToolkit.Inspector;
using WebTestToolkit.Llm.Skills;

namespace WebTestToolkit.Api.Controllers;

// Commands for an inspect session. The live feed of captured steps goes over SignalR
// (/hubs/inspect); everything that needs validation and a status code lives here.
[ApiController]
[Route("api/inspect")]
public class InspectController : ControllerBase
{
    private readonly InspectorSessionManager _sessions;
    private readonly StepLabelSuggestionSkill _labelSkill;
    private readonly FlowStore _flows;
    private readonly ILogger<InspectController> _logger;

    public InspectController(
        InspectorSessionManager sessions,
        StepLabelSuggestionSkill labelSkill,
        FlowStore flows,
        ILogger<InspectController> logger)
    {
        _sessions = sessions;
        _labelSkill = labelSkill;
        _flows = flows;
        _logger = logger;
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

        // Persist here rather than at Generate: stopping is the moment the recording is
        // complete, and it is also the last moment the session is guaranteed to still exist
        // (InspectorSessionManager evicts completed sessions after CompletedRetention, and
        // an API restart drops them immediately). Saving at Generate would lose every
        // recording the user did not immediately generate from.
        //
        // A failure to save must not fail the stop — the steps are still in the response, so
        // the immediate Inspect → Generate handoff keeps working either way.
        try
        {
            await _flows.SaveAsync(session.ToFlow(), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not save flow for inspect session {SessionId}", id);
        }

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

    // Read-only: returns a suggestion, never applies it. "Suggestions appear but stay
    // editable" (P8's own acceptance line) means the user reviews it in the label field and
    // PATCHes to commit — same review-before-write posture P5's edge-case/outline skills use
    // for their speculative output, applied here even though a wrong guess is low-stakes.
    [HttpPost("{id}/steps/{sequence:int}/suggest-label")]
    public async Task<ActionResult<SuggestLabelResponse>> SuggestLabel(string id, int sequence, CancellationToken ct)
    {
        var session = _sessions.Find(id);
        if (session is null)
            return SessionNotFound(id);

        var step = session.Steps.FirstOrDefault(s => s.Sequence == sequence);
        if (step is null)
            return NotFound(new { error = $"Step {sequence} is not part of session '{id}'." });

        var element = step.Element;
        var input = new StepLabelSuggestionInput(
            ActionType: step.ActionType.ToString(),
            PageName: step.PageName,
            DeterministicLabel: step.SuggestedLabel,
            TagName: element?.TagName ?? "",
            ElementType: element?.Type,
            VisibleText: element?.VisibleText,
            Placeholder: element?.Placeholder,
            AriaLabel: element?.AriaLabel,
            AssociatedLabelText: element?.AssociatedLabelText,
            AncestorContext: element?.AncestorContext);

        var result = await _labelSkill.RunAsync(input, ct);

        return Ok(result.IsSuccess
            ? new SuggestLabelResponse(true, result.Value!.Label, null)
            : new SuggestLabelResponse(false, null, result.Reason ?? "Suggestion is unavailable."));
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
