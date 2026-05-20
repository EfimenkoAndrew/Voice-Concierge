using System.Net;
using System.Net.Http.Json;
using VoiceConcierge.Contracts;

namespace VoiceConcierge.Api.Tests;

[Collection(HttpCollection.Name)]
public class HttpVoiceTests(HttpFixture fx)
{
    [Theory]
    [InlineData(1, "James", "en-GB-RyanNeural")]
    [InlineData(3, "Marcus", "en-US-GuyNeural")]
    [InlineData(4, "Elena", "en-US-AriaNeural")]
    public async Task Voices_listing_round_trips_seeded_personas(int id, string name, string providerVoiceId)
    {
        var ct = TestContext.Current.CancellationToken;
        var voices = await fx.NewClient().GetFromJsonAsync<List<VoiceDto>>("/voices", ct);
        var v = voices!.Single(x => x.Id == id);
        Assert.Equal(name, v.Name);
        Assert.Equal(providerVoiceId, v.ProviderVoiceId);
    }

    [Fact]
    public async Task Voices_listing_returns_exactly_one_active()
    {
        var ct = TestContext.Current.CancellationToken;
        var voices = await fx.NewClient().GetFromJsonAsync<List<VoiceDto>>("/voices", ct);
        Assert.Equal(4, voices!.Count);
        Assert.Single(voices, v => v.Active);
    }

    [Fact]
    public async Task SetActiveVoice_persists_and_unknown_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;
        var c = fx.NewClient();
        var set = await c.PutAsJsonAsync("/config/voice", new SetActiveVoiceRequest(2), ct);
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);

        var miss = await c.PutAsJsonAsync("/config/voice", new SetActiveVoiceRequest(99), ct);
        Assert.Equal(HttpStatusCode.NotFound, miss.StatusCode);

        var active = await c.GetFromJsonAsync<ActiveVoiceDto>("/config/voice", ct);
        Assert.Equal(2, active!.VoiceId);

        var voicesAfter = await c.GetFromJsonAsync<List<VoiceDto>>("/voices", ct);
        var nowActive = Assert.Single(voicesAfter!, v => v.Active);
        Assert.Equal(2, nowActive.Id);
    }

    [Theory]
    [InlineData(2, HttpStatusCode.OK)]
    [InlineData(99, HttpStatusCode.NotFound)]
    public async Task Preview_returns_audio_or_404(int voiceId, HttpStatusCode expected)
    {
        var ct = TestContext.Current.CancellationToken;
        var r = await fx.NewClient().GetAsync($"/voices/{voiceId}/preview", ct);
        Assert.Equal(expected, r.StatusCode);
        if (expected == HttpStatusCode.OK)
        {
            Assert.Equal("audio/mpeg", r.Content.Headers.ContentType?.MediaType);
            Assert.NotEmpty(await r.Content.ReadAsByteArrayAsync(ct));
        }
    }
}
