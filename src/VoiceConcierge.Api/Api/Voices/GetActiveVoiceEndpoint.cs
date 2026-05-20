using Microsoft.AspNetCore.Mvc;
using VoiceConcierge.Api.Voices;
using VoiceConcierge.Contracts;

namespace VoiceConcierge.Api.Api.Voices;

public class GetActiveVoiceEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group
            .MapGet("", Handle)
            .WithName("GetActiveVoice");
    }

    private static async Task<IResult> Handle(
        [FromServices] VoiceService v,
        CancellationToken ct) =>
        Results.Ok(new ActiveVoiceDto(await v.GetActiveVoiceIdAsync(ct), DateTimeOffset.UtcNow));
}
