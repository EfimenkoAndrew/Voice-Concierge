using Microsoft.AspNetCore.Mvc;
using VoiceConcierge.Api.Voices;

namespace VoiceConcierge.Api.Api.Voices;

public class GetVoicePreviewEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group
            .MapGet("{id:int}/preview", Handle)
            .RequireRateLimiting("preview")
            .WithName("GetVoicePreview");
    }

    private static async Task<IResult> Handle(
        int id,
        [FromServices] VoiceService v,
        CancellationToken ct)
    {
        var audio = await v.PreviewAsync(id, null, ct);
        return audio is null ? Results.NotFound() : Results.File(audio, "audio/mpeg");
    }
}
