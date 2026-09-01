using Microsoft.AspNetCore.Mvc;
using WebTestToolkit.Api.Dtos;
using WebTestToolkit.Api.Services;
using WebTestToolkit.Contracts.Models;
using WebTestToolkit.Execution.Generation;
using WebTestToolkit.Llm.Skills;

namespace WebTestToolkit.Api.Controllers;

[ApiController]
[Route("api/flows")]
public class FlowsController : ControllerBase
{
    private readonly HybridTestCodeGenerator _generator;
    private readonly EdgeCaseGenerationSkill _edgeCaseSkill;
    private readonly FlowStore _flows;

    public FlowsController(
        HybridTestCodeGenerator generator, EdgeCaseGenerationSkill edgeCaseSkill, FlowStore flows)
    {
        _generator = generator;
        _edgeCaseSkill = edgeCaseSkill;
        _flows = flows;
    }

    // --- Saved flows (P19) ------------------------------------------------------------
    //
    // A recorded flow used to exist only in the live inspect session and the browser tab that
    // recorded it, so it could never be re-generated after the UI it targets changed. These
    // three endpoints plus save-on-stop (InspectController) are what make that possible.

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SavedFlowSummary>>> List(CancellationToken ct) =>
        Ok(await _flows.ListAsync(ct));

    [HttpGet("{name}")]
    public async Task<ActionResult<TestFlow>> Get(string name, CancellationToken ct)
    {
        var flow = await _flows.GetAsync(name, ct);
        return flow is null ? NotFound(new { error = $"No saved flow named '{name}'." }) : Ok(flow);
    }

    // Explicit save, for a flow assembled or edited outside an inspect session (the built-in
    // sample, or an accepted edge case) — save-on-stop covers the recorded path.
    [HttpPut("{name}")]
    public async Task<ActionResult<SavedFlowSummary>> Save(string name, [FromBody] TestFlow flow, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(flow.Name))
            flow.Name = name;

        await _flows.SaveAsync(flow, ct);
        return Ok(new SavedFlowSummary(flow.Name, flow.StartUrl, flow.Steps.Count, DateTimeOffset.UtcNow));
    }

    [HttpDelete("{name}")]
    public async Task<IActionResult> Delete(string name, CancellationToken ct) =>
        await _flows.DeleteAsync(name, ct)
            ? NoContent()
            : NotFound(new { error = $"No saved flow named '{name}'." });

    // Same pipeline as generate, including the sandbox compile, but writes nothing —
    // this is what lets the UI show a verified diff before anything touches tests/.
    [HttpPost("preview")]
    public Task<ActionResult<GenerateFlowResponse>> Preview([FromBody] GenerateFlowRequest request, CancellationToken ct) =>
        RunAsync(request, writeToProject: false, ct);

    [HttpPost("generate")]
    public Task<ActionResult<GenerateFlowResponse>> Generate([FromBody] GenerateFlowRequest request, CancellationToken ct) =>
        RunAsync(request, writeToProject: true, ct);

    private async Task<ActionResult<GenerateFlowResponse>> RunAsync(GenerateFlowRequest request, bool writeToProject, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Flow.Name))
            return BadRequest(new { error = "Flow name is required." });

        if (request.Flow.Steps.Count == 0)
            return BadRequest(new { error = "Flow has no steps." });

        var options = new GenerationOptions(request.UseLlm, request.MaxRepairAttempts, writeToProject);
        var result = await _generator.GenerateAsync(request.Flow, options, progress: null, ct);

        return Ok(GenerateFlowResponse.From(result));
    }

    // Speculative output, reviewed before it touches anything — same posture as the label
    // suggestion in Inspect. Each returned option already carries a complete TestFlow
    // (EdgeCaseFlowBuilder), so "accept" is just POSTing option.flow to preview/generate
    // like any other flow; nothing here writes or compiles.
    [HttpPost("edge-cases")]
    public async Task<ActionResult<EdgeCaseResponse>> EdgeCases([FromBody] EdgeCaseRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Flow.Name))
            return BadRequest(new { error = "Flow name is required." });

        if (request.Flow.Steps.Count == 0)
            return BadRequest(new { error = "Flow has no steps." });

        var input = new EdgeCaseGenerationInput(
            request.Flow.Name,
            request.Flow.StartUrl,
            request.Flow.Steps
                .OrderBy(s => s.Order)
                .Select(s => new EdgeCaseStepSummary(
                    s.Order, s.ActionType.ToString(), s.Label, s.PageName,
                    !string.IsNullOrEmpty(s.InputValue), !string.IsNullOrEmpty(s.ExpectedText)))
                .ToList());

        var result = await _edgeCaseSkill.RunAsync(input, ct);

        if (!result.IsSuccess)
            return Ok(new EdgeCaseResponse(false, [], result.Reason ?? "Edge-case suggestions are unavailable."));

        var options = result.Value!.EdgeCases
            .Select(s => new EdgeCaseOptionDto(s.NameSuffix, s.Title, s.Rationale, EdgeCaseFlowBuilder.Build(request.Flow, s)))
            .ToList();

        return Ok(new EdgeCaseResponse(true, options, null));
    }
}
