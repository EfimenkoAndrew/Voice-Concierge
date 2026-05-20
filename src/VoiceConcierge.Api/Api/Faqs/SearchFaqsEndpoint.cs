using Microsoft.AspNetCore.Mvc;
using VoiceConcierge.Api.Search;
using VoiceConcierge.Contracts;

namespace VoiceConcierge.Api.Api.Faqs;

public class SearchFaqsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group
            .MapPost("search", Handle)
            .WithName("SearchFaqs");
    }

    private static async Task<IResult> Handle(
        FaqSearchRequest req,
        [FromServices] FaqSearchService search,
        CancellationToken ct)
    {
        var result = await search.SearchAsync(req.Query, ct);
        return Results.Ok(result);
    }
}
