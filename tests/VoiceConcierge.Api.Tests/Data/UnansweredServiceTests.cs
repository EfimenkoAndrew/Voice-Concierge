using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Pgvector.EntityFrameworkCore;
using Pgvector.Npgsql;
using Testcontainers.PostgreSql;
using VoiceConcierge.Api.Data;
using VoiceConcierge.Api.Search;

namespace VoiceConcierge.Api.Tests;

file sealed class NotReadyEmbedder : IEmbeddingService
{
    public int Dimension => Embeddings.Dimension;
    public bool Ready => false;
    public float[] Embed(string text) => throw new InvalidOperationException("not ready");
}

file sealed class DeterministicEmbedder : IEmbeddingService
{
    public int Dimension => Embeddings.Dimension;
    public bool Ready => true;
    public float[] Embed(string text)
    {
        var v = new float[Embeddings.Dimension];
        var norm = TextNormalization.Normalize(text);
        var tokens = norm.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var t in tokens)
            v[Math.Abs(t.GetHashCode(StringComparison.Ordinal)) % Dimension] = 1f;
        var mag = 0f;
        foreach (var x in v) mag += x * x;
        mag = MathF.Sqrt(mag);
        if (mag > 0)
            for (var i = 0; i < v.Length; i++) v[i] /= mag;
        return v;
    }
}

public class UnansweredServiceTests : IAsyncLifetime
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

    private async Task Migrate()
    {
        await using var db = NewContext();
        await new DbBootstrapper(db, NullLogger<DbBootstrapper>.Instance).RunAsync();
    }

    [Fact] // AC1 + AC2 + AC3 + AC4: insert then increment, no duplicate row
    public async Task Repeated_question_increments_frequency_without_duplicate()
    {
        var ct = TestContext.Current.CancellationToken;
        await Migrate();

        await using (var db = NewContext())
        {
            var svc = new UnansweredService(db, new NotReadyEmbedder());
            await svc.RecordAsync("Can I bring my dog to the hotel?", ct);
            await svc.RecordAsync("  can i BRING my dog to the hotel??? ", ct); // variant
            await svc.RecordAsync("Do you allow cats?", ct); // distinct
        }

        await using var verify = NewContext();
        var dogRows = await verify.UnansweredQuestions
            .Where(u => u.NormalizedQuestion == "can i bring my dog to the hotel")
            .ToListAsync(ct);

        Assert.Single(dogRows);                              // AC3: no duplicate
        Assert.Equal(2, dogRows[0].Frequency);               // AC1/AC2
        Assert.Equal(UnansweredStatus.Open, dogRows[0].Status);
        Assert.True(dogRows[0].LastAskedAt >= dogRows[0].FirstAskedAt);
        Assert.Equal(2, await verify.UnansweredQuestions.CountAsync(ct)); // dog + cats
    }

    [Fact]
    public async Task Semantically_similar_paraphrases_collapse_to_one_row()
    {
        var ct = TestContext.Current.CancellationToken;
        await Migrate();

        await using (var db = NewContext())
        {
            var svc = new UnansweredService(db, new DeterministicEmbedder());
            await svc.RecordAsync("Where is the spa located?", ct);
            await svc.RecordAsync("Where is the spa located?", ct);
            await svc.RecordAsync("How much for parking?", ct);
        }

        await using var verify = NewContext();
        var rows = await verify.UnansweredQuestions.ToListAsync(ct);
        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows.Single(r => r.Question.Contains("spa", StringComparison.OrdinalIgnoreCase)).Frequency);
    }
}
