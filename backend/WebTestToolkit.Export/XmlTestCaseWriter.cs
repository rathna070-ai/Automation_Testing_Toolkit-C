using System.Text;
using System.Xml.Linq;
using WebTestToolkit.Contracts.Models;

namespace WebTestToolkit.Export;

// A generic, readable schema — not any one test-management tool's import format. The point
// is a shape simple enough to transform into whatever a real tool wants later with an XSLT
// or a small script, not to guess at TestRail/Zephyr/etc.'s actual schema today.
public static class XmlTestCaseWriter
{
    public static XDocument Write(TestCaseSuite suite)
    {
        var root = new XElement("TestSuite",
            new XAttribute("name", suite.FlowName),
            new XAttribute("startUrl", suite.StartUrl),
            new XAttribute("generatedAtUtc", suite.GeneratedAtUtc.ToString("o")),
            suite.TestCases.Select(ToElement));

        return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
    }

    public static byte[] WriteBytes(TestCaseSuite suite)
    {
        using var stream = new MemoryStream();
        // XDocument.Save defaults to UTF-16 over a raw stream unless told the encoding
        // explicitly via an XmlWriter — do that, or the file's own <?xml ... encoding="utf-8"?>
        // declaration lies about its actual bytes.
        using (var writer = System.Xml.XmlWriter.Create(stream, new System.Xml.XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true
        }))
        {
            Write(suite).Save(writer);
        }
        return stream.ToArray();
    }

    private static XElement ToElement(TestCaseDocument testCase) =>
        new("TestCase",
            new XAttribute("id", testCase.Id),
            new XAttribute("priority", Lower(testCase.Priority.ToString())),
            new XAttribute("source", Lower(testCase.Source.ToString())),
            testCase.LastRunStatus is { } outcome
                ? new XAttribute("lastRunStatus", Lower(outcome.ToString()))
                : new XAttribute("lastRunStatus", "notRun"),
            new XElement("Title", testCase.Title),
            new XElement("Precondition", testCase.Precondition),
            new XElement("Steps", testCase.Steps.Select(ToElement)));

    private static XElement ToElement(TestCaseStep step) =>
        new("Step",
            new XAttribute("number", step.Number),
            new XElement("Action", step.Action),
            // Omit entirely rather than an empty element — an XPath consumer testing for the
            // element's presence should learn "this step has no test data" cleanly.
            step.TestData is { Length: > 0 } data ? new XElement("TestData", data) : null,
            new XElement("ExpectedResult", step.ExpectedResult));

    private static string Lower(string pascalCase) =>
        pascalCase.Length == 0 ? pascalCase : char.ToLowerInvariant(pascalCase[0]) + pascalCase[1..];
}
