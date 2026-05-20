using Microsoft.EntityFrameworkCore;
using Pgvector;
using VoiceConcierge.Api.Data;
using VoiceConcierge.Api.Search;
using VoiceConcierge.Contracts;

namespace VoiceConcierge.Api.Unanswered;

public sealed class UnansweredQueueService(AppDbContext db, IEmbeddingService embedder)
{
    public async Task<PagedResult<UnansweredDto>> ListAsync(
        string? status, Guid? after, int limit, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 100);
        var q = db.UnansweredQuestions.AsQueryable();

        if (!string.Equals(status, "All", StringComparison.OrdinalIgnoreCase))
        {
            var parsed = ParseStatus(status);
            q = q.Where(u => u.Status == parsed);
        }

        q = q.OrderByDescending(u => u.LastAskedAt).ThenBy(u => u.Id);

        if (after is { } cursor)
        {
            var anchorQuery = db.UnansweredQuestions.Where(u => u.Id == cursor);
            if (!string.Equals(status, "All", StringComparison.OrdinalIgnoreCase))
            {
                var parsedForAnchor = ParseStatus(status);
                anchorQuery = anchorQuery.Where(u => u.Status == parsedForAnchor);
            }
            var anchor = await anchorQuery
                .Select(u => new { u.LastAskedAt, u.Id })
                .FirstOrDefaultAsync(ct);
            if (anchor is not null)
                q = (IOrderedQueryable<UnansweredQuestion>)q.Where(u =>
                    u.LastAskedAt < anchor.LastAskedAt
                    || (u.LastAskedAt == anchor.LastAskedAt && u.Id > anchor.Id));
        }

        var rows = await q.Take(limit + 1)
            .Select(u => new UnansweredDto(u.Id, u.Question, u.Frequency,
                u.FirstAskedAt, u.LastAskedAt, u.Status.ToString()))
            .ToListAsync(ct);
        Guid? nextCursor = rows.Count > limit ? rows[limit - 1].Id : null;
        if (rows.Count > limit) rows.RemoveAt(limit);
        return new PagedResult<UnansweredDto>(rows, nextCursor);
    }

    public async Task<IReadOnlyList<UnansweredDto>> ListOpenAsync(CancellationToken ct = default)
    {
        var result = await ListAsync("Open", null, 100, ct);
        return result.Items;
    }

    public async Task<(Guid? FaqId, bool AlreadyResolved)> ConvertAsync(
        Guid id, ConvertUnansweredRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        const string openName = nameof(UnansweredStatus.Open);
        const string convertedName = nameof(UnansweredStatus.Converted);
        var claimed = await db.Database.ExecuteSqlInterpolatedAsync(
            $@"UPDATE unanswered_questions
               SET status = {convertedName}
               WHERE id = {id} AND status = {openName}", ct);
        if (claimed == 0)
        {
            await tx.RollbackAsync(ct);
            var exists = await db.UnansweredQuestions.AnyAsync(u => u.Id == id, ct);
            return (null, exists);
        }

        var question = await db.UnansweredQuestions
            .Where(u => u.Id == id)
            .Select(u => u.Question)
            .FirstOrDefaultAsync(ct);

        if (question is null)
        {
            await tx.RollbackAsync(ct);
            return (null, false);
        }

        var faq = new FaqItem
        {
            Question = question,
            Answer = req.Answer.Trim(),
            NormalizedQuestion = TextNormalization.Normalize(question),
            Tags = req.Tags?.Select(t => t.Trim().ToLowerInvariant()).ToArray() ?? [],
            Embedding = Embed(question, req.Answer),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.FaqItems.Add(faq);

        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
        return (faq.Id, false);
    }

    public async Task<bool> DismissAsync(Guid id, CancellationToken ct = default)
    {
        const string openName = nameof(UnansweredStatus.Open);
        const string dismissedName = nameof(UnansweredStatus.Dismissed);
        var rows = await db.Database.ExecuteSqlInterpolatedAsync(
            $@"UPDATE unanswered_questions
               SET status = {dismissedName}
               WHERE id = {id} AND status = {openName}", ct);
        return rows > 0;
    }

    private static UnansweredStatus ParseStatus(string? s) =>
        s?.ToLowerInvariant() switch
        {
            "converted" => UnansweredStatus.Converted,
            "dismissed" => UnansweredStatus.Dismissed,
            _ => UnansweredStatus.Open,
        };

    private Vector? Embed(string q, string a)
    {
        try { return new Vector(embedder.Embed($"{q}\n{a}")); }
        catch (InvalidOperationException) { return null; }
    }
}
