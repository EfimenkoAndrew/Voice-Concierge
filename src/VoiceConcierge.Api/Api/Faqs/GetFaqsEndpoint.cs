using Microsoft.AspNetCore.Mvc;
using VoiceConcierge.Api.Faqs;

namespace VoiceConcierge.Api.Api.Faqs;

public class GetFaqsEndpoint
{
    private const int DefaultPageSize = 50;

    public static void Map(RouteGroupBuilder group)
    {
        group
            .MapGet("", Handle)
            .WithName("GetFaqs");
    }

    private static async Task<IResult> Handle(
        [FromQuery] Guid? after,
        [FromQuery] int? limit,
        [FromServices] FaqAdminService f,
        CancellationToken ct)
    {
        var effectiveLimit = limit is null or <= 0 ? DefaultPageSize : limit.Value;
        return Results.Ok(await f.ListAsync(after, effectiveLimit, ct));
    }
}
