using WebTestToolkit.Contracts.Models;

namespace WebTestToolkit.Api.Dtos;

// The flow travels in the body, same as GenerateFlowRequest — nothing in this toolkit
// persists flows by name yet, so "export flow X" means "export the flow you're handing me
// right now," consistently with /api/flows/preview and /api/flows/generate.
public record ExportTestCasesRequest(TestFlow Flow, bool UseLlm = true);
