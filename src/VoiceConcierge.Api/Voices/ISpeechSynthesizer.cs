namespace VoiceConcierge.Api.Voices;

public interface ISpeechSynthesizer
{
    Task<byte[]> SynthesizeAsync(string text, string providerVoiceId, CancellationToken ct = default);
}
