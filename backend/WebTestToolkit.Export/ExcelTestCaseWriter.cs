using ClosedXML.Excel;
using WebTestToolkit.Contracts.Models;

namespace WebTestToolkit.Export;

// One row per step, case-level fields repeated on every row — the layout a manual tester
// actually scans (filter/sort by Test Case ID, read straight down Action/Expected Result),
// not one row per test case with steps packed into a single cell.
public static class ExcelTestCaseWriter
{
    private static readonly string[] Headers =
    {
        "Test Case ID", "Title", "Precondition", "Priority", "Source",
        "Step #", "Action", "Test Data", "Expected Result", "Last Run Status"
    };

    public static byte[] WriteBytes(TestCaseSuite suite)
    {
        using var workbook = new XLWorkbook();

        WriteTestCasesSheet(workbook, suite);
        WriteSummarySheet(workbook, suite);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteTestCasesSheet(XLWorkbook workbook, TestCaseSuite suite)
    {
        var sheet = workbook.Worksheets.Add("Test Cases");

        for (var col = 0; col < Headers.Length; col++)
            sheet.Cell(1, col + 1).Value = Headers[col];
        sheet.Row(1).Style.Font.Bold = true;

        var row = 2;
        foreach (var testCase in suite.TestCases)
        {
            foreach (var step in testCase.Steps)
            {
                sheet.Cell(row, 1).Value = testCase.Id;
                sheet.Cell(row, 2).Value = testCase.Title;
                sheet.Cell(row, 3).Value = testCase.Precondition;
                sheet.Cell(row, 4).Value = testCase.Priority.ToString();
                sheet.Cell(row, 5).Value = testCase.Source.ToString();
                sheet.Cell(row, 6).Value = step.Number;
                sheet.Cell(row, 7).Value = step.Action;
                sheet.Cell(row, 8).Value = step.TestData ?? "";
                sheet.Cell(row, 9).Value = step.ExpectedResult;
                sheet.Cell(row, 10).Value = testCase.LastRunStatus?.ToString() ?? "Not run";
                row++;
            }
        }

        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents();
        // AdjustToContents has no sense of "too wide" — a long Action/ExpectedResult sentence
        // would otherwise stretch that column to its full unwrapped length.
        sheet.Column(7).Width = System.Math.Min(sheet.Column(7).Width, 60);
        sheet.Column(9).Width = System.Math.Min(sheet.Column(9).Width, 60);
    }

    private static void WriteSummarySheet(XLWorkbook workbook, TestCaseSuite suite)
    {
        var sheet = workbook.Worksheets.Add("Summary");

        var totalSteps = suite.TestCases.Sum(tc => tc.Steps.Count);
        var bySource = suite.TestCases.GroupBy(tc => tc.Source).ToDictionary(g => g.Key, g => g.Count());

        var rows = new (string Label, string Value)[]
        {
            ("Flow", suite.FlowName),
            ("Start URL", suite.StartUrl),
            ("Generated (UTC)", suite.GeneratedAtUtc.ToString("u")),
            ("Test cases", suite.TestCases.Count.ToString()),
            ("Total steps", totalSteps.ToString()),
            ("Recorded", bySource.GetValueOrDefault(TestCaseSource.Recorded).ToString()),
            ("Edge cases", bySource.GetValueOrDefault(TestCaseSource.EdgeCase).ToString()),
            ("Outline rows", bySource.GetValueOrDefault(TestCaseSource.Outline).ToString())
        };

        for (var i = 0; i < rows.Length; i++)
        {
            sheet.Cell(i + 1, 1).Value = rows[i].Label;
            sheet.Cell(i + 1, 2).Value = rows[i].Value;
        }

        sheet.Column(1).Style.Font.Bold = true;
        sheet.Columns().AdjustToContents();
    }
}
