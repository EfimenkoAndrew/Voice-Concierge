using VoiceConcierge.Agent.Audio;

namespace VoiceConcierge.Agent.Tests;

public class AudioTests
{
    [Fact]
    public void Resampler_changes_length_by_rate_ratio_and_round_trips()
    {
        var src = new short[16000]; // 1s @16k
        for (var i = 0; i < src.Length; i++) src[i] = (short)(8000 * Math.Sin(2 * Math.PI * 220 * i / 16000));

        // Tightened from ±1000/500 to ±100 (Q4-v4 weak-assertion note):
        // length should land within a single resampler-phase of the ideal.
        var up = AudioResampler.Resample(src, 16000, 48000);
        Assert.InRange(up.Length, 48000 - 100, 48000 + 100); // ~3x

        var back = AudioResampler.Resample(up, 48000, 16000);
        Assert.InRange(back.Length, 16000 - 100, 16000 + 100); // ~original
    }

    [Fact]
    public void Pcm_byte_round_trip_is_lossless()
    {
        short[] pcm = [0, 1, -1, 32767, -32768, 1234];
        var rt = AudioResampler.BytesToPcm16(AudioResampler.Pcm16ToBytes(pcm));
        Assert.Equal(pcm, rt);
    }

    [Theory]
    [InlineData(40)]
    [InlineData(60)]
    public void Opus_decodes_oversized_frames_without_throwing(int frameMs)
    {
        var opus = new OpusAudio();
        var n = OpusAudio.SampleRate / 1000 * frameMs;
        var frame = new short[n];
        for (var i = 0; i < n; i++)
            frame[i] = (short)(8000 * Math.Sin(2 * Math.PI * 330 * i / OpusAudio.SampleRate));
        var enc = Concentus.OpusCodecFactory.CreateEncoder(OpusAudio.SampleRate, 1,
            Concentus.Enums.OpusApplication.OPUS_APPLICATION_VOIP);
        var buf = new byte[4000];
        var len = enc.Encode(frame, n, buf, buf.Length);
        var decoded = opus.Decode(buf.AsSpan(0, len));
        Assert.True(decoded.Length >= n - 1);
    }

    [Fact] // H2 regression: StreamResampler keeps phase continuous across blocks
    public void StreamResampler_is_continuous_across_blocks()
    {
        short[] Tone(int n, int off) =>
            Enumerable.Range(0, n).Select(i =>
                (short)(10000 * Math.Sin(2 * Math.PI * 200 * (i + off) / 48000))).ToArray();

        // Whole-buffer reference vs the same signal fed in 960-sample blocks.
        var whole = AudioResampler.Resample(Tone(4800, 0), 48000, 16000);
        var sr = new StreamResampler(48000, 16000);
        var streamed = new List<short>();
        for (var off = 0; off < 4800; off += 960)
            streamed.AddRange(sr.Process(Tone(960, off)));

        Assert.InRange(streamed.Count, whole.Length - 4, whole.Length + 4);
        var n = Math.Min(streamed.Count, whole.Length);
        double err = 0;
        for (var i = 16; i < n; i++) err += Math.Abs(streamed[i] - whole[i]);
        Assert.True(err / n < 600, $"avg abs error {err / n:F1} too high (phase reset?)");
    }

    [Fact]
    public void Opus_encodes_and_decodes_a_frame()
    {
        var opus = new OpusAudio();
        var frame = new short[OpusAudio.FrameSamples];
        for (var i = 0; i < frame.Length; i++)
            frame[i] = (short)(10000 * Math.Sin(2 * Math.PI * 440 * i / OpusAudio.SampleRate));

        var encoded = opus.Encode(frame);
        Assert.True(encoded.Length > 0);

        var decoded = opus.Decode(encoded.Span);
        Assert.Equal(OpusAudio.FrameSamples, decoded.Length);
        Assert.Contains(decoded, s => s != 0); // signal survived (lossy but present)
    }

    private sealed class ScriptedVad(Queue<float> probs) : IVoiceActivityDetector
    {
        public float SpeechProbability(ReadOnlySpan<short> f) => probs.Count > 0 ? probs.Dequeue() : 0f;
        public void Reset() { }
    }

    [Fact]
    public void Segmenter_emits_one_utterance_after_speech_then_silence_hangover()
    {
        // 5 speech frames, then enough silence frames to exceed the 700ms hangover.
        var frameMs = UtteranceSegmenter.FrameSamples * 1000 / 16000; // 32ms
        var silenceFrames = SilenceFramesNeeded(frameMs);
        var script = new Queue<float>();
        for (var i = 0; i < 5; i++) script.Enqueue(0.9f);
        for (var i = 0; i < silenceFrames; i++) script.Enqueue(0.0f);

        var seg = new UtteranceSegmenter(new ScriptedVad(script));
        var frame = new short[UtteranceSegmenter.FrameSamples];
        short[]? utterance = null;
        var emissions = 0;

        for (var i = 0; i < 5 + silenceFrames; i++)
        {
            var r = seg.Push(frame);
            if (r is not null) { utterance = r; emissions++; }
        }

        Assert.Equal(1, emissions);
        Assert.NotNull(utterance);
        // 5 speech + the silence frames buffered up to the trigger.
        Assert.True(utterance!.Length >= 5 * UtteranceSegmenter.FrameSamples);
    }

    [Fact]
    public void Segmenter_does_not_emit_while_only_speech()
    {
        var script = new Queue<float>();
        for (var i = 0; i < 50; i++) script.Enqueue(0.9f);
        var seg = new UtteranceSegmenter(new ScriptedVad(script));
        var frame = new short[UtteranceSegmenter.FrameSamples];
        for (var i = 0; i < 50; i++) Assert.Null(seg.Push(frame));
    }

    private static int SilenceFramesNeeded(int frameMs)
    {
        var n = 0; var ms = 0;
        while (ms < UtteranceSegmenter.SilenceHangoverMs) { ms += frameMs; n++; }
        return n + 1;
    }
}
