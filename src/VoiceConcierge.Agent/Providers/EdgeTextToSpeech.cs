using Microsoft.Extensions.Logging;
using NLayer;
using VoiceConcierge.Agent.Audio;
using VoiceConcierge.Agent.Pipeline;
using VoiceConcierge.Contracts;

namespace VoiceConcierge.Agent.Providers;

public sealed class EdgeTextToSpeech(ILogger<EdgeTextToSpeech> log) : ITextToSpeech
{
    private const string DefaultVoice = "en-GB-RyanNeural";
    private const int TargetSampleRate = 48_000;
    private const int Pcm16BytesPerSample = 2;
    private const int ReadChunkSize = 8192;

    public async Task<byte[]> SynthesizeAsync(string text, string providerVoiceId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        var voice = string.IsNullOrWhiteSpace(providerVoiceId) ? DefaultVoice : providerVoiceId;
        var mp3 = await EdgeTtsClient.SynthesizeAsync(text, voice, EdgeTtsClient.Mp3Format, ct);
        if (mp3.Length == 0)
        {
            log.LogWarning("Edge TTS returned 0 bytes for voice={Voice}", voice);
            return [];
        }
        var pcm = DecodeMp3ToMono48k(mp3);
        log.LogInformation("Edge TTS: voice={Voice} chars={Chars} mp3={Mp3}B pcm={Pcm}B dur={DurMs}ms",
            voice, text.Length, mp3.Length, pcm.Length, pcm.Length * 1000 / (TargetSampleRate * Pcm16BytesPerSample));
        log.LogDebug("Edge TTS full text: {Text}", text);
        return pcm;
    }

    private static byte[] DecodeMp3ToMono48k(byte[] mp3Bytes)
    {
        ArgumentNullException.ThrowIfNull(mp3Bytes);
        using var ms = new MemoryStream(mp3Bytes);
        using var mp3 = new MpegFile(ms);
        var sr = mp3.SampleRate;
        var channels = mp3.Channels;
        var floats = new List<float>(capacity: TargetSampleRate * 8);
        var chunk = new float[ReadChunkSize];
        while (true)
        {
            var n = mp3.ReadSamples(chunk, 0, chunk.Length);
            if (n <= 0) break;
            floats.AddRange(chunk.AsSpan(0, n).ToArray());
        }
        var monoCount = floats.Count / channels;
        var mono = new float[monoCount];
        for (var i = 0; i < monoCount; i++)
        {
            float sum = 0;
            for (var c = 0; c < channels; c++) sum += floats[i * channels + c];
            mono[i] = sum / channels;
        }
        var mono48k = sr == TargetSampleRate ? mono : ResampleFloat(mono, sr, TargetSampleRate);
        var pcm48k = new short[mono48k.Length];
        for (var i = 0; i < mono48k.Length; i++)
        {
            var v = mono48k[i];
            if (v > 1f) v = 1f; else if (v < -1f) v = -1f;
            pcm48k[i] = (short)(v * 32767);
        }
        return AudioResampler.Pcm16ToBytes(pcm48k);
    }

    private static float[] ResampleFloat(float[] input, int inRate, int outRate)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (inRate == outRate || input.Length == 0) return input;
        var outLen = (int)((long)input.Length * outRate / inRate);
        var output = new float[Math.Max(outLen, 1)];
        var ratio = (double)input.Length / outLen;
        for (var i = 0; i < outLen; i++)
        {
            var pos = i * ratio;
            var i1 = (int)pos;
            var frac = (float)(pos - i1);
            var i0 = Math.Max(i1 - 1, 0);
            var i2 = Math.Min(i1 + 1, input.Length - 1);
            var i3 = Math.Min(i1 + 2, input.Length - 1);
            var y0 = input[i0];
            var y1 = input[i1];
            var y2 = input[i2];
            var y3 = input[i3];
            var a = -0.5f * y0 + 1.5f * y1 - 1.5f * y2 + 0.5f * y3;
            var b = y0 - 2.5f * y1 + 2f * y2 - 0.5f * y3;
            var c = -0.5f * y0 + 0.5f * y2;
            var d = y1;
            output[i] = ((a * frac + b) * frac + c) * frac + d;
        }
        return output;
    }
}
