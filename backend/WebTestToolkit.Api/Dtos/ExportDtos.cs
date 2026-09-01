using WebTestToolkit.Contracts.Models;

namespace WebTestToolkit.Api.Dtos;

// The flow travels in the body, same as GenerateFlowRequest. P19 added a flow store, so a
// caller *could* now pass a name instead — but exporting the flow in hand keeps this
// consistent with /api/flows/preview and /api/flows/generate, and lets the Export page
// document a flow that has been edited since it was saved.
public record ExportTestCasesRequest(TestFlow Flow, bool UseLlm = true);

// Takes the already-generated file list, not a TestFlow to regenerate from — the frontend
// already holds result.files/result.deterministicFiles in memory after a Preview/Generate
// call, so exporting must never re-trigger TestCodeGenerator just to zip content
// the client already has.
public record ExportGeneratedFilesRequest(string FlowName, IReadOnlyList<GeneratedFile> Files);
