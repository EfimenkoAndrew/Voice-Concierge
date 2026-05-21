using VoiceConcierge.Agent.Audio;
using VoiceConcierge.Agent.Backend;
using VoiceConcierge.Agent.Pipeline;
using VoiceConcierge.Agent.Providers;

namespace VoiceConcierge.Agent;

public static class VoiceConciergeAgentConfiguration
{
    public static IServiceCollection AddVoiceConciergeAgent(this IServiceCollection services, IConfiguration cfg)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(cfg);

        services.AddBackendApi(cfg);
        services.AddSpeechAndLanguageProviders(cfg);
        services.AddAudioStack();
        services.AddConciergePipeline();
        services.AddScoped<ConciergeSession>();

        services.AddHostedService<ModelWarmupHostedService>();
        services.AddHostedService<ConciergeWorker>();
        services.AddHostedService<HealthEndpointHostedService>();

        return services;
    }
}
