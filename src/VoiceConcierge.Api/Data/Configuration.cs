using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pgvector.EntityFrameworkCore;
using Pgvector.Npgsql;

namespace VoiceConcierge.Api.Data;

public static class DataConfiguration
{
    public static bool AddDataContext(this IServiceCollection services, IConfiguration cfg)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(cfg);

        var connString = cfg.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(connString) && cfg["ALLOW_NO_DB"] != "true")
            throw new InvalidOperationException(
                "ConnectionStrings:Postgres is required. Set it in .env " +
                "or ALLOW_NO_DB=true for DB-less unit hosting.");

        var dbConfigured = !string.IsNullOrWhiteSpace(connString);
        if (!dbConfigured) return false;

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connString);
        dataSourceBuilder.UseVector();
        var dataSource = dataSourceBuilder.Build();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(dataSource, npg => npg.UseVector()));
        services.AddScoped<DbBootstrapper>();
        services.AddScoped<UnansweredService>();
        return true;
    }
}
