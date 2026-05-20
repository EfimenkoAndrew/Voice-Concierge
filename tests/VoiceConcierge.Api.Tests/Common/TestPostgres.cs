using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Images;
using Testcontainers.PostgreSql;

namespace VoiceConcierge.Api.Tests;

/// <summary>
/// Builds the official-postgres + pgvector image (database/postgres/Dockerfile)
/// via Testcontainers itself (so it is loaded into the same Docker the tests
/// use), then hands out PostgreSql builders bound to it. No third-party
/// pgvector image; nothing pulled from a private registry.
/// </summary>
internal static class TestPostgres
{
    private static readonly Lazy<IFutureDockerImage> Image = new(() =>
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "VoiceConcierge.slnx")))
            dir = Directory.GetParent(dir)?.FullName;
        if (dir is null) throw new InvalidOperationException("repo root not found");

        var img = new ImageFromDockerfileBuilder()
            .WithDockerfileDirectory(Path.Combine(dir, "database", "postgres"))
            .WithDockerfile("Dockerfile")
            .WithName("voiceconcierge-postgres:test")
            .WithCleanUp(false)
            .Build();
        img.CreateAsync().GetAwaiter().GetResult();
        return img;
    });

    public static PostgreSqlBuilder NewBuilder() =>
        new PostgreSqlBuilder(Image.Value.FullName).WithImagePullPolicy(PullPolicy.Never);
}
