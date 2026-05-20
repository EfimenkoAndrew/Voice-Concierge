namespace VoiceConcierge.Api.LiveKit;

public static class LiveKitConfiguration
{
    public static IServiceCollection AddLiveKit(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<LiveKitTokenService>();
        return services;
    }
}
