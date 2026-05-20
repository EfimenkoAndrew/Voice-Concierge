using System.Net.WebSockets;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VoiceConcierge.Contracts;

public sealed class EdgeTtsClient
{
    private const string TrustedClientToken = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";
    private const string EndpointBase =
        "wss://speech.platform.bing.com/consumer/speech/synthesize/readaloud/edge/v1";
    private const string ChromiumVersion = "143.0.3650.75";
    private const string SecMsGecVersion = "1-" + ChromiumVersion;
    private const string EdgeUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36 Edg/143.0.0.0";
    private const string EdgeOrigin = "chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold";
    private const double WindowsEpochOffsetSec = 11_644_473_600d;
    private const double SecMsGecBucketSec = 300d;
    private const double SecMsGecSkewStepSec = 300d;
    private const int ConnectAttempts = 2;
    private const int ReceiveBufferBytes = 16 * 1024;
    private const int BinaryMessageHeaderLengthPrefixBytes = 2;

    public const string Mp3Format = "audio-24khz-96kbitrate-mono-mp3";

    private const string ProsodyRate = "-25%";

    public static EdgeTtsClient Shared { get; } = new();

    private readonly object _skewLock = new();
    private double _clockSkewSec;

    public static Task<byte[]> SynthesizeAsync(
        string text, string voiceName, string outputFormat, CancellationToken ct = default) =>
        Shared.SynthesizeInstanceAsync(text, voiceName, outputFormat, ct);

    public async Task<byte[]> SynthesizeInstanceAsync(
        string text, string voiceName, string outputFormat, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(voiceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFormat);

        for (var attempt = 0; attempt < ConnectAttempts; attempt++)
        {
            using var ws = BuildSocket();
            var url = BuildConnectUrl();
            try
            {
                await ws.ConnectAsync(url, ct).ConfigureAwait(false);
            }
            catch (WebSocketException ex) when (attempt < ConnectAttempts - 1 && IsClockSkewRejection(ex))
            {
                AdvanceClockSkew();
                continue;
            }
            return await ReceiveAudioAsync(ws, voiceName, text, outputFormat, ct).ConfigureAwait(false);
        }
        throw new InvalidOperationException("Edge TTS connect failed");
    }

    private void AdvanceClockSkew()
    {
        lock (_skewLock) _clockSkewSec += SecMsGecSkewStepSec;
    }

    private static ClientWebSocket BuildSocket()
    {
        var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Pragma", "no-cache");
        ws.Options.SetRequestHeader("Cache-Control", "no-cache");
        ws.Options.SetRequestHeader("Origin", EdgeOrigin);
        ws.Options.SetRequestHeader("Accept-Encoding", "gzip, deflate, br, zstd");
        ws.Options.SetRequestHeader("Accept-Language", "en-US,en;q=0.9");
        ws.Options.SetRequestHeader("User-Agent", EdgeUserAgent);
        return ws;
    }

    private Uri BuildConnectUrl()
    {
        var connectionId = Guid.NewGuid().ToString("N");
        return new Uri(
            $"{EndpointBase}?TrustedClientToken={TrustedClientToken}" +
            $"&ConnectionId={connectionId}" +
            $"&Sec-MS-GEC={ComputeSecMsGec()}" +
            $"&Sec-MS-GEC-Version={SecMsGecVersion}");
    }

    private string ComputeSecMsGec()
    {
        double skew;
        lock (_skewLock) skew = _clockSkewSec;
        var unixSec = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d + skew;
        var ticksSec = unixSec + WindowsEpochOffsetSec;
        ticksSec -= ticksSec % SecMsGecBucketSec;
        var ticks100Ns = ticksSec * 10_000_000d;
        var input = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{ticks100Ns:F0}{TrustedClientToken}");
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(input));
        return Convert.ToHexString(hash);
    }

    private static bool IsClockSkewRejection(WebSocketException ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        return ex.Message.Contains("403", StringComparison.Ordinal);
    }

    private static async Task<byte[]> ReceiveAudioAsync(
        ClientWebSocket ws, string voiceName, string text, string outputFormat, CancellationToken ct)
    {
        await SendText(ws,
            "Content-Type:application/json; charset=utf-8\r\nPath:speech.config\r\n\r\n" +
            BuildSpeechConfig(outputFormat), ct).ConfigureAwait(false);
        var requestId = Guid.NewGuid().ToString("N");
        await SendText(ws,
            $"X-RequestId:{requestId}\r\nContent-Type:application/ssml+xml\r\nPath:ssml\r\n\r\n" +
            $"<speak version='1.0' xml:lang='en-US'><voice name='{voiceName}'>" +
            $"<prosody rate='{ProsodyRate}'>{SecurityElement.Escape(text)}</prosody>" +
            $"</voice></speak>", ct).ConfigureAwait(false);

        using var audio = new MemoryStream();
        var buf = new byte[ReceiveBufferBytes];
        using var msg = new MemoryStream();
        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            msg.SetLength(0);
            var r = await ReadMessageAsync(ws, buf, msg, ct).ConfigureAwait(false);
            if (r is null) return audio.ToArray();
            AppendAudioOrCheckTurnEnd(r, msg, audio, out var done);
            if (done) break;
        }
        return audio.ToArray();
    }

    private static async Task<WebSocketReceiveResult?> ReadMessageAsync(
        ClientWebSocket ws, byte[] buf, MemoryStream msg, CancellationToken ct)
    {
        WebSocketReceiveResult r;
        do
        {
            r = await ws.ReceiveAsync(buf, ct).ConfigureAwait(false);
            if (r.MessageType == WebSocketMessageType.Close) return null;
            msg.Write(buf, 0, r.Count);
        } while (!r.EndOfMessage);
        return r;
    }

    private static void AppendAudioOrCheckTurnEnd(
        WebSocketReceiveResult r, MemoryStream msg, MemoryStream audio, out bool turnEnded)
    {
        turnEnded = false;
        var bytes = msg.GetBuffer();
        var len = (int)msg.Length;
        if (r.MessageType == WebSocketMessageType.Text)
        {
            if (Encoding.UTF8.GetString(bytes, 0, len).Contains("Path:turn.end", StringComparison.Ordinal))
                turnEnded = true;
            return;
        }
        if (len <= BinaryMessageHeaderLengthPrefixBytes) return;
        var headerLen = (bytes[0] << 8) | bytes[1];
        var start = BinaryMessageHeaderLengthPrefixBytes + headerLen;
        if (start < len) audio.Write(bytes, start, len - start);
    }

    public static string BuildSpeechConfig(string outputFormat)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFormat);
        return JsonSerializer.Serialize(new
        {
            context = new
            {
                synthesis = new
                {
                    audio = new
                    {
                        metadataoptions = new
                        {
                            sentenceBoundaryEnabled = false,
                            wordBoundaryEnabled = false,
                        },
                        outputFormat,
                    },
                },
            },
        });
    }

    public static byte[] StripWavHeader(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length < 12 ||
            !(bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F'))
            return bytes;
        for (var i = 12; i + 8 <= bytes.Length;)
        {
            var id = Encoding.ASCII.GetString(bytes, i, 4);
            var size = BitConverter.ToInt32(bytes, i + 4);
            if (string.Equals(id, "data", StringComparison.Ordinal)) return bytes[(i + 8)..];
            if (size < 0) break;
            i += 8 + size + (size & 1);
        }
        return bytes;
    }

    private static Task SendText(ClientWebSocket ws, string s, CancellationToken ct) =>
        ws.SendAsync(Encoding.UTF8.GetBytes(s), WebSocketMessageType.Text, true, ct);
}
