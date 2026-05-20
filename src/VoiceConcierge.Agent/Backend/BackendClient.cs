using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using VoiceConcierge.Agent.Pipeline;
using VoiceConcierge.Contracts;

namespace VoiceConcierge.Agent.Backend;

public sealed class BackendClient : IBackendApi
{
    private const string FallbackProviderVoice = "en-GB-RyanNeural";

    private readonly HttpClient _http;
    private readonly ILogger<BackendClient> _log;

    public BackendClient(HttpClient http, ILogger<BackendClient> log)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task<FaqSearchResult> SearchAsync(string query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var res = await _http.PostAsJsonAsync("/faqs/search",
            new FaqSearchRequest(query), ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<FaqSearchResult>(ct).ConfigureAwait(false))
               ?? new FaqSearchResult(false, null, 0, null, "none");
    }

    public async Task RecordUnansweredAsync(string question, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(question);
        var res = await _http.PostAsJsonAsync("/unanswered",
            new RecordUnansweredRequest(question), ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
    }

    public async Task<string> GetActiveProviderVoiceIdAsync(CancellationToken ct = default)
    {
        try
        {
            var voices = await _http.GetFromJsonAsync<List<VoiceDto>>("/voices", ct)
                .ConfigureAwait(false);
            var active = voices?.FirstOrDefault(v => v.Active) ?? voices?.FirstOrDefault();
            return string.IsNullOrWhiteSpace(active?.ProviderVoiceId)
                ? FallbackProviderVoice : active.ProviderVoiceId;
        }
        catch (HttpRequestException ex)
        {
            _log.LogWarning(ex, "Active voice lookup failed; defaulting to {Fallback}", FallbackProviderVoice);
            return FallbackProviderVoice;
        }
    }

    public async Task<(string Token, string Url)> GetLiveKitTokenAsync(
        string room, string identity, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(room);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        var res = await _http.PostAsJsonAsync("/livekit/token",
            new LiveKitTokenRequest(room, identity), ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        var dto = await res.Content.ReadFromJsonAsync<LiveKitTokenResponse>(ct).ConfigureAwait(false)
                  ?? throw new InvalidOperationException("empty token response");
        return (dto.Token, dto.Url);
    }
}
