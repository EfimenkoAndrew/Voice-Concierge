namespace VoiceConcierge.Api.Api.Voices;

public static class VoicesApiConfiguration
{
    public static void MapVoices(this RouteGroupBuilder group)
    {
        var voices = group
            .MapGroup("voices")
            .WithTags("Voices");

        GetVoicesEndpoint.Map(voices);
        UpdateVoiceEndpoint.Map(voices);
        GetVoicePreviewEndpoint.Map(voices);
        PostVoicePreviewEndpoint.Map(voices);

        var activeVoice = group
            .MapGroup("config/voice")
            .WithTags("Voices");

        GetActiveVoiceEndpoint.Map(activeVoice);
        SetActiveVoiceEndpoint.Map(activeVoice);
    }
}
