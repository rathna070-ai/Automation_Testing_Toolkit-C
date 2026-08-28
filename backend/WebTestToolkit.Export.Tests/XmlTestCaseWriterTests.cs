using System.Xml.Linq;
using WebTestToolkit.Contracts.Models;

namespace WebTestToolkit.Export.Tests;

public class XmlTestCaseWriterTests
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
                LastRunStatus = ScenarioOutcome.Passed,
                Steps =
                [
                    new TestCaseStep(1, "Enter a valid username.", "tomsmith", "The Username field contains the value."),
                    new TestCaseStep(2, "Click the Login button.", null, "The form submits.")
                ]
            }
        ]
    };

    [Test]
    public void Write_ProducesTheDocumentedShape()
    {
        var doc = XmlTestCaseWriter.Write(SampleSuite());
        var suite = doc.Root!;

        Assert.That(suite.Name.LocalName, Is.EqualTo("TestSuite"));
        Assert.That(suite.Attribute("name")!.Value, Is.EqualTo("Login"));

        var testCase = suite.Element("TestCase")!;
        Assert.That(testCase.Attribute("id")!.Value, Is.EqualTo("TC-001"));
        Assert.That(testCase.Attribute("priority")!.Value, Is.EqualTo("high"));
        Assert.That(testCase.Attribute("source")!.Value, Is.EqualTo("recorded"));
        Assert.That(testCase.Attribute("lastRunStatus")!.Value, Is.EqualTo("passed"));
        Assert.That(testCase.Element("Title")!.Value, Is.EqualTo("Successful login"));

        var steps = testCase.Element("Steps")!.Elements("Step").ToList();
        Assert.That(steps, Has.Count.EqualTo(2));
        Assert.That(steps[0].Attribute("number")!.Value, Is.EqualTo("1"));
        Assert.That(steps[0].Element("TestData")!.Value, Is.EqualTo("tomsmith"));
        Assert.That(steps[0].Element("ExpectedResult")!.Value, Is.EqualTo("The Username field contains the value."));
    }

    // A step with no test data (a click) shouldn't grow an empty <TestData/> element that
    // looks like a data-carrying step to whatever reads this later.
    [Test]
    public void Write_OmitsTestDataElementWhenStepHasNone()
    {
        var doc = XmlTestCaseWriter.Write(SampleSuite());
        var secondStep = doc.Root!.Element("TestCase")!.Element("Steps")!.Elements("Step").ElementAt(1);

        Assert.That(secondStep.Element("TestData"), Is.Null);
    }

    [Test]
    public void Write_DefaultsLastRunStatusToNotRun_WhenNeverRun()
    {
        var suite = SampleSuite();
        suite.TestCases[0].LastRunStatus = null;

        var doc = XmlTestCaseWriter.Write(suite);

        Assert.That(doc.Root!.Element("TestCase")!.Attribute("lastRunStatus")!.Value, Is.EqualTo("notRun"));
    }

    // The bytes are what actually gets downloaded — round-trip them through XDocument.Load
    // to prove the file is valid XML with UTF-8 encoding that matches its own declaration,
    // not just that the in-memory XElement tree looks right.
    [Test]
    public void WriteBytes_ProducesParsableWellFormedXml()
    {
        var bytes = XmlTestCaseWriter.WriteBytes(SampleSuite());

        using var stream = new MemoryStream(bytes);
        var reloaded = XDocument.Load(stream);

        Assert.That(reloaded.Declaration!.Encoding, Is.EqualTo("utf-8"));
        Assert.That(reloaded.Root!.Name.LocalName, Is.EqualTo("TestSuite"));
        Assert.That(reloaded.Root.Element("TestCase")!.Element("Title")!.Value, Is.EqualTo("Successful login"));
    }
}
