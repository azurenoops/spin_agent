using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Workspaces;

[ProviderScoped]
public sealed class OrganizationOnboardingDraft
{
    public Guid Id { get; set; }
    public Guid ProviderId { get; set; }
    public string CreatedBy { get; set; } = "";
    public string UpdatedBy { get; set; } = "";
    public long Revision { get; set; } = 1;
    public int SchemaVersion { get; set; } = 1;
    public string ValuesJson { get; set; } = "{}";
    public string CurrentStep { get; set; } = "details";
    public string State { get; set; } = "Draft";
    public string CreationKey { get; set; } = Guid.NewGuid().ToString("D");
    public string? ProvisioningKey { get; set; }
    public string? ConfirmedIntentHash { get; set; }
    public long? ConfirmedRevision { get; set; }
    public Guid? TenantId { get; set; }
    public Guid? OperationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public long UpdatedAtTicks { get; set; } = DateTimeOffset.UtcNow.UtcTicks;
}
