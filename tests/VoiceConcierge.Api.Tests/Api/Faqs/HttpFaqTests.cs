using System.Net;
using System.Net.Http.Json;
using VoiceConcierge.Contracts;

namespace VoiceConcierge.Api.Tests;

[Collection(HttpCollection.Name)]
public class HttpFaqTests(HttpFixture fx)
{
    [Theory]
    [InlineData("is the poker room open?", true, "poker")]
    [InlineData("zzz qqq nonsense", false, null)]
    public async Task Search_returns_match_or_no_match(string query, bool expectMatch, string? expectInAnswer)
    {
        var ct = TestContext.Current.CancellationToken;
        var resp = await fx.NewClient().PostAsJsonAsync("/faqs/search", new FaqSearchRequest(query), ct);
        var result = await resp.Content.ReadFromJsonAsync<FaqSearchResult>(ct);
        Assert.NotNull(result);
        Assert.Equal(expectMatch, result!.Match);
        if (expectMatch)
        {
            Assert.False(string.IsNullOrWhiteSpace(result.Answer));
            Assert.Contains(expectInAnswer!, result.Answer!, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Null(result.Answer);
        }
    }

    [Fact]
    public async Task Create_and_delete_round_trip()
    {
        var ct = TestContext.Current.CancellationToken;
        var c = fx.NewClient();
        var created = await c.PostAsJsonAsync("/faqs",
            new UpsertFaqRequest("Is there valet?", "Yes, complimentary for hotel guests.", ["general"]), ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var dto = await created.Content.ReadFromJsonAsync<FaqDto>(ct);

        var del = await c.DeleteAsync($"/faqs/{dto!.Id}", ct);
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var delAgain = await c.DeleteAsync($"/faqs/{dto.Id}", ct);
        Assert.Equal(HttpStatusCode.NotFound, delAgain.StatusCode);
    }

    public static TheoryData<string, string, string> OversizedInputCases() => new()
    {
        { "", "Has answer", "question and answer are required" },
        { "Q?", "", "question and answer are required" },
        { new string('q', 501), "ok", "question ≤500 chars" },
        { "Q?", new string('a', 4001), "answer ≤4000 chars" },
    };

    [Theory]
    [MemberData(nameof(OversizedInputCases))]
    public async Task Post_rejects_oversized_inputs(string question, string answer, string expectedError)
    {
        var ct = TestContext.Current.CancellationToken;
        var bad = await fx.NewClient()
            .PostAsJsonAsync("/faqs", new UpsertFaqRequest(question, answer, null), ct);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains(expectedError, await bad.Content.ReadAsStringAsync(ct));
    }

    public static TheoryData<string[]> InvalidTagSets() => new()
    {
        Enumerable.Range(0, 9).Select(i => "t" + i).ToArray(),
        new[] { new string('t', 25) },
    };

    [Theory]
    [MemberData(nameof(InvalidTagSets))]
    public async Task Post_rejects_invalid_tag_sets(string[] tags)
    {
        var ct = TestContext.Current.CancellationToken;
        var r = await fx.NewClient()
            .PostAsJsonAsync("/faqs", new UpsertFaqRequest("Q?", "A.", tags), ct);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Put_validates_and_404s_unknown()
    {
        var ct = TestContext.Current.CancellationToken;
        var c = fx.NewClient();
        var created = await c.PostAsJsonAsync("/faqs",
            new UpsertFaqRequest("Is there parking?", "Yes, valet is complimentary.", null), ct);
        var dto = await created.Content.ReadFromJsonAsync<FaqDto>(ct);

        var badContent = await c.PutAsJsonAsync($"/faqs/{dto!.Id}",
            new UpsertFaqRequest("", "", null), ct);
        Assert.Equal(HttpStatusCode.BadRequest, badContent.StatusCode);

        var unknownId = await c.PutAsJsonAsync($"/faqs/{Guid.NewGuid()}",
            new UpsertFaqRequest("Q?", "A.", null), ct);
        Assert.Equal(HttpStatusCode.NotFound, unknownId.StatusCode);

        var ok = await c.PutAsJsonAsync($"/faqs/{dto.Id}",
            new UpsertFaqRequest("Q?", "A.", null), ct);
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
    }

    [Fact]
    public async Task Put_with_null_tags_preserves_them()
    {
        var ct = TestContext.Current.CancellationToken;
        var c = fx.NewClient();
        var created = await c.PostAsJsonAsync("/faqs",
            new UpsertFaqRequest("Spa hours preserved?", "9-9 daily.", ["spa", "wellness"]), ct);
        var dto = await created.Content.ReadFromJsonAsync<FaqDto>(ct);
        Assert.Equal(["spa", "wellness"], dto!.Tags);

        await c.PutAsJsonAsync($"/faqs/{dto.Id}",
            new UpsertFaqRequest("Spa hours preserved?", "9-9 daily.", null), ct);
        var after = (await c.GetFromJsonAsync<PagedResult<FaqDto>>("/faqs?limit=200", ct))!.Items
            .Single(f => f.Id == dto.Id);
        Assert.Equal(["spa", "wellness"], after.Tags);
    }

    [Fact]
    public async Task Put_with_empty_tags_and_UpdateTags_true_clears_them()
    {
        var ct = TestContext.Current.CancellationToken;
        var c = fx.NewClient();
        var created = await c.PostAsJsonAsync("/faqs",
            new UpsertFaqRequest("Spa hours cleared?", "9-9 daily.", ["spa", "wellness"]), ct);
        var dto = await created.Content.ReadFromJsonAsync<FaqDto>(ct);

        await c.PutAsJsonAsync($"/faqs/{dto!.Id}",
            new UpsertFaqRequest("Spa hours cleared?", "9-9 daily.", [], UpdateTags: true), ct);
        var cleared = (await c.GetFromJsonAsync<PagedResult<FaqDto>>("/faqs?limit=200", ct))!.Items
            .Single(f => f.Id == dto.Id);
        Assert.Empty(cleared.Tags);
    }
}
