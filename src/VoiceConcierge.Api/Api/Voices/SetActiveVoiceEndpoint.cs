using Microsoft.AspNetCore.Mvc;
using VoiceConcierge.Api.AdminKey;
using VoiceConcierge.Api.Voices;
using VoiceConcierge.Contracts;

namespace VoiceConcierge.Api.Api.Voices;

public class SetActiveVoiceEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group
            .MapPut("", Handle)
            .RequireRateLimiting("writes")
            .RequireAdminKey()
            .WithName("SetActiveVoice");
    }

    private static async Task<IResult> Handle(
        SetActiveVoiceRequest req,
        [FromServices] VoiceService v,
        CancellationToken ct)
    {
        var ok = await v.SetActiveVoiceAsync(req.VoiceId, ct);
        return ok
            ? Results.Ok(new ActiveVoiceDto(req.VoiceId, DateTimeOffset.UtcNow))
            : Results.NotFound(new { error = "unknown or disabled voice id" });
    }
}
