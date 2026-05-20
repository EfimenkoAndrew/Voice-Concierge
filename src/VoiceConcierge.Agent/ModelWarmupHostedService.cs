using VoiceConcierge.Agent.Audio;

namespace VoiceConcierge.Agent;

public sealed class ModelWarmupHostedService(
    SileroVad vad,
    ILogger<ModelWarmupHostedService> log) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(vad);
        log.LogInformation("Warming up Silero VAD model");
        await vad.EnsureReadyAsync(ct).ConfigureAwait(false);
        log.LogInformation("Model warmup complete");
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
