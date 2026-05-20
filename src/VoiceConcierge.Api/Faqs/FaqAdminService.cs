using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pgvector;
using VoiceConcierge.Api.Data;
using VoiceConcierge.Api.Search;
using VoiceConcierge.Contracts;

namespace VoiceConcierge.Api.Faqs;

public sealed class FaqAdminService(AppDbContext db, IEmbeddingService embedder, ILogger<FaqAdminService> log)
{
    private const string UniqueViolation = "23505";
    private const int MaxTagCount = 8;
    private const int MaxTagLength = 24;
    private static readonly Regex TagCharset = new(@"^[a-z0-9-]+$", RegexOptions.Compiled);

    public async Task<PagedResult<FaqDto>> ListAsync(
        Guid? after, int limit, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 100);
        var q = db.FaqItems.OrderBy(f => f.CreatedAt).ThenBy(f => f.Id);
        if (after is { } cursor)
        {
            var anchor = await db.FaqItems.Where(f => f.Id == cursor)
                .Select(f => new { f.CreatedAt, f.Id }).FirstOrDefaultAsync(ct);
            if (anchor is not null)
                q = (IOrderedQueryable<FaqItem>)q.Where(f =>
                    f.CreatedAt > anchor.CreatedAt
                    || (f.CreatedAt == anchor.CreatedAt && f.Id > anchor.Id));
        }
        var items = await q.Take(limit + 1)
            .Select(f => new FaqDto(f.Id, f.Question, f.Answer, f.Tags, f.UpdatedAt))
            .ToListAsync(ct);
        Guid? nextCursor = items.Count > limit ? items[limit - 1].Id : null;
        if (items.Count > limit) items.RemoveAt(limit);
        return new PagedResult<FaqDto>(items, nextCursor);
    }

    public async Task<FaqDto> CreateAsync(UpsertFaqRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        ValidateTags(req.Tags);
        var item = new FaqItem
        {
            Question = req.Question.Trim(),
            Answer = req.Answer.Trim(),
            NormalizedQuestion = TextNormalization.Normalize(req.Question),
            Tags = NormalizeTags(req.Tags),
            Embedding = Embed(req.Question, req.Answer),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.FaqItems.Add(item);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            var existing = await db.FaqItems
                .Where(f => f.NormalizedQuestion == item.NormalizedQuestion)
                .Select(f => f.Id).FirstOrDefaultAsync(ct);
            if (existing == Guid.Empty) throw;
            throw new DuplicateFaqException(existing);
        }
        return new FaqDto(item.Id, item.Question, item.Answer, item.Tags, item.UpdatedAt);
    }

    public async Task<bool> UpdateAsync(Guid id, UpsertFaqRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        if (req.UpdateTags) ValidateTags(req.Tags);
        var item = await db.FaqItems.SingleOrDefaultAsync(f => f.Id == id, ct);
        if (item is null) return false;

        item.Question = req.Question.Trim();
        item.Answer = req.Answer.Trim();
        item.NormalizedQuestion = TextNormalization.Normalize(req.Question);
        if (req.UpdateTags) item.Tags = NormalizeTags(req.Tags);

        var newEmbedding = TryEmbed(req.Question, req.Answer);
        if (newEmbedding is not null) item.Embedding = newEmbedding;

        item.UpdatedAt = DateTimeOffset.UtcNow;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            var existing = await db.FaqItems
                .Where(f => f.NormalizedQuestion == item.NormalizedQuestion && f.Id != id)
                .Select(f => f.Id).FirstOrDefaultAsync(ct);
            throw new DuplicateFaqException(existing);
        }
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var rows = await db.FaqItems.Where(f => f.Id == id).ExecuteDeleteAsync(ct);
        return rows > 0;
    }

    private static void ValidateTags(string[]? tags)
    {
        if (tags is null) return;
        if (tags.Length > MaxTagCount)
            throw new ArgumentException($"≤{MaxTagCount} tags allowed", nameof(tags));
        foreach (var t in tags)
        {
            if (string.IsNullOrWhiteSpace(t) || t.Length > MaxTagLength)
                throw new ArgumentException($"each tag must be 1-{MaxTagLength} chars", nameof(tags));
            if (!TagCharset.IsMatch(t.ToLowerInvariant()))
                throw new ArgumentException("tags must match [a-z0-9-]", nameof(tags));
        }
    }

    private static string[] NormalizeTags(string[]? tags) =>
        tags?.Select(t => t.Trim().ToLowerInvariant()).ToArray() ?? [];

    private Vector? TryEmbed(string q, string a)
    {
        try { return new Vector(embedder.Embed($"{q}\n{a}")); }
        catch (InvalidOperationException ex)
        {
            log.LogWarning(ex, "Embedding failed for FAQ update - preserving existing vector");
            return null;
        }
    }

    private Vector? Embed(string q, string a)
    {
        try { return new Vector(embedder.Embed($"{q}\n{a}")); }
        catch (InvalidOperationException ex)
        {
            log.LogWarning(ex, "Embedding not ready for new FAQ - stored without vector (lexical search only)");
            return null;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: UniqueViolation };
}
