using Microsoft.AspNetCore.Mvc;
using WebTestToolkit.Api.Dtos;
using WebTestToolkit.Api.Services;
using WebTestToolkit.Contracts.Models;
using WebTestToolkit.Execution.Generation;
using WebTestToolkit.Llm.Skills;

namespace WebTestToolkit.Api.Controllers;

[ApiController]
[Route("api")]
public class LlmController : ControllerBase
{
    private readonly ISettingsStore _settingsStore;
    private readonly FailureAnalysisSkill _failureAnalysisSkill;
    private readonly RunFailureAnalysisSkill _runFailureAnalysisSkill;
    private readonly TestRunSessionManager _runs;

    public LlmController(
        ISettingsStore settingsStore,
        FailureAnalysisSkill failureAnalysisSkill,
        RunFailureAnalysisSkill runFailureAnalysisSkill,
        TestRunSessionManager runs)
    {
        _settingsStore = settingsStore;
        _failureAnalysisSkill = failureAnalysisSkill;
        _runFailureAnalysisSkill = runFailureAnalysisSkill;
        _runs = runs;
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

    // Triages the *whole* latest run at once. Per-scenario analysis structurally cannot tell
    // you that five of six failures share one cause, because each call only ever sees one
    // failure — and "how many problems is this actually?" is the first question anyone looking
    // at a wall of red has.
    //
    // Takes no body: the run to analyse is the latest one, and asking the client to carry run
    // state around would let it request an analysis of a run that is no longer current.
    [HttpPost("failures/analyze-run")]
    public async Task<ActionResult<AnalyzeRunResponse>> AnalyzeRun(CancellationToken ct)
    {
        var summary = _runs.Latest()?.Summary;
        if (summary is null)
            return Ok(new AnalyzeRunResponse(false, null, "No test run has completed yet."));

        var failures = summary.Scenarios
            .Where(s => s.Outcome == ScenarioOutcome.Failed)
            .ToList();

        if (failures.Count == 0)
            return Ok(new AnalyzeRunResponse(false, null, "The last run had no failures."));

        var input = new RunFailureAnalysisInput(
            failures
                .Select(f => new RunFailureScenarioInput(f.FeatureName, f.ScenarioName, f.ErrorMessage, f.StackTrace))
                .ToList(),
            // The locator entries on disk, so the model can say "UsernameInput points at
            // #username" rather than only "the element was not found". Cheap: a handful of
            // one-line entries, never source files.
            ReadKnownLocators());

        var result = await _runFailureAnalysisSkill.RunAsync(input, ct);

        return Ok(result.IsSuccess
            ? new AnalyzeRunResponse(true, result.Value, null)
            : new AnalyzeRunResponse(false, null, result.Reason ?? "Analysis is unavailable."));
    }

    private static List<RunFailureLocatorInput> ReadKnownLocators()
    {
        var locators = new List<RunFailureLocatorInput>();
        foreach (var page in LocatorJsonPatcher.ListPages())
        {
            foreach (var (key, entry) in LocatorJsonPatcher.Load(page).Locators)
                locators.Add(new RunFailureLocatorInput(page, key, entry.Strategy, entry.Value));
        }
        return locators;
    }
}
