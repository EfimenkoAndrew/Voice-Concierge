using Microsoft.Extensions.DependencyInjection;
using VoiceConcierge.Agent.Audio;
using VoiceConcierge.Agent.LiveKit;
using VoiceConcierge.Agent.Pipeline;

namespace VoiceConcierge.Agent;

public sealed class ConciergeWorker(
    IServiceProvider sp,
    IBackendApi backend,
    ISpeechToText stt,
    ConciergePipeline pipeline,
    GuestAudioStream guestAudio,
    IVoiceActivityDetector vad,
    IConfiguration config,
    ILogger<ConciergeWorker> log) : BackgroundService
{
    private const string AgentIdentity = "concierge-agent";
    private const string DefaultRoom = "concierge";
    private const string Greeting =
        "Welcome to The Meridian. I'm your concierge — how may I assist you today?";
    private static readonly TimeSpan InitialBackoff = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan GreetingHeadDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan GreetingTailDelay = TimeSpan.FromMilliseconds(300);
    private const int BusyIdle = 0;
    private const int BusyTrue = 1;

    private AgentLiveKitClient? _live;
    private int _busy;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var room = config["LIVEKIT_ROOM"] ?? DefaultRoom;
        var backoff = InitialBackoff;
        while (!ct.IsCancellationRequested)
        {
            var live = sp.GetRequiredService<AgentLiveKitClient>();
            _live = live;
            try
            {
                await RunSessionAsync(live, room, ct).ConfigureAwait(false);
                backoff = InitialBackoff;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Agent transport failed; reconnecting in {Backoff}ms",
                    backoff.TotalMilliseconds);
            }
            finally { await live.DisposeAsync().ConfigureAwait(false); }
            if (ct.IsCancellationRequested) break;
            try { await Task.Delay(backoff, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            backoff = NextBackoff(backoff);
        }
    }

    private async Task RunSessionAsync(AgentLiveKitClient live, string room, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(live);
        ArgumentException.ThrowIfNullOrWhiteSpace(room);

        var (token, url) = await backend.GetLiveKitTokenAsync(room, AgentIdentity, ct).ConfigureAwait(false);
        var lkUrl = config["LIVEKIT_URL"];

        live.ParticipantJoined += identity => _ = SpeakGreetingAsync(live, identity, ct);

        await live.ConnectAsync(string.IsNullOrWhiteSpace(lkUrl) ? url : lkUrl, token, ct)
            .ConfigureAwait(false);
        log.LogInformation("Concierge agent joined '{Room}' — listening.", room);

        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var consume = ConsumeAsync(live, sessionCts.Token);
        var winner = await Task.WhenAny(consume, live.Disconnected).ConfigureAwait(false);
        if (ReferenceEquals(winner, live.Disconnected))
        {
            var reason = await live.Disconnected.ConfigureAwait(false);
            log.LogInformation("LiveKit disconnected ({Reason}); reconnecting", reason);
            await sessionCts.CancelAsync().ConfigureAwait(false);
            try { await consume.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        else
        {
            await consume.ConfigureAwait(false);
        }
    }

    private static TimeSpan NextBackoff(TimeSpan current)
    {
        var doubled = TimeSpan.FromTicks(current.Ticks * 2);
        return doubled > MaxBackoff ? MaxBackoff : doubled;
    }

    private async Task ConsumeAsync(AgentLiveKitClient live, CancellationToken ct)
    {
        var segmenter = new UtteranceSegmenter(vad);
        var pending = new Queue<short>();
        var frame = new short[UtteranceSegmenter.FrameSamples];
        await foreach (var chunk in guestAudio.ReadAllAsync(ct).ConfigureAwait(false))
        {
            if (Volatile.Read(ref _busy) == BusyTrue) continue;
            foreach (var s in chunk) pending.Enqueue(s);
            while (pending.Count >= UtteranceSegmenter.FrameSamples)
            {
                for (var i = 0; i < frame.Length; i++) frame[i] = pending.Dequeue();
                var utterance = segmenter.Push(frame);
                if (utterance is not { Length: > 0 }) continue;
                if (!TryClaimBusy()) continue;
                try { await HandleTurnAsync(live, utterance, ct).ConfigureAwait(false); }
                finally
                {
                    vad.Reset();
                    pending.Clear();
                    DrainGuestChannel();
                    ReleaseBusy();
                }
            }
        }
    }

    private bool TryClaimBusy() =>
        Interlocked.CompareExchange(ref _busy, BusyTrue, BusyIdle) == BusyIdle;

    private void ReleaseBusy() => Volatile.Write(ref _busy, BusyIdle);

    private void DrainGuestChannel()
    {
        var drained = 0;
        while (guestAudio.TryRead(out _)) drained++;
        if (drained > 0) log.LogDebug("Drained {N} stale audio chunks", drained);
    }

    public async Task HandleTurnAsync(AgentLiveKitClient live, short[] guestPcm16k, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(guestPcm16k);
        if (guestPcm16k.Length == 0) return;

        var transcript = await stt.TranscribeAsync(
            AudioResampler.Pcm16ToBytes(guestPcm16k), ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(transcript)) return;
        log.LogDebug("Guest: {Transcript}", transcript);

        await foreach (var c in pipeline.StreamAsync(transcript, ct).ConfigureAwait(false))
        {
            log.LogInformation("Turn chunk (strategy={Strategy}, answered={Answered})",
                c.Strategy, c.Answered);
            log.LogDebug("Concierge: {Reply}", c.Text);
            if (c.Audio is { Length: > 0 })
                await live.PublishPcm48kAsync(c.Audio, ct).ConfigureAwait(false);
        }
    }

    private async Task SpeakGreetingAsync(AgentLiveKitClient live, string identity, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(live);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        if (!TryClaimBusy()) return;
        try
        {
            log.LogInformation("Greeting guest {Identity}", identity);
            await Task.Delay(GreetingHeadDelay, ct).ConfigureAwait(false);
            var voice = await backend.GetActiveProviderVoiceIdAsync(ct).ConfigureAwait(false);
            var tts = sp.GetRequiredService<ITextToSpeech>();
            var pcm = await tts.SynthesizeAsync(Greeting, voice, ct).ConfigureAwait(false);
            if (pcm.Length > 0) await live.PublishPcm48kAsync(pcm, ct).ConfigureAwait(false);
            else log.LogWarning("Greeting TTS returned empty audio");
            await Task.Delay(GreetingTailDelay, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { log.LogWarning(ex, "Greeting failed for {Identity}", identity); }
        finally
        {
            vad.Reset();
            DrainGuestChannel();
            ReleaseBusy();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        if (_live is not null) await _live.DisposeAsync().ConfigureAwait(false);
    }
}
