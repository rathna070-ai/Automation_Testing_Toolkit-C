using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WebTestToolkit.Contracts.Models;
using WebTestToolkit.Execution.Generation;
using WebTestToolkit.Llm;
using WebTestToolkit.Llm.Skills;
using WebTestToolkit.Llm.Transport;

namespace WebTestToolkit.Execution.Tests;

// These drive the real sandbox compile, so each takes a couple of seconds — that is the
// point: they prove the generate/validate/repair/fall-back loop against an actual compiler
// rather than a mocked one. Only the model is faked.
[Category("SandboxBuild")]
public class HybridTestCodeGeneratorTests
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

    private static string ValidModelResponse() => JsonSerializer.Serialize(new GeneratedFileSet(
        Files:
        [
            new GeneratedFileDto($"Features/{FlowName}.feature", $"""
                Feature: {FlowName}
                  As a probe
                  I want to verify the pipeline
                  So that generation is trustworthy

                  Scenario: The probe reaches the page
                    Given I visit the orchestrator probe page
                    When I type the probe user name "tomsmith"
                """),
            new GeneratedFileDto($"PageObjects/{FlowName}Page.cs", $$"""
                using OpenQA.Selenium;
                using OpenQA.Selenium.Support.UI;
                using WebTestToolkit.GeneratedTests.Support;

                namespace WebTestToolkit.GeneratedTests.PageObjects;

                public class {{FlowName}}Page
                {
                    private readonly IWebDriver _driver;
                    private readonly WebDriverWait _wait;
                    private readonly PageLocators _locators;

                    public {{FlowName}}Page(DriverContext driverContext)
                    {
                        _driver = driverContext.Driver;
                        _wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(10));
                        _locators = LocatorRepository.Load("{{FlowName}}Page");
                    }

                    public void NavigateTo() => _driver.Navigate().GoToUrl(_locators.Url);

                    public void EnterUserName(string value)
                    {
                        var element = FindVisible("UsernameInput");
                        element.Clear();
                        element.SendKeys(value);
                    }

                    private IWebElement FindVisible(string locatorKey)
                    {
                        var entry = _locators.Locators[locatorKey];
                        var by = LocatorRepository.ToBy(entry);
                        return _wait.Until(driver =>
                        {
                            var element = driver.FindElement(by);
                            return element.Displayed ? element : null;
                        });
                    }
                }
                """),
            // Four-quote delimiter: the Gherkin capture group below contains a run of three
            // quotes, which would otherwise terminate a """ raw string.
            new GeneratedFileDto($"Steps/{FlowName}Steps.cs", $$""""
                using Reqnroll;
                using WebTestToolkit.GeneratedTests.PageObjects;

                namespace WebTestToolkit.GeneratedTests.Steps;

                [Binding]
                public class {{FlowName}}Steps
                {
                    private readonly {{FlowName}}Page _page;

                    public {{FlowName}}Steps({{FlowName}}Page page) => _page = page;

                    [Given(@"I visit the orchestrator probe page")]
                    public void GivenIVisitTheProbePage() => _page.NavigateTo();

                    [When(@"I type the probe user name ""(.*)""")]
                    public void WhenITypeTheProbeUserName(string value) => _page.EnterUserName(value);
                }
                """")
        ],
        Locators: [new GeneratedLocatorDto($"{FlowName}Page", "UsernameInput", "id", "username", "https://the-internet.herokuapp.com/login")],
        Summary: "Probe flow."));

    // Calls a page-object method that does not exist — a compile error the static
    // validator cannot see, so this specifically exercises the sandbox-build stage.
    private static string ModelResponseThatWillNotCompile()
    {
        var valid = JsonSerializer.Deserialize<GeneratedFileSet>(ValidModelResponse())!;
        var brokenSteps = valid.Files.Single(f => f.Path.StartsWith("Steps/"))
            .Content.Replace("_page.EnterUserName(value)", "_page.ThisMethodDoesNotExist(value)");

        var files = valid.Files
            .Select(f => f.Path.StartsWith("Steps/") ? f with { Content = brokenSteps } : f)
            .ToList();

        return JsonSerializer.Serialize(valid with { Files = files });
    }

    // Rejected by the static validator before a build is ever spent.
    private static string ModelResponseWithHardcodedLocator()
    {
        var valid = JsonSerializer.Deserialize<GeneratedFileSet>(ValidModelResponse())!;
        var brokenPage = valid.Files.Single(f => f.Path.StartsWith("PageObjects/"))
            .Content.Replace("""FindVisible("UsernameInput")""", """_driver.FindElement(By.Id("username"))""");

        var files = valid.Files
            .Select(f => f.Path.StartsWith("PageObjects/") ? f with { Content = brokenPage } : f)
            .ToList();

        return JsonSerializer.Serialize(valid with { Files = files });
    }

    // A second page-object method sharing EnterUserName's exact wait-then-interact shape,
    // differing only by locator key — WTT160, Advisory. Compiles fine and is never called
    // from any step; nothing about this response is actually broken.
    private static string ModelResponseWithAdvisoryOnly()
    {
        var valid = JsonSerializer.Deserialize<GeneratedFileSet>(ValidModelResponse())!;

        var duplicatedMethod = """

                    public void EnterOtherField(string value)
                    {
                        var element = FindVisible("OtherField");
                        element.Clear();
                        element.SendKeys(value);
                    }
            """;

        var pageWithDuplicate = valid.Files.Single(f => f.Path.StartsWith("PageObjects/"))
            .Content.Replace(
                "private IWebElement FindVisible(string locatorKey)",
                duplicatedMethod + "\n\n            private IWebElement FindVisible(string locatorKey)");

        var files = valid.Files
            .Select(f => f.Path.StartsWith("PageObjects/") ? f with { Content = pageWithDuplicate } : f)
            .ToList();

        var locators = valid.Locators
            .Append(new GeneratedLocatorDto($"{FlowName}Page", "OtherField", "id", "other", "https://the-internet.herokuapp.com/login"))
            .ToList();

        return JsonSerializer.Serialize(valid with { Files = files, Locators = locators });
    }

    private static HybridTestCodeGenerator BuildGenerator(IChatClient chatClient)
    {
        var prompts = new PromptLibrary();
        return new HybridTestCodeGenerator(
            new ScriptGenerationSkill(chatClient, prompts, NullLogger<ScriptGenerationSkill>.Instance),
            new ScriptRepairSkill(chatClient, prompts, NullLogger<ScriptRepairSkill>.Instance),
            new ReferenceBundleBuilder(),
            new BuildSandbox(NullLogger<BuildSandbox>.Instance),
            new GeneratedProjectWriter(),
            NullLogger<HybridTestCodeGenerator>.Instance);
    }

    // WriteToProject:false throughout — these tests must never mutate tests/.
    private static GenerationOptions Options(int repairs = 2) => new(UseLlm: true, MaxRepairAttempts: repairs, WriteToProject: false);

    [Test]
    public async Task LlmDisabled_UsesDeterministicAndCompiles()
    {
        var generator = BuildGenerator(new SequencedChatClient());

        var result = await generator.GenerateAsync(BuildFlow(), new GenerationOptions(UseLlm: false, WriteToProject: false));

        Assert.That(result.Source, Is.EqualTo(GenerationSource.Deterministic));
        Assert.That(result.Files, Is.Not.Empty);
        Assert.That(result.WrittenPaths, Is.Empty, "WriteToProject:false must not touch the real project.");
    }

    // A real, large captured flow (many steps, each carrying DOM context) hit a genuine
    // Groq 413 in practice — the request was too large before the model ever ran. Rather
    // than spend a request only to have it bounce, an oversized prompt should skip AI
    // generation entirely and go straight to the deterministic generator.
    [Test]
    public async Task OversizedPrompt_SkipsAiEntirelyAndFallsBackToDeterministic()
    {
        var flow = BuildFlow();
        // Comfortably over the 6000-token (~24,000-char) estimate threshold on its own.
        flow.Steps[0].Element = new CapturedElement
        {
            TagName = "div",
            OuterHtmlSnippet = new string('a', 30_000),
            Candidates = [new LocatorCandidate("id", "probe", 100)]
        };

        // Zero scripted responses: if the code actually called Groq, this would throw a
        // TransportError ("No more scripted responses") rather than silently succeeding —
        // so a passing DeterministicFallback here proves the call was skipped, not attempted.
        var generator = BuildGenerator(new SequencedChatClient());

        var result = await generator.GenerateAsync(flow, Options());

        Assert.That(result.Source, Is.EqualTo(GenerationSource.DeterministicFallback));
        Assert.That(result.FallbackReason, Does.Contain("too large for AI generation"));
        Assert.That(result.Attempts, Has.Count.EqualTo(1),
            "Only the deterministic compile attempt should be recorded — no LLM call was made.");
        Assert.That(result.Attempts[0].Kind, Is.EqualTo(GenerationAttemptKind.Deterministic));
    }

    [Test]
    public async Task ValidLlmOutput_CompilesAndReportsLlmVerified()
    {
        var chatClient = new SequencedChatClient(
            ChatResult.Success(ValidModelResponse(), "openai/gpt-oss-120b", 500, 200));

        var result = await BuildGenerator(chatClient).GenerateAsync(BuildFlow(), Options());

        Assert.That(result.Source, Is.EqualTo(GenerationSource.LlmVerified),
            "Attempts: " + string.Join(" | ", result.Attempts.SelectMany(a => a.Issues).Select(i => $"{i.Code} {i.Message}")));
        Assert.That(result.Attempts, Has.Count.EqualTo(1));
        Assert.That(result.DeterministicFiles, Is.Not.Empty, "The deterministic set is always kept for the compare view.");
    }

    // An Advisory issue (WTT160, a duplicated-shape style nit) must ride along for the UI
    // without ever gating the build or burning a repair attempt on something that isn't
    // actually broken — this is the whole point of severity existing as a concept.
    [Test]
    public async Task AdvisoryOnlyIssue_DoesNotBlockGenerationOrTriggerRepair()
    {
        var chatClient = new SequencedChatClient(
            ChatResult.Success(ModelResponseWithAdvisoryOnly(), "openai/gpt-oss-120b", 500, 200));

        var result = await BuildGenerator(chatClient).GenerateAsync(BuildFlow(), Options());

        Assert.That(result.Source, Is.EqualTo(GenerationSource.LlmVerified),
            "Attempts: " + string.Join(" | ", result.Attempts.SelectMany(a => a.Issues).Select(i => $"{i.Code} {i.Message}")));
        Assert.That(result.Attempts, Has.Count.EqualTo(1),
            "An advisory-only issue must not trigger a second, repair attempt.");
        Assert.That(result.Attempts[0].Succeeded, Is.True);

        var advisory = result.Attempts[0].Issues.FirstOrDefault(i => i.Code == "WTT160");
        Assert.That(advisory, Is.Not.Null, "The advisory issue should still be surfaced for the UI.");
        Assert.That(advisory!.Severity, Is.EqualTo(IssueSeverity.Advisory));
    }

    [Test]
    public async Task OutputThatFailsToCompile_IsRepairedOnTheSecondAttempt()
    {
        var chatClient = new SequencedChatClient(
            ChatResult.Success(ModelResponseThatWillNotCompile(), "openai/gpt-oss-120b", 500, 200),
            ChatResult.Success(ValidModelResponse(), "openai/gpt-oss-120b", 700, 220));

        var result = await BuildGenerator(chatClient).GenerateAsync(BuildFlow(), Options());

        Assert.That(result.Source, Is.EqualTo(GenerationSource.LlmRepaired));
        Assert.That(result.Attempts, Has.Count.EqualTo(2));
        Assert.That(result.Attempts[0].Succeeded, Is.False);
        Assert.That(result.Attempts[0].Issues.Any(i => i.Source == IssueSource.Compiler), Is.True,
            "The first attempt should have failed at the compiler, not the static validator.");
        Assert.That(result.Attempts[1].Succeeded, Is.True);

        // The repair turn must replay the original request and the model's own prior answer.
        var repairRequest = chatClient.Requests[1];
        Assert.That(repairRequest.Messages.Any(m => m.Role == "assistant"), Is.True);
        Assert.That(repairRequest.Messages.Last().Content, Does.Contain("ThisMethodDoesNotExist"));
    }

    [Test]
    public async Task HardcodedLocator_IsCaughtStatically_BeforeAnyBuild()
    {
        var chatClient = new SequencedChatClient(
            ChatResult.Success(ModelResponseWithHardcodedLocator(), "openai/gpt-oss-120b", 500, 200),
            ChatResult.Success(ValidModelResponse(), "openai/gpt-oss-120b", 700, 220));

        var result = await BuildGenerator(chatClient).GenerateAsync(BuildFlow(), Options());

        Assert.That(result.Source, Is.EqualTo(GenerationSource.LlmRepaired));
        Assert.That(result.Attempts[0].Issues.Any(i => i.Code == "WTT100"), Is.True);
        Assert.That(result.Attempts[0].Issues.All(i => i.Source == IssueSource.Static), Is.True,
            "A hardcoded locator compiles fine, so it must be rejected before a build is spent.");
    }

    [Test]
    public async Task AllAttemptsExhausted_FallsBackToDeterministicWithAReason()
    {
        var broken = ModelResponseThatWillNotCompile();
        var chatClient = new SequencedChatClient(
            ChatResult.Success(broken, "openai/gpt-oss-120b", 500, 200),
            ChatResult.Success(broken, "openai/gpt-oss-120b", 500, 200));

        var result = await BuildGenerator(chatClient).GenerateAsync(BuildFlow(), Options(repairs: 1));

        Assert.That(result.Source, Is.EqualTo(GenerationSource.DeterministicFallback));
        Assert.That(result.FallbackReason, Is.Not.Null.And.Not.Empty);
        Assert.That(result.Files, Is.Not.Empty, "The user must still end up with compiling code.");
    }

    [Test]
    public async Task NoApiKey_FallsBackImmediatelyWithoutBurningRepairAttempts()
    {
        var chatClient = new SequencedChatClient(ChatResult.Unavailable("No Groq API key is configured."));

        var result = await BuildGenerator(chatClient).GenerateAsync(BuildFlow(), Options());

        Assert.That(result.Source, Is.EqualTo(GenerationSource.DeterministicFallback));
        Assert.That(result.FallbackReason, Does.Contain("API key"));
        Assert.That(chatClient.Requests, Has.Count.EqualTo(1),
            "A transport failure won't fix itself on retry — retries should not be spent on it.");
    }
}
