using System.Net.Http.Json;
using System.Text.Json.Serialization;
using VoiceConcierge.Agent.Pipeline;

namespace VoiceConcierge.Agent.Providers;

public sealed class GroqLanguageModel : ILanguageModel
{
    private const string DefaultModel = "llama-3.3-70b-versatile";
    private const double Temperature = 0.4;

    private readonly HttpClient _http;
    private readonly IConfiguration _config;

    public GroqLanguageModel(HttpClient http, IConfiguration config)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(systemPrompt);
        ArgumentNullException.ThrowIfNull(userPrompt);
        var model = _config["LLM_MODEL"] ?? DefaultModel;
        var req = new ChatRequest(model, [
            new("system", systemPrompt),
            new("user", userPrompt),
        ], Temperature);

        using var res = await _http.PostAsJsonAsync("chat/completions", req, ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<ChatResponse>(ct).ConfigureAwait(false);
        return body?.Choices?.FirstOrDefault()?.Message?.Content?.Trim() ?? string.Empty;
    }

    private sealed record ChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("temperature")] double Temperature);

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record ChatResponse(
        [property: JsonPropertyName("choices")] List<Choice>? Choices);
    private sealed record Choice(
        [property: JsonPropertyName("message")] ChatMessage? Message);
}
