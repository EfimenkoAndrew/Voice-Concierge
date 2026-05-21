using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using VoiceConcierge.Contracts;

namespace VoiceConcierge.Agent.Audio;

public interface IVoiceActivityDetector
{
    float SpeechProbability(ReadOnlySpan<short> frame16k);
    void Reset();
}

public sealed class SileroVadModel : IDisposable
{
    private const string ModelUrl =
        "https://github.com/snakers4/silero-vad/raw/v5.1.2/src/silero_vad/data/silero_vad.onnx";
    private const string ModelFileName = "silero_vad.onnx";
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(5);

    private readonly IConfiguration _config;
    private readonly ILogger<SileroVadModel> _log;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private volatile InferenceSession? _session;

    public SileroVadModel(IConfiguration config, ILogger<SileroVadModel> log)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task EnsureReadyAsync(CancellationToken ct)
    {
        if (_session is not null) return;
        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_session is not null) return;
            var dir = _config["VAD_MODEL_DIR"] ?? Path.Combine(AppContext.BaseDirectory, "models");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, ModelFileName);

            if (!File.Exists(path) || new FileInfo(path).Length == 0)
            {
                _log.LogWarning("Downloading Silero VAD model (pinned v5.1.2) to {Path} — NOT recommended in production", path);
                using var http = new HttpClient { Timeout = DownloadTimeout };
                var bytes = await http.GetByteArrayAsync(new Uri(ModelUrl), ct).ConfigureAwait(false);
                await ModelIntegrity.VerifyStreamAsync(
                    new MemoryStream(bytes), _config["VAD_MODEL_SHA256"], "Silero VAD",
                    m => _log.LogWarning("{Msg}", m), ct).ConfigureAwait(false);
                await File.WriteAllBytesAsync(path, bytes, ct).ConfigureAwait(false);
            }
            else if (_config["VAD_MODEL_SHA256"] is { Length: > 0 })
            {
                await using var fs = File.OpenRead(path);
                await ModelIntegrity.VerifyStreamAsync(fs, _config["VAD_MODEL_SHA256"], "Silero VAD",
                    m => _log.LogWarning("{Msg}", m), ct).ConfigureAwait(false);
            }
            _session = new InferenceSession(path);
            _log.LogInformation("Silero VAD ready");
        }
        finally { _initLock.Release(); }
    }

    public float Run(float[] audio, float[] state, long[] sampleRate)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(sampleRate);
        var session = _session ?? throw new InvalidOperationException(
            "SileroVadModel.EnsureReadyAsync must be awaited before Run");

        var inputs = new List<NamedOnnxValue>(3)
        {
            NamedOnnxValue.CreateFromTensor("input",
                new DenseTensor<float>(audio, [1, audio.Length])),
            NamedOnnxValue.CreateFromTensor("state",
                new DenseTensor<float>(state, [2, 1, 128])),
            NamedOnnxValue.CreateFromTensor("sr",
                new DenseTensor<long>(sampleRate, [1])),
        };
        using var run = session.Run(inputs);
        var prob = 0f;
        foreach (var o in run)
        {
            if (o.Name == "output") prob = o.AsTensor<float>()[0];
            else if (o.Name is "stateN" or "state")
            {
                var t = o.AsTensor<float>();
                for (var i = 0; i < state.Length && i < t.Length; i++) state[i] = t.GetValue(i);
            }
        }
        return prob;
    }

    public void Dispose()
    {
        _session?.Dispose();
        _initLock.Dispose();
    }
}

public sealed class SileroVad(SileroVadModel model) : IVoiceActivityDetector
{
    public const int FrameSamples = 512;
    private const int StateSize = 2 * 1 * 128;

    private readonly SileroVadModel _model =
        model ?? throw new ArgumentNullException(nameof(model));
    private readonly float[] _state = new float[StateSize];
    private readonly float[] _audio = new float[FrameSamples];
    private readonly long[] _sr = [16000];

