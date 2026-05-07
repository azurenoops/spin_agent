using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Agents.Compliance.Services.Onboarding.Auditing;

/// <summary>
/// Two-track audit writer (Serilog + persisted <see cref="WizardAuditEntry"/>) used by
/// every mutating wizard action (FR-097, research §R12). The Serilog stream powers
/// real-time alerting and the persisted rows power admin-facing audit views.
/// </summary>
public class WizardAuditService : IWizardAuditService
{
    private readonly IDbContextFactory<AtoCopilotContext> _contextFactory;
    private readonly ILogger<WizardAuditService> _logger;

    public WizardAuditService(
        IDbContextFactory<AtoCopilotContext> contextFactory,
        ILogger<WizardAuditService> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task RecordAsync(
        Guid tenantId,
        Guid actorUserId,
        WizardAuditAction action,
        string resourceType,
        Guid? resourceId,
        string? beforeJson,
        string? afterJson,
        string? effectsJson,
        Guid correlationId,
        CancellationToken ct = default)
    {
        var entry = new WizardAuditEntry
        {
            TenantId = tenantId,
            Timestamp = DateTimeOffset.UtcNow,
            ActorUserId = actorUserId,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            BeforeJson = beforeJson,
            AfterJson = afterJson,
            EffectsJson = effectsJson,
            CorrelationId = correlationId,
        };

        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        db.WizardAuditEntries.Add(entry);
        await db.SaveChangesAsync(ct);

        // Track 2 — Serilog with WizardAudit enricher; downstream sinks can route by source-context.
        using (_logger.BeginScope(new Dictionary<string, object?>
        {
            ["WizardAudit"] = true,
            ["TenantId"] = tenantId,
            ["ActorUserId"] = actorUserId,
            ["Action"] = action.ToString(),
            ["ResourceType"] = resourceType,
            ["ResourceId"] = resourceId,
            ["CorrelationId"] = correlationId,
        }))
        {
            _logger.LogInformation(
                "wizard.audit {Action} on {ResourceType} {ResourceId}",
                action, resourceType, resourceId);
        }
    }
}
