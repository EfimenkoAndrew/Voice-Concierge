namespace VoiceConcierge.Api.Unanswered;

public static class UnansweredConfiguration
{
    public static IServiceCollection AddUnanswered(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<UnansweredQueueService>();
        return services;
    }
}
