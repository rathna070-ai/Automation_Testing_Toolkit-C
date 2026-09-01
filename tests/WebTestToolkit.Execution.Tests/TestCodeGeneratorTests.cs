using Microsoft.Extensions.Logging.Abstractions;
using WebTestToolkit.Contracts.Models;
using WebTestToolkit.Execution.Generation;

namespace WebTestToolkit.Execution.Tests;

// These drive the real sandbox compile, so each takes a couple of seconds — that is the
// point: they prove generate → merge → validate → compile → write against an actual compiler
// rather than a mocked one.
//
// This file replaces HybridTestCodeGeneratorTests, which was mostly about the LLM attempt/
// repair/fallback loop. That loop is gone, and so are those tests: what they proved (the model
// can fail and we still ship something) is no longer a scenario. The two that survived are the
// ones about the path that actually runs.
[Category("SandboxBuild")]
public class TestCodeGeneratorTests
{
    private const string FlowName = "OrchestratorProbe";

    private static TestFlow BuildFlow() => new()
    {
        Name = FlowName,
        StartUrl = "https://the-internet.herokuapp.com/login",
        Steps =
        [
            new TestStep
            {
                Order = 1, ActionType = ActionType.Navigate,
                Label = "I visit the orchestrator probe page", PageName = $"{FlowName}Page"
            },
            new TestStep
            {
                Order = 2, ActionType = ActionType.Type,
                Label = "I type the probe user name", InputValue = "tomsmith",
                PageName = $"{FlowName}Page", LocatorKey = "UsernameInput",
                Element = new CapturedElement { TagName = "input", Candidates = [new LocatorCandidate("id", "username", 100)] }
            }
        ]
    };

    private static TestCodeGenerator BuildGenerator() =>
        new(new BuildSandbox(NullLogger<BuildSandbox>.Instance),
            new GeneratedProjectWriter(),
            NullLogger<TestCodeGenerator>.Instance);

    // WriteToProject:false throughout — these tests must never mutate tests/.
    private static GenerationOptions Options() => new(WriteToProject: false);

    [Test]
    public async Task GeneratesAndCompiles()
    {
        var result = await BuildGenerator().GenerateAsync(BuildFlow(), Options());

        Assert.Multiple(() =>
        {
            Assert.That(result.Source, Is.EqualTo(GenerationSource.Deterministic),
                "Issues: " + string.Join(" | ", result.Attempts.SelectMany(a => a.Issues).Select(i => $"{i.Code} {i.Message}")));
            Assert.That(result.Files, Is.Not.Empty);
            Assert.That(result.WrittenPaths, Is.Empty, "WriteToProject:false must not touch the real project.");
            Assert.That(result.Attempts, Has.Count.EqualTo(1));
            Assert.That(result.Attempts[0].Kind, Is.EqualTo(GenerationAttemptKind.Deterministic));
        });
    }

    // The generated path used to be compiled and nothing more, which left the one path that
    // always runs as the only one with no static gate. The checks that matter here describe
    // *runtime* failures, so a real compile can never stand in for them: this flow compiles
    // perfectly and then throws the moment LocatorRepository.ToBy meets a strategy it does not
    // support. Retiring the LLM did not retire this — the emitter needs the gate too.
    [Test]
    public async Task Output_IsStaticallyValidated_NotJustCompiled()
    {
        var flow = BuildFlow();
        flow.Steps[1].Element = new CapturedElement
        {
            TagName = "input",
            Candidates = [new LocatorCandidate("linktext", "Sign in", 100)]
        };

        var result = await BuildGenerator().GenerateAsync(flow, Options());
        var attempt = result.Attempts.Single();

        Assert.Multiple(() =>
        {
            Assert.That(attempt.Issues.Select(i => i.Code), Does.Contain("WTT110"),
                "An unsupported locator strategy compiles fine — only the static gate can catch it.");
            Assert.That(attempt.Succeeded, Is.False,
                "A blocking issue must be reported as a failed attempt even when the compile passed.");

            // But it must still hand back output: there is nothing left to fall back to, and
            // returning nothing would leave the user empty-handed.
            Assert.That(result.Files, Is.Not.Empty);
            Assert.That(result.FallbackReason, Does.Contain("WTT110"));
        });
    }
}
