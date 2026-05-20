using Microsoft.AspNetCore.Mvc;
using VoiceConcierge.Api.AdminKey;
using VoiceConcierge.Api.Search;
using VoiceConcierge.Api.Voices;
using VoiceConcierge.Contracts;

namespace VoiceConcierge.Api.Api.Voices;

public class PostVoicePreviewEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group
            .MapPost("{id:int}/preview", Handle)
            .RequireRateLimiting("preview")
            .RequireAdminKey()
            .WithName("PostVoicePreview");
    }

    private static async Task<IResult> Handle(
        int id,
        PreviewWithRequest? req,
        [FromServices] VoiceService v,
        CancellationToken ct)
    {
        string? text = null;
        if (req is not null)
        {
            text = TextSanitization.Sanitize(req.Text);
            if (text.Length > 200)
                return Results.BadRequest(new { error = "text ≤200 chars" });
            if (text.Length == 0) text = null;
        }
        var audio = await v.PreviewAsync(id, text, ct);
        return audio is null ? Results.NotFound() : Results.File(audio, "audio/mpeg");
    }
}
