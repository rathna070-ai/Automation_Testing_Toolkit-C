using WebTestToolkit.Contracts.Models;

namespace WebTestToolkit.Api.Dtos;

// The flow travels in the body, same as GenerateFlowRequest — nothing in this toolkit
// persists flows by name yet, so "export flow X" means "export the flow you're handing me
// right now," consistently with /api/flows/preview and /api/flows/generate.
public record ExportTestCasesRequest(TestFlow Flow, bool UseLlm = true);

// Takes the already-generated file list, not a TestFlow to regenerate from — the frontend
// already holds result.files/result.deterministicFiles in memory after a Preview/Generate
// call, so exporting must never re-trigger HybridTestCodeGenerator/Groq just to zip content
// the client already has.
public record ExportGeneratedFilesRequest(string FlowName, IReadOnlyList<GeneratedFile> Files);
