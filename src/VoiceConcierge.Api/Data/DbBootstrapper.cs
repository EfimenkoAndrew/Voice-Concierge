using Microsoft.EntityFrameworkCore;
using VoiceConcierge.Contracts;

namespace VoiceConcierge.Api.Data;

public sealed class DbBootstrapper(AppDbContext db, ILogger<DbBootstrapper> log)
{
    private const long BootstrapLockKey = 0x564F_4943;

    public async Task RunAsync(CancellationToken ct = default)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        await using (var lk = conn.CreateCommand())
        {
            lk.CommandText = $"SELECT pg_advisory_lock({BootstrapLockKey})";
            await lk.ExecuteNonQueryAsync(ct);
        }
        try
        {
            await RunCoreAsync(ct);
        }
        finally
        {
            await using var ul = conn.CreateCommand();
            ul.CommandText = $"SELECT pg_advisory_unlock({BootstrapLockKey})";
            await ul.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    private async Task RunCoreAsync(CancellationToken ct)
    {
        await db.Database.MigrateAsync(ct);

        var existing = await db.FaqItems
            .Select(f => f.NormalizedQuestion)
            .ToHashSetAsync(ct);

        var added = 0;
        foreach (var f in MeridianSeed.Faqs)
        {
            var norm = TextNormalization.Normalize(f.Question);
            if (!existing.Add(norm)) continue;
            db.FaqItems.Add(new FaqItem
            {
                Question = f.Question,
                Answer = f.Answer,
                NormalizedQuestion = norm,
                Tags = f.Tags,
                Embedding = null,
            });
            added++;
        }

        var voiceIds = await db.Voices.Select(v => v.Id).ToHashSetAsync(ct);
        foreach (var v in MeridianSeed.Voices)
        {
            if (voiceIds.Contains(v.Id)) continue;
            db.Voices.Add(new Voice
            {
                Id = v.Id,
                Name = v.Name,
                Description = v.Description,
                Provider = "edge",
                ProviderVoiceId = v.ProviderVoiceId,
                SampleText = MeridianSeed.SampleText,
            });
        }

        if (!await db.AppConfigs.AnyAsync(c => c.Key == "active_voice_id", ct))
            db.AppConfigs.Add(new AppConfig
            {
                Key = "active_voice_id",
                Value = MeridianSeed.DefaultActiveVoiceId.ToString(),
            });

        await db.SaveChangesAsync(ct);

        var conn2 = db.Database.GetDbConnection();
        await using (var cmd = conn2.CreateCommand())
        {
            cmd.CommandText = """
                CREATE EXTENSION IF NOT EXISTS pg_trgm;
                CREATE INDEX IF NOT EXISTS ix_faq_embedding_hnsw
                    ON faq_items USING hnsw (embedding vector_cosine_ops);
                CREATE INDEX IF NOT EXISTS ix_faq_question_trgm
                    ON faq_items USING gin (question gin_trgm_ops);
                CREATE INDEX IF NOT EXISTS ix_faq_answer_trgm
                    ON faq_items USING gin (answer gin_trgm_ops);
                """;
            await cmd.ExecuteNonQueryAsync(ct);
        }
        log.LogInformation("DB bootstrap complete: migrations applied; {Added} new FAQ(s) seeded; search indexes ensured.", added);
    }
}
