using System.Reflection;

namespace WebTestToolkit.Inspector.Overlay;

// Reads inspector-overlay.js out of the assembly. Embedded rather than copied to the output
// directory so the script can never go missing at runtime — a null overlay would look like
// "inspect silently captures nothing", which is a miserable bug to diagnose.
internal static class OverlayScript
{
    private const string ResourceName = "WebTestToolkit.Inspector.Overlay.inspector-overlay.js";

    private static readonly Lazy<string> Source = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    public static string Value => Source.Value;

    private static string Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' is missing. Check that the .csproj still has " +
                "an <EmbeddedResource Include=\"Overlay\\inspector-overlay.js\" /> item.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
