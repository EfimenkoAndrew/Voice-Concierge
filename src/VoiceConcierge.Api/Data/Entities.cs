using Pgvector;

namespace VoiceConcierge.Api.Data;

public static class Embeddings
{
    public const int Dimension = 384;
}

public class FaqItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Question { get; set; } = "";
    public string Answer { get; set; } = "";
    public string NormalizedQuestion { get; set; } = "";
    public Vector? Embedding { get; set; }
    public string[] Tags { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum UnansweredStatus { Open, Converted, Dismissed }

public class UnansweredQuestion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Question { get; set; } = "";
    public string NormalizedQuestion { get; set; } = "";
    public Vector? Embedding { get; set; }
    public int Frequency { get; set; } = 1;
    public DateTimeOffset FirstAskedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastAskedAt { get; set; } = DateTimeOffset.UtcNow;
    public UnansweredStatus Status { get; set; } = UnansweredStatus.Open;
}

public class Voice
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Provider { get; set; } = "";
    public string ProviderVoiceId { get; set; } = "";
    public string SampleText { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public class AppConfig
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
