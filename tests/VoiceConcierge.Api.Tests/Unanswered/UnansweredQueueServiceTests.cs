using Microsoft.EntityFrameworkCore;
using VoiceConcierge.Api.Data;
using VoiceConcierge.Api.Unanswered;
using VoiceConcierge.Contracts;

namespace VoiceConcierge.Api.Tests;

public class UnansweredQueueServiceTests : PostgresTestBase
{
    [Fact]
    public async Task List_returns_open_questions_sorted_newest_first()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var db = NewContext())
        {
            var rec = new UnansweredService(db, new FakeEmbedder());
            await rec.RecordAsync("Do you allow pets?", ct);
            await rec.RecordAsync("Do you allow pets?", ct);
            await rec.RecordAsync("Is there a shuttle?", ct);
        }

        await using var db2 = NewContext();
        var q = new UnansweredQueueService(db2, new FakeEmbedder());
        var open = await q.ListOpenAsync(ct);

        Assert.Equal(2, open.Count);
        Assert.Equal("Is there a shuttle?", open[0].Question);
        Assert.Equal(2, open.Single(u => u.Question == "Do you allow pets?").Frequency);
    }

    [Fact]
    public async Task Convert_creates_faq_and_marks_question_converted()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var db = NewContext())
            await new UnansweredService(db, new FakeEmbedder())
                .RecordAsync("Do you allow pets?", ct);

        await using var db2 = NewContext();
        var q = new UnansweredQueueService(db2, new FakeEmbedder());
        var pets = (await q.ListOpenAsync(ct)).Single(u => u.Question == "Do you allow pets?");

        var (faqId, _) = await q.ConvertAsync(pets.Id,
            new ConvertUnansweredRequest("Yes, pets under 25 lbs are welcome.", ["general"]), ct);

        Assert.NotNull(faqId);
        await using var v = NewContext();
        Assert.NotNull(await v.FaqItems.FindAsync([faqId!.Value], ct));
        Assert.Equal(UnansweredStatus.Converted,
            (await v.UnansweredQuestions.SingleAsync(u => u.Question == "Do you allow pets?", ct)).Status);
    }

    [Fact]
    public async Task Dismiss_marks_question_dismissed_and_removes_from_open_list()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var db = NewContext())
            await new UnansweredService(db, new FakeEmbedder())
                .RecordAsync("Is there a shuttle?", ct);

        await using var db2 = NewContext();
        var q = new UnansweredQueueService(db2, new FakeEmbedder());
        var row = (await q.ListOpenAsync(ct)).Single(u => u.Question == "Is there a shuttle?");

        Assert.True(await q.DismissAsync(row.Id, ct));
        Assert.Empty(await q.ListOpenAsync(ct));

        await using var v = NewContext();
        Assert.Equal(UnansweredStatus.Dismissed,
            (await v.UnansweredQuestions.SingleAsync(u => u.Question == "Is there a shuttle?", ct)).Status);
    }
}
