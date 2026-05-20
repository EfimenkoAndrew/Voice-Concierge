using System.Net;
using System.Net.Http.Json;
using VoiceConcierge.Contracts;

namespace VoiceConcierge.Api.Tests;

[Collection(HttpCollection.Name)]
public class HttpUnansweredTests(HttpFixture fx)
{
    [Fact]
    public async Task Post_returns_202_and_increments_frequency_on_repeat()
    {
        var ct = TestContext.Current.CancellationToken;
        var c = fx.NewClient();
        Assert.Equal(HttpStatusCode.Accepted,
            (await c.PostAsJsonAsync("/unanswered", new RecordUnansweredRequest("Do you allow drones?"), ct)).StatusCode);
        await c.PostAsJsonAsync("/unanswered", new RecordUnansweredRequest("do you ALLOW drones??"), ct);

        var list = await c.GetFromJsonAsync<PagedResult<UnansweredDto>>("/unanswered", ct);
        var row = list!.Items.Single(u => u.Question == "Do you allow drones?");
        Assert.Equal(2, row.Frequency);
    }

    public static TheoryData<string> InvalidQuestions() => new()
    {
        "",
        new string('q', 1001),
        "\r\0\a\r",
    };

    [Theory]
    [MemberData(nameof(InvalidQuestions))]
    public async Task Post_rejects_empty_oversized_and_control_only_payload(string question)
    {
        var ct = TestContext.Current.CancellationToken;
        var r = await fx.NewClient().PostAsJsonAsync("/unanswered", new RecordUnansweredRequest(question), ct);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Convert_happy_path_returns_201_and_404s_unknown()
    {
        var ct = TestContext.Current.CancellationToken;
        var c = fx.NewClient();
        await c.PostAsJsonAsync("/unanswered",
            new RecordUnansweredRequest("What is the convert happy path?"), ct);
        var row = (await c.GetFromJsonAsync<PagedResult<UnansweredDto>>("/unanswered", ct))!
            .Items.Single(u => u.Question == "What is the convert happy path?");

        var ok = await c.PostAsJsonAsync($"/unanswered/{row.Id}/convert",
            new ConvertUnansweredRequest("Documented in the test.", ["safety"]), ct);
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);

        var miss = await c.PostAsJsonAsync($"/unanswered/{Guid.NewGuid()}/convert",
            new ConvertUnansweredRequest("ok.", null), ct);
        Assert.Equal(HttpStatusCode.NotFound, miss.StatusCode);
    }

    [Theory]
    [InlineData("", "answer required")]
    [InlineData("ok", "too many tags", true)]
    public async Task Convert_rejects_bad_inputs(string answer, string expectedFragment, bool tooManyTags = false)
    {
        var ct = TestContext.Current.CancellationToken;
        var c = fx.NewClient();
        await c.PostAsJsonAsync("/unanswered",
            new RecordUnansweredRequest($"What about input {expectedFragment}?"), ct);
        var row = (await c.GetFromJsonAsync<PagedResult<UnansweredDto>>("/unanswered", ct))!
            .Items.Single(u => u.Question.Contains(expectedFragment));

        var tags = tooManyTags ? Enumerable.Range(0, 9).Select(i => "t" + i).ToArray() : null;
        var r = await c.PostAsJsonAsync($"/unanswered/{row.Id}/convert",
            new ConvertUnansweredRequest(answer, tags), ct);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Dismiss_happy_path_then_dismiss_twice_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;
        var c = fx.NewClient();
        await c.PostAsJsonAsync("/unanswered",
            new RecordUnansweredRequest("Can I bring a flamingo?"), ct);
        var row = (await c.GetFromJsonAsync<PagedResult<UnansweredDto>>("/unanswered", ct))!
            .Items.Single(u => u.Question == "Can I bring a flamingo?");

        Assert.Equal(HttpStatusCode.NoContent,
            (await c.PostAsync($"/unanswered/{row.Id}/dismiss", null, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await c.PostAsync($"/unanswered/{row.Id}/dismiss", null, ct)).StatusCode);
    }
}
