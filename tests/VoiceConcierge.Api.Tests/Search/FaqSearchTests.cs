using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Pgvector.EntityFrameworkCore;
using Pgvector.Npgsql;
using Testcontainers.PostgreSql;
using VoiceConcierge.Api.Data;
using VoiceConcierge.Api.Search;

namespace VoiceConcierge.Api.Tests;

public class FaqSearchTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg =
        TestPostgres.NewBuilder().Build();

    public async ValueTask InitializeAsync() => await _pg.StartAsync();
    public async ValueTask DisposeAsync() => await _pg.DisposeAsync();

    private AppDbContext NewContext()
    {
        var dsb = new NpgsqlDataSourceBuilder(_pg.GetConnectionString());
        dsb.UseVector();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(dsb.Build(), npg => npg.UseVector()).Options;
        return new AppDbContext(options);
    }

    private static IConfiguration Config(double threshold) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Search:SemanticThreshold"] = threshold.ToString() }).Build();

    private FaqSearchService NewSearch(AppDbContext db, IEmbeddingService emb, double threshold = 0.62) =>
        new(db, emb, Config(threshold), NullLogger<FaqSearchService>.Instance);

    private async Task SeedAndBackfill(IEmbeddingService emb)
    {
        await using var db = NewContext();
        await new DbBootstrapper(db, NullLogger<DbBootstrapper>.Instance).RunAsync();
        await NewSearch(db, emb).BackfillEmbeddingsAsync();
    }

    [Fact] // QA-H-1: REAL bge-small embedder relevance + 0.62 threshold.
           // Gated (downloads ~130 MB) — set RUN_MODEL_TESTS=1 to run.
    public async Task Real_embedder_relevance_and_threshold()
    {
        if (Environment.GetEnvironmentVariable("RUN_MODEL_TESTS") != "1")
            Assert.Skip("RUN_MODEL_TESTS!=1 (real bge-small download).");

        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["EMBEDDING_MODEL_DIR"] = Path.Combine(Path.GetTempPath(), "vc-bge") }).Build();
        var emb = new OnnxEmbeddingService(cfg, NullLogger<OnnxEmbeddingService>.Instance);
        await SeedAndBackfill(emb);

        await using var db = NewContext();
        var search = NewSearch(db, emb);
        var ct = TestContext.Current.CancellationToken;
        var hit = await search.SearchAsync("is the card room open late at night?", ct);
        Assert.True(hit.Match && hit.Score >= 0.62, $"paraphrase should match (score {hit.Score})");
        Assert.Contains("poker", hit.Answer!, StringComparison.OrdinalIgnoreCase);
        var miss = await search.SearchAsync("what is the airspeed of an unladen swallow?", ct);
        Assert.False(miss.Match, "off-domain query must not match");
    }

    [Fact] // AC1 + AC2: paraphrase without keyword overlap matches semantically
    public async Task Paraphrased_query_matches_via_semantic_search()
    {
        var emb = new TopicEmbeddingService();
        await SeedAndBackfill(emb);

        await using var db = NewContext();
        var result = await NewSearch(db, emb)
            .SearchAsync("is the card room open late at night?", TestContext.Current.CancellationToken);

        Assert.True(result.Match);
        Assert.Equal("semantic", result.Strategy);
        Assert.Contains("poker room", result.Answer);
        Assert.True(result.Score >= 0.62);
    }

    [Fact] // AC3: no confident match -> { match:false }
    public async Task Unknown_query_returns_no_match()
    {
        var emb = new TopicEmbeddingService();
        await SeedAndBackfill(emb);

        await using var db = NewContext();
        var result = await NewSearch(db, emb)
            .SearchAsync("zxqw flarp nonsensical gibberish unrelated", TestContext.Current.CancellationToken);

        Assert.False(result.Match);
        Assert.Equal("none", result.Strategy);
        Assert.Null(result.Answer);
    }

    [Fact] // Documented degradation: embeddings unavailable -> lexical fallback
    public async Task Lexical_fallback_used_when_embeddings_unavailable()
    {
        var throwing = new ThrowingEmbeddingService();
        await SeedAndBackfill(throwing); // backfill swallows -> embeddings stay null

        await using var db = NewContext();
        var result = await NewSearch(db, throwing)
            .SearchAsync("what are the casino hours today", TestContext.Current.CancellationToken);

        Assert.True(result.Match);
        Assert.Equal("lexical", result.Strategy);
        Assert.Contains("24 hours", result.Answer);
    }

    // Deterministic topic-based embedder: same topic -> identical unit vector
    // (cosine 1.0); different topic -> near-orthogonal. Simulates paraphrase.
    private sealed class TopicEmbeddingService : IEmbeddingService
    {
        public int Dimension => Embeddings.Dimension;
        public bool Ready => true;

        public float[] Embed(string text)
        {
            var t = (text ?? "").ToLowerInvariant();
            string topic =
                t.Contains("poker") || t.Contains("card room") || t.Contains("cards") ? "poker"
                : t.Contains("restaurant") || t.Contains("dining") || t.Contains("michelin") ? "dining"
                : t.Contains("parking") || t.Contains("valet") ? "parking"
                : "other:" + (t.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "x");

            // Stable per-topic seed (independent of randomized string hashing).
            var rnd = new Random(StableSeed(topic));
            var v = new float[Embeddings.Dimension];
            double norm = 0;
            for (var i = 0; i < v.Length; i++) { v[i] = (float)(rnd.NextDouble() - 0.5); norm += v[i] * v[i]; }
            norm = Math.Sqrt(norm);
            for (var i = 0; i < v.Length; i++) v[i] = (float)(v[i] / norm);
            return v;
        }

        private static int StableSeed(string s)
        {
            unchecked { var h = 17; foreach (var c in s) h = h * 31 + c; return h; }
        }
    }

    private sealed class ThrowingEmbeddingService : IEmbeddingService
    {
        public int Dimension => Embeddings.Dimension;
        public bool Ready => false;
        public float[] Embed(string text) => throw new InvalidOperationException("model unavailable");
    }
}
