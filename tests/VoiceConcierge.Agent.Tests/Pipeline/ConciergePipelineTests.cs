using Microsoft.Extensions.Logging.Abstractions;
using VoiceConcierge.Agent.Pipeline;
using VoiceConcierge.Contracts;

namespace VoiceConcierge.Agent.Tests;

public class ConciergePipelineTests
{
    private sealed class FakeBackend : IBackendApi
    {
        public FaqSearchResult Result = new(false, null, 0, null, "none");
        public string ActiveVoice = "en-GB-RyanNeural";
        public List<string> Recorded { get; } = [];

        public Task<FaqSearchResult> SearchAsync(string q, CancellationToken ct = default) => Task.FromResult(Result);
        public Task RecordUnansweredAsync(string q, CancellationToken ct = default) { Recorded.Add(q); return Task.CompletedTask; }
        public Task<string> GetActiveProviderVoiceIdAsync(CancellationToken ct = default) => Task.FromResult(ActiveVoice);
        public Task<(string Token, string Url)> GetLiveKitTokenAsync(string room, string identity, CancellationToken ct = default)
            => Task.FromResult(("tok", "ws://localhost:7880"));
    }

    private sealed class FakeLlm : ILanguageModel
    {
        public string? LastSystem, LastUser;
        public Task<string> CompleteAsync(string system, string user, CancellationToken ct = default)
        {
            LastSystem = system; LastUser = user;
            return Task.FromResult("Certainly — " + user.Length + " chars of grounded context received.");
        }
    }

    private sealed class TwoSentenceLlm : ILanguageModel
    {
        public Task<string> CompleteAsync(string s, string u, CancellationToken ct = default)
            => Task.FromResult("Our poker room is open 24/7. Tournaments run at 7 PM.");
    }

    [Fact] // QA-H-2 / NFR-1: a multi-sentence answer emits one chunk (sentence-splitting
           // removed to avoid audible mid-reply gaps in the TTS pipeline).
    public async Task Match_streams_sentence_chunks()
    {
        var be = new FakeBackend
        {
            Result = new FaqSearchResult(true, "poker room 24/7", 0.9, Guid.NewGuid(), "semantic"),
        };
        var chunks = new List<ConciergeChunk>();
        await foreach (var c in New(be, new TwoSentenceLlm(), new FakeTts())
            .StreamAsync("is the poker room open?", TestContext.Current.CancellationToken))
            chunks.Add(c);

        Assert.Single(chunks);                                           // single chunk per turn
        Assert.Contains("poker room", chunks[0].Text);                  // grounded answer present
        Assert.True(chunks[0].Answered);
        Assert.NotEmpty(chunks[0].Audio);
    }

    private sealed class FakeTts : ITextToSpeech
    {
        public string? LastText; public string? LastVoice;
        public Task<byte[]> SynthesizeAsync(string text, string providerVoiceId, CancellationToken ct = default)
        { LastText = text; LastVoice = providerVoiceId; return Task.FromResult(new byte[] { 1, 2, 3 }); }
    }

    private static ConciergePipeline New(IBackendApi b, ILanguageModel l, ITextToSpeech t) =>
        new(b, l, t, NullLogger<ConciergePipeline>.Instance);

    [Fact] // 2-3: match -> grounded LLM answer in luxury tone, spoken via active voice
    public async Task Match_produces_grounded_spoken_answer()
    {
        var be = new FakeBackend
        {
            Result = new FaqSearchResult(true,
                "Our poker room is open 24 hours a day, 7 days a week.", 0.91, Guid.NewGuid(), "semantic"),
            ActiveVoice = "en-US-GuyNeural",
        };
        var llm = new FakeLlm();
        var tts = new FakeTts();

        var turn = await New(be, llm, tts).HandleAsync("is the card room open late?", TestContext.Current.CancellationToken);

        Assert.True(turn.Answered);
        Assert.Equal("semantic", turn.Strategy);
        Assert.Equal(ConciergePrompt.System, llm.LastSystem);                 // 2-5 luxury persona
        Assert.Contains("poker room is open 24 hours", llm.LastUser);          // 2-3 grounding
        Assert.Contains("do not add anything beyond this", llm.LastUser);      // grounding guard
        Assert.Equal(turn.SpokenText, tts.LastText);
        Assert.Equal("en-US-GuyNeural", tts.LastVoice);                        // 6-4 per-turn voice (provider id)
        Assert.NotEmpty(turn.Audio);
    }

