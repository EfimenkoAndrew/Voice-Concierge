using System.Security.Cryptography;

namespace VoiceConcierge.Contracts;

public static class ModelIntegrity
{
    private const int StreamBufferBytes = 81_920;

    public static void Verify(byte[] content, string? expectedSha256, string name,
        Action<string>? warn = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (content.Length == 0)
            throw new ArgumentException("content is empty — refusing hash verification", nameof(content));
        if (string.IsNullOrWhiteSpace(expectedSha256))
        {
            warn?.Invoke($"{name}: no SHA-256 configured — accepting on TLS + pinned URL only");
            return;
        }
        var actual = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        if (!HashesMatch(actual, expectedSha256))
            throw new InvalidOperationException(
                $"{name} model integrity check failed: expected {expectedSha256}, got {actual}");
    }

    public static async Task VerifyStreamAsync(Stream content, string? expectedSha256, string name,
        Action<string>? warn = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (string.IsNullOrWhiteSpace(expectedSha256))
        {
            warn?.Invoke($"{name}: no SHA-256 configured — accepting on TLS + pinned URL only");
            return;
        }
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buf = new byte[StreamBufferBytes];
        int read;
        while ((read = await content.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
            sha.AppendData(buf, 0, read);
        var actual = Convert.ToHexString(sha.GetCurrentHash()).ToLowerInvariant();
        if (!HashesMatch(actual, expectedSha256))
            throw new InvalidOperationException(
                $"{name} model integrity check failed: expected {expectedSha256}, got {actual}");
    }

    private static bool HashesMatch(string actual, string expected) =>
        string.Equals(actual, expected.Trim().ToLowerInvariant(), StringComparison.Ordinal);
}
