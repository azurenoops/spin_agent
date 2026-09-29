using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Poam;

[TenantScoped]
public sealed class PoamTaskLink
{
    public Guid TenantId { get; set; }
    public string RegisteredSystemId { get; set; } = "";
    public string PoamItemId { get; set; } = "";
    public string RemediationTaskId { get; set; } = "";
    public string LinkedBy { get; set; } = "";
    public DateTime LinkedAt { get; set; } = DateTime.UtcNow;
}
