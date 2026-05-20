using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VoiceConcierge.Api.LiveKit;

public sealed class LiveKitTokenService(IConfiguration config)
{
    private static readonly TimeSpan DefaultTokenTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan NotBeforeSkew = TimeSpan.FromSeconds(10);

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(config["LIVEKIT_API_KEY"]) &&
        !string.IsNullOrWhiteSpace(config["LIVEKIT_API_SECRET"]);

    public string Url => config["LIVEKIT_URL"] ?? "ws://localhost:7880";

    public string CreateToken(string room, string identity, TimeSpan? ttl = null)
    {
        ArgumentNullException.ThrowIfNull(room);
        ArgumentNullException.ThrowIfNull(identity);
        var apiKey = config["LIVEKIT_API_KEY"] ?? throw new InvalidOperationException("LIVEKIT_API_KEY missing");
        var apiSecret = config["LIVEKIT_API_SECRET"] ?? throw new InvalidOperationException("LIVEKIT_API_SECRET missing");
        var now = DateTimeOffset.UtcNow;
        var exp = now.Add(ttl ?? DefaultTokenTtl);

        var header = new { alg = "HS256", typ = "JWT" };
        var payload = new Dictionary<string, object>
        {
            ["iss"] = apiKey,
            ["sub"] = identity,
            ["nbf"] = now.Subtract(NotBeforeSkew).ToUnixTimeSeconds(),
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = exp.ToUnixTimeSeconds(),
            ["name"] = identity,
            ["video"] = new Dictionary<string, object>
            {
                ["roomJoin"] = true,
                ["room"] = room,
                ["canPublish"] = true,
                ["canSubscribe"] = true,
            },
        };

        var h = B64Url(JsonSerializer.SerializeToUtf8Bytes(header));
        var p = B64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
        var input = $"{h}.{p}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(apiSecret));
        var sig = B64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes(input)));
        return $"{input}.{sig}";
    }

    private static string B64Url(byte[] b) => System.Buffers.Text.Base64Url.EncodeToString(b);
}
