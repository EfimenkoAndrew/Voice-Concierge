namespace VoiceConcierge.Agent.LiveKit;

public static class LiveKitConfiguration
{
    public static IServiceCollection AddLiveKitClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddTransient<AgentLiveKitClient>();
        return services;
    }
}
