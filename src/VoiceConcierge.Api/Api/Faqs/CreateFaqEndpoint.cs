using Microsoft.AspNetCore.Mvc;
using VoiceConcierge.Api.AdminKey;
using VoiceConcierge.Api.Faqs;
using VoiceConcierge.Contracts;

namespace VoiceConcierge.Api.Api.Faqs;

public class CreateFaqEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group
            .MapPost("", Handle)
            .RequireRateLimiting("writes")
            .RequireAdminKey()
            .WithName("CreateFaq");
    }

    private static async Task<IResult> Handle(
        UpsertFaqRequest req,
        [FromServices] FaqAdminService f,
        CancellationToken ct)
    {
        var clean = FaqRequestValidation.Clean(req);
        if (FaqRequestValidation.Validate(clean) is { } e)
            return Results.BadRequest(new { error = e });
        try
        {
            var created = await f.CreateAsync(clean, ct);
            return Results.Created($"/faqs/{created.Id}", created);
        }
        catch (ArgumentException ae) { return Results.BadRequest(new { error = ae.Message }); }
        catch (DuplicateFaqException de) { return Results.Conflict(new { error = "duplicate_question", existingId = de.ExistingId }); }
    }
}
