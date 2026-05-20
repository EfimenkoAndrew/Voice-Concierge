namespace VoiceConcierge.Api.Api.Faqs;

public static class FaqsApiConfiguration
{
    public static RouteGroupBuilder MapFaqs(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);
        group = group
            .MapGroup("faqs")
            .WithTags("Faqs");

        SearchFaqsEndpoint.Map(group);
        GetFaqsEndpoint.Map(group);
        CreateFaqEndpoint.Map(group);
        UpdateFaqEndpoint.Map(group);
        DeleteFaqEndpoint.Map(group);

        return group;
    }
}
