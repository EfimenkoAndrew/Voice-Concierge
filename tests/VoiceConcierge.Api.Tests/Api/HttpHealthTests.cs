using System.Net;
using System.Net.Http.Json;

namespace VoiceConcierge.Api.Tests;

[Collection(HttpCollection.Name)]
public class HttpHealthTests(HttpFixture fx)
{
    [Fact]
    public async Task Health_ok_and_reports_db_and_embedding()
    {
        var ct = TestContext.Current.CancellationToken;
        var r = await fx.NewClient().GetAsync("/health", ct);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadFromJsonAsync<HealthDto>(ct);
        Assert.Equal("ok", body!.Status);
        Assert.Equal("configured", body.Db);
        Assert.Equal("ready", body.Embedding);
    }
}
