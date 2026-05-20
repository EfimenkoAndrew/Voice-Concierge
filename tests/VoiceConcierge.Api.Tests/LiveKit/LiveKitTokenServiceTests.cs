using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using VoiceConcierge.Api.LiveKit;

namespace VoiceConcierge.Api.Tests;

public class LiveKitTokenServiceTests
{
    private const string Key = "devkey";
    private const string Secret = "devsecret_devsecret_devsecret_0123456789";

    private static LiveKitTokenService NewService() =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LIVEKIT_API_KEY"] = Key,
            ["LIVEKIT_API_SECRET"] = Secret,
            ["LIVEKIT_URL"] = "ws://localhost:7880",
        }).Build());

    private static byte[] B64Url(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        s = (s.Length % 4) switch { 2 => s + "==", 3 => s + "=", _ => s };
        return Convert.FromBase64String(s);
    }

    [Fact] // AC1 + AC2 + AC3: signed token with correct grants/expiry; secret not leaked
    public void Token_has_expected_grants_and_valid_signature()
    {
        var svc = NewService();

        var token = svc.CreateToken("spike-room", "agent-1");

        var parts = token.Split('.');
        Assert.Equal(3, parts.Length);

        var header = JsonDocument.Parse(B64Url(parts[0])).RootElement;
        Assert.Equal("HS256", header.GetProperty("alg").GetString());

        var payload = JsonDocument.Parse(B64Url(parts[1])).RootElement;
        Assert.Equal(Key, payload.GetProperty("iss").GetString());
        Assert.Equal("agent-1", payload.GetProperty("sub").GetString());
        var video = payload.GetProperty("video");
        Assert.True(video.GetProperty("roomJoin").GetBoolean());
        Assert.Equal("spike-room", video.GetProperty("room").GetString());
        Assert.True(video.GetProperty("canPublish").GetBoolean());
        Assert.True(video.GetProperty("canSubscribe").GetBoolean());
        var exp = payload.GetProperty("exp").GetInt64();
        Assert.True(exp > DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        // Signature must validate with the secret (proves secret used server-side).
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        var expectedSig = Convert.ToBase64String(
            hmac.ComputeHash(Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}")))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(expectedSig, parts[2]);

        // The response contract carries only token + url — never the secret.
        Assert.DoesNotContain(Secret, token);
    }

    [Fact]
    public void IsConfigured_false_when_credentials_missing()
    {
        var svc = new LiveKitTokenService(
            new ConfigurationBuilder().AddInMemoryCollection([]).Build());
        Assert.False(svc.IsConfigured);
    }
}
