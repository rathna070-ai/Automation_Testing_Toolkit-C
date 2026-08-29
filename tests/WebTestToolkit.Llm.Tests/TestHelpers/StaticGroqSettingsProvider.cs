using WebTestToolkit.Llm;

namespace WebTestToolkit.Llm.Tests.TestHelpers;

public class StaticGroqSettingsProvider : IGroqSettingsProvider
{
    private readonly GroqSettings _settings;

    public StaticGroqSettingsProvider(GroqSettings settings) => _settings = settings;

    public static StaticGroqSettingsProvider WithKey(string apiKey, string model = "openai/gpt-oss-120b") =>
        new(new GroqSettings(apiKey, model));

    public static StaticGroqSettingsProvider NoKey(string model = "openai/gpt-oss-120b") =>
        new(new GroqSettings(null, model));

    public Task<GroqSettings> GetAsync(CancellationToken ct = default) => Task.FromResult(_settings);
}
