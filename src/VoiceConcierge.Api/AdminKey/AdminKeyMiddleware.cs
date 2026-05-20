using System.Security.Cryptography;
using System.Text;

namespace VoiceConcierge.Api.AdminKey;

public sealed class AdminKeyMiddleware(
    RequestDelegate next,
    AdminKeyOptions opts,
    ILogger<AdminKeyMiddleware> log)
{
    private const string AdminKeyHeader = "X-Admin-Key";

    public async Task InvokeAsync(HttpContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var endpoint = ctx.GetEndpoint();
        var requiresAdminKey = endpoint?.Metadata.GetMetadata<RequireAdminKeyAttribute>() is not null;
        if (!requiresAdminKey)
        {
            await next(ctx);
            return;
        }

        var presented = ctx.Request.Headers[AdminKeyHeader].ToString();
        if (Accept(presented))
        {
            log.LogInformation("Admin key accepted: {Path}", ctx.Request.Path);
            await next(ctx);
            return;
        }

        log.LogWarning("Admin key rejected: {Path} from {Ip}",
            ctx.Request.Path, ctx.Connection.RemoteIpAddress);
        if (!ctx.Response.HasStarted)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsJsonAsync(new { error = "admin_key_required" });
        }
    }

    private bool Accept(string presented)
    {
        if (!opts.IsEnabled) return true;
        return ConstantTimeEquals(presented, opts.Primary!)
            || (opts.Previous is { Length: >= 16 } && ConstantTimeEquals(presented, opts.Previous));
    }

    private static bool ConstantTimeEquals(string a, string b)
    {
        var aHash = SHA256.HashData(Encoding.UTF8.GetBytes(a));
        var bHash = SHA256.HashData(Encoding.UTF8.GetBytes(b));
        return CryptographicOperations.FixedTimeEquals(aHash, bHash);
    }
}
