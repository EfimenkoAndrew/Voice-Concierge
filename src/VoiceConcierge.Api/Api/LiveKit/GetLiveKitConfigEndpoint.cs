using Microsoft.AspNetCore.Mvc;
using VoiceConcierge.Contracts;

namespace VoiceConcierge.Api.Api.LiveKit;

public class GetLiveKitConfigEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group
            .MapGet("config", Handle)
            .WithName("GetLiveKitConfig");
    }

    private static IResult Handle([FromServices] IConfiguration cfg) =>
        Results.Ok(new LiveKitConfigDto(cfg["LIVEKIT_ROOM"] ?? "concierge"));
}
