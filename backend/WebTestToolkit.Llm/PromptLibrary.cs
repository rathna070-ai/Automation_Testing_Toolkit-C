using System.Reflection;
using System.Text.Json.Nodes;

namespace WebTestToolkit.Llm;

// Prompts and schemas live as embedded .md/.json resources under Prompts/ and Schemas/ —
// one binary, no path resolution to get wrong, impossible to forget to deploy. Set
// WTT_PROMPT_DIR to a folder during development to read loose files instead, for fast
// iteration on prompt wording without a rebuild.
public class PromptLibrary
{
    private readonly Assembly _assembly = typeof(PromptLibrary).Assembly;

    public string GetPrompt(string name) => Get("Prompts", name, "md");

    public JsonNode GetSchema(string name)
    {
        var json = Get("Schemas", name, "json");
        return JsonNode.Parse(json) ?? throw new InvalidOperationException($"Schema '{name}' parsed to null.");
    }

    private string Get(string folder, string name, string extension)
    {
        var overrideDir = Environment.GetEnvironmentVariable("WTT_PROMPT_DIR");
        if (!string.IsNullOrWhiteSpace(overrideDir))
        {
            var path = Path.Combine(overrideDir, folder, $"{name}.{extension}");
            if (File.Exists(path))
                return File.ReadAllText(path);
        }

        var resourceName = $"WebTestToolkit.Llm.{folder}.{name}.{extension}";
        using var stream = _assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
