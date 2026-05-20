using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace VoiceConcierge.Agent.Pipeline;

public sealed class ConciergePipeline
{
    public const int GuardMaxLength = 600;
    private static readonly TimeSpan VoiceCacheTtl = TimeSpan.FromSeconds(30);

    private readonly IBackendApi _backend;
    private readonly ILanguageModel _llm;
    private readonly ITextToSpeech _tts;
    private readonly ILogger<ConciergePipeline> _log;
    private readonly SemaphoreSlim _voiceLock = new(1, 1);

    private sealed record VoiceSnapshot(string Voice, DateTimeOffset At);
    private VoiceSnapshot? _voice;

    public ConciergePipeline(
        IBackendApi backend,
        ILanguageModel llm,
        ITextToSpeech tts,
        ILogger<ConciergePipeline> log)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _llm = llm ?? throw new ArgumentNullException(nameof(llm));
        _tts = tts ?? throw new ArgumentNullException(nameof(tts));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    private async Task<string> GetVoiceAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var snap = Volatile.Read(ref _voice);
        if (snap is not null && now - snap.At < VoiceCacheTtl) return snap.Voice;
        await _voiceLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            snap = Volatile.Read(ref _voice);
            if (snap is not null && DateTimeOffset.UtcNow - snap.At < VoiceCacheTtl) return snap.Voice;
            var voice = await _backend.GetActiveProviderVoiceIdAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(voice))
                throw new InvalidOperationException("backend returned empty voice id");
            Volatile.Write(ref _voice, new VoiceSnapshot(voice, DateTimeOffset.UtcNow));
            return voice;
        }
        finally { _voiceLock.Release(); }
    }

    public async Task<ConciergeTurn> HandleAsync(string transcript, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        var texts = new List<string>();
        using var audio = new MemoryStream();
        var answered = false; var strategy = "none";
        await foreach (var c in StreamAsync(transcript, ct).ConfigureAwait(false))
        {
            texts.Add(c.Text);
            audio.Write(c.Audio);
            answered = c.Answered; strategy = c.Strategy;
        }
        return new ConciergeTurn(string.Join(" ", texts), audio.ToArray(), answered, strategy);
    }

    public async IAsyncEnumerable<ConciergeChunk> StreamAsync(
        string transcript, [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transcript);

        var voice = await GetVoiceAsync(ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(transcript))
        {
            yield return await SpeakChunk(ConciergePrompt.GracefulFallback, false, "empty", voice, ct)
                .ConfigureAwait(false);
            yield break;
        }

        var match = await _backend.SearchAsync(transcript, ct).ConfigureAwait(false);

        if (match.Match && !string.IsNullOrWhiteSpace(match.Answer))
        {
            var answer = match.Answer!;
            string spoken;
            try
            {
                spoken = await _llm.CompleteAsync(
                    ConciergePrompt.System,
                    ConciergePrompt.GroundedUser(transcript, answer), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "LLM unavailable; speaking the grounded FAQ answer verbatim");
                spoken = answer;
            }
            if (string.IsNullOrWhiteSpace(spoken)) spoken = answer;

            yield return await SpeakChunk(spoken, true, match.Strategy, voice, ct).ConfigureAwait(false);
            yield break;
        }

        _log.LogInformation("No FAQ match; recording unanswered question");
        await _backend.RecordUnansweredAsync(transcript, ct).ConfigureAwait(false);
        yield return await SpeakChunk(ConciergePrompt.GracefulFallback, false, "none", voice, ct)
            .ConfigureAwait(false);
    }

    public static string Guard(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var clean = new string(s.Where(c => !char.IsControl(c) || c is '\n' or '\t').ToArray()).Trim();
        return clean.Length > GuardMaxLength ? clean[..GuardMaxLength] : clean;
    }

    private async Task<ConciergeChunk> SpeakChunk(
        string rawText, bool answered, string strategy, string voice, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rawText);
        ArgumentException.ThrowIfNullOrWhiteSpace(strategy);
        ArgumentException.ThrowIfNullOrWhiteSpace(voice);

        var text = Guard(rawText);
        byte[] audio;
        try { audio = await _tts.SynthesizeAsync(text, voice, ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "TTS unavailable; text-only chunk");
            audio = [];
        }
        return new ConciergeChunk(text, audio, answered, strategy);
    }
}
