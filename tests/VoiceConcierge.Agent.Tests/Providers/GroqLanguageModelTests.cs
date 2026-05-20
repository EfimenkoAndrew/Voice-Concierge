using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using VoiceConcierge.Agent.Providers;

namespace VoiceConcierge.Agent.Tests;

public class GroqLanguageModelTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request, body);
        }
    }

    [Fact] // 2-3: LLM provider sends system+user and returns the assistant content
    public async Task Sends_messages_and_parses_response()
    {
        string? sentBody = null;
        var handler = new StubHandler((_, body) =>
        {
            sentBody = body;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"choices":[{"message":{"role":"assistant","content":"Yes, the poker room is open 24/7."}}]}""",
                    Encoding.UTF8, "application/json"),
            };
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.groq.com/openai/v1/") };
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["LLM_MODEL"] = "llama-3.3-70b-versatile" }).Build();

        var llm = new GroqLanguageModel(http, cfg);
        var reply = await llm.CompleteAsync("SYS-PERSONA", "GUEST asks about poker",
            TestContext.Current.CancellationToken);

        Assert.Equal("Yes, the poker room is open 24/7.", reply);
        Assert.NotNull(sentBody);
        Assert.Contains("SYS-PERSONA", sentBody);
        Assert.Contains("GUEST asks about poker", sentBody);
        Assert.Contains("llama-3.3-70b-versatile", sentBody);
        Assert.Contains("\"role\":\"system\"", sentBody);
        Assert.Contains("\"role\":\"user\"", sentBody);
    }

    private static GroqLanguageModel ClientReturning(HttpStatusCode code, string body = "{}")
    {
        var handler = new StubHandler((_, _) =>
            new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.groq.com/openai/v1/") };
        var cfg = new ConfigurationBuilder().Build();
        return new GroqLanguageModel(http, cfg);
    }

    [Fact] // Q4-11: non-2xx surfaces as HttpRequestException so the pipeline's
    // catch-and-fall-back-to-FAQ-answer path actually fires.
    public async Task Throws_on_non_2xx()
    {
        await Assert.ThrowsAnyAsync<HttpRequestException>(() =>
            ClientReturning(HttpStatusCode.InternalServerError, """{"error":"oops"}""")
                .CompleteAsync("sys", "user", TestContext.Current.CancellationToken));
    }

    [Fact] // Q4-11: empty choices[] yields "" (pipeline falls back to FAQ answer
    // via the `if (string.IsNullOrWhiteSpace(spoken)) spoken = match.Answer!;` arm).
    public async Task Returns_empty_when_choices_missing()
    {
        var reply = await ClientReturning(HttpStatusCode.OK, """{"choices":[]}""")
            .CompleteAsync("sys", "user", TestContext.Current.CancellationToken);
        Assert.Equal("", reply);
    }
}
