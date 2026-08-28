using ClosedXML.Excel;
using WebTestToolkit.Contracts.Models;

namespace WebTestToolkit.Export.Tests;

public class ExcelTestCaseWriterTests
{
    private static TestCaseSuite SampleSuite() => new()
    {
        FlowName = "Login",
        StartUrl = "https://the-internet.herokuapp.com/login",
        GeneratedAtUtc = new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc),
        TestCases =
        [
            new TestCaseDocument
            {
                Id = "TC-001",
                Title = "Successful login",
                Precondition = "User is on the login page.",
                Priority = TestCasePriority.High,
                Source = TestCaseSource.Recorded,
                LastRunStatus = null,
                Steps =
                [
                    new TestCaseStep(1, "Enter a valid username.", "tomsmith", "The Username field contains the value."),
                    new TestCaseStep(2, "Click the Login button.", null, "The form submits.")
                ]
            }
        ]
    };

    // The bytes are what a user opens in Excel — round-trip through ClosedXML's own reader
    // to prove the workbook is actually well-formed, not just that no exception was thrown
    // while writing it.
    [Test]
    public void WriteBytes_ProducesAWorkbookThatReopensWithBothSheets()
    {
        var bytes = ExcelTestCaseWriter.WriteBytes(SampleSuite());

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);

        Assert.That(workbook.Worksheets.Contains("Test Cases"), Is.True);
        Assert.That(workbook.Worksheets.Contains("Summary"), Is.True);
    }

    [Test]
    public void TestCasesSheet_HasTheDocumentedHeaderRow()
    {
        var bytes = ExcelTestCaseWriter.WriteBytes(SampleSuite());
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet("Test Cases");

        var expected = new[]
        {
            "Test Case ID", "Title", "Precondition", "Priority", "Source",
            "Step #", "Action", "Test Data", "Expected Result", "Last Run Status"
        };

        for (var col = 0; col < expected.Length; col++)
            Assert.That(sheet.Cell(1, col + 1).GetString(), Is.EqualTo(expected[col]));
    }

    [Test]
    public void TestCasesSheet_HasOneRowPerStep_WithCaseFieldsRepeated()
    {
        var bytes = ExcelTestCaseWriter.WriteBytes(SampleSuite());
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet("Test Cases");

        // Row 1 is the header; two steps -> rows 2 and 3.
        Assert.That(sheet.Cell(2, 1).GetString(), Is.EqualTo("TC-001"));
        Assert.That(sheet.Cell(2, 6).GetValue<int>(), Is.EqualTo(1));
        Assert.That(sheet.Cell(2, 8).GetString(), Is.EqualTo("tomsmith"));
        Assert.That(sheet.Cell(2, 10).GetString(), Is.EqualTo("Not run"));

        Assert.That(sheet.Cell(3, 1).GetString(), Is.EqualTo("TC-001"), "case-level fields must repeat on every step row");
        Assert.That(sheet.Cell(3, 6).GetValue<int>(), Is.EqualTo(2));
        Assert.That(sheet.Cell(3, 8).GetString(), Is.Empty, "a step with no TestData must render as blank, not \"null\"");
    }

    [Test]
    public void SummarySheet_ReportsFlowAndCounts()
    {
        var bytes = ExcelTestCaseWriter.WriteBytes(SampleSuite());
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet("Summary");

        var rows = sheet.RowsUsed()
            .ToDictionary(r => r.Cell(1).GetString(), r => r.Cell(2).GetString());

        Assert.That(rows["Flow"], Is.EqualTo("Login"));
        Assert.That(rows["Test cases"], Is.EqualTo("1"));
        Assert.That(rows["Total steps"], Is.EqualTo("2"));
        Assert.That(rows["Recorded"], Is.EqualTo("1"));
    }
}
