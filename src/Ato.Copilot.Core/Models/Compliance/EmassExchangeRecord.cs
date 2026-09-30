using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Compliance;

[TenantScoped]
public sealed class EmassExchangeRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public Guid TenantId { get; set; }
    public string RegisteredSystemId { get; set; } = "";
    public long Version { get; set; }
    public string PackageId { get; set; } = "";
    public string PackageHash { get; set; } = "";
    public DateTimeOffset ExportGeneratedAt { get; set; }
    public string Outcome { get; set; } = "";
    public string ReceivingWorkflow { get; set; } = "";
    public string ExternalReference { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public string RecordedBy { get; set; } = "";
    public string Notes { get; set; } = "";
    public string? SupersedesId { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public string RequestHash { get; set; } = "";
}
