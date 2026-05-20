using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace VoiceConcierge.Api.RateLimiting;

public static class RateLimitingConfiguration
{
    private const int WriteRateLimit = 60;
    private const int TokenRateLimit = 20;
    private const int PreviewRateLimit = 60;

    public static IServiceCollection AddVoiceConciergeRateLimiting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddFixedWindowLimiter("writes", w =>
            {
                w.PermitLimit = WriteRateLimit;
                w.Window = TimeSpan.FromMinutes(1);
                w.QueueLimit = 0;
            });
            o.AddPolicy("token", ctx =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = TokenRateLimit,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));
            o.AddPolicy("preview", ctx =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = PreviewRateLimit,
                        Window = TimeSpan.FromMinutes(10),
                        QueueLimit = 0,
                    }));
        });
        return services;
    }
}
