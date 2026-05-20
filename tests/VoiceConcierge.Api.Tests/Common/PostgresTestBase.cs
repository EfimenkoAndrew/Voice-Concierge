using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Pgvector.EntityFrameworkCore;
using Pgvector.Npgsql;
using Testcontainers.PostgreSql;
using VoiceConcierge.Api.Data;

namespace VoiceConcierge.Api.Tests;

public abstract class PostgresTestBase : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = TestPostgres.NewBuilder().Build();

    public virtual async ValueTask InitializeAsync()
    {
        await _pg.StartAsync();
        await using var db = NewContext();
        await new DbBootstrapper(db, NullLogger<DbBootstrapper>.Instance).RunAsync();
    }

    public virtual async ValueTask DisposeAsync() => await _pg.DisposeAsync();

    protected AppDbContext NewContext()
    {
        var dsb = new NpgsqlDataSourceBuilder(_pg.GetConnectionString());
        dsb.UseVector();
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(dsb.Build(), n => n.UseVector()).Options);
    }

    protected string ConnectionString => _pg.GetConnectionString();
}
