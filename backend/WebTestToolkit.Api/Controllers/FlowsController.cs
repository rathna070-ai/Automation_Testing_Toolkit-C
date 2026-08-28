using Microsoft.AspNetCore.Mvc;
using WebTestToolkit.Api.Dtos;
using WebTestToolkit.Execution.Generation;

namespace WebTestToolkit.Api.Controllers;

[ApiController]
[Route("api/flows")]
public class FlowsController : ControllerBase
{
    private readonly HybridTestCodeGenerator _generator;

    public FlowsController(HybridTestCodeGenerator generator)
    {
        _generator = generator;
    }

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
}
