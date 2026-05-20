using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Pgvector.EntityFrameworkCore;
using Pgvector.Npgsql;
using Testcontainers.PostgreSql;
using VoiceConcierge.Api.Data;
using VoiceConcierge.Api.Search;
using VoiceConcierge.Api.Voices;

namespace VoiceConcierge.Api.Tests;

file sealed class FakeEmbedder : IEmbeddingService
{
    public int Dimension => Embeddings.Dimension;
    public bool Ready => true;
    public float[] Embed(string text)
    {
        var v = new float[Embeddings.Dimension];
        v[Math.Abs(text.GetHashCode(StringComparison.Ordinal)) % v.Length] = 1f;
        return v;
    }
}

/// <summary>Closes QA tightening gaps: seed topic coverage (M-1), voice
/// names (M-4), preview-doesn't-change-active (M-5), unanswered concurrency
/// (M-3).</summary>
public class QaGapTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = TestPostgres.NewBuilder().Build();
    public async ValueTask InitializeAsync()
    {
        await _pg.StartAsync();
        await using var db = Ctx();
        await new DbBootstrapper(db, NullLogger<DbBootstrapper>.Instance).RunAsync();
    }
    public async ValueTask DisposeAsync() => await _pg.DisposeAsync();

    private AppDbContext Ctx()
    {
        var dsb = new NpgsqlDataSourceBuilder(_pg.GetConnectionString());
        dsb.UseVector();
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(dsb.Build(), n => n.UseVector()).Options);
    }

    private sealed class FakeSynth : ISpeechSynthesizer
    {
        public Task<byte[]> SynthesizeAsync(string t, string v, CancellationToken ct = default)
            => Task.FromResult(new byte[] { 1 });
    }

    [Fact] // M-1: every required topic tag is represented in the seed
    public async Task Seed_covers_every_required_topic()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Ctx();
        foreach (var tag in new[] { "general", "gaming", "rooms", "dining", "bars", "amenities", "events", "partners" })
            Assert.True(await db.FaqItems.AnyAsync(f => f.Tags.Contains(tag), ct), $"missing topic: {tag}");
    }

    [Theory]
    [InlineData(1, "James", "en-GB-RyanNeural")]
    [InlineData(2, "Sofia", "en-IE-EmilyNeural")]
    [InlineData(3, "Marcus", "en-US-GuyNeural")]
    [InlineData(4, "Elena", "en-US-AriaNeural")]
    public async Task Voice_has_expected_persona(int id, string expectedName, string expectedProviderVoiceId)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Ctx();
        var voice = await db.Voices.SingleAsync(v => v.Id == id, ct);
        Assert.Equal(expectedName, voice.Name);
        Assert.Equal(expectedProviderVoiceId, voice.ProviderVoiceId);
    }

    [Fact] // M-5: preview must not change the active voice
    public async Task Preview_does_not_change_active_voice()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Ctx();
        var svc = new VoiceService(db, new FakeSynth());
        await svc.SetActiveVoiceAsync(1, ct);
        await svc.PreviewAsync(3, null, ct);
        Assert.Equal(1, await svc.GetActiveVoiceIdAsync(ct));
    }

    [Fact] // M-2: documented decision — a deleted SEED FAQ is re-created on
           // restart (seed is the source of truth); admin-added rows are not.
    public async Task Deleted_seed_faq_is_reseeded_on_restart()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeded = MeridianSeed.Faqs[0].Question;
        await using (var db = Ctx())
        {
            var row = await db.FaqItems.FirstAsync(f => f.Question == seeded, ct);
            db.FaqItems.Remove(row);
            await db.SaveChangesAsync(ct);
        }
        await using (var db = Ctx())
            await new DbBootstrapper(db, NullLogger<DbBootstrapper>.Instance).RunAsync(ct);
        await using (var verify = Ctx())
            Assert.True(await verify.FaqItems.AnyAsync(f => f.Question == seeded, ct),
                "deleted seed FAQ should be re-created on restart (documented behavior)");
    }

    [Fact] // M-3: concurrent records of the same question → one row, freq == N
    public async Task Unanswered_upsert_is_concurrency_safe()
    {
        var ct = TestContext.Current.CancellationToken;
        const string q = "Is there a concurrency safe shuttle?";
        await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
        {
            await using var db = Ctx();
            await new UnansweredService(db, new FakeEmbedder()).RecordAsync(q, ct);
        }));
        await using var verify = Ctx();
        var rows = await verify.UnansweredQuestions
            .Where(u => u.Question == q).ToListAsync(ct);
        Assert.Single(rows);
        Assert.Equal(12, rows[0].Frequency);
    }
}
