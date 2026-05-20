using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using VoiceConcierge.Agent.Pipeline;

namespace VoiceConcierge.Agent.Providers;

public static class ProvidersConfiguration
{
    private const string GroqBaseUrl = "https://api.groq.com/openai/v1/";
    private static readonly TimeSpan GroqTimeout = TimeSpan.FromSeconds(20);
    private const string GroqProviderName = "groq";

    public static IServiceCollection AddSpeechAndLanguageProviders(this IServiceCollection services, IConfiguration cfg)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(cfg);

        services.AddSingleton<ILanguageModel>(_ => BuildGroqLanguageModel(cfg));

        var sttProvider = (cfg["STT_PROVIDER"] ?? "").Trim().ToLowerInvariant();
        if (sttProvider == GroqProviderName && !string.IsNullOrWhiteSpace(cfg["GROQ_API_KEY"]))
        {
            services.AddSingleton<ISpeechToText>(sp =>
            {
                var http = NewGroqClient(cfg);
                return new GroqSpeechToText(http, cfg, sp.GetRequiredService<ILogger<GroqSpeechToText>>());
            });
        }
        else
        {
            services.AddSingleton<ISpeechToText, WhisperSpeechToText>();
        }

        services.AddSingleton<ITextToSpeech, EdgeTextToSpeech>();
        return services;
    }

    private static GroqLanguageModel BuildGroqLanguageModel(IConfiguration cfg)
    {
        var http = NewGroqClient(cfg);
        return new GroqLanguageModel(http, cfg);
    }

    private static HttpClient NewGroqClient(IConfiguration cfg)
    {
        var http = new HttpClient { BaseAddress = new Uri(GroqBaseUrl), Timeout = GroqTimeout };
        var key = cfg["GROQ_API_KEY"];
        if (!string.IsNullOrWhiteSpace(key))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return http;
    }
}
