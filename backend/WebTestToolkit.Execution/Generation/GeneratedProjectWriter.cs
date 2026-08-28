using WebTestToolkit.Contracts.Models;

namespace WebTestToolkit.Execution.Generation;

// Writes a verified file set into the real tests/ project. Only ever called after the set
// has compiled in the sandbox, so this never introduces a broken build.
public class GeneratedProjectWriter
{
    public IReadOnlyList<string> Write(IEnumerable<GeneratedFile> files)
    {
        var projectDir = SolutionPaths.GeneratedTestsDirectory();
        var written = new List<string>();

        foreach (var file in files)
        {
            var relative = file.RelativePath.Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.Combine(projectDir, relative);

            // Defence in depth: StaticValidator already enforces the path whitelist, but a
            // path that escapes the project directory must never reach the filesystem.
            var resolved = Path.GetFullPath(fullPath);
            if (!resolved.StartsWith(Path.GetFullPath(projectDir), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Refusing to write outside the test project: {file.RelativePath}");

            Directory.CreateDirectory(Path.GetDirectoryName(resolved)!);

            // Write to a temp file then move, so an interrupted write can't leave a
            // half-written source file behind in the user's repo.
            var tempPath = resolved + ".tmp";
            File.WriteAllText(tempPath, file.Content);
            File.Move(tempPath, resolved, overwrite: true);

            written.Add(file.RelativePath);
        }

        return written;
    }
}
