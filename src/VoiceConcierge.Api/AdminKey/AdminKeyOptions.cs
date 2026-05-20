namespace VoiceConcierge.Api.AdminKey;

public sealed class AdminKeyOptions
{
    public string? Primary { get; set; }
    public string? Previous { get; set; }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(Primary);
}
