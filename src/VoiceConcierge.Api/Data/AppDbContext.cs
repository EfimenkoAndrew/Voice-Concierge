using Microsoft.EntityFrameworkCore;

namespace VoiceConcierge.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<FaqItem> FaqItems => Set<FaqItem>();
    public DbSet<UnansweredQuestion> UnansweredQuestions => Set<UnansweredQuestion>();
    public DbSet<Voice> Voices => Set<Voice>();
    public DbSet<AppConfig> AppConfigs => Set<AppConfig>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.HasPostgresExtension("vector");

        b.Entity<FaqItem>(e =>
        {
            e.ToTable("faq_items");
            e.HasKey(x => x.Id);
            e.Property(x => x.Question).HasColumnName("question");
            e.Property(x => x.Answer).HasColumnName("answer");
            e.Property(x => x.NormalizedQuestion).HasColumnName("normalized_question");
            e.HasIndex(x => x.NormalizedQuestion).IsUnique();
            e.Property(x => x.Embedding).HasColumnName("embedding").HasColumnType($"vector({Embeddings.Dimension})");
            e.Property(x => x.Tags).HasColumnName("tags");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.Property(x => x.Id).HasColumnName("id");
        });

        b.Entity<UnansweredQuestion>(e =>
        {
            e.ToTable("unanswered_questions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Question).HasColumnName("question");
            e.Property(x => x.NormalizedQuestion).HasColumnName("normalized_question");
            e.HasIndex(x => x.NormalizedQuestion).IsUnique();
            e.Property(x => x.Embedding).HasColumnName("embedding").HasColumnType($"vector({Embeddings.Dimension})");
            e.Property(x => x.Frequency).HasColumnName("frequency");
            e.Property(x => x.FirstAskedAt).HasColumnName("first_asked_at");
            e.Property(x => x.LastAskedAt).HasColumnName("last_asked_at");
            e.Property(x => x.Status).HasColumnName("status").HasConversion<string>();
        });

        b.Entity<Voice>(e =>
        {
            e.ToTable("voices");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.Provider).HasColumnName("provider");
            e.Property(x => x.ProviderVoiceId).HasColumnName("provider_voice_id");
            e.Property(x => x.SampleText).HasColumnName("sample_text");
            e.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        });

        b.Entity<AppConfig>(e =>
        {
            e.ToTable("app_config");
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasColumnName("key");
            e.Property(x => x.Value).HasColumnName("value");
        });
    }
}
