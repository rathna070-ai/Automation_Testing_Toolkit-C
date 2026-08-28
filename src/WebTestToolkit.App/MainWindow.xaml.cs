using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;

namespace WebTestToolkit.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void RunSampleTestButton_Click(object sender, RoutedEventArgs e)
    {
        RunSampleTestButton.IsEnabled = false;
        StatusText.Text = "Running... (a Chrome window will open)";
        OutputTextBox.Clear();

        try
        {
            var testProjectPath = FindGeneratedTestsProject();
            var output = await RunDotnetTestAsync(testProjectPath);
            OutputTextBox.Text = output;
            StatusText.Text = output.Contains("Failed!") || output.Contains("error")
                ? "Finished with failures — see output below"
                : "Finished — see output below";
        }
        catch (Exception ex)
        {
            OutputTextBox.Text = $"Could not run tests: {ex.Message}";
            StatusText.Text = "Error";
        }
        finally
        {
            RunSampleTestButton.IsEnabled = true;
        }
    }

    // Walks up from the app's build output folder to find the solution root,
    // so this works the same whether run from Visual Studio or `dotnet run`.
    private static string FindGeneratedTestsProject()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.GetFiles("*.sln").Any())
        {
            dir = dir.Parent;
        }

        if (dir == null)
            throw new InvalidOperationException("Could not locate the solution root (WebTestToolkit.sln).");

        var projectPath = Path.Combine(dir.FullName, "src", "WebTestToolkit.GeneratedTests", "WebTestToolkit.GeneratedTests.csproj");
        if (!File.Exists(projectPath))
            throw new InvalidOperationException($"Test project not found at: {projectPath}");

        return projectPath;
    }

    private static Task<string> RunDotnetTestAsync(string projectPath)
    {
        return Task.Run(() =>
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"test \"{projectPath}\" --logger \"console;verbosity=normal\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start dotnet test process.");

            var output = new StringBuilder();
            output.Append(process.StandardOutput.ReadToEnd());
            output.Append(process.StandardError.ReadToEnd());
            process.WaitForExit();

            return output.ToString();
        });
    }
}
