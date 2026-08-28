using Microsoft.Extensions.DependencyInjection;

namespace WebTestToolkit.Inspector;

public static class ServiceCollectionExtensions
{
    // Singleton: browser sessions have to outlive the request that opened them. The manager
    // implements IDisposable, and the DI container disposes singletons on host shutdown,
    // which is what closes stray Chrome windows when the API stops.
    public static IServiceCollection AddWebTestToolkitInspector(
        this IServiceCollection services,
        Action<InspectorOptions>? configure = null)
    {
        services.AddOptions<InspectorOptions>();
        if (configure is not null)
            services.Configure(configure);

        services.AddSingleton<InspectorSessionManager>();
        return services;
    }
}
