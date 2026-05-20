using Concentus;
using Concentus.Enums;

namespace VoiceConcierge.Agent.Audio;

public sealed class OpusAudio
{
    public const int SampleRate = 48_000;
    public const int FrameSamples = 960;
    public const int MaxFrameSamples = 5760;
    public const int MaxOpusPacketBytes = 4000;

    private readonly IOpusEncoder _encoder =
        OpusCodecFactory.CreateEncoder(SampleRate, 1, OpusApplication.OPUS_APPLICATION_VOIP);
    private readonly IOpusDecoder _decoder =
        OpusCodecFactory.CreateDecoder(SampleRate, 1);
    private readonly byte[] _encScratch = new byte[MaxOpusPacketBytes];

    public ReadOnlyMemory<byte> Encode(ReadOnlySpan<short> pcm48kFrame)
    {
        if (pcm48kFrame.Length != FrameSamples)
            throw new ArgumentException(
                $"frame must be exactly {FrameSamples} samples (got {pcm48kFrame.Length})",
                nameof(pcm48kFrame));
        var n = _encoder.Encode(pcm48kFrame, FrameSamples, _encScratch, _encScratch.Length);
        if (n < 0) throw new InvalidOperationException($"opus encode failed: {n}");
        return _encScratch.AsMemory(0, n);
    }

    public short[] Decode(ReadOnlySpan<byte> opus)
    {
        if (opus.IsEmpty)
            throw new ArgumentException("packet must not be empty", nameof(opus));
        var pcm = new short[MaxFrameSamples];
        var n = _decoder.Decode(opus, pcm, MaxFrameSamples, false);
        if (n < 0) throw new InvalidOperationException($"opus decode failed: {n}");
        return pcm[..n];
    }

    public short[] DecodePlc()
    {
        var pcm = new short[MaxFrameSamples];
        var n = _decoder.Decode(ReadOnlySpan<byte>.Empty, pcm, FrameSamples, false);
        if (n < 0) throw new InvalidOperationException($"opus PLC decode failed: {n}");
        return pcm[..n];
    }
}
