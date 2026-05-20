using VoiceConcierge.Contracts;

namespace VoiceConcierge.Agent.Pipeline;

public interface IBackendApi
{
    Task<FaqSearchResult> SearchAsync(string query, CancellationToken ct = default);
    Task RecordUnansweredAsync(string question, CancellationToken ct = default);
    Task<string> GetActiveProviderVoiceIdAsync(CancellationToken ct = default);
    Task<(string Token, string Url)> GetLiveKitTokenAsync(string room, string identity, CancellationToken ct = default);
}

public interface ISpeechToText
{
    Task<string> TranscribeAsync(ReadOnlyMemory<byte> pcm, CancellationToken ct = default);
}

public interface ILanguageModel
{
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);
}

public interface ITextToSpeech
{
    Task<byte[]> SynthesizeAsync(string text, string providerVoiceId, CancellationToken ct = default);
}

public sealed record ConciergeTurn(
    string SpokenText,
    byte[] Audio,
    bool Answered,
    string Strategy);

public sealed record ConciergeChunk(
    string Text,
    byte[] Audio,
    bool Answered,
    string Strategy);
