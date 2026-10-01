namespace Ato.Copilot.Core.Configuration;

public sealed class ProviderAdministrationOptions
{
    public const string SectionName = "ProviderAdministration";

    public bool AllowLegacyCustomerContent { get; init; }

    public DateTimeOffset? LegacyCustomerContentExpiresAt { get; init; }
}
