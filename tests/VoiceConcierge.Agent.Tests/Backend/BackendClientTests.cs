using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using VoiceConcierge.Agent.Backend;

namespace VoiceConcierge.Agent.Tests;

public class BackendClientTests
{
    private sealed class Stub(HttpStatusCode code, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(code)
            { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
    }

    private static BackendClient Client(HttpStatusCode code, string body = "[]") =>
        new(new HttpClient(new Stub(code, body)) { BaseAddress = new Uri("http://x") },
            NullLogger<BackendClient>.Instance);

    [Fact] // QA-L-2: voice lookup failure degrades to a safe default, not a throw
    public async Task ActiveVoice_defaults_on_error()
    {
        var v = await Client(HttpStatusCode.InternalServerError)
            .GetActiveProviderVoiceIdAsync(TestContext.Current.CancellationToken);
        Assert.Equal("en-GB-RyanNeural", v);
    }

    [Fact] // QA-L-2: search surfaces a non-2xx (caller/turn handles it)
    public async Task Search_throws_on_non_2xx()
    {
        await Assert.ThrowsAnyAsync<HttpRequestException>(() =>
            Client(HttpStatusCode.BadGateway).SearchAsync("q", TestContext.Current.CancellationToken));
    }

    [Fact] // happy path: active voice resolved from /voices Active flag
    public async Task ActiveVoice_resolves_from_voices_list()
    {
        const string json =
            "[{\"id\":1,\"name\":\"James\",\"description\":\"d\",\"providerVoiceId\":\"en-GB-RyanNeural\",\"active\":false}," +
            "{\"id\":3,\"name\":\"Marcus\",\"description\":\"d\",\"providerVoiceId\":\"en-US-GuyNeural\",\"active\":true}]";
        var v = await Client(HttpStatusCode.OK, json)
            .GetActiveProviderVoiceIdAsync(TestContext.Current.CancellationToken);
        Assert.Equal("en-US-GuyNeural", v);
    }

    [Fact] // Q4-10: unanswered-record non-2xx must surface as HttpRequestException
    // so the caller's catch / log path runs (silent swallow would mask DB outages).
    public async Task Record_throws_on_non_2xx()
    {
        await Assert.ThrowsAnyAsync<HttpRequestException>(() =>
            Client(HttpStatusCode.InternalServerError)
                .RecordUnansweredAsync("anything", TestContext.Current.CancellationToken));
    }

    [Fact] // Q4-10: token round-trip happy path returns parsed dto verbatim
    public async Task GetToken_returns_dto_on_2xx()
    {
        const string body = "{\"token\":\"jwt.here.sig\",\"url\":\"ws://localhost:7880\"}";
        var (tok, url) = await Client(HttpStatusCode.OK, body)
            .GetLiveKitTokenAsync("concierge", "agent-1", TestContext.Current.CancellationToken);
        Assert.Equal("jwt.here.sig", tok);
        Assert.Equal("ws://localhost:7880", url);
    }

    [Fact] // Q4-10: token non-2xx surfaces; null body surfaces as the
    // documented InvalidOperationException ("empty token response").
    public async Task GetToken_throws_on_null_body()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Client(HttpStatusCode.OK, "null")
                .GetLiveKitTokenAsync("concierge", "agent-1", TestContext.Current.CancellationToken));
    }
}
