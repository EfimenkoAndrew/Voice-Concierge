using Microsoft.AspNetCore.Mvc;
using VoiceConcierge.Api.AdminKey;
using VoiceConcierge.Api.Faqs;
using VoiceConcierge.Contracts;

namespace VoiceConcierge.Api.Api.Faqs;

public class UpdateFaqEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group
            .MapPut("{id:guid}", Handle)
            .RequireRateLimiting("writes")
            .RequireAdminKey()
            .WithName("UpdateFaq");
    }

    private static async Task<IResult> Handle(
        Guid id,
        UpsertFaqRequest req,
        [FromServices] FaqAdminService f,
        CancellationToken ct)
    {
        var clean = FaqRequestValidation.Clean(req);
        if (FaqRequestValidation.Validate(clean) is { } e)
            return Results.BadRequest(new { error = e });
        try
        {
            return await f.UpdateAsync(id, clean, ct)
                ? Results.NoContent()
                : Results.NotFound();
        }
        catch (ArgumentException ae) { return Results.BadRequest(new { error = ae.Message }); }
        catch (DuplicateFaqException de) { return Results.Conflict(new { error = "duplicate_question", existingId = de.ExistingId }); }
    }
}
