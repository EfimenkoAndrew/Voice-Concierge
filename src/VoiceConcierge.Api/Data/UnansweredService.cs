using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using VoiceConcierge.Api.Search;

namespace VoiceConcierge.Api.Data;

public sealed class UnansweredService(AppDbContext db, IEmbeddingService embedder)
{
    private const string UniqueViolation = "23505";
    private const double MaxCosineDistanceForDedup = 0.15;

    public async Task RecordAsync(string question, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(question);
        var norm = TextNormalization.Normalize(question);
        if (norm.Length == 0) return;

        var embedding = TryEmbed(question);

        if (embedding is not null
            && await TryBumpSemanticallySimilarAsync(embedding, ct).ConfigureAwait(false))
            return;

        db.UnansweredQuestions.Add(new UnansweredQuestion
        {
            Question = question,
            NormalizedQuestion = norm,
            Embedding = embedding,
        });
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();
            await BumpByNormalizedAsync(norm, ct).ConfigureAwait(false);
        }
    }

    private Vector? TryEmbed(string question)
    {
        try { return new Vector(embedder.Embed(question)); }
        catch (InvalidOperationException) { return null; }
    }

    private async Task<bool> TryBumpSemanticallySimilarAsync(Vector embedding, CancellationToken ct)
    {
        var hitId = await db.UnansweredQuestions
            .Where(u => u.Status == UnansweredStatus.Open
                     && u.Embedding != null
                     && u.Embedding!.CosineDistance(embedding) <= MaxCosineDistanceForDedup)
            .OrderBy(u => u.Embedding!.CosineDistance(embedding))
            .Select(u => u.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (hitId == Guid.Empty) return false;

        await db.UnansweredQuestions
            .Where(u => u.Id == hitId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.Frequency, u => u.Frequency + 1)
                .SetProperty(u => u.LastAskedAt, DateTimeOffset.UtcNow), ct)
            .ConfigureAwait(false);
        return true;
    }

    private async Task BumpByNormalizedAsync(string normalizedQuestion, CancellationToken ct) =>
        await db.UnansweredQuestions
            .Where(u => u.NormalizedQuestion == normalizedQuestion)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.Frequency, u => u.Frequency + 1)
                .SetProperty(u => u.LastAskedAt, DateTimeOffset.UtcNow), ct)
            .ConfigureAwait(false);

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: UniqueViolation };
}
