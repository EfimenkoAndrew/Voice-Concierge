using System.Text;
using Microsoft.Extensions.Logging;
using VoiceConcierge.Agent.Pipeline;
using VoiceConcierge.Contracts;
using Whisper.net;
using Whisper.net.Ggml;

namespace VoiceConcierge.Agent.Providers;

public sealed class WhisperSpeechToText : ISpeechToText, IAsyncDisposable
{
    private const string ModelFileName = "ggml-base.en.bin";
    private const string Language = "en";
    private const int Pcm16BytesPerSample = sizeof(short);
    private const float Pcm16Scale = 32768f;

    private readonly ILogger<WhisperSpeechToText> _log;
    private readonly string _modelPath;
    private readonly string? _sha256;
    private readonly bool _allowRuntimeDownload;
    private readonly SemaphoreSlim _init = new(1, 1);
    private readonly SemaphoreSlim _proc = new(1, 1);

    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;

    public WhisperSpeechToText(IConfiguration config, ILogger<WhisperSpeechToText> log)
    {
        ArgumentNullException.ThrowIfNull(config);
        _log = log ?? throw new ArgumentNullException(nameof(log));
        var dir = config["STT_MODEL_DIR"] ?? Path.Combine(AppContext.BaseDirectory, "models");
        Directory.CreateDirectory(dir);
        _modelPath = Path.Combine(dir, ModelFileName);
        _sha256 = config["WHISPER_MODEL_SHA256"];
        _allowRuntimeDownload = string.Equals(
            config["ALLOW_RUNTIME_WHISPER_DOWNLOAD"], "true", StringComparison.OrdinalIgnoreCase);
    }

    private async Task EnsureModelAsync(CancellationToken ct)
    {
        if (_factory is not null) return;
        await _init.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_factory is not null) return;
            await EnsureModelFileAsync(ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(_sha256))
            {
                await using var fs = File.OpenRead(_modelPath);
                await ModelIntegrity.VerifyStreamAsync(fs, _sha256, "whisper model",
                    m => _log.LogWarning("{Msg}", m), ct).ConfigureAwait(false);
            }
            _factory = WhisperFactory.FromPath(_modelPath);
            _processor = _factory.CreateBuilder().WithLanguage(Language).Build();
        }
        finally { _init.Release(); }
    }

    private async Task EnsureModelFileAsync(CancellationToken ct)
    {
        if (File.Exists(_modelPath)) return;
        if (!_allowRuntimeDownload)
            throw new InvalidOperationException(
                $"Whisper model missing at {_modelPath}. The image bakes it at " +
                "build time; set ALLOW_RUNTIME_WHISPER_DOWNLOAD=true to fetch at runtime.");
        _log.LogWarning("Downloading Whisper base.en model at runtime (NOT recommended)");
        await using var s = await WhisperGgmlDownloader.Default
            .GetGgmlModelAsync(GgmlType.BaseEn, cancellationToken: ct).ConfigureAwait(false);
        await using var f = File.Create(_modelPath);
        await s.CopyToAsync(f, ct).ConfigureAwait(false);
    }

    public async Task<string> TranscribeAsync(ReadOnlyMemory<byte> pcm16Mono16k, CancellationToken ct = default)
    {
        if (pcm16Mono16k.Length < Pcm16BytesPerSample) return string.Empty;
        await EnsureModelAsync(ct).ConfigureAwait(false);

        var bytes = pcm16Mono16k.Span;
        var sampleCount = bytes.Length / Pcm16BytesPerSample;
        var samples = new float[sampleCount];
        for (var i = 0; i < sampleCount; i++)
            samples[i] = BitConverter.ToInt16(bytes.Slice(i * Pcm16BytesPerSample, Pcm16BytesPerSample)) / Pcm16Scale;

        await _proc.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var sb = new StringBuilder();
            await foreach (var seg in _processor!.ProcessAsync(samples, ct).ConfigureAwait(false))
                sb.Append(seg.Text);
            return sb.ToString().Trim();
        }
        finally { _proc.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_processor is not null) await _processor.DisposeAsync().ConfigureAwait(false);
        _factory?.Dispose();
        _init.Dispose();
        _proc.Dispose();
    }
}
