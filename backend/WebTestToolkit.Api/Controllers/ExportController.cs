using Microsoft.AspNetCore.Mvc;
using WebTestToolkit.Api.Dtos;
using WebTestToolkit.Api.Services;
using WebTestToolkit.Contracts.Models;
using WebTestToolkit.Export;
using WebTestToolkit.Llm.Skills;

namespace WebTestToolkit.Api.Controllers;

[ApiController]
[Route("api/export")]
public class ExportController : ControllerBase
{
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string XmlContentType = "application/xml";
    private const string ZipContentType = "application/zip";

    private readonly TestCaseProseSkill _proseSkill;
    private readonly TestRunSessionManager _runs;

    public ExportController(TestCaseProseSkill proseSkill, TestRunSessionManager runs)
    {
        _proseSkill = proseSkill;
        _runs = runs;
    }

    // The last run's per-scenario outcomes, so an exported case can say whether it actually
    // passed rather than only what it is supposed to do. Read from the run manager rather than
    // asked for in the request: the client should not have to carry run state around, and the
    // answer is only meaningful if it is the *latest* run.
    private IReadOnlyList<ScenarioResult>? LastRunScenarios() => _runs.Latest()?.Summary?.Scenarios;

    private Task<TestCaseSuite> BuildSuiteAsync(ExportTestCasesRequest request, CancellationToken ct) =>
        TestCaseSuiteBuilder.BuildAsync(
            request.Flow, _proseSkill, request.UseLlm, ct, request.EdgeCaseFlows, LastRunScenarios());

    // Preview, not a download — the UI shows this table before the user commits to
    // exporting a file, same shape /api/flows/preview gives Generate.
    [HttpPost("testcases/preview")]
    public async Task<ActionResult<TestCaseSuite>> Preview([FromBody] ExportTestCasesRequest request, CancellationToken ct)
    {
        var validation = Validate(request.Flow);
        if (validation is not null)
            return validation;

        var suite = await BuildSuiteAsync(request, ct);
        return Ok(suite);
    }

    [HttpPost("testcases/xlsx")]
    public async Task<IActionResult> Xlsx([FromBody] ExportTestCasesRequest request, CancellationToken ct)
    {
        var validation = Validate(request.Flow);
        if (validation is not null)
            return validation;

        var suite = await BuildSuiteAsync(request, ct);
        var bytes = ExcelTestCaseWriter.WriteBytes(suite);
        return File(bytes, XlsxContentType, $"{FileSafeName(request.Flow.Name)}-test-cases.xlsx");
    }

    [HttpPost("testcases/xml")]
    public async Task<IActionResult> Xml([FromBody] ExportTestCasesRequest request, CancellationToken ct)
    {
        var validation = Validate(request.Flow);
        if (validation is not null)
            return validation;

        var suite = await BuildSuiteAsync(request, ct);
        var bytes = XmlTestCaseWriter.WriteBytes(suite);
        return File(bytes, XmlContentType, $"{FileSafeName(request.Flow.Name)}-test-cases.xml");
    }

    // P17: exports the generator's own output (Features/*.feature, Steps/*.cs,
    // PageObjects/*.cs, LocatorRepository/*.locators.json) as a zip, alongside the
    // testcases/xlsx and testcases/xml exports above which cover the documentation view
    // instead. Takes the already-generated files rather than a flow to regenerate from — see
    // ExportGeneratedFilesRequest's own comment for why.
    [HttpPost("generated-files/zip")]
    public ActionResult Zip([FromBody] ExportGeneratedFilesRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FlowName))
            return BadRequest(new { error = "A flow name is required." });
        if (request.Files.Count == 0)
            return BadRequest(new { error = "No files to export." });

        var bytes = GeneratedFilesZipWriter.WriteBytes(request.Files);
        return File(bytes, ZipContentType, $"{FileSafeName(request.FlowName)}-generated.zip");
    }

    private BadRequestObjectResult? Validate(TestFlow flow)
    {
        if (string.IsNullOrWhiteSpace(flow.Name))
            return BadRequest(new { error = "Flow name is required." });
        if (flow.Steps.Count == 0)
            return BadRequest(new { error = "Flow has no steps." });
        return null;
    }

    private static string FileSafeName(string flowName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(flowName.Select(c => invalid.Contains(c) ? '-' : c).ToArray());
        return cleaned.Length == 0 ? "flow" : cleaned;
    }
}
