namespace VoiceConcierge.Api.Api.Unanswered;

public static class UnansweredApiConfiguration
{
    public static RouteGroupBuilder MapUnanswered(this RouteGroupBuilder group)
    {
        group = group
            .MapGroup("unanswered")
            .WithTags("Unanswered");

        RecordUnansweredEndpoint.Map(group);
        GetUnansweredEndpoint.Map(group);
        ConvertUnansweredEndpoint.Map(group);
        DismissUnansweredEndpoint.Map(group);

        return group;
    }
}
