using System.Buffers.Binary;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using VoiceConcierge.Agent.Pipeline;

namespace VoiceConcierge.Agent.Providers;

public sealed class GroqSpeechToText : ISpeechToText
{
    private const string DefaultModel = "whisper-large-v3";
    private const int SampleRateHz = 16_000;
    private const int Channels = 1;
    private const int BitsPerSample = 16;
    private const int WavHeaderBytes = 44;

    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<GroqSpeechToText> _log;

    public GroqSpeechToText(HttpClient http, IConfiguration config, ILogger<GroqSpeechToText> log)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task<string> TranscribeAsync(ReadOnlyMemory<byte> pcm16Mono16k, CancellationToken ct = default)
    {
        if (pcm16Mono16k.Length < sizeof(short)) return string.Empty;

        var wav = WrapPcmAsWav(pcm16Mono16k.Span);
        var model = _config["STT_MODEL"] ?? DefaultModel;

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(wav);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(fileContent, "file", "utterance.wav");
        form.Add(new StringContent(model), "model");
        form.Add(new StringContent("en"), "language");
        form.Add(new StringContent("text"), "response_format");

        using var res = await _http.PostAsync("audio/transcriptions", form, ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _log.LogWarning("Groq STT failed: {Status} {Body}", (int)res.StatusCode, body);
            return string.Empty;
        }
        return (await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false)).Trim();
    }

    private static byte[] WrapPcmAsWav(ReadOnlySpan<byte> pcm)
    {
        var totalLen = WavHeaderBytes + pcm.Length;
        var wav = new byte[totalLen];
        var span = wav.AsSpan();
        "RIFF"u8.CopyTo(span[..4]);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(4, 4), (uint)(totalLen - 8));
        "WAVE"u8.CopyTo(span.Slice(8, 4));
        "fmt "u8.CopyTo(span.Slice(12, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(16, 4), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(20, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(22, 2), Channels);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(24, 4), SampleRateHz);
        var byteRate = (uint)(SampleRateHz * Channels * BitsPerSample / 8);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(28, 4), byteRate);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(32, 2), (ushort)(Channels * BitsPerSample / 8));
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(34, 2), BitsPerSample);
        "data"u8.CopyTo(span.Slice(36, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(40, 4), (uint)pcm.Length);
        pcm.CopyTo(span[WavHeaderBytes..]);
        return wav;
    }
}
