namespace VoiceConcierge.Agent.Audio;

public static class AudioConfiguration
{
    public static IServiceCollection AddAudioStack(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<SileroVad>();
        services.AddSingleton<IVoiceActivityDetector>(sp => sp.GetRequiredService<SileroVad>());
        services.AddSingleton<GuestAudioStream>();
        return services;
    }
}
