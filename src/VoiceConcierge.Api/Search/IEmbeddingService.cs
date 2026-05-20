namespace VoiceConcierge.Api.Search;

public interface IEmbeddingService
{
    int Dimension { get; }
    bool Ready { get; }
    float[] Embed(string text);
}
