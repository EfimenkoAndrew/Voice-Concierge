using Microsoft.AspNetCore.Mvc;
using VoiceConcierge.Api.AdminKey;
using VoiceConcierge.Api.Unanswered;

namespace VoiceConcierge.Api.Api.Unanswered;

public class DismissUnansweredEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group
            .MapPost("{id:guid}/dismiss", Handle)
            .RequireRateLimiting("writes")
            .RequireAdminKey()
            .WithName("DismissUnanswered");
    }

    private static async Task<IResult> Handle(
        Guid id,
        [FromServices] UnansweredQueueService q,
        CancellationToken ct) =>
        await q.DismissAsync(id, ct) ? Results.NoContent() : Results.NotFound();
}
