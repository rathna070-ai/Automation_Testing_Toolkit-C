namespace WebTestToolkit.Execution;

// Locates the repo layout at runtime by walking up from the running assembly until a .sln
// turns up. Salvaged from the retired WPF shell, which used the same trick so the tool worked
// identically whether launched from an IDE or `dotnet run`.
public static class SolutionPaths
{
    public static string FindSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.GetFiles("*.sln").Any())
        {
            dir = dir.Parent;
        }

        if (dir == null)
            throw new InvalidOperationException("Could not locate the solution root (WebTestToolkit.sln).");

        return dir.FullName;
    }

    public static string GeneratedTestsDirectory() =>
        Path.Combine(FindSolutionRoot(), "tests", "WebTestToolkit.GeneratedTests");

    public static string GeneratedTestsProject()
    {
        var projectPath = Path.Combine(GeneratedTestsDirectory(), "WebTestToolkit.GeneratedTests.csproj");
        if (!File.Exists(projectPath))
            throw new InvalidOperationException($"Test project not found at: {projectPath}");

        return projectPath;
    }
}
