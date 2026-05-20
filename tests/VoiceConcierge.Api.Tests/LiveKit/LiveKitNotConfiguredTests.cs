using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using VoiceConcierge.Api.Search;
using VoiceConcierge.Api.Voices;
using VoiceConcierge.Contracts;

namespace VoiceConcierge.Api.Tests;

/// <summary>
/// Q4-03: when LIVEKIT_API_KEY/SECRET are unset, POST /livekit/token must
/// surface 503 (not 200 with a garbage token). HttpEndpointTests boots a host
/// WITH the secrets set; this fixture is its negative twin.
/// </summary>
public class LiveKitNotConfiguredTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = TestPostgres.NewBuilder().Build();
    private WebApplicationFactory<Program> _app = null!;

    public async ValueTask InitializeAsync()
    {
        await _pg.StartAsync();
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Postgres", _pg.GetConnectionString());
            // deliberately do NOT set LIVEKIT_API_KEY / LIVEKIT_API_SECRET
            b.ConfigureServices(s =>
            {
                s.RemoveAll<IEmbeddingService>();
                s.AddSingleton<IEmbeddingService, Fake>();
                s.RemoveAll<ISpeechSynthesizer>();
                s.AddSingleton<ISpeechSynthesizer, FakeSynth>();
            });
        });
        using var _ = _app.CreateClient();
    }
    public async ValueTask DisposeAsync()
    { await _app.DisposeAsync(); await _pg.DisposeAsync(); }

    private sealed class Fake : IEmbeddingService
    { public int Dimension => 384; public bool Ready => true; public float[] Embed(string t){var v=new float[384];v[0]=1;return v;} }
    private sealed class FakeSynth : ISpeechSynthesizer
    { public Task<byte[]> SynthesizeAsync(string t,string v,CancellationToken ct=default)=>Task.FromResult(new byte[]{1}); }

    [Fact]
    public async Task Token_endpoint_returns_503_when_unconfigured()
    {
        var ct = TestContext.Current.CancellationToken;
        var res = await _app.CreateClient().PostAsJsonAsync("/livekit/token",
            new LiveKitTokenRequest("concierge", "agent-1"), ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
    }
}
