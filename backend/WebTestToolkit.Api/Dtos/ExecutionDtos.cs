using WebTestToolkit.Api.Services;
using WebTestToolkit.Contracts.Models;

namespace WebTestToolkit.Api.Dtos;

public record StartRunResponse(string RunId);

public record RunResponse(
    string RunId,
    string Status,
    IReadOnlyList<string> ConsoleLines,
    RunSummary? Summary,
    string? Error)
{
    public static RunResponse From(TestRunSession session) => new(
        session.Id,
        session.Status.ToString(),
        session.ConsoleLines(),
        session.Summary,
        session.Error);
}
