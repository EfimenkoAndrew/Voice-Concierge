using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using VoiceConcierge.Api.Search;
using VoiceConcierge.Api.Voices;
using VoiceConcierge.Contracts;

namespace VoiceConcierge.Api.Tests;

/// <summary>Sec-A-1 / N-1: optional ADMIN_API_KEY gate — enforced (incl.
/// case-insensitively, the N-1 bypass) on mutations; agent/GET paths open.</summary>
public class AdminGateTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = TestPostgres.NewBuilder().Build();
    private WebApplicationFactory<Program> _app = null!;
    private const string Key = "s3cret-admin-key";

    public async ValueTask InitializeAsync()
    {
        await _pg.StartAsync();
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Postgres", _pg.GetConnectionString());
            b.UseSetting("LIVEKIT_API_KEY", "devkey");
            b.UseSetting("LIVEKIT_API_SECRET", "devsecret_devsecret_devsecret_0123456789");
            b.UseSetting("ADMIN_API_KEY", Key);
            b.ConfigureServices(s =>
            {
                s.RemoveAll<IEmbeddingService>();
                s.AddSingleton<IEmbeddingService, Fake>();
                s.RemoveAll<ISpeechSynthesizer>();
                s.AddSingleton<ISpeechSynthesizer, FakeS>();
            });
        });
        using var _ = _app.CreateClient();
    }
    public async ValueTask DisposeAsync()
    { await _app.DisposeAsync(); await _pg.DisposeAsync(); }

    private sealed class Fake : IEmbeddingService
    { public int Dimension => 384; public bool Ready => true; public float[] Embed(string t){var v=new float[384];v[0]=1;return v;} }
    private sealed class FakeS : ISpeechSynthesizer
    { public Task<byte[]> SynthesizeAsync(string t,string v,CancellationToken ct=default)=>Task.FromResult(new byte[]{1}); }

    [Fact]
    public async Task Gate_enforced_on_mutations_incl_mixed_case_open_for_agent_and_GET()
    {
        var ct = TestContext.Current.CancellationToken;
        var c = _app.CreateClient();
        var faq = new UpsertFaqRequest("Q?", "A.", null);

        // No key → 401 on mutation, and on the mixed-case bypass attempt.
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await c.PostAsJsonAsync("/faqs", faq, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await c.PostAsJsonAsync("/FAQS", faq, ct)).StatusCode); // N-1 fixed

        // With key → passes the gate (then normal validation/creation).
        var ok = new HttpRequestMessage(HttpMethod.Post, "/faqs")
        { Content = JsonContent.Create(faq) };
        ok.Headers.Add("X-Admin-Key", Key);
        Assert.NotEqual(HttpStatusCode.Unauthorized, (await c.SendAsync(ok, ct)).StatusCode);

        // Agent + read paths stay open without a key.
        Assert.Equal(HttpStatusCode.OK,
            (await c.PostAsJsonAsync("/faqs/search", new FaqSearchRequest("hi"), ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/faqs", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted,
            (await c.PostAsJsonAsync("/unanswered", new RecordUnansweredRequest("q"), ct)).StatusCode);
    }

    // S4-10: lock in the case-insensitive predicate across EVERY mutation path
    // the v3 N-1 finding called out — a future refactor that drops the lower-
    // casing or rewrites the predicate set will break THIS test, not slip by.
    [Theory]
    [InlineData("POST", "/unanswered/00000000-0000-0000-0000-000000000001/CONVERT")]
    [InlineData("POST", "/unanswered/00000000-0000-0000-0000-000000000001/DISMISS")]
    [InlineData("PUT", "/CONFIG/voice")]
    [InlineData("POST", "/voices/2/PREVIEW")]
    public async Task Mixed_case_paths_for_every_mutation_are_gated(string method, string path)
    {
        var ct = TestContext.Current.CancellationToken;
        var c = _app.CreateClient();
        var req = new HttpRequestMessage(new HttpMethod(method), path)
        { Content = JsonContent.Create(new { voiceId = 2, answer = "x", tags = (string[]?)null }) };
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(req, ct)).StatusCode);
    }
}
