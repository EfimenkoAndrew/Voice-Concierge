namespace VoiceConcierge.Agent.Pipeline;

public static class PipelineConfiguration
{
    public static IServiceCollection AddConciergePipeline(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ConciergePipeline>();
        return services;
    }
}
