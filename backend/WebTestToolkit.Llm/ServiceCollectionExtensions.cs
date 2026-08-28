using Microsoft.Extensions.DependencyInjection;
using WebTestToolkit.Llm.Skills;
using WebTestToolkit.Llm.Transport;

namespace WebTestToolkit.Llm;

public static class ServiceCollectionExtensions
{
    // Callers must also register IGroqSettingsProvider — the Llm project has no opinion on
    // where settings live (file, env var, database), only on how they're consumed.
    public static IServiceCollection AddWebTestToolkitLlm(this IServiceCollection services)
    {
        services.AddSingleton<PromptLibrary>();
        services.AddHttpClient<GroqClient>();
        services.AddScoped<IChatClient>(sp => sp.GetRequiredService<GroqClient>());
        services.AddScoped<FailureAnalysisSkill>();
        return services;
    }
}
