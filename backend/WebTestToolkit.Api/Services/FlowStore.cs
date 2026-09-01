using System.Text.Json;
using System.Text.Json.Serialization;
using WebTestToolkit.Contracts.Models;

namespace WebTestToolkit.Api.Services;

public record SavedFlowSummary(string Name, string StartUrl, int StepCount, DateTimeOffset SavedUtc);

// P19. Until this existed, a recorded flow lived only in the live InspectorSession (evicted
// after InspectorOptions.CompletedRetention, and gone entirely on an API restart) and in the
// React router state of whichever tab recorded it. Closing the tab lost the recording, which
// meant a flow could never be re-generated after the UI it targets changed — the single thing
// this toolkit does that `playwright codegen` cannot, and it was unavailable.
//
// Deliberately files-on-disk rather than a database: this is a single-user local dev tool and
// FileSettingsStore already established the pattern (same %AppData%\WebTestToolkit root, same
// SemaphoreSlim, same write-temp-then-move so an interrupted write cannot leave a truncated
// flow behind).
public class FlowStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ILogger<FlowStore> _logger;
    private readonly string _directory;

    // baseDirectory is injectable for the same reason LocatorJsonPatcher's is: a test must
    // never write into the developer's real %AppData%.
    public FlowStore(ILogger<FlowStore> logger, string? baseDirectory = null)
    {
        _logger = logger;
        _directory = baseDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WebTestToolkit", "flows");
    }

    public async Task SaveAsync(TestFlow flow, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(flow.Name))
            throw new ArgumentException("A flow needs a name to be saved.", nameof(flow));

        await _lock.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(_directory);
            var path = PathFor(flow.Name);

            var json = JsonSerializer.Serialize(flow, JsonOptions);
            var temp = path + ".tmp";
            await File.WriteAllTextAsync(temp, json, ct);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<SavedFlowSummary>> ListAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!Directory.Exists(_directory))
                return [];

            var summaries = new List<SavedFlowSummary>();
            foreach (var path in Directory.EnumerateFiles(_directory, "*.json").OrderBy(p => p, StringComparer.Ordinal))
            {
                var flow = ReadFlow(path);
                if (flow is null)
                    continue;

                summaries.Add(new SavedFlowSummary(
                    flow.Name, flow.StartUrl, flow.Steps.Count, File.GetLastWriteTimeUtc(path)));
            }

            // Most recently saved first — the one you just recorded is the one you want.
            return summaries.OrderByDescending(s => s.SavedUtc).ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<TestFlow?> GetAsync(string name, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var path = PathFor(name);
            return File.Exists(path) ? ReadFlow(path) : null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> DeleteAsync(string name, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var path = PathFor(name);
            if (!File.Exists(path))
                return false;

            File.Delete(path);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    private TestFlow? ReadFlow(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<TestFlow>(File.ReadAllText(path), ReadOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // One unreadable file must not take out the whole list.
            _logger.LogWarning(ex, "Could not read saved flow at {Path}; skipping it", path);
            return null;
        }
    }

    // The flow name is free text a user typed, and it becomes a path here — so it is
    // sanitized the same way TestFlowCodeGenerator sanitizes it into a class name, and the
    // result is then confirmed to still sit inside the flows directory. Without the second
    // check a name like "../../settings" would escape it.
    private string PathFor(string name)
    {
        var safe = new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c).ToArray()).Trim();
        if (safe.Length == 0)
            safe = "flow";

        var full = Path.GetFullPath(Path.Combine(_directory, safe + ".json"));
        var root = Path.GetFullPath(_directory);
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"'{name}' does not resolve to a path inside the flow store.", nameof(name));

        return full;
    }
}
