using Microsoft.Extensions.Logging;
using VoiceConcierge.Agent.Pipeline;

namespace VoiceConcierge.Agent.Backend;

public static class BackendConfiguration
{
    private const string DefaultApiBase = "http://voiceconcierge-api:8080";
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    public static IServiceCollection AddBackendApi(this IServiceCollection services, IConfiguration cfg)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(cfg);

        var apiBase = cfg["API_BASE"] ?? DefaultApiBase;
        services.AddSingleton<IBackendApi>(sp =>
            new BackendClient(
                new HttpClient { BaseAddress = new Uri(apiBase), Timeout = DefaultTimeout },
                sp.GetRequiredService<ILogger<BackendClient>>()));
        return services;
    }
}
