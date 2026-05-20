using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VoiceConcierge.Api.Data;
using VoiceConcierge.Api.Faqs;
using VoiceConcierge.Contracts;

namespace VoiceConcierge.Api.Tests;

public class FaqAdminServiceTests : PostgresTestBase
{
    private FaqAdminService NewService(AppDbContext db) =>
        new(db, new FakeEmbedder(), NullLogger<FaqAdminService>.Instance);

    [Fact]
    public async Task Create_inserts_and_persists_embedding()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext();
        var svc = NewService(db);

        var before = (await svc.ListAsync(null, 100, ct)).Items.Count;
        var created = await svc.CreateAsync(
            new UpsertFaqRequest("Do you have a gym?", "Yes, 24-hour fitness center.", ["amenities"]), ct);

        Assert.Equal(before + 1, (await svc.ListAsync(null, 100, ct)).Items.Count);
        await using var v = NewContext();
        Assert.NotNull((await v.FaqItems.SingleAsync(f => f.Id == created.Id, ct)).Embedding);
    }

    [Fact]
    public async Task Update_changes_answer_text()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext();
        var svc = NewService(db);

        var created = await svc.CreateAsync(
            new UpsertFaqRequest("Do you have a gym?", "Yes, 24-hour fitness center.", ["amenities"]), ct);

        Assert.True(await svc.UpdateAsync(created.Id,
            new UpsertFaqRequest("Do you have a gym?", "Yes, open 24 hours with Peloton bikes.", null), ct));

        await using var v = NewContext();
        Assert.Contains("Peloton",
            (await v.FaqItems.SingleAsync(f => f.Id == created.Id, ct)).Answer);
    }

    [Fact]
    public async Task Delete_removes_row_and_second_delete_returns_false()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext();
        var svc = NewService(db);

        var created = await svc.CreateAsync(
            new UpsertFaqRequest("Do you have a gym?", "Yes, 24-hour fitness center.", ["amenities"]), ct);
        var before = (await svc.ListAsync(null, 100, ct)).Items.Count;

        Assert.True(await svc.DeleteAsync(created.Id, ct));
        Assert.False(await svc.DeleteAsync(created.Id, ct));
        Assert.Equal(before - 1, (await svc.ListAsync(null, 100, ct)).Items.Count);
    }
}
