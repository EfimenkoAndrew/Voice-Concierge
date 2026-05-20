using VoiceConcierge.Api.Voices;

namespace VoiceConcierge.Api.Tests;

public sealed class RecordingSynth : ISpeechSynthesizer
{
    private static readonly byte[] StubMp3 = [9, 9, 9];
    public string? LastVoice { get; private set; }
    public Task<byte[]> SynthesizeAsync(string text, string providerVoiceId, CancellationToken ct = default)
    {
        LastVoice = providerVoiceId;
        return Task.FromResult(StubMp3);
    }
}
