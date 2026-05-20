using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using Google.Protobuf;
using LiveKit.Proto;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using VoiceConcierge.Agent.Audio;

namespace VoiceConcierge.Agent.LiveKit;

public sealed class AgentLiveKitClient(GuestAudioStream guestAudio, ILogger<AgentLiveKitClient> log)
    : IAsyncDisposable
{
    private const int Protocol = 15;
    private const string Sdk = "go";
    private const string ClientVersion = "1.0.0";
    private const string AgentIdentity = "concierge-agent";
    private const string AgentTrackCid = "agent-audio-0";
    private const string AgentTrackName = "concierge";
    private const int OpusPayloadType = 111;
    private const int OpusChannels = 2;
    private const int ReceiveBufferBytes = 64 * 1024;
    private const int JoinReadMaxAttempts = 10;
    private const int InboundResampleInputRate = 48_000;
    private const int InboundResampleOutputRate = 16_000;
    private const int PublishFrameIntervalMs = 20;
    private const int PublishTailMs = 200;
    private const int DecodePlcMaxGap = 10;
    private const int LogRateLimitEveryN = 50;
    private const int DefaultPingIntervalSec = 30;
    private const int DefaultPingTimeoutSec = 60;
    private const int DisposeDrainSeconds = 3;
    private static readonly short[] PublishTailSilence =
        new short[InboundResampleInputRate * PublishTailMs / 1000];

    private readonly OpusAudio _opus = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly StreamResampler _inboundResampler =
        new(InboundResampleInputRate, InboundResampleOutputRate);
    private readonly TaskCompletionSource<string> _disconnected =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _internalCts = new();
    private readonly ConcurrentDictionary<string, byte> _greeted = new();
    private readonly ConcurrentDictionary<string, byte> _subscribedTrackSids = new();
    private readonly ConcurrentQueue<string> _pendingGreetings = new();

    private ClientWebSocket? _ws;
    private RTCPeerConnection? _pcPub;
    private RTCPeerConnection? _pcSub;
    private List<RTCIceServer>? _iceServers;
    private CancellationToken _ct;
    private Task? _signalLoop;
    private Task? _pingLoop;
    private ushort? _lastSeq;
    private int? _audioPt;
    private long _decodeFailures;
    private long _droppedFrames;
    private int _pingIntervalSec = DefaultPingIntervalSec;
    private int _pingTimeoutSec = DefaultPingTimeoutSec;
    private long _lastPongTimestamp;
    private int _subAudioTracks;
    private int _disposed;

    public Task<string> Disconnected => _disconnected.Task;

    public event Action<string>? ParticipantJoined;

    public bool IsConnected =>
        _pcPub?.connectionState == RTCPeerConnectionState.connected
        && _pcSub?.connectionState == RTCPeerConnectionState.connected;

    public async Task ConnectAsync(string serverUrl, string token, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        _ct = ct;
        await OpenWebSocketAsync(serverUrl, token, ct).ConfigureAwait(false);

        var join = await ReadUntilJoinAsync(ct).ConfigureAwait(false);
        ApplyJoinResponse(join);

        SetUpPublisher();
        await SendAddTrackAsync(ct).ConfigureAwait(false);
        await SendPublisherOfferAsync(ct).ConfigureAwait(false);
        StartBackgroundLoops(ct);
    }

    private async Task OpenWebSocketAsync(string serverUrl, string token, CancellationToken ct)
    {
        _ws = new ClientWebSocket();
        var wsBase = serverUrl
            .Replace("http://", "ws://", StringComparison.OrdinalIgnoreCase)
            .Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase)
            .TrimEnd('/');
        var url = new Uri(
            $"{wsBase}/rtc?access_token={token}&auto_subscribe=1" +
            $"&protocol={Protocol}&sdk={Sdk}&version={ClientVersion}");
        await _ws.ConnectAsync(url, ct).ConfigureAwait(false);
    }

    private void ApplyJoinResponse(JoinResponse join)
    {
        ArgumentNullException.ThrowIfNull(join);
        _iceServers = join.IceServers.Select(i => new RTCIceServer
        {
            urls = string.Join(",", i.Urls),
            username = i.Username ?? string.Empty,
            credential = i.Credential ?? string.Empty,
        }).ToList();
        if (join.PingInterval > 0) _pingIntervalSec = join.PingInterval;
        if (join.PingTimeout > 0) _pingTimeoutSec = join.PingTimeout;
        log.LogInformation("Joined LiveKit room {Room} (server {Ver}) ping={Interval}s/{Timeout}s",
            join.Room?.Name, join.ServerInfo?.Version, _pingIntervalSec, _pingTimeoutSec);
        foreach (var p in join.OtherParticipants)
            if (p.Identity != AgentIdentity) _pendingGreetings.Enqueue(p.Identity);
    }

    private void SetUpPublisher()
    {
        _pcPub = new RTCPeerConnection(new RTCConfiguration { iceServers = _iceServers });
        var opusFmt = new SDPAudioVideoMediaFormat(
            SDPMediaTypesEnum.audio, OpusPayloadType, "OPUS", OpusAudio.SampleRate, OpusChannels);
        _pcPub.addTrack(new MediaStreamTrack(
            SDPMediaTypesEnum.audio, false, [opusFmt], MediaStreamStatusEnum.SendOnly));
        _pcPub.onicecandidate += c => FireAndForgetIce(c, SignalTarget.Publisher);
        _pcPub.onconnectionstatechange += OnPublisherStateChanged;
    }

    private void OnPublisherStateChanged(RTCPeerConnectionState s)
    {
        log.LogInformation("LiveKit pub pc state: {S}", s);
        if (s != RTCPeerConnectionState.connected) return;
        while (_pendingGreetings.TryDequeue(out var id))
        {
            if (!_greeted.TryAdd(id, 0)) continue;
            try { ParticipantJoined?.Invoke(id); }
            catch (Exception ex) { log.LogWarning(ex, "ParticipantJoined handler"); }
        }
    }

    private Task SendAddTrackAsync(CancellationToken ct) =>
        Send(new SignalRequest
        {
            AddTrack = new AddTrackRequest
            {
                Cid = AgentTrackCid,
                Name = AgentTrackName,
                Type = TrackType.Audio,
                Source = TrackSource.Microphone,
            },
        }, ct);

    private async Task SendPublisherOfferAsync(CancellationToken ct)
    {
        var offer = _pcPub!.createOffer(null);
        await _pcPub.setLocalDescription(offer).ConfigureAwait(false);
        var sdp = NormalizePublisherSdp(offer.sdp);
        await Send(new SignalRequest { Offer = new SessionDescription { Type = "offer", Sdp = sdp } }, ct)
            .ConfigureAwait(false);
    }

    private void StartBackgroundLoops(CancellationToken ct)
    {
        _signalLoop = Task.Run(() => SignalLoopAsync(ct), ct);
        _ = _signalLoop.ContinueWith(t =>
        {
            if (t.IsFaulted && !ct.IsCancellationRequested)
            {
                log.LogWarning(t.Exception, "LiveKit signal loop faulted; aborting WS to trigger reconnect");
                try { _ws?.Abort(); } catch (Exception ex) { log.LogDebug(ex, "ws abort"); }
            }
            SignalDisconnected(t.IsFaulted ? "signal-loop-faulted" : "signal-loop-ended");
        }, TaskScheduler.Default);

        _lastPongTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _pingLoop = Task.Run(() => PingLoopAsync(ct), ct);
    }

    private void SignalDisconnected(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (_disconnected.TrySetResult(reason))
            log.LogInformation("LiveKit session ended: {Reason}", reason);
    }

    public static int CountAudioMLines(string sdp)
    {
        if (string.IsNullOrEmpty(sdp)) return 0;
        var count = 0;
        var normalized = sdp.Replace("\r\n", "\n");
        foreach (var line in normalized.Split('\n'))
            if (line.StartsWith("m=audio ", StringComparison.Ordinal)) count++;
        return count;
    }

    public static string NormalizePublisherSdp(string sdp)
    {
        ArgumentNullException.ThrowIfNull(sdp);
        var lines = sdp.Replace("\r\n", "\n").Split('\n').ToList();
        if (!lines.Any(l => l.StartsWith("m=audio ", StringComparison.Ordinal))) return sdp;

        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].StartsWith("m=audio ", StringComparison.Ordinal))
            {
                lines[i] = lines[i]
                    .Replace("UDP/TLS/RTP/SAVP ", "UDP/TLS/RTP/SAVPF ", StringComparison.Ordinal)
                    .Replace(" 101", string.Empty, StringComparison.Ordinal);
            }
        }
        lines.RemoveAll(l =>
            l.StartsWith("a=rtpmap:101 ", StringComparison.Ordinal) ||
            l.StartsWith("a=fmtp:101 ", StringComparison.Ordinal));
        var idx = lines.FindIndex(l => l.StartsWith("a=rtpmap:111 ", StringComparison.Ordinal));
        if (idx >= 0)
        {
            lines.Insert(idx + 1, "a=rtcp-fb:111 transport-cc");
            lines.Insert(idx + 2, "a=rtcp-fb:111 nack");
            lines.Insert(idx + 3, "a=fmtp:111 minptime=10;useinbandfec=1;stereo=0;sprop-stereo=0");
        }
        return string.Join("\r\n", lines);
    }

    private async Task PingLoopAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_pingIntervalSec));
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var staleMs = nowMs - Interlocked.Read(ref _lastPongTimestamp);
                if (staleMs > (long)_pingTimeoutSec * 1000)
                {
                    log.LogWarning("LiveKit pong timeout ({Stale}ms stale); ending session", staleMs);
                    try { _ws?.Abort(); } catch (Exception ex) { log.LogDebug(ex, "ws abort"); }
                    SignalDisconnected("pong-timeout");
                    return;
                }
                try { await Send(new SignalRequest { PingReq = new Ping { Timestamp = nowMs, Rtt = 0 } }, ct).ConfigureAwait(false); }
                catch (Exception ex)
                {
                    log.LogDebug(ex, "ping send failed");
                    SignalDisconnected("ping-send-failed");
                    return;
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private void FireAndForgetIce(RTCIceCandidate? c, SignalTarget target)
    {
        if (c is null || _ws is null) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await Send(new SignalRequest
                {
                    Trickle = new TrickleRequest
                    {
                        CandidateInit = JsonSerializer.Serialize(new
                        {
                            candidate = c.candidate,
                            sdpMid = c.sdpMid,
                            sdpMLineIndex = c.sdpMLineIndex,
                        }),
                        Target = target,
                    },
                }, _ct).ConfigureAwait(false);
            }
            catch (Exception ex) { log.LogDebug(ex, "ice send ({Target})", target); }
        }, _ct);
    }

    private async Task CreateOrAnswerSubscriberAsync(SessionDescription remoteOffer, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(remoteOffer);
        if (_pcSub is null)
        {
            _pcSub = new RTCPeerConnection(new RTCConfiguration { iceServers = _iceServers ?? [] });
            _pcSub.OnRtpPacketReceived += OnRtp;
            _pcSub.onicecandidate += c => FireAndForgetIce(c, SignalTarget.Subscriber);
            _pcSub.onconnectionstatechange += s => log.LogInformation("LiveKit sub pc state: {S}", s);
        }

        var audioMLines = CountAudioMLines(remoteOffer.Sdp);
        while (_subAudioTracks < audioMLines)
        {
            var opusFmt = new SDPAudioVideoMediaFormat(
                SDPMediaTypesEnum.audio, OpusPayloadType, "OPUS", OpusAudio.SampleRate, OpusChannels);
            _pcSub.addTrack(new MediaStreamTrack(
                SDPMediaTypesEnum.audio, false, [opusFmt], MediaStreamStatusEnum.RecvOnly));
            _subAudioTracks++;
        }
        if (audioMLines > 0)
            log.LogDebug("Subscriber audio transceivers: {Have} / offer m-lines: {Want}",
                _subAudioTracks, audioMLines);

        var sdrRes = _pcSub.setRemoteDescription(new RTCSessionDescriptionInit
        { type = RTCSdpType.offer, sdp = remoteOffer.Sdp });
        if (sdrRes != SetDescriptionResultEnum.OK)
            log.LogWarning("setRemoteDescription returned {Res}", sdrRes);
        var answer = _pcSub.createAnswer(null);
        await _pcSub.setLocalDescription(answer).ConfigureAwait(false);
        var fixedAnswer = NormalizePublisherSdp(answer.sdp);
        log.LogInformation("Sending subscriber answer ({Bytes} bytes)", fixedAnswer.Length);
        await Send(new SignalRequest
        { Answer = new SessionDescription { Type = "answer", Sdp = fixedAnswer } }, ct)
            .ConfigureAwait(false);
    }

    private void OnRtp(IPEndPoint ep, SDPMediaTypesEnum media, RTPPacket pkt)
    {
        if (media != SDPMediaTypesEnum.audio || pkt.Payload is not { Length: > 0 }) return;
        var pt = pkt.Header.PayloadType;
        _audioPt ??= pt;
        if (pt != _audioPt) return;
        try
        {
            if (!AdvanceSequence(pkt.Header.SequenceNumber)) return;
            var pcm48 = _opus.Decode(pkt.Payload);
            Enqueue(_inboundResampler.Process(pcm48));
        }
        catch (Exception ex)
        {
            var n = Interlocked.Increment(ref _decodeFailures);
            if (n % LogRateLimitEveryN == 1)
                log.LogWarning(ex, "Inbound audio decode failing ({Count} so far)", n);
        }
    }

    private bool AdvanceSequence(ushort seq)
    {
        if (_lastSeq is not { } prev)
        {
            _lastSeq = seq;
            return true;
        }
        var diff = (short)(seq - prev);
        if (diff > 1 && diff <= DecodePlcMaxGap)
        {
            for (var i = 0; i < diff - 1; i++)
                Enqueue(_inboundResampler.Process(_opus.DecodePlc()));
        }
        if (diff <= 0) return false;
        _lastSeq = seq;
        return true;
    }

    private void Enqueue(short[] pcm16)
    {
        if (pcm16.Length == 0) return;
        if (guestAudio.TryWrite(pcm16)) return;
        var n = Interlocked.Increment(ref _droppedFrames);
        if (n % LogRateLimitEveryN == 1)
            log.LogWarning("Guest audio backpressure: {Dropped} frame(s) dropped", n);
    }

    public async Task PublishPcm48kAsync(byte[] pcm48kMono, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pcm48kMono);
        if (_pcPub is null) { log.LogWarning("Publish skipped: publisher PC is null"); return; }
        if (_pcPub.connectionState != RTCPeerConnectionState.connected)
        {
            log.LogWarning("Publish skipped: pub pc state={State}", _pcPub.connectionState);
            return;
        }
        if (pcm48kMono.Length < sizeof(short)) return;

        var pcm48 = AudioResampler.BytesToPcm16(pcm48kMono);
        log.LogInformation("Publishing TTS audio: {Bytes} bytes / {Samples} samples (48kHz) +{Tail}ms tail",
            pcm48kMono.Length, pcm48.Length, PublishTailMs);

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(PublishFrameIntervalMs));
        var contentFrames = await PublishFramesAsync(pcm48, timer, ct).ConfigureAwait(false);
        var tailFrames = await PublishFramesAsync(PublishTailSilence, timer, ct).ConfigureAwait(false);
        var total = contentFrames + tailFrames;
        log.LogInformation("Publishing TTS audio: sent {Frames} Opus frames ({Ms}ms)",
            total, total * PublishFrameIntervalMs);
    }

    private async Task<int> PublishFramesAsync(short[] pcm48, PeriodicTimer timer, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(pcm48);
        ArgumentNullException.ThrowIfNull(timer);
        var frames = 0;
        for (var off = 0; off + OpusAudio.FrameSamples <= pcm48.Length; off += OpusAudio.FrameSamples)
        {
            if (ct.IsCancellationRequested) break;
            var encoded = _opus.Encode(pcm48.AsSpan(off, OpusAudio.FrameSamples));
            _pcPub!.SendAudio(OpusAudio.FrameSamples, encoded.ToArray());
            frames++;
            await timer.WaitForNextTickAsync(ct).ConfigureAwait(false);
        }
        return frames;
    }

    private async Task<JoinResponse> ReadUntilJoinAsync(CancellationToken ct)
    {
        var buf = new byte[ReceiveBufferBytes];
        for (var i = 0; i < JoinReadMaxAttempts; i++)
        {
            var m = await ReceiveAsync(buf, ct).ConfigureAwait(false);
            if (m?.MessageCase == SignalResponse.MessageOneofCase.Join) return m.Join;
        }
        throw new InvalidOperationException("No LiveKit JoinResponse received");
    }

    private async Task SignalLoopAsync(CancellationToken ct)
    {
        var buf = new byte[ReceiveBufferBytes];
        while (_ws?.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var m = await ReceiveAsync(buf, ct).ConfigureAwait(false);
            if (m is null) continue;
            log.LogDebug("signal: {Case}", m.MessageCase);
            await DispatchSignalAsync(m, ct).ConfigureAwait(false);
            if (_disconnected.Task.IsCompleted) return;
        }
        SignalDisconnected("ws-closed");
    }

    private async Task DispatchSignalAsync(SignalResponse m, CancellationToken ct)
    {
        switch (m.MessageCase)
        {
            case SignalResponse.MessageOneofCase.Answer: HandleAnswer(m.Answer); break;
            case SignalResponse.MessageOneofCase.Reconnect: HandleReconnect(); break;
            case SignalResponse.MessageOneofCase.Offer: await HandleOfferAsync(m.Offer, ct).ConfigureAwait(false); break;
            case SignalResponse.MessageOneofCase.Trickle: HandleTrickle(m.Trickle); break;
            case SignalResponse.MessageOneofCase.PongResp: HandlePong(); break;
            case SignalResponse.MessageOneofCase.Leave: HandleLeave(m.Leave); break;
            case SignalResponse.MessageOneofCase.Update: HandleUpdate(m.Update); break;
            case SignalResponse.MessageOneofCase.TrackPublished: HandleTrackPublished(m.TrackPublished); break;
            default: break;
        }
    }

    private void HandleAnswer(SessionDescription answer)
    {
        var res = _pcPub?.setRemoteDescription(new RTCSessionDescriptionInit
        { type = RTCSdpType.answer, sdp = answer.Sdp });
        if (res is not null && res != SetDescriptionResultEnum.OK)
            log.LogWarning("Publisher setRemoteDescription returned {Res}", res);
    }

    private void HandleReconnect()
    {
        log.LogInformation("SFU requested reconnect");
        SignalDisconnected("server-reconnect");
    }

    private async Task HandleOfferAsync(SessionDescription offer, CancellationToken ct)
    {
        try { await CreateOrAnswerSubscriberAsync(offer, ct).ConfigureAwait(false); }
        catch (Exception ex) { log.LogWarning(ex, "subscriber offer"); }
    }

    private void HandleTrickle(TrickleRequest trickle)
    {
        try
        {
            var el = JsonDocument.Parse(trickle.CandidateInit).RootElement;
            var init = new RTCIceCandidateInit
            {
                candidate = el.GetProperty("candidate").GetString(),
                sdpMid = el.TryGetProperty("sdpMid", out var x) ? x.GetString() : "0",
                sdpMLineIndex = el.TryGetProperty("sdpMLineIndex", out var ix)
                    && ix.ValueKind == JsonValueKind.Number ? (ushort)ix.GetInt32() : (ushort)0,
            };
            var pc = trickle.Target == SignalTarget.Subscriber ? _pcSub : _pcPub;
            pc?.addIceCandidate(init);
        }
        catch (Exception ex) { log.LogDebug(ex, "trickle"); }
    }

    private void HandlePong() =>
        Interlocked.Exchange(ref _lastPongTimestamp, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    private void HandleLeave(LeaveRequest leave)
    {
        log.LogInformation("LiveKit Leave from server (reason={Reason})", leave.Reason);
        SignalDisconnected($"server-leave:{leave.Reason}");
    }

    private void HandleUpdate(ParticipantUpdate update)
    {
        foreach (var p in update.Participants)
        {
            log.LogInformation("Participant {Identity} state={State}", p.Identity, p.State);
            if (p.Identity == AgentIdentity) continue;

            if (p.State == ParticipantInfo.Types.State.Disconnected)
            {
                if (_greeted.TryRemove(p.Identity, out _))
                {
                    log.LogInformation("Guest {Identity} left — ending session for clean reconnect", p.Identity);
                    SignalDisconnected("guest-left");
                }
                continue;
            }

            if (p.State != ParticipantInfo.Types.State.Active) continue;
            if (!_greeted.TryAdd(p.Identity, 0)) continue;
            try { ParticipantJoined?.Invoke(p.Identity); }
            catch (Exception ex) { log.LogWarning(ex, "ParticipantJoined handler"); }
        }
    }

    private void HandleTrackPublished(TrackPublishedResponse t) =>
        log.LogInformation("SFU confirmed track published: cid={Cid} sid={Sid}",
            t.Cid, t.Track?.Sid);

    private async Task<SignalResponse?> ReceiveAsync(byte[] buf, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        WebSocketReceiveResult r;
        do
        {
            r = await _ws!.ReceiveAsync(buf, ct).ConfigureAwait(false);
            if (r.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buf, 0, r.Count);
        } while (!r.EndOfMessage);
        if (r.MessageType != WebSocketMessageType.Binary) return null;
        try { return SignalResponse.Parser.ParseFrom(ms.ToArray()); }
        catch (InvalidProtocolBufferException) { return null; }
    }

    private async Task Send(SignalRequest req, CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try { await _ws!.SendAsync(req.ToByteArray(), WebSocketMessageType.Binary, true, ct).ConfigureAwait(false); }
        finally
        {
            if (Volatile.Read(ref _disposed) == 0) _sendLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        SignalDisconnected("disposed");
        try { await _internalCts.CancelAsync().ConfigureAwait(false); }
        catch (Exception ex) { log.LogDebug(ex, "internal cts cancel"); }
        try
        {
            if (_ws is { State: WebSocketState.Open })
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None)
                    .ConfigureAwait(false);
        }
        catch (Exception ex) { log.LogDebug(ex, "ws close on dispose"); }
        await DrainLoopsAsync().ConfigureAwait(false);
        _pcPub?.Dispose();
        _pcSub?.Dispose();
        _ws?.Dispose();
        _sendLock.Dispose();
        _internalCts.Dispose();
    }

    private async Task DrainLoopsAsync()
    {
        if (_signalLoop is not null)
            try { await _signalLoop.WaitAsync(TimeSpan.FromSeconds(DisposeDrainSeconds)).ConfigureAwait(false); }
            catch (Exception ex) { log.LogDebug(ex, "signal loop drain on dispose"); }

        if (_pingLoop is not null)
            try { await _pingLoop.WaitAsync(TimeSpan.FromSeconds(DisposeDrainSeconds)).ConfigureAwait(false); }
            catch (Exception ex) { log.LogDebug(ex, "ping loop drain on dispose"); }
    }
}