    [Fact] // 2-4: no match -> graceful apology + record unanswered, conversation continues
    public async Task No_match_falls_back_gracefully_and_records()
    {
        var be = new FakeBackend { Result = new FaqSearchResult(false, null, 0, null, "none") };
        var tts = new FakeTts();

        var turn = await New(be, new FakeLlm(), tts).HandleAsync("can I bring my dog?", TestContext.Current.CancellationToken);

        Assert.False(turn.Answered);
        Assert.Equal(ConciergePrompt.GracefulFallback, turn.SpokenText);
        Assert.Contains("noted your question", turn.SpokenText);               // VC-5
        Assert.Single(be.Recorded);
        Assert.Equal("can I bring my dog?", be.Recorded[0]);
        Assert.NotEmpty(turn.Audio);                                           // still speaks (continues)
    }


    [Fact] // safety: never go silent even if the LLM returns empty
    public async Task Empty_llm_output_falls_back_to_faq_answer()
    {
        var be = new FakeBackend
        {
            Result = new FaqSearchResult(true, "Self parking is free for all guests.", 0.8, Guid.NewGuid(), "semantic"),
        };
        var emptyLlm = new EmptyLlm();

        var turn = await New(be, emptyLlm, new FakeTts()).HandleAsync("how much is parking", TestContext.Current.CancellationToken);

        Assert.True(turn.Answered);
        Assert.Equal("Self parking is free for all guests.", turn.SpokenText);
    }

    private sealed class EmptyLlm : ILanguageModel
    {
        public Task<string> CompleteAsync(string s, string u, CancellationToken ct = default) => Task.FromResult("");
    }

    private sealed class ThrowingLlm : ILanguageModel
    {
        public Task<string> CompleteAsync(string s, string u, CancellationToken ct = default)
            => throw new HttpRequestException("429 rate limited");
    }

    [Fact] // Arch-H2 / QA-L4: provider error must NOT abort the turn
    public async Task Llm_exception_falls_back_to_grounded_faq_answer()
    {
        var be = new FakeBackend
        {
            Result = new FaqSearchResult(true, "Self parking is free for all guests.", 0.8, Guid.NewGuid(), "semantic"),
        };
        var turn = await New(be, new ThrowingLlm(), new FakeTts())
            .HandleAsync("how much is parking", TestContext.Current.CancellationToken);

        Assert.True(turn.Answered);                                   // conversation continued
        Assert.Equal("Self parking is free for all guests.", turn.SpokenText);
    }

    private sealed class ThrowingTts : ITextToSpeech
    {
        public Task<byte[]> SynthesizeAsync(string t, string v, CancellationToken ct = default)
            => throw new HttpRequestException("tts down");
    }

    [Fact] // TTS failure must not abort the turn either (text-only turn)
    public async Task Tts_exception_yields_text_only_turn()
    {
        var be = new FakeBackend { Result = new FaqSearchResult(false, null, 0, null, "none") };
        var turn = await New(be, new FakeLlm(), new ThrowingTts())
            .HandleAsync("can I bring a llama?", TestContext.Current.CancellationToken);

        Assert.False(turn.Answered);
        Assert.Equal(ConciergePrompt.GracefulFallback, turn.SpokenText);
        Assert.Empty(turn.Audio);
        // Q4-v4 weak-assertion fix: also assert the unanswered question was
        // recorded with the original transcript, not normalized/empty/garbled.
        Assert.Single(be.Recorded);
        Assert.Equal("can I bring a llama?", be.Recorded[0]);
    }

