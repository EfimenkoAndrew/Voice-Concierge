using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using VoiceConcierge.Api.LiveKit;
using VoiceConcierge.Contracts;

namespace VoiceConcierge.Api.Api.LiveKit;

public class IssueLiveKitTokenEndpoint
{
    private const int MaxIdentifierLength = 64;
    private const string IdentifierPattern = "^[A-Za-z0-9_-]+$";

    public static void Map(RouteGroupBuilder group)
    {
        group
            .MapPost("token", Handle)
            .RequireRateLimiting("token")
            .WithName("IssueLiveKitToken");
    }

    private static IResult Handle(
        LiveKitTokenRequest req,
        [FromServices] LiveKitTokenService lk)
    {
        ArgumentNullException.ThrowIfNull(req);
        ArgumentNullException.ThrowIfNull(lk);
        if (!IsValid(req.Room) || !IsValid(req.Identity))
            return Results.BadRequest(new { error = "room/identity required: ^[A-Za-z0-9_-]{1,64}$" });
        if (!lk.IsConfigured)
            return Results.Problem("LiveKit is not configured (LIVEKIT_API_KEY/SECRET).", statusCode: 503);
        var token = lk.CreateToken(req.Room, req.Identity);
        return Results.Ok(new LiveKitTokenResponse(token, lk.Url));
    }

    private static bool IsValid(string? s) =>
        !string.IsNullOrWhiteSpace(s) && s.Length <= MaxIdentifierLength &&
        Regex.IsMatch(s, IdentifierPattern);
}
