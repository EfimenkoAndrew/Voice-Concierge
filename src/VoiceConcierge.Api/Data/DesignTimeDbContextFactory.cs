using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;
using Pgvector.EntityFrameworkCore;
using Pgvector.Npgsql;

namespace VoiceConcierge.Api.Data;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Port=5432;Database=voiceconcierge;Username=postgres;Password=postgres";
        var dsb = new NpgsqlDataSourceBuilder(conn);
        dsb.UseVector();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(dsb.Build(), npg => npg.UseVector())
            .Options;
        return new AppDbContext(options);
    }
}