    public void Reset() => Array.Clear(_state);

    public float SpeechProbability(ReadOnlySpan<short> frame16k)
    {
        if (frame16k.Length != FrameSamples)
            throw new ArgumentException(
                $"frame must be exactly {FrameSamples} samples (got {frame16k.Length})",
                nameof(frame16k));

        for (var i = 0; i < FrameSamples; i++) _audio[i] = frame16k[i] / 32768f;
        return _model.Run(_audio, _state, _sr);
    }
}

public sealed class UtteranceSegmenter
{
    public const int FrameSamples = SileroVad.FrameSamples;
    public const int SilenceHangoverMs = 300;
    public const int SampleRateHz = 16_000;
    public const int MaxUtteranceSeconds = 30;
    public const int MaxUtteranceSamples = SampleRateHz * MaxUtteranceSeconds;
    public const int LeadInFrames = 6;

    private readonly IVoiceActivityDetector _vad;
    private readonly float _threshold;
    private readonly List<short> _buffer = new(capacity: SampleRateHz);
    private readonly Queue<short[]> _leadIn = new(LeadInFrames + 1);

    private bool _inSpeech;
    private int _silenceMs;

    public UtteranceSegmenter(IVoiceActivityDetector vad, float threshold = 0.5f)
    {
        _vad = vad ?? throw new ArgumentNullException(nameof(vad));
        if (threshold < 0f || threshold > 1f)
            throw new ArgumentOutOfRangeException(nameof(threshold), threshold, "must be in [0,1]");
        _threshold = threshold;
    }

    public short[]? Push(ReadOnlySpan<short> frame16k)
    {
        if (frame16k.Length != FrameSamples)
            throw new ArgumentException(
                $"frame must be exactly {FrameSamples} samples (got {frame16k.Length})",
                nameof(frame16k));

        var speech = _vad.SpeechProbability(frame16k) >= _threshold;
        var frameMs = frame16k.Length * 1000 / SampleRateHz;

        if (speech)
        {
            if (!_inSpeech)
            {
                foreach (var lead in _leadIn) _buffer.AddRange(lead);
                _leadIn.Clear();
            }
            _inSpeech = true;
            _silenceMs = 0;
            AppendFrame(frame16k);
            return _buffer.Count >= MaxUtteranceSamples ? FinishUtterance() : null;
        }

        if (!_inSpeech)
        {
            _leadIn.Enqueue(frame16k.ToArray());
            while (_leadIn.Count > LeadInFrames) _leadIn.Dequeue();
            return null;
        }
        AppendFrame(frame16k);
        _silenceMs += frameMs;
        if (_silenceMs < SilenceHangoverMs && _buffer.Count < MaxUtteranceSamples) return null;
        return FinishUtterance();
    }

    private void AppendFrame(ReadOnlySpan<short> frame16k)
    {
        for (var i = 0; i < frame16k.Length; i++) _buffer.Add(frame16k[i]);
    }

    private short[]? FinishUtterance()
    {
        var u = _buffer.ToArray();
        _buffer.Clear();
        _leadIn.Clear();
        _inSpeech = false;
        _silenceMs = 0;
        _vad.Reset();
        return u.Length > 0 ? u : null;
    }
}

public sealed class GuestAudioStream
{
    public const int Capacity = 256;

    private readonly Channel<short[]> _ch =
        Channel.CreateBounded<short[]>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
        });

    public bool TryWrite(short[] pcm16k)
    {
        ArgumentNullException.ThrowIfNull(pcm16k);
        return _ch.Writer.TryWrite(pcm16k);
    }

    public bool TryRead(out short[] pcm16k) => _ch.Reader.TryRead(out pcm16k!);

    public IAsyncEnumerable<short[]> ReadAllAsync(CancellationToken ct) =>
        _ch.Reader.ReadAllAsync(ct);
}
