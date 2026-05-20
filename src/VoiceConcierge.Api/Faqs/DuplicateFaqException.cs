namespace VoiceConcierge.Api.Faqs;

public sealed class DuplicateFaqException(Guid existingId)
    : InvalidOperationException($"A FAQ with the same normalized question already exists (id={existingId})")
{
    public Guid ExistingId { get; } = existingId;
}
