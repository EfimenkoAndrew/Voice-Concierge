namespace VoiceConcierge.Api.Voices;

public static class VoicesConfiguration
{
    public static IServiceCollection AddVoices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ISpeechSynthesizer, EdgeTtsSynthesizer>();
        services.AddScoped<VoiceService>();
        return services;
    }
}
