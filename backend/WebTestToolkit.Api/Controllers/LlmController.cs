using Microsoft.AspNetCore.Mvc;
using WebTestToolkit.Api.Dtos;
using WebTestToolkit.Api.Services;
using WebTestToolkit.Contracts.Models;
using WebTestToolkit.Llm.Skills;

namespace WebTestToolkit.Api.Controllers;

[ApiController]
[Route("api")]
public class LlmController : ControllerBase
{
    private readonly ISettingsStore _settingsStore;
    private readonly FailureAnalysisSkill _failureAnalysisSkill;

    public LlmController(ISettingsStore settingsStore, FailureAnalysisSkill failureAnalysisSkill)
    {
        _settingsStore = settingsStore;
        _failureAnalysisSkill = failureAnalysisSkill;
    }

    [HttpGet("llm/status")]
    public async Task<ActionResult<LlmStatusResponse>> Status(CancellationToken ct)
    {
        var settings = await _settingsStore.GetAsync(ct);
        return Ok(new LlmStatusResponse(!string.IsNullOrWhiteSpace(settings.GroqApiKey), settings.GroqModel));
    }

    // Never returns non-2xx for "no key configured" or "Groq had a bad day" — those are
    // ordinary outcomes the caller distinguishes via Available/UnavailableReason, not errors.
    [HttpPost("failures/analyze")]
    public async Task<ActionResult<AnalyzeFailureResponse>> AnalyzeFailure([FromBody] ScenarioResult scenario, CancellationToken ct)
    {
        var result = await _failureAnalysisSkill.RunAsync(scenario, ct);

        return Ok(result.IsSuccess
            ? new AnalyzeFailureResponse(true, result.Value, null)
            : new AnalyzeFailureResponse(false, null, result.Reason ?? "Analysis is unavailable."));
    }
}
