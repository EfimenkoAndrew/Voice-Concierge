namespace VoiceConcierge.Api.Search;

public static class SearchConfiguration
{
    public static IServiceCollection AddEmbeddings(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<OnnxEmbeddingService>();
        services.AddSingleton<IEmbeddingService>(sp => sp.GetRequiredService<OnnxEmbeddingService>());
        services.AddHostedService<EmbeddingWarmupHostedService>();
        services.AddScoped<FaqSearchService>();
        return services;
    }
}
