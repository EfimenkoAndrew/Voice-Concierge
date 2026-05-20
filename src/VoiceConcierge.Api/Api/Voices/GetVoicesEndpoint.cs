using Microsoft.AspNetCore.Mvc;
using VoiceConcierge.Api.Voices;

namespace VoiceConcierge.Api.Api.Voices;

public class GetVoicesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group
            .MapGet("", Handle)
            .WithName("GetVoices");
    }

    private static async Task<IResult> Handle(
        [FromQuery] bool? includeDisabled,
        [FromServices] VoiceService v,
        CancellationToken ct) =>
        Results.Ok(await v.ListAsync(includeDisabled ?? false, ct));
}
