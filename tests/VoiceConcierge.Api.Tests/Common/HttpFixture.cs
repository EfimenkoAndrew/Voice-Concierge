using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using VoiceConcierge.Api.Data;
using VoiceConcierge.Api.Search;
using VoiceConcierge.Api.Voices;

namespace VoiceConcierge.Api.Tests;

public sealed class HttpFixture : IAsyncLifetime
{
    public PostgreSqlContainer Postgres { get; } = TestPostgres.NewBuilder().Build();
    public WebApplicationFactory<Program> App { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await Postgres.StartAsync();
        App = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Postgres", Postgres.GetConnectionString());
            b.UseSetting("LIVEKIT_API_KEY", "devkey");
            b.UseSetting("LIVEKIT_API_SECRET", "devsecret_devsecret_devsecret_0123456789");
            b.ConfigureServices(s =>
            {
                s.RemoveAll<IEmbeddingService>();
                s.AddSingleton<IEmbeddingService, HashEmbedder>();
                s.RemoveAll<ISpeechSynthesizer>();
                s.AddSingleton<ISpeechSynthesizer, StubSynth>();
            });
        });
        using var _ = App.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        await App.DisposeAsync();
        await Postgres.DisposeAsync();
    }

    public HttpClient NewClient() => App.CreateClient();

    private sealed class HashEmbedder : IEmbeddingService
    {
        public int Dimension => Embeddings.Dimension;
        public bool Ready => true;
        public float[] Embed(string text)
        {
            var v = new float[Dimension];
            v[Math.Abs(text.GetHashCode(StringComparison.Ordinal)) % v.Length] = 1f;
            return v;
        }
    }

    private sealed class StubSynth : ISpeechSynthesizer
    {
        private static readonly byte[] StubMp3 = [1, 2, 3];
        public Task<byte[]> SynthesizeAsync(string text, string voiceId, CancellationToken ct = default) =>
            Task.FromResult(StubMp3);
    }
}

[CollectionDefinition(Name)]
public sealed class HttpCollection : ICollectionFixture<HttpFixture>
{
    public const string Name = "Http";
}

public record HealthDto(string Status, string Service, string Db, string Embedding);
