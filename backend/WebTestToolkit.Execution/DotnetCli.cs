using System.Diagnostics;
using System.Text;

namespace WebTestToolkit.Execution;

public record DotnetResult(int ExitCode, string Output)
{
    public bool Succeeded => ExitCode == 0;
}

// Thin wrapper over `dotnet <args>`. Salvaged from the retired WPF shell's process shell-out,
// with the blocking ReadToEnd() calls replaced by async reads — reading stdout and stderr
// sequentially can deadlock if either pipe's buffer fills before the other is drained.
public static class DotnetCli
{
    public static async Task<DotnetResult> RunAsync(
        string arguments,
        string? workingDirectory = null,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory
        };

        // Without this MSBuild leaves worker nodes alive holding file handles, which breaks
        // the next write into the same directory on Windows.
        psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var output = new StringBuilder();

        void Capture(string? line)
        {
            if (line is null) return;
            lock (output) output.AppendLine(line);
            progress?.Report(line);
        }

        process.OutputDataReceived += (_, e) => Capture(e.Data);
        process.ErrorDataReceived += (_, e) => Capture(e.Data);

        if (!process.Start())
            throw new InvalidOperationException($"Failed to start: dotnet {arguments}");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        string text;
        lock (output) text = output.ToString();
        return new DotnetResult(process.ExitCode, text);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Process already gone — nothing to clean up.
        }
    }
}
