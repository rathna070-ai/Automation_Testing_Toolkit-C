namespace WebTestToolkit.Execution.Generation;

// The shape a candidate suite is validated in: files to write, plus locators as *data* rather
// than as an already-serialized .locators.json (LocatorFileBuilder owns that serialization, so
// there is exactly one place the JSON format is decided).
//
// These lived in WebTestToolkit.Llm.Skills while the LLM wrote code, because they were that
// skill's response schema. They outlived it: StaticValidator validates this shape, and it is
// what the deterministic generator's output is projected into before the static gate and the
// sandbox build. Moving them here is what lets WebTestToolkit.Execution stop referencing
// WebTestToolkit.Llm at all — the generator no longer depends on the LLM stack to compile.
public record GeneratedFileDto(string Path, string Content);

public record GeneratedLocatorDto(string Page, string Key, string Strategy, string Value, string Url);

public record GeneratedFileSet(
    List<GeneratedFileDto> Files,
    List<GeneratedLocatorDto> Locators,
    string Summary);
