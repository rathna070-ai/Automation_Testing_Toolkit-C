using Microsoft.AspNetCore.Mvc;
using WebTestToolkit.Api.Dtos;
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

    private readonly TestCaseProseSkill _proseSkill;

    public ExportController(TestCaseProseSkill proseSkill)
    {
        _proseSkill = proseSkill;
    }

    // Preview, not a download — the UI shows this table before the user commits to
    // exporting a file, same shape /api/flows/preview gives Generate.
    [HttpPost("testcases/preview")]
    public async Task<ActionResult<TestCaseSuite>> Preview([FromBody] ExportTestCasesRequest request, CancellationToken ct)
    {
        var validation = Validate(request.Flow);
        if (validation is not null)
            return validation;

        var suite = await TestCaseSuiteBuilder.BuildAsync(request.Flow, _proseSkill, request.UseLlm, ct);
        return Ok(suite);
    }

    [HttpPost("testcases/xlsx")]
    public async Task<IActionResult> Xlsx([FromBody] ExportTestCasesRequest request, CancellationToken ct)
    {
        var validation = Validate(request.Flow);
        if (validation is not null)
            return validation;

        var suite = await TestCaseSuiteBuilder.BuildAsync(request.Flow, _proseSkill, request.UseLlm, ct);
        var bytes = ExcelTestCaseWriter.WriteBytes(suite);
        return File(bytes, XlsxContentType, $"{FileSafeName(request.Flow.Name)}-test-cases.xlsx");
    }

    [HttpPost("testcases/xml")]
    public async Task<IActionResult> Xml([FromBody] ExportTestCasesRequest request, CancellationToken ct)
    {
        var validation = Validate(request.Flow);
        if (validation is not null)
            return validation;

        var suite = await TestCaseSuiteBuilder.BuildAsync(request.Flow, _proseSkill, request.UseLlm, ct);
        var bytes = XmlTestCaseWriter.WriteBytes(suite);
        return File(bytes, XmlContentType, $"{FileSafeName(request.Flow.Name)}-test-cases.xml");
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
