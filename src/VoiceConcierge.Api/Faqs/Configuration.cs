namespace VoiceConcierge.Api.Faqs;

public static class FaqsConfiguration
{
    public static IServiceCollection AddFaqs(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<FaqAdminService>();
        return services;
    }
}
