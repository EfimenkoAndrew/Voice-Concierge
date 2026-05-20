using Microsoft.AspNetCore.Mvc;
using VoiceConcierge.Api.AdminKey;
using VoiceConcierge.Api.Search;
using VoiceConcierge.Api.Unanswered;
using VoiceConcierge.Contracts;

namespace VoiceConcierge.Api.Api.Unanswered;

public class ConvertUnansweredEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group
            .MapPost("{id:guid}/convert", Handle)
            .RequireRateLimiting("writes")
            .RequireAdminKey()
            .WithName("ConvertUnanswered");
    }

    private static async Task<IResult> Handle(
        Guid id,
        ConvertUnansweredRequest req,
        [FromServices] UnansweredQueueService q,
        CancellationToken ct)
    {
        var ans = TextSanitization.Sanitize(req.Answer);
        var tags = req.Tags?.Select(TextSanitization.Sanitize).ToArray();
        if (string.IsNullOrWhiteSpace(ans) || ans.Length > 4000)
            return Results.BadRequest(new { error = "answer required, ≤4000 chars" });
        if ((tags?.Length ?? 0) > 8)
            return Results.BadRequest(new { error = "≤8 tags" });
        if (tags?.Any(t => string.IsNullOrWhiteSpace(t) || t.Length > 24) == true)
            return Results.BadRequest(new { error = "each tag must be 1-24 chars" });
        var (faqId, alreadyResolved) = await q.ConvertAsync(id, new ConvertUnansweredRequest(ans, tags), ct);
        if (faqId is null && !alreadyResolved) return Results.NotFound();
        if (faqId is null) return Results.Conflict(new { error = "already_resolved" });
        return Results.Created($"/faqs/{faqId}", new { faqId });
    }
}
