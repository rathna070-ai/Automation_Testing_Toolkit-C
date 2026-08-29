using WebTestToolkit.Contracts.Models;
using WebTestToolkit.Execution.Generation;

namespace WebTestToolkit.Execution.Tests;

public class MsBuildErrorParserTests
{
    // Shape captured from a real `dotnet build` run with -p:GenerateFullPaths=true.
    private const string SampleOutput = """
        Determining projects to restore...
        C:\sandbox\GeneratedTests\Steps\LoginSteps.cs(23,9): error CS1061: 'LoginPage' does not contain a definition for 'ClickSubmit' and no accessible extension method 'ClickSubmit' accepting a first argument of type 'LoginPage' could be found [C:\sandbox\GeneratedTests\WebTestToolkit.GeneratedTests.csproj]
        C:\sandbox\GeneratedTests\PageObjects\LoginPage.cs(10,5): error CS0246: The type or namespace name 'Foo' could not be found [C:\sandbox\GeneratedTests\WebTestToolkit.GeneratedTests.csproj]
            Build FAILED.
        """;

    [Test]
    public void ParsesPositionalErrors_WithRelativePaths()
    {
        var issues = MsBuildErrorParser.Parse(SampleOutput, @"C:\sandbox\GeneratedTests");

        Assert.That(issues, Has.Count.EqualTo(2));

        var first = issues.First(i => i.Code == "CS1061");
        Assert.That(first.Source, Is.EqualTo(IssueSource.Compiler));
        Assert.That(first.File, Is.EqualTo("Steps/LoginSteps.cs"), "Absolute sandbox paths must not leak into the prompt.");
        Assert.That(first.Line, Is.EqualTo(23));
        Assert.That(first.Message, Does.Contain("ClickSubmit"));
    }

    [Test]
    public void DeduplicatesRepeatedErrors()
    {
        var repeated = string.Join("\n", Enumerable.Repeat(
            @"C:\s\Steps\A.cs(1,1): error CS0103: The name 'x' does not exist [C:\s\p.csproj]", 50));

        var issues = MsBuildErrorParser.Parse(repeated, @"C:\s");

        Assert.That(issues, Has.Count.EqualTo(1), "One missing using can emit hundreds of identical errors.");
    }

    [Test]
    public void CapsIssueCount()
    {
        var many = string.Join("\n", Enumerable.Range(1, 100)
            .Select(n => $@"C:\s\Steps\A.cs({n},1): error CS0103: The name 'x{n}' does not exist [C:\s\p.csproj]"));

        var issues = MsBuildErrorParser.Parse(many, @"C:\s", maxIssues: 25);

        Assert.That(issues, Has.Count.EqualTo(25));
    }

    [Test]
    public void ParsesErrorsWithoutFilePosition()
    {
        var output = "error NETSDK1004: Assets file not found.";
        var issues = MsBuildErrorParser.Parse(output, @"C:\s");

        Assert.That(issues, Has.Count.EqualTo(1));
        Assert.That(issues[0].Code, Is.EqualTo("NETSDK1004"));
        Assert.That(issues[0].File, Is.Null);
    }

    [Test]
    public void SuccessfulBuildOutput_YieldsNoIssues()
    {
        var issues = MsBuildErrorParser.Parse("Build succeeded.\n    0 Warning(s)\n    0 Error(s)", @"C:\s");
        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void FormatForPrompt_IncludesSurroundingSourceLines()
    {
        var issues = new List<ValidationIssue>
        {
            new(IssueSource.Compiler, "CS1061", "Steps/LoginSteps.cs", 3, "'LoginPage' has no 'ClickSubmit'")
        };
        var candidate = new Dictionary<string, string>
        {
            ["Steps/LoginSteps.cs"] = "line one\nline two\n_loginPage.ClickSubmit();\nline four\nline five"
        };

        var formatted = MsBuildErrorParser.FormatForPrompt(issues, candidate);

        Assert.That(formatted, Does.Contain("CS1061"));
        Assert.That(formatted, Does.Contain("ClickSubmit"));
        Assert.That(formatted, Does.Contain("> "), "The failing line should be marked.");
        Assert.That(formatted, Does.Contain("line two"), "Context lines around the error help the repair land.");
    }
}
