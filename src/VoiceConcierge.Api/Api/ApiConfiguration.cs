using VoiceConcierge.Api.Api.Faqs;
using VoiceConcierge.Api.Api.LiveKit;
using VoiceConcierge.Api.Api.Unanswered;
using VoiceConcierge.Api.Api.Voices;

namespace VoiceConcierge.Api.Api;

public static class ApiConfiguration
{
    public static void MapApi(this IEndpointRouteBuilder endpoints, bool dbConfigured)
    {
        var group = endpoints.MapGroup("");

        if (dbConfigured)
        {
            group.MapFaqs();
            group.MapUnanswered();
            group.MapVoices();
        }

        group.MapLiveKit();
    }
}
