namespace Ato.Copilot.Core.Configuration;

public sealed class PlatformOperationsOptions
{
    public const string SectionName = "PlatformOperations";

    public HashSet<Guid> AuthorizedObjectIds { get; init; } = [];

    public bool AllowLegacyCspAdminMigration { get; init; }

    public DateTimeOffset? LegacyCspAdminMigrationExpiresAt { get; init; }
}
