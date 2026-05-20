using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using VoiceConcierge.Api.Data;
using VoiceConcierge.Api.Metrics;
using VoiceConcierge.Contracts;

namespace VoiceConcierge.Api.Search;

public sealed class OnnxEmbeddingService : IEmbeddingService, IDisposable
{
    private const string Rev = "5c38ec7c405ec4b44b94cc5a9bb96e735b38267a";
    private const string ModelUrl = $"https://huggingface.co/BAAI/bge-small-en-v1.5/resolve/{Rev}/onnx/model.onnx";
    private const string VocabUrl = $"https://huggingface.co/BAAI/bge-small-en-v1.5/resolve/{Rev}/vocab.txt";
    private const int MaxTokens = 512;
    private const long ClsTokenId = 101;
    private const long SepTokenId = 102;
    private const string LastHiddenStateOutput = "last_hidden_state";
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(10);

    private readonly IConfiguration _config;
    private readonly ILogger<OnnxEmbeddingService> _log;
    private InferenceSession? _session;
    private BertTokenizer? _tokenizer;
    private readonly SemaphoreSlim _tokLock = new(1, 1);

    public int Dimension => Embeddings.Dimension;
    public bool Ready => _session is not null && _tokenizer is not null;

    public OnnxEmbeddingService(IConfiguration config, ILogger<OnnxEmbeddingService> log)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task EnsureReadyAsync(CancellationToken ct)
    {
        if (Ready) return;
        var dir = _config["EMBEDDING_MODEL_DIR"]
            ?? Path.Combine(AppContext.BaseDirectory, "models", "bge-small-en-v1.5");
        Directory.CreateDirectory(dir);
        var modelPath = Path.Combine(dir, "model.onnx");
        var vocabPath = Path.Combine(dir, "vocab.txt");
        await EnsureFileAsync(modelPath, ModelUrl, _config["EMBEDDING_MODEL_SHA256"], ct).ConfigureAwait(false);
        await EnsureFileAsync(vocabPath, VocabUrl, expectedSha256: null, ct).ConfigureAwait(false);
        _tokenizer = BertTokenizer.Create(vocabPath);
        _session = new InferenceSession(modelPath);
        _log.LogInformation("ONNX embedding model loaded from {Dir}", dir);
    }

    private async Task EnsureFileAsync(string path, string url, string? expectedSha256, CancellationToken ct)
    {
        if (File.Exists(path) && new FileInfo(path).Length > 0)
        {
            if (expectedSha256 is { Length: > 0 })
            {
                await using var fs = File.OpenRead(path);
                await ModelIntegrity.VerifyStreamAsync(fs, expectedSha256, "embedding model",
                    m => _log.LogWarning("{Msg}", m), ct).ConfigureAwait(false);
            }
            return;
        }
        if (!string.Equals(_config["ALLOW_RUNTIME_EMBEDDING_DOWNLOAD"], "true",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Embedding asset missing at {path}. The API image bakes it at build time; " +
                "set ALLOW_RUNTIME_EMBEDDING_DOWNLOAD=true to fetch at runtime.");
        }
        _log.LogWarning("Downloading embedding asset (pinned) {Url} - NOT recommended in production", url);
        ModelDownloadMetrics.RecordDownload("embedding");
        using var http = new HttpClient { Timeout = DownloadTimeout };
        var bytes = await http.GetByteArrayAsync(new Uri(url), ct).ConfigureAwait(false);
        await ModelIntegrity.VerifyStreamAsync(
            new MemoryStream(bytes), expectedSha256, "embedding model",
            m => _log.LogWarning("{Msg}", m), ct).ConfigureAwait(false);
        await File.WriteAllBytesAsync(path, bytes, ct).ConfigureAwait(false);
    }

    public float[] Embed(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (_session is null || _tokenizer is null)
            throw new InvalidOperationException(
                "OnnxEmbeddingService.EnsureReadyAsync must be awaited before Embed");

        _tokLock.Wait();
        long[] ids;
        try { ids = _tokenizer.EncodeToIds(text).Select(i => (long)i).ToArray(); }
        finally { _tokLock.Release(); }
        if (ids.Length == 0) ids = [ClsTokenId, SepTokenId];
        if (ids.Length > MaxTokens) ids = [.. ids.Take(MaxTokens - 1), SepTokenId];
        var len = ids.Length;
        var mask = Enumerable.Repeat(1L, len).ToArray();
        var types = new long[len];

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(ids, [1, len])),
            NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<long>(mask, [1, len])),
            NamedOnnxValue.CreateFromTensor("token_type_ids", new DenseTensor<long>(types, [1, len])),
        };

        using var results = _session.Run(inputs);
        var hiddenResult = results.FirstOrDefault(r => r.Name == LastHiddenStateOutput) ?? results[0];
        var hidden = hiddenResult.AsTensor<float>();
        var dim = hidden.Dimensions[2];
        var vec = new float[dim];
        for (var d = 0; d < dim; d++) vec[d] = hidden[0, 0, d];

        double norm = 0;
        for (var d = 0; d < dim; d++) norm += vec[d] * (double)vec[d];
        norm = Math.Sqrt(norm);
        if (norm > 0)
            for (var d = 0; d < dim; d++) vec[d] = (float)(vec[d] / norm);
        return vec;
    }

    public void Dispose()
    {
        _session?.Dispose();
        _tokLock.Dispose();
    }
}

public sealed class EmbeddingWarmupHostedService(
    OnnxEmbeddingService embedder,
    ILogger<EmbeddingWarmupHostedService> log) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        log.LogInformation("Warming up ONNX embedding model");
        try
        {
            await embedder.EnsureReadyAsync(ct).ConfigureAwait(false);
            log.LogInformation("Embedding model ready");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Embedding warmup failed; search will degrade to lexical");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
