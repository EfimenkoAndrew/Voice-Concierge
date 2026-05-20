using Microsoft.AspNetCore.Mvc;
using VoiceConcierge.Api.AdminKey;
using VoiceConcierge.Api.Voices;
using VoiceConcierge.Contracts;

namespace VoiceConcierge.Api.Api.Voices;

public class UpdateVoiceEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group
            .MapPut("{id:int}", Handle)
            .RequireRateLimiting("writes")
            .RequireAdminKey()
            .WithName("UpdateVoice");
    }

    private static async Task<IResult> Handle(
        int id,
        UpdateVoiceRequest req,
        [FromServices] VoiceService v,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Description))
            return Results.BadRequest(new { error = "description is required" });
        if (req.SampleText is not null && string.IsNullOrWhiteSpace(req.SampleText))
            return Results.BadRequest(new { error = "sampleText must not be blank if provided" });
        return await v.UpdateAsync(id, req, ct) switch
        {
            UpdateVoiceResult.Updated => Results.NoContent(),
            UpdateVoiceResult.NotFound => Results.NotFound(new { error = "voice not found" }),
            UpdateVoiceResult.CannotDisableActive => Results.Conflict(new { error = "cannot disable the currently-active voice" }),
            _ => Results.Problem("unexpected update result"),
        };
    }
}
