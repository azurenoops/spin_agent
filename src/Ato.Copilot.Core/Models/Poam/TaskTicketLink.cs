using System.ComponentModel.DataAnnotations;
using Ato.Copilot.Core.Models.Kanban;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Poam;

[TenantScoped]
public sealed class TaskTicketLink : ConcurrentEntity
{
    [MaxLength(36)] public string Id { get; set; } = Guid.NewGuid().ToString();
    public Guid TenantId { get; set; }
    [MaxLength(36)] public string RegisteredSystemId { get; set; } = "";
    [MaxLength(36)] public string TaskId { get; set; } = "";
    [MaxLength(36)] public string TicketingIntegrationId { get; set; } = "";
    public TicketingProvider Provider { get; set; }
    [MaxLength(500)] public string BaseUrl { get; set; } = "";
    [MaxLength(200)] public string ProjectKey { get; set; } = "";
    [MaxLength(100)] public string CorrelationKey { get; set; } = "";
    [MaxLength(200)] public string? ExternalRef { get; set; }
    [MaxLength(100)] public string? ExternalStatus { get; set; }
    [MaxLength(200)] public string? ExternalAssignee { get; set; }
    [MaxLength(30)] public string State { get; set; } = "Pending";
    [MaxLength(1000)] public string? LastError { get; set; }
    public bool CreateAttempted { get; set; }
    public DateTime? LastSuccessfulSyncAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

[TenantScoped]
public sealed class TaskTicketAudit
{
    [MaxLength(36)] public string Id { get; set; } = Guid.NewGuid().ToString();
    public Guid TenantId { get; set; }
    [MaxLength(36)] public string TaskTicketLinkId { get; set; } = "";
    [MaxLength(36)] public string ActorId { get; set; } = "";
    [MaxLength(30)] public string Action { get; set; } = "";
    [MaxLength(200)] public string? ExternalRef { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
