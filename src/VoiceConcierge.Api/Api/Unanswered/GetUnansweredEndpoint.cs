using Microsoft.AspNetCore.Mvc;
using VoiceConcierge.Api.Unanswered;

namespace VoiceConcierge.Api.Api.Unanswered;

public class GetUnansweredEndpoint
{
    private const int DefaultPageSize = 50;

    public static void Map(RouteGroupBuilder group)
    {
        group
            .MapGet("", Handle)
            .WithName("GetUnanswered");
    }

    private static async Task<IResult> Handle(
        [FromQuery] string? status,
        [FromQuery] Guid? after,
        [FromQuery] int? limit,
        [FromServices] UnansweredQueueService q,
        CancellationToken ct)
    {
        var effectiveLimit = limit is null or <= 0 ? DefaultPageSize : limit.Value;
        return Results.Ok(await q.ListAsync(status, after, effectiveLimit, ct));
    }
}
