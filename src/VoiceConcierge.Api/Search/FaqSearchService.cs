using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using VoiceConcierge.Api.Data;
using VoiceConcierge.Contracts;

namespace VoiceConcierge.Api.Search;

public sealed class FaqSearchService(
    AppDbContext db,
    IEmbeddingService embedder,
    IConfiguration config,
    ILogger<FaqSearchService> log)
{
    private const double DefaultSemanticThreshold = 0.62;
    private const double LexicalMinScore = 0.5;
    private const int LexicalCandidateLimit = 25;
    private const int MinLexicalWordLength = 2;

    private double Threshold =>
        double.TryParse(config["Search:SemanticThreshold"], out var t) ? t : DefaultSemanticThreshold;

    public async Task BackfillEmbeddingsAsync(CancellationToken ct = default)
    {
        var pending = await db.FaqItems.Where(f => f.Embedding == null).ToListAsync(ct);
        if (pending.Count == 0) return;
        try
        {
            foreach (var f in pending)
                f.Embedding = new Vector(embedder.Embed($"{f.Question}\n{f.Answer}"));
            await db.SaveChangesAsync(ct);
            log.LogInformation("Backfilled embeddings for {Count} FAQ(s)", pending.Count);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Embedding backfill skipped (model unavailable); semantic search degraded to lexical");
        }
    }

    public async Task<FaqSearchResult> SearchAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new FaqSearchResult(false, null, 0, null, "none");

        Vector? qVec = null;
        try { qVec = new Vector(embedder.Embed(query)); }
        catch (Exception ex) { log.LogWarning(ex, "Embedding unavailable; degrading to lexical search"); }

        if (qVec is not null)
        {
            var best = await db.FaqItems
                .Where(f => f.Embedding != null)
                .Select(f => new
                {
                    f.Id,
                    f.Answer,
                    Distance = f.Embedding!.CosineDistance(qVec),
                })
                .OrderBy(x => x.Distance)
                .FirstOrDefaultAsync(ct);

            if (best is not null)
            {
                var similarity = 1.0 - best.Distance;
                if (similarity >= Threshold)
                    return new FaqSearchResult(true, best.Answer, Math.Round(similarity, 4), best.Id, "semantic");
            }
        }

        var norm = TextNormalization.Normalize(query);
        var words = norm.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > MinLexicalWordLength).Distinct().ToArray();
        if (words.Length > 0)
        {
            var patterns = words.Select(w => $"%{w}%").ToArray();
            var candidates = await db.FaqItems
                .Where(f => patterns.Any(p =>
                    EF.Functions.ILike(f.Question, p) ||
                    EF.Functions.ILike(f.Answer, p)))
                .Select(f => new { f.Id, f.Answer, f.Question })
                .Take(LexicalCandidateLimit)
                .ToListAsync(ct);

            var ranked = candidates
                .Select(c =>
                {
                    var hay = TextNormalization.Normalize($"{c.Question} {c.Answer}");
                    var hits = words.Count(w => hay.Contains(w, StringComparison.Ordinal));
                    return (c, score: (double)hits / words.Length);
                })
                .Where(x => x.score > 0)
                .OrderByDescending(x => x.score)
                .ToList();

            if (ranked.Count > 0 && ranked[0].score >= LexicalMinScore)
            {
                var (c, score) = ranked[0];
                return new FaqSearchResult(true, c.Answer, Math.Round(score, 4), c.Id, "lexical");
            }
        }

        return new FaqSearchResult(false, null, 0, null, "none");
    }
}
