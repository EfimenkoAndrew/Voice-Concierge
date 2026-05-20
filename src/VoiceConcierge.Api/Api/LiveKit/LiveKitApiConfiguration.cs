namespace VoiceConcierge.Api.Api.LiveKit;

public static class LiveKitApiConfiguration
{
    public static RouteGroupBuilder MapLiveKit(this RouteGroupBuilder group)
    {
        group = group
            .MapGroup("livekit")
            .WithTags("LiveKit");

        IssueLiveKitTokenEndpoint.Map(group);
        GetLiveKitConfigEndpoint.Map(group);

        return group;
    }
}
