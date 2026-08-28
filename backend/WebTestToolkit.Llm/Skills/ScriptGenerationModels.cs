namespace WebTestToolkit.Llm.Skills;

// Everything the model needs to write the tests, as plain strings. The Llm project never
// reads the filesystem — the caller (Execution) assembles this from the live project so
// the model is always shown current reality, not a snapshot baked into a prompt.
public record ScriptGenerationInput(
    string FlowName,
    string FlowJson,
    string ProjectFile,
    string SupportApi,
    string GoldSample,
    string ReferenceImplementation,
    string ExistingProjectIndex,
    string? UntrustedPageContent = null);

// A repair turn: the original request, the model's own previous answer, and what went wrong.
public record ScriptRepairInput(
    ScriptGenerationInput Original,
    string PreviousResponseJson,
    string IssuesReport);

public record GeneratedFileDto(string Path, string Content);

public record GeneratedLocatorDto(string Page, string Key, string Strategy, string Value, string Url);

public record GeneratedFileSet(
    List<GeneratedFileDto> Files,
    List<GeneratedLocatorDto> Locators,
    string Summary);
