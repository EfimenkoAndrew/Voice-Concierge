namespace VoiceConcierge.Agent.Audio;

public static class AudioResampler
{
    public static short[] Resample(ReadOnlySpan<short> input, int inRate, int outRate)
    {
        if (inRate <= 0) throw new ArgumentOutOfRangeException(nameof(inRate));
        if (outRate <= 0) throw new ArgumentOutOfRangeException(nameof(outRate));
        if (inRate == outRate) return input.ToArray();
        if (input.Length == 0) return [];
        var outLen = (int)((long)input.Length * outRate / inRate);
        var output = new short[Math.Max(outLen, 1)];
        var ratio = (double)(input.Length - 1) / Math.Max(outLen - 1, 1);
        for (var i = 0; i < outLen; i++)
        {
            var pos = i * ratio;
            var i0 = (int)pos;
            var i1 = Math.Min(i0 + 1, input.Length - 1);
            var frac = pos - i0;
            output[i] = (short)(input[i0] * (1 - frac) + input[i1] * frac);
        }
        return output;
    }

    public static short[] BytesToPcm16(ReadOnlySpan<byte> bytes)
    {
        var pcm = new short[bytes.Length / 2];
        for (var i = 0; i < pcm.Length; i++)
            pcm[i] = BitConverter.ToInt16(bytes.Slice(i * 2, 2));
        return pcm;
    }

    public static byte[] Pcm16ToBytes(ReadOnlySpan<short> pcm)
    {
        var bytes = new byte[pcm.Length * 2];
        for (var i = 0; i < pcm.Length; i++)
            BitConverter.GetBytes(pcm[i]).CopyTo(bytes, i * 2);
        return bytes;
    }
}

public sealed class StreamResampler
{
    private const int MaxInputSamplesPerCall = 16_384;
    private const int DefensiveGrowChunk = 32;

    private readonly int _inRate;
    private readonly int _outRate;
    private readonly double _step;
    private readonly short[] _work;

    private double _pos;
    private short _prev;
    private bool _hasPrev;

    public StreamResampler(int inRate, int outRate)
    {
        if (inRate <= 0) throw new ArgumentOutOfRangeException(nameof(inRate));
        if (outRate <= 0) throw new ArgumentOutOfRangeException(nameof(outRate));
        _inRate = inRate;
        _outRate = outRate;
        _step = (double)inRate / outRate;
        _work = new short[MaxInputSamplesPerCall + 1];
    }

    public short[] Process(ReadOnlySpan<short> input)
    {
        if (_inRate == _outRate) return input.ToArray();
        if (input.Length == 0) return [];
        if (input.Length > MaxInputSamplesPerCall)
            throw new ArgumentException(
                $"input must be ≤ {MaxInputSamplesPerCall} samples per call (got {input.Length})",
                nameof(input));

        var offset = _hasPrev ? 1 : 0;
        if (_hasPrev) _work[0] = _prev;
        input.CopyTo(_work.AsSpan(offset));
        var workLen = offset + input.Length;

        var estimated = (int)((input.Length - _pos) / _step) + 1;
        if (estimated < 0) estimated = 0;
        var output = new short[estimated];
        var written = 0;
        var maxStart = workLen - 1;
        while (_pos <= maxStart)
        {
            var i0 = (int)_pos;
            var frac = _pos - i0;
            var s0 = _work[i0];
            var s1 = i0 + 1 < workLen ? _work[i0 + 1] : _work[i0];
            if (written == output.Length)
            {
                Array.Resize(ref output, output.Length + DefensiveGrowChunk);
            }
            output[written++] = (short)(s0 * (1 - frac) + s1 * frac);
            _pos += _step;
        }

        _prev = input[^1];
        _hasPrev = true;
        _pos -= workLen - 1;
        if (_pos < 0) _pos = 0;

        if (written == output.Length) return output;
        var trimmed = new short[written];
        Array.Copy(output, trimmed, written);
        return trimmed;
    }
}
