using VoiceConcierge.Api.Data;
using VoiceConcierge.Api.Search;

namespace VoiceConcierge.Api.Tests;

public sealed class FakeEmbedder : IEmbeddingService
{
    public int Dimension => Embeddings.Dimension;
    public bool Ready => true;
    public float[] Embed(string text)
    {
        var v = new float[Embeddings.Dimension];
        v[Math.Abs(text.GetHashCode(StringComparison.Ordinal)) % v.Length] = 1f;
        return v;
    }
}
