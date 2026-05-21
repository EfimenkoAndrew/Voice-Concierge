namespace VoiceConcierge.Agent.Audio;

public static class AudioConfiguration
{
    public static IServiceCollection AddAudioStack(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<SileroVadModel>();
        services.AddScoped<SileroVad>();
        services.AddScoped<IVoiceActivityDetector>(sp => sp.GetRequiredService<SileroVad>());
        services.AddScoped<GuestAudioStream>();
        return services;
    }
}
