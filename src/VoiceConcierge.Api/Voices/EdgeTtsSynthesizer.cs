using VoiceConcierge.Contracts;

namespace VoiceConcierge.Api.Voices;

public sealed class EdgeTtsSynthesizer(ILogger<EdgeTtsSynthesizer> log) : ISpeechSynthesizer
{
    public async Task<byte[]> SynthesizeAsync(string text, string providerVoiceId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(providerVoiceId);
        var mp3 = await EdgeTtsClient.SynthesizeAsync(
            text, providerVoiceId, EdgeTtsClient.Mp3Format, ct);
        log.LogInformation("Edge TTS synthesized {Bytes} bytes for {Voice}", mp3.Length, providerVoiceId);
        return mp3;
    }
}
