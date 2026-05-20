namespace VoiceConcierge.Api.AdminKey;

public static class AdminKeyConfiguration
{
    private const int MinAdminKeyLength = 16;

    public static IServiceCollection AddAdminKey(this IServiceCollection services, IConfiguration cfg)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(cfg);

        var adminKey = cfg["ADMIN_API_KEY"];
        var adminKeyPrev = cfg["ADMIN_API_KEY_PREV"];
        if (adminKey is { Length: > 0 } && adminKey.Length < MinAdminKeyLength)
            throw new InvalidOperationException(
                $"ADMIN_API_KEY is too short - set >={MinAdminKeyLength} characters (or unset to disable the gate).");
        if (adminKeyPrev is { Length: > 0 } && adminKeyPrev.Length < MinAdminKeyLength)
            throw new InvalidOperationException(
                $"ADMIN_API_KEY_PREV is too short - set >={MinAdminKeyLength} characters (or unset).");

        services.AddSingleton(new AdminKeyOptions { Primary = adminKey, Previous = adminKeyPrev });
        return services;
    }
}
