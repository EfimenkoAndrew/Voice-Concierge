using VoiceConcierge.Agent.Audio;

namespace VoiceConcierge.Agent;

public sealed class ModelWarmupHostedService(
    SileroVadModel model,
    ILogger<ModelWarmupHostedService> log) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(model);
        log.LogInformation("Warming up Silero VAD model");
        await model.EnsureReadyAsync(ct).ConfigureAwait(false);
        log.LogInformation("Model warmup complete");
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
