using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using WebTestToolkit.CodeGenerator;
using WebTestToolkit.Contracts.Models;
using WebTestToolkit.Llm.Skills;

namespace WebTestToolkit.Execution.Generation;

// Assembles what the model is shown, reading from the live project every time rather than
// from a snapshot baked into the prompt — so a change to Support/*.cs propagates into the
// next generation automatically instead of silently drifting out of date.
public class ReferenceBundleBuilder
{
    private static readonly JsonSerializerOptions FlowJsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    // Dropped from the flow JSON before it reaches the codegen prompt. Both fields exist to
    // give the *label-suggestion and assertion-inference* prompts enough DOM context during
    // Inspect (see CapturedElement) — by the time codegen runs, those labels are already
    // chosen and sitting in TestStep.Label, so here the raw HTML is pure prompt weight. It is
    // also by far the largest per-step contributor: a real captured flow's snippets are what
    // pushed one request past Groq's request-size limit into a 413.
    private static readonly string[] PromptOmittedElementFields = ["outerHtmlSnippet", "ancestorContext"];

    private static readonly string[] SupportFiles =
        ["Support/LocatorRepository.cs", "Support/DriverContext.cs", "Support/Hooks.cs"];

    private static readonly string[] GoldSampleFiles =
    [
        "Features/SampleLogin.feature",
        "PageObjects/LoginPage.cs",
        "Steps/LoginSteps.cs",
        "LocatorRepository/LoginPage.locators.json"
    ];

    public ScriptGenerationInput Build(TestFlow flow, IReadOnlyDictionary<string, string> deterministicFiles)
    {
        var projectDir = SolutionPaths.GeneratedTestsDirectory();

        return new ScriptGenerationInput(
            FlowName: flow.Name,
            FlowJson: SerializeFlowForPrompt(flow),
            ProjectFile: ReadIfExists(Path.Combine(projectDir, "WebTestToolkit.GeneratedTests.csproj")) ?? "",
            SupportApi: Concatenate(projectDir, SupportFiles),
            GoldSample: Concatenate(projectDir, GoldSampleFiles),
            ReferenceImplementation: RenderFileSet(deterministicFiles),
            ExistingProjectIndex: BuildProjectIndex(projectDir, flow.Name));
    }

    // Existing class names and binding patterns, so the model avoids colliding with them.
    // Files belonging to the flow being regenerated are excluded — they're about to be
    // replaced, and listing them would look like a conflict with itself.
    public string BuildProjectIndex(string projectDir, string flowName)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Existing files in the project (excluding the flow being generated):");

        foreach (var relative in EnumerateProjectSources(projectDir))
        {
            if (BelongsToFlow(relative, flowName))
                continue;
            builder.AppendLine($"  {relative}");
        }

        builder.AppendLine();
        builder.AppendLine("Existing step bindings (do not duplicate or ambiguously overlap these):");

        var anyBindings = false;
        foreach (var relative in EnumerateProjectSources(projectDir).Where(p => p.StartsWith("Steps/", StringComparison.OrdinalIgnoreCase)))
        {
            if (BelongsToFlow(relative, flowName))
                continue;

            var content = ReadIfExists(Path.Combine(projectDir, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (content is null)
                continue;

            foreach (var binding in BindingIndex.Extract(relative, content))
            {
                builder.AppendLine($"  [{binding.Keyword}(@\"{binding.Pattern}\")]  ({relative})");
                anyBindings = true;
            }
        }

        if (!anyBindings)
            builder.AppendLine("  (none)");

        return builder.ToString();
    }

    public List<BindingPattern> ExistingBindings(string flowName)
    {
        var projectDir = SolutionPaths.GeneratedTestsDirectory();
        var bindings = new List<BindingPattern>();

        foreach (var relative in EnumerateProjectSources(projectDir).Where(p => p.StartsWith("Steps/", StringComparison.OrdinalIgnoreCase)))
        {
            if (BelongsToFlow(relative, flowName))
                continue;

            var content = ReadIfExists(Path.Combine(projectDir, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (content is not null)
                bindings.AddRange(BindingIndex.Extract(relative, content));
        }

        return bindings;
    }

    private static bool BelongsToFlow(string relativePath, string flowName)
    {
        // flowName is free text a user typed; TestFlowCodeGenerator writes its files under
        // the sanitized identifier, so comparisons here must use the same one or a flow
        // like "flow new 1" would never recognize its own about-to-be-replaced files.
        var className = Naming.ToPascalCaseIdentifier(flowName);
        var fileName = Path.GetFileName(relativePath);
        return fileName.Equals($"{className}.feature", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals($"{className}.feature.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals($"{className}Steps.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> EnumerateProjectSources(string projectDir)
    {
        if (!Directory.Exists(projectDir))
            yield break;

        foreach (var folder in new[] { "Features", "PageObjects", "Steps", "LocatorRepository" })
        {
            var dir = Path.Combine(projectDir, folder);
            if (!Directory.Exists(dir))
                continue;

            foreach (var path in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly).OrderBy(p => p))
            {
                if (path.EndsWith(".feature.cs", StringComparison.OrdinalIgnoreCase))
                    continue;
                yield return $"{folder}/{Path.GetFileName(path)}";
            }
        }
    }

    private static string Concatenate(string projectDir, IEnumerable<string> relativePaths)
    {
        var builder = new StringBuilder();
        foreach (var relative in relativePaths)
        {
            var content = ReadIfExists(Path.Combine(projectDir, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (content is null)
                continue;

            builder.AppendLine($"--- {relative} ---");
            builder.AppendLine(content.TrimEnd());
            builder.AppendLine();
        }
        return builder.ToString();
    }

    private static string RenderFileSet(IReadOnlyDictionary<string, string> files)
    {
        var builder = new StringBuilder();
        foreach (var (path, content) in files.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            builder.AppendLine($"--- {path} ---");
            builder.AppendLine(content.TrimEnd());
            builder.AppendLine();
        }
        return builder.ToString();
    }

    // The captured flow as the codegen prompt sees it: everything TestFlow carries, minus the
    // raw-DOM fields in PromptOmittedElementFields. Serializing and then pruning the tree
    // (rather than projecting onto a hand-written prompt-shaped record) keeps this a
    // subtraction — a field added to CapturedElement later reaches the model automatically,
    // and only these two are ever deliberately withheld.
    public static string SerializeFlowForPrompt(TestFlow flow)
    {
        var node = JsonSerializer.SerializeToNode(flow, FlowJsonOptions);
        if (node is null)
            return JsonSerializer.Serialize(flow, FlowJsonOptions);

        RemoveOmittedFields(node);
        return node.ToJsonString(FlowJsonOptions);
    }

    // Matches on name alone, at any depth and in either casing, rather than walking a fixed
    // steps[].element[] path — the serializer's property-naming policy is then irrelevant, and
    // both names are distinctive enough to TestFlow that nothing else can collide with them.
    private static void RemoveOmittedFields(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var omitted = obj
                    .Select(property => property.Key)
                    .Where(key => PromptOmittedElementFields.Contains(key, StringComparer.OrdinalIgnoreCase))
                    .ToList();
                foreach (var key in omitted)
                    obj.Remove(key);

                foreach (var property in obj.ToList())
                    RemoveOmittedFields(property.Value);
                break;

            case JsonArray array:
                foreach (var item in array)
                    RemoveOmittedFields(item);
                break;
        }
    }

    private static string? ReadIfExists(string path) => File.Exists(path) ? File.ReadAllText(path) : null;
}