    private sealed class HugeUglyLlm(string body) : ILanguageModel
    {
        public Task<string> CompleteAsync(string s, string u, CancellationToken ct = default)
            => Task.FromResult(body);
    }

    [Fact] // Q4-01 / Sec-L-4: Guard() must strip control chars AND cap length
    // before audio is synthesized — defeats a prompt-injected/runaway model
    // driving the TTS into unbounded synthesis or log-poisoned text.
    public async Task Llm_runaway_output_is_guarded_before_tts()
    {
        var be = new FakeBackend
        {
            Result = new FaqSearchResult(true, "fallback", 0.9, Guid.NewGuid(), "semantic"),
        };
        // 2 KB of payload + interspersed control chars and a NULL — must arrive
        // at the TTS sanitized and ≤ GuardMaxLength chars.
        var poison = "abc\0def\r\n\t\a" + new string('x', 2000) + "end";
        var tts = new FakeTts();
        var pipeline = New(be, new HugeUglyLlm(poison), tts);

        await pipeline.HandleAsync("anything", TestContext.Current.CancellationToken);

        Assert.NotNull(tts.LastText);
        Assert.True(tts.LastText!.Length <= ConciergePipeline.GuardMaxLength,
            $"TTS received {tts.LastText.Length} chars (cap {ConciergePipeline.GuardMaxLength})");
        Assert.DoesNotContain('\0', tts.LastText); // NUL stripped
        Assert.DoesNotContain('\a', tts.LastText); // BEL stripped
        Assert.DoesNotContain('\r', tts.LastText); // \r is a control char (not \n / \t)
    }

    [Fact] // Q4-02: empty/whitespace transcript -> single "empty" chunk via the
    // graceful fallback (was never exercised by any test).
    public async Task Empty_transcript_yields_fallback_chunk_with_empty_strategy()
    {
        var be = new FakeBackend();
        foreach (var transcript in new[] { "", "   ", "\t\n " })
        {
            var chunks = new List<ConciergeChunk>();
            await foreach (var c in New(be, new FakeLlm(), new FakeTts())
                .StreamAsync(transcript, TestContext.Current.CancellationToken))
                chunks.Add(c);

            Assert.Single(chunks);
            Assert.Equal("empty", chunks[0].Strategy);
            Assert.False(chunks[0].Answered);
            Assert.Equal(ConciergePrompt.GracefulFallback, chunks[0].Text);
        }
        // CRITICAL: empty input must NEVER be recorded into the unanswered queue.
        Assert.Empty(be.Recorded);
    }

    [Fact] // A4-06: GetActiveProviderVoiceIdAsync is cached per-conversation,
    // not called per-turn. Per-turn HTTP on the NFR-1 speech path was an
    // architectural smell that this verifies has been removed.
    public async Task Voice_lookup_is_cached_across_turns()
    {
        var be = new CountingBackend();
        var pipeline = New(be, new FakeLlm(), new FakeTts());
        // Three back-to-back turns within the cache TTL.
        await pipeline.HandleAsync("first?", TestContext.Current.CancellationToken);
        await pipeline.HandleAsync("second?", TestContext.Current.CancellationToken);
        await pipeline.HandleAsync("third?", TestContext.Current.CancellationToken);
        Assert.Equal(1, be.VoiceCalls);
    }

    private sealed class CountingBackend : IBackendApi
    {
        public int VoiceCalls;
        public Task<FaqSearchResult> SearchAsync(string q, CancellationToken ct = default)
            => Task.FromResult(new FaqSearchResult(false, null, 0, null, "none"));
        public Task RecordUnansweredAsync(string q, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> GetActiveProviderVoiceIdAsync(CancellationToken ct = default)
        { Interlocked.Increment(ref VoiceCalls); return Task.FromResult("en-GB-RyanNeural"); }
        public Task<(string Token, string Url)> GetLiveKitTokenAsync(string room, string identity, CancellationToken ct = default)
            => Task.FromResult(("tok", "ws://localhost:7880"));
    }
}
