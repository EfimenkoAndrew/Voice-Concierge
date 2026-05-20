using System.Reflection;
using VoiceConcierge.Api;
using VoiceConcierge.Api.AdminKey;
using VoiceConcierge.Api.Api;
using VoiceConcierge.Api.Data;
using VoiceConcierge.Api.Metrics;
using VoiceConcierge.Api.Search;

const int MaxRequestBodyBytes = 256 * 1024;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = MaxRequestBodyBytes);

var dbConfigured = builder.Services.AddVoiceConciergeApi(builder.Configuration);

var app = builder.Build();

if (dbConfigured)
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<DbBootstrapper>().RunAsync();
    try
    {
        await scope.ServiceProvider.GetRequiredService<OnnxEmbeddingService>()
            .EnsureReadyAsync(CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<FaqSearchService>()
            .BackfillEmbeddingsAsync();
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Embedding backfill failed at startup; search degrades to lexical");
    }
}

app.UseExceptionHandler(_ => _.Run(async ctx =>
{
    ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await ctx.Response.WriteAsJsonAsync(new { error = "internal" });
}));

app.UseCors();
app.UseRouting();
app.UseRateLimiter();
app.UseMiddleware<AdminKeyMiddleware>();

var version = Assembly.GetExecutingAssembly()
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
    ?? "0.0.0";

app.MapGet("/health", (IServiceProvider sp) => Results.Ok(new
{
    status = "ok",
    service = "voiceconcierge-backend",
    version,
    db = dbConfigured ? "configured" : "not-configured",
    embedding = dbConfigured
        ? (sp.GetService<IEmbeddingService>()?.Ready == true ? "ready" : "degraded")
        : "n/a",
}));

app.MapGet("/metrics", (IServiceProvider sp) =>
{
    var emb = sp.GetService<IEmbeddingService>();
    return Results.Ok(new
    {
        model_downloads = ModelDownloadMetrics.Snapshot(),
        embedding_ready = emb?.Ready ?? false,
    });
});

app.MapGet("/", () => Results.Redirect("/health"));

app.MapApi(dbConfigured);

app.Run();

public partial class Program;
