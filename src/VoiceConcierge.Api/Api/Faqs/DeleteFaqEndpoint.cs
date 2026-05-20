using Microsoft.AspNetCore.Mvc;
using VoiceConcierge.Api.AdminKey;
using VoiceConcierge.Api.Faqs;

namespace VoiceConcierge.Api.Api.Faqs;

public class DeleteFaqEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group
            .MapDelete("{id:guid}", Handle)
            .RequireRateLimiting("writes")
            .RequireAdminKey()
            .WithName("DeleteFaq");
    }

    private static async Task<IResult> Handle(
        Guid id,
        [FromServices] FaqAdminService f,
        CancellationToken ct) =>
        await f.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound();
}
