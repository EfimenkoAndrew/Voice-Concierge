using System.Net;
using System.Net.Http.Json;
using VoiceConcierge.Contracts;

namespace VoiceConcierge.Api.Tests;

[Collection(HttpCollection.Name)]
public class HttpLiveKitTests(HttpFixture fx)
{
    [Theory]
    [InlineData("bad room!", "id")]
    [InlineData("", "agent-1")]
    [InlineData("concierge", "")]
    public async Task Token_rejects_invalid_room_or_identity(string room, string identity)
    {
        var ct = TestContext.Current.CancellationToken;
        var r = await fx.NewClient().PostAsJsonAsync("/livekit/token",
            new LiveKitTokenRequest(room, identity), ct);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Token_issues_jwt_for_valid_room_and_identity()
    {
        var ct = TestContext.Current.CancellationToken;
        var ok = await fx.NewClient().PostAsJsonAsync("/livekit/token",
            new LiveKitTokenRequest("concierge", "agent-1"), ct);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var tok = await ok.Content.ReadFromJsonAsync<LiveKitTokenResponse>(ct);
        Assert.Equal(3, tok!.Token.Split('.').Length);
    }

    [Fact]
    public async Task Config_returns_room_name()
    {
        var ct = TestContext.Current.CancellationToken;
        var dto = await fx.NewClient().GetFromJsonAsync<LiveKitConfigDto>("/livekit/config", ct);
        Assert.NotNull(dto);
        Assert.False(string.IsNullOrWhiteSpace(dto!.Room));
        Assert.Equal("concierge", dto.Room);
    }
}
