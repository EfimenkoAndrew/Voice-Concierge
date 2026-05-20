namespace VoiceConcierge.Api.AdminKey;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireAdminKeyAttribute : Attribute { }

public static class RequireAdminKeyExtensions
{
    public static TBuilder RequireAdminKey<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.WithMetadata(new RequireAdminKeyAttribute());
        return builder;
    }
}
