using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Pgvector.EntityFrameworkCore;
using Pgvector.Npgsql;
using Testcontainers.PostgreSql;
using VoiceConcierge.Api.Data;

namespace VoiceConcierge.Api.Tests;

public class DbBootstrapperTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg =
        TestPostgres.NewBuilder().Build();

    public async ValueTask InitializeAsync() => await _pg.StartAsync();
    public async ValueTask DisposeAsync() => await _pg.DisposeAsync();

    private AppDbContext NewContext()
    {
        var dsb = new NpgsqlDataSourceBuilder(_pg.GetConnectionString());
        dsb.UseVector();
        var ds = dsb.Build();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ds, npg => npg.UseVector())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task Bootstrap_seeds_and_is_idempotent()
    {
        var ct = TestContext.Current.CancellationToken;

        // Run twice — second run must not duplicate anything (AC3).
        await using (var db = NewContext())
            await new DbBootstrapper(db, NullLogger<DbBootstrapper>.Instance).RunAsync(ct);
        await using (var db = NewContext())
            await new DbBootstrapper(db, NullLogger<DbBootstrapper>.Instance).RunAsync(ct);

        await using var verify = NewContext();
        var faqCount = await verify.FaqItems.CountAsync(ct);
        var voiceCount = await verify.Voices.CountAsync(ct);
        var activeVoice = await verify.AppConfigs
            .Where(c => c.Key == "active_voice_id")
            .Select(c => c.Value)
            .SingleOrDefaultAsync(ct);

        Assert.Equal(MeridianSeed.Faqs.Length, faqCount);   // AC1/AC2 + idempotent
        Assert.Equal(4, voiceCount);                          // AC4: 4 voices
        Assert.Equal("1", activeVoice);                       // AC4: default active voice
    }

    [Fact]
    public async Task Seeded_faqs_have_normalized_questions_and_tables_exist()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var db = NewContext())
            await new DbBootstrapper(db, NullLogger<DbBootstrapper>.Instance).RunAsync(ct);

        await using var verify = NewContext();
        var sample = await verify.FaqItems.FirstAsync(
            f => f.Question == "Is the poker room open?", ct);

        Assert.False(string.IsNullOrWhiteSpace(sample.NormalizedQuestion));
        Assert.Null(sample.Embedding); // Story 1-3 backfills
        Assert.Contains("poker", sample.Tags);
        // unanswered_questions table exists and is queryable (empty)
        Assert.Equal(0, await verify.UnansweredQuestions.CountAsync(ct));
    }
}
