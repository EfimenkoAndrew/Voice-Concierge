using System.Threading.Channels;
using VoiceConcierge.Agent.Audio;
using VoiceConcierge.Agent.LiveKit;
using VoiceConcierge.Agent.Pipeline;

namespace VoiceConcierge.Agent;

public sealed class ConciergeSession(
    IBackendApi backend,
    ISpeechToText stt,
    ITextToSpeech tts,
    ConciergePipeline pipeline,
    GuestAudioStream guestAudio,
    IVoiceActivityDetector vad,
    IConfiguration config,
    ILogger<ConciergeSession> log,
    ILogger<AgentLiveKitClient> liveLog)
{
    private const string AgentIdentity = "concierge-agent";
    private const string Greeting =
        "Welcome to The Meridian. I'm your concierge — how may I assist you today?";
    private static readonly TimeSpan InitialBackoff = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MinStableUptime = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan GreetingHeadDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan GreetingTailDelay = TimeSpan.FromMilliseconds(300);
    private const int BusyIdle = 0;
    private const int BusyTrue = 1;
    private const int GreetingQueueCapacity = 32;

    private int _busy;

    public async Task RunAsync(string room, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(room);
        var backoff = InitialBackoff;
        while (!ct.IsCancellationRequested)
        {
            var live = new AgentLiveKitClient(guestAudio, liveLog);
            var startedAt = DateTimeOffset.UtcNow;
            try
            {
                await RunSessionAsync(live, room, ct).ConfigureAwait(false);
                if (DateTimeOffset.UtcNow - startedAt >= MinStableUptime)
                    backoff = InitialBackoff;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                log.LogWarning(ex, "[{Room}] Agent transport failed; reconnecting in {Backoff}ms",
                    room, backoff.TotalMilliseconds);
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

        var greetings = Channel.CreateBounded<string>(
            new BoundedChannelOptions(GreetingQueueCapacity)
            {
                SingleReader = true,
                FullMode = BoundedChannelFullMode.DropOldest,
            });
        live.ParticipantJoined += identity => greetings.Writer.TryWrite(identity);

        await live.ConnectAsync(string.IsNullOrWhiteSpace(lkUrl) ? url : lkUrl, token, ct)
            .ConfigureAwait(false);
        log.LogInformation("Concierge agent joined '{Room}' — listening.", room);

        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var consume = ConsumeAsync(live, sessionCts.Token);
        var greet = GreetLoopAsync(live, greetings.Reader, sessionCts.Token);
        try
        {
            var winner = await Task.WhenAny(consume, live.Disconnected).ConfigureAwait(false);
            if (ReferenceEquals(winner, live.Disconnected))
            {
                var reason = await live.Disconnected.ConfigureAwait(false);
                log.LogInformation("[{Room}] LiveKit disconnected ({Reason}); reconnecting", room, reason);
                await sessionCts.CancelAsync().ConfigureAwait(false);
                try { await consume.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
            else
            {
                await consume.ConfigureAwait(false);
            }
        }
        finally
        {
            greetings.Writer.TryComplete();
            try { await greet.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { log.LogWarning(ex, "[{Room}] Greeting loop teardown failed", room); }
        }
    }

    private async Task GreetLoopAsync(
        AgentLiveKitClient live, ChannelReader<string> greetings, CancellationToken ct)
    {
        try
        {
            await foreach (var identity in greetings.ReadAllAsync(ct).ConfigureAwait(false))
                await SpeakGreetingAsync(live, identity, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
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
        DrainGuestChannel();
        await foreach (var chunk in guestAudio.ReadAllAsync(ct).ConfigureAwait(false))
        {
            if (Volatile.Read(ref _busy) == BusyTrue)
            {
                pending.Clear();
                continue;
            }
            foreach (var s in chunk) pending.Enqueue(s);
            while (pending.Count >= UtteranceSegmenter.FrameSamples
                   && Volatile.Read(ref _busy) == BusyIdle
                   && !ct.IsCancellationRequested)
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

    private async Task HandleTurnAsync(AgentLiveKitClient live, short[] guestPcm16k, CancellationToken ct)
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
}
