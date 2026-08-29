using WebTestToolkit.Contracts.Models;
using WebTestToolkit.Execution;

namespace WebTestToolkit.Execution.Tests;

// Fixtures below are trimmed but structurally faithful copies of a real .trx produced by
// `dotnet test --logger trx` against tests/WebTestToolkit.GeneratedTests on this exact
// Reqnroll 3.3.4 / NUnit 3.14.0 / NUnit3TestAdapter 4.5.0 / .NET 8 stack (one passing run,
// one with a deliberately-forced failure) - not guessed from documentation.
public class TrxParserTests
{
    private const string PassingTrx = """
        <?xml version="1.0" encoding="utf-8"?>
        <TestRun id="084a71c9-9d93-40cc-96a0-1d6c662b65a0" name="run" runUser="me" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results>
            <UnitTestResult executionId="2bd917f3-ba71-4252-ac4e-4592a436e079" testId="5e631b9c-8ff7-d442-d747-b2027038b6dd" testName="FailedLoginWithInvalidCredentials" duration="00:00:08.8212590" outcome="Passed">
              <Output>
                <StdOut>Given I am on the login page
        -&gt; done: LoginSteps.GivenIAmOnTheLoginPage() (7.5s)</StdOut>
              </Output>
            </UnitTestResult>
            <UnitTestResult executionId="620892cb-7995-4c2a-9071-fd1c6120206f" testId="53bb925e-fbd2-cb67-ea0f-60a6c64f5b17" testName="SuccessfulLoginWithValidCredentials" duration="00:00:13.2720220" outcome="Passed">
              <Output>
                <StdOut>Given I am on the login page</StdOut>
              </Output>
            </UnitTestResult>
          </Results>
          <TestDefinitions>
            <UnitTest name="SuccessfulLoginWithValidCredentials" id="53bb925e-fbd2-cb67-ea0f-60a6c64f5b17">
              <TestMethod className="WebTestToolkit.GeneratedTests.Features.LoginFeature" name="SuccessfulLoginWithValidCredentials" />
            </UnitTest>
            <UnitTest name="FailedLoginWithInvalidCredentials" id="5e631b9c-8ff7-d442-d747-b2027038b6dd">
              <TestMethod className="WebTestToolkit.GeneratedTests.Features.LoginFeature" name="FailedLoginWithInvalidCredentials" />
            </UnitTest>
          </TestDefinitions>
          <ResultSummary outcome="Completed">
            <Counters total="2" executed="2" passed="2" failed="0" />
          </ResultSummary>
        </TestRun>
        """;

    private const string FailingTrx = """
        <?xml version="1.0" encoding="utf-8"?>
        <TestRun id="x" name="run" runUser="me" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results>
            <UnitTestResult executionId="bd00e8de-3cea-44e6-9d44-9acff9168bc3" testId="53bb925e-fbd2-cb67-ea0f-60a6c64f5b17" testName="SuccessfulLoginWithValidCredentials" duration="00:00:32.6312000" outcome="Failed">
              <Output>
                <StdOut>Then I should see a success message
        -&gt; error:   Expected: String containing "X"
        [WTT_SCREENSHOT]C:\tests\Screenshots\SuccessfulLoginWithValidCredentials_20260828_215233.png</StdOut>
                <ErrorInfo>
                  <Message>  Expected: String containing "X"
          But was:  "You logged into a secure area!"
        </Message>
                  <StackTrace>   at WebTestToolkit.GeneratedTests.Steps.LoginSteps.ThenIShouldSeeASuccessMessage() in C:\tests\Steps\LoginSteps.cs:line 39</StackTrace>
                </ErrorInfo>
              </Output>
            </UnitTestResult>
          </Results>
          <TestDefinitions>
            <UnitTest name="SuccessfulLoginWithValidCredentials" id="53bb925e-fbd2-cb67-ea0f-60a6c64f5b17">
              <TestMethod className="WebTestToolkit.GeneratedTests.Features.LoginFeature" name="SuccessfulLoginWithValidCredentials" />
            </UnitTest>
          </TestDefinitions>
          <ResultSummary outcome="Failed">
            <Counters total="1" executed="1" passed="0" failed="1" />
          </ResultSummary>
        </TestRun>
        """;

