using Microsoft.AspNetCore.Mvc;
using VoiceConcierge.Api.Data;
using VoiceConcierge.Api.Search;
using VoiceConcierge.Contracts;

namespace VoiceConcierge.Api.Api.Unanswered;

public class RecordUnansweredEndpoint
{
    private const int MaxQuestionLength = 1000;

    public static void Map(RouteGroupBuilder group)
    {
        group
            .MapPost("", Handle)
            .RequireRateLimiting("writes")
            .WithName("RecordUnanswered");
    }

    private static async Task<IResult> Handle(
        RecordUnansweredRequest req,
        [FromServices] UnansweredService svc,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);
        var q = TextSanitization.Sanitize(req.Question);
        if (string.IsNullOrWhiteSpace(q) || q.Length > MaxQuestionLength)
            return Results.BadRequest(new { error = "question required, ≤1000 chars" });
        await svc.RecordAsync(q, ct);
        return Results.Accepted();
    }
}
