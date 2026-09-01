using WebTestToolkit.Llm;

namespace WebTestToolkit.Api.Services;

public class ApiGroqSettingsProvider : IGroqSettingsProvider
{
    private readonly ISettingsStore _settingsStore;

    public ApiGroqSettingsProvider(ISettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    public async Task<GroqSettings> GetAsync(CancellationToken ct = default)
    {
        var settings = await _settingsStore.GetAsync(ct);
        return new GroqSettings(settings.GroqApiKey, settings.GroqModel);
    }
}