    [Test]
    public void Parse_PassingRun_CountsAndFeatureNameAreCorrect()
    {
        var runAt = new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);
        var summary = TrxParser.Parse(PassingTrx, runAt);

        Assert.That(summary.Total, Is.EqualTo(2));
        Assert.That(summary.Passed, Is.EqualTo(2));
        Assert.That(summary.Failed, Is.EqualTo(0));
        Assert.That(summary.RunAtUtc, Is.EqualTo(runAt));
        Assert.That(summary.Scenarios, Has.All.Matches<ScenarioResult>(s => s.FeatureName == "Login"));
        Assert.That(summary.Scenarios.Select(s => s.ScenarioName),
            Is.EquivalentTo(new[] { "FailedLoginWithInvalidCredentials", "SuccessfulLoginWithValidCredentials" }));
    }

    [Test]
    public void Parse_SumsScenarioDurations_IntoTotalDuration()
    {
        var summary = TrxParser.Parse(PassingTrx, DateTime.UtcNow);

        // 00:00:08.8212590 + 00:00:13.2720220
        Assert.That(summary.Duration, Is.EqualTo(TimeSpan.Parse("00:00:08.8212590") + TimeSpan.Parse("00:00:13.2720220")));
    }

    [Test]
    public void Parse_FailedScenario_CapturesErrorMessageAndStackTrace()
    {
        var summary = TrxParser.Parse(FailingTrx, DateTime.UtcNow);

        Assert.That(summary.Failed, Is.EqualTo(1));
        var scenario = summary.Scenarios.Single();
        Assert.That(scenario.Outcome, Is.EqualTo(ScenarioOutcome.Failed));
        Assert.That(scenario.ErrorMessage, Does.Contain("Expected: String containing"));
        Assert.That(scenario.StackTrace, Does.Contain("LoginSteps.ThenIShouldSeeASuccessMessage"));
    }

    [Test]
    public void Parse_FailedScenario_ExtractsScreenshotPathFromStdOutMarker()
    {
        var summary = TrxParser.Parse(FailingTrx, DateTime.UtcNow);

        Assert.That(summary.Scenarios.Single().ScreenshotPath,
            Is.EqualTo(@"C:\tests\Screenshots\SuccessfulLoginWithValidCredentials_20260828_215233.png"));
    }

    [Test]
    public void Parse_PassingScenario_HasNoScreenshotPath()
    {
        var summary = TrxParser.Parse(PassingTrx, DateTime.UtcNow);

        Assert.That(summary.Scenarios, Has.All.Matches<ScenarioResult>(s => s.ScreenshotPath == null));
        Assert.That(summary.Scenarios, Has.All.Matches<ScenarioResult>(s => s.ErrorMessage == null));
    }

    private const string SkippedTrx = """
        <?xml version="1.0" encoding="utf-8"?>
        <TestRun id="x" name="run" runUser="me" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results>
            <UnitTestResult executionId="a" testId="b" testName="IgnoredScenario" duration="00:00:00" outcome="NotExecuted">
              <Output><StdOut>skipped</StdOut></Output>
            </UnitTestResult>
          </Results>
          <TestDefinitions>
            <UnitTest name="IgnoredScenario" id="b">
              <TestMethod className="WebTestToolkit.GeneratedTests.Features.LoginFeature" name="IgnoredScenario" />
            </UnitTest>
          </TestDefinitions>
          <ResultSummary outcome="Completed"><Counters total="1" executed="0" passed="0" failed="0" /></ResultSummary>
        </TestRun>
        """;

    [Test]
    public void Parse_UnknownOutcome_MapsToSkipped_NotPassedOrFailed()
    {
        var summary = TrxParser.Parse(SkippedTrx, DateTime.UtcNow);

        Assert.That(summary.Passed, Is.EqualTo(0));
        Assert.That(summary.Failed, Is.EqualTo(0));
        Assert.That(summary.Scenarios.Single().Outcome, Is.EqualTo(ScenarioOutcome.Skipped));
    }
}
