using VoiceConcierge.Api.AdminKey;
using VoiceConcierge.Api.Data;
using VoiceConcierge.Api.Faqs;
using VoiceConcierge.Api.LiveKit;
using VoiceConcierge.Api.RateLimiting;
using VoiceConcierge.Api.Search;
using VoiceConcierge.Api.Unanswered;
using VoiceConcierge.Api.Voices;

namespace VoiceConcierge.Api;

public static class VoiceConciergeApiConfiguration
{
    public static bool AddVoiceConciergeApi(this IServiceCollection services, IConfiguration cfg)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(cfg);

        services.AddCors(o => o.AddDefaultPolicy(p =>
            p.WithOrigins((cfg["UI_ORIGIN"]
                    ?? "http://localhost:5173,http://localhost:8080,http://localhost").Split(','))
             .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
             .WithHeaders("Content-Type", "Accept", "X-Admin-Key")));

        services.AddVoiceConciergeRateLimiting();
        services.AddAdminKey(cfg);

        var dbConfigured = services.AddDataContext(cfg);
        if (dbConfigured)
        {
            services.AddEmbeddings();
            services.AddFaqs();
            services.AddVoices();
            services.AddUnanswered();
        }

        services.AddLiveKit();
        return dbConfigured;
    }
}
