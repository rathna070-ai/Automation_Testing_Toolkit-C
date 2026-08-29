using Microsoft.AspNetCore.Mvc;
using WebTestToolkit.Api.Dtos;
using WebTestToolkit.Contracts.Models;
using WebTestToolkit.Execution.Generation;
using WebTestToolkit.Inspector;

namespace WebTestToolkit.Api.Controllers;

// Auto-heal is a locator picker plus a single-capture re-inspect session, reusing P7's
// InspectorSession wholesale: /autoheal/start opens a normal inspect session at the broken
// locator's own page, the live feed the user already knows from Inspect shows what gets
// clicked, and /autoheal/apply is the only genuinely new step — rewriting one JSON entry.
[ApiController]
[Route("api")]
public class AutoHealController : ControllerBase
{
    private readonly InspectorSessionManager _sessions;

    public AutoHealController(InspectorSessionManager sessions)
    {
        _sessions = sessions;
    }

    [HttpGet("locators")]
    public ActionResult<IReadOnlyList<LocatorPageDto>> ListLocators()
    {
        var pages = LocatorJsonPatcher.ListPages()
            .Select(page =>
            {
                var locators = LocatorJsonPatcher.Load(page);
                var keys = locators.Locators
                    .Select(kv => new LocatorKeyDto(kv.Key, kv.Value.Strategy, kv.Value.Value))
                    .OrderBy(k => k.Key, StringComparer.Ordinal)
                    .ToList();
                return new LocatorPageDto(page, locators.Url, keys);
            })
            .ToList();

        return Ok(pages);
    }

    // Opens a real Chrome window at the broken locator's own page — an ordinary inspect
    // session under the hood, so it shows up in GET /api/inspect/sessions too and the
    // frontend drives it with the exact same SignalR feed and stop/steps endpoints Inspect
    // uses. The only auto-heal-specific step is /apply below.
    [HttpPost("autoheal/start")]
    public async Task<ActionResult<InspectSessionResponse>> Start(
        [FromBody] AutoHealStartRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Page) || string.IsNullOrWhiteSpace(request.Key))
            return BadRequest(new { error = "A page and locator key are both required." });

        var page = request.Page.Trim();
        var key = request.Key.Trim();

        PageLocators current;
        try
        {
            current = LocatorJsonPatcher.Load(page);
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { error = $"No locator file for page '{page}'." });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        if (!current.Locators.ContainsKey(key))
            return NotFound(new { error = $"Page '{page}' has no locator key '{key}'." });

        if (string.IsNullOrWhiteSpace(current.Url))
        {
            return UnprocessableEntity(new
            {
                error = $"Page '{page}' has no recorded URL to re-open — heal it by hand or re-record the flow."
            });
        }

        try
        {
            var session = await _sessions.StartAsync(
                new InspectorStartRequest($"Auto-heal: {page}.{key}", current.Url, Headless: false), ct);

            return Ok(InspectSessionResponse.From(session));
        }
        catch (InvalidOperationException ex)
        {
            // Concurrent-session limit — same as InspectController's Start.
            return Conflict(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = "Could not start Chrome for the auto-heal session.",
                detail = ex.Message
            });
        }
    }

    // The one write in the whole phase: patch a single key in an existing *.locators.json.
    // No .cs file is ever touched — that's the acceptance test for this entire phase.
    [HttpPost("autoheal/apply")]
    public ActionResult<LocatorKeyDto> Apply([FromBody] AutoHealApplyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Page) || string.IsNullOrWhiteSpace(request.Key) ||
            string.IsNullOrWhiteSpace(request.Strategy) || string.IsNullOrWhiteSpace(request.Value))
        {
            return BadRequest(new { error = "Page, key, strategy, and value are all required." });
        }

        try
        {
            var key = request.Key.Trim();
            var patched = LocatorJsonPatcher.Patch(
                request.Page.Trim(), key,
                new LocatorEntry(request.Strategy.Trim(), request.Value.Trim()));

            var entry = patched.Locators[key];
            return Ok(new LocatorKeyDto(key, entry.Strategy, entry.Value));
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { error = $"No locator file for page '{request.Page}'." });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
