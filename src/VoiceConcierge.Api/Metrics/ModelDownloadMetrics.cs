using System.Collections.Concurrent;

namespace VoiceConcierge.Api.Metrics;

public static class ModelDownloadMetrics
{
    private static readonly ConcurrentDictionary<string, long> _counters = new();

    public static void RecordDownload(string model) =>
        _counters.AddOrUpdate(model, 1, (_, v) => Interlocked.Increment(ref v));

    public static IReadOnlyDictionary<string, long> Snapshot() => _counters;
}
