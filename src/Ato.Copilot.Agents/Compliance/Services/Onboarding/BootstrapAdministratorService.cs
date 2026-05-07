using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Onboarding;

namespace Ato.Copilot.Agents.Compliance.Services.Onboarding;

/// <summary>
/// Atomic first-user-becomes-Administrator implementation (research §R10).
/// Uses a per-tenant <see cref="SemaphoreSlim"/> to serialize the bootstrap window;
/// the second concurrent caller receives <c>WIZARD_BOOTSTRAP_RACE</c> and is told
/// to retry (the first caller will already have grants when the retry arrives).
/// </summary>
public class BootstrapAdministratorService : IBootstrapAdministratorService
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> _tenantLocks = new();

    private readonly IDbContextFactory<AtoCopilotContext> _contextFactory;
    private readonly IWizardAuditService _audit;
    private readonly ILogger<BootstrapAdministratorService> _logger;

    public BootstrapAdministratorService(
        IDbContextFactory<AtoCopilotContext> contextFactory,
        IWizardAuditService audit,
        ILogger<BootstrapAdministratorService> logger)
    {
        _contextFactory = contextFactory;
        _audit = audit;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<BootstrapAdministratorResult> GrantAsync(
        Guid tenantId,
        Guid subjectUserId,
        string? subjectDisplayName,
        string? subjectEmail,
        Guid correlationId,
        CancellationToken ct = default)
    {
        var gate = _tenantLocks.GetOrAdd(tenantId, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(TimeSpan.FromSeconds(2), ct))
        {
            _logger.LogWarning(
                "Bootstrap admin lock contended for tenant {TenantId}",
                tenantId);
            return new BootstrapAdministratorResult(
                Granted: false,
                AssignmentId: null,
                ErrorCode: WizardErrorCodes.BootstrapRace,
                Message: "Another administrator setup is already in progress. Please retry.");
        }

        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(ct);

            var existingAdmin = await db.OrganizationRoleAssignments
                .FirstOrDefaultAsync(a => a.TenantId == tenantId
                                       && a.Role == OrganizationRole.Administrator
                                       && a.RemovedAt == null, ct);
            if (existingAdmin is not null)
            {
                _logger.LogDebug(
                    "Bootstrap admin grant skipped — tenant {TenantId} already has administrator",
                    tenantId);
                return new BootstrapAdministratorResult(
                    Granted: false,
                    AssignmentId: existingAdmin.Id,
                    ErrorCode: WizardErrorCodes.BootstrapRace,
                    Message: "Administrator already exists for this tenant.");
            }

            var person = await db.Persons
                .FirstOrDefaultAsync(
                    p => p.TenantId == tenantId
                         && (p.Id == subjectUserId || p.EntraObjectId == subjectUserId),
                    ct);

            if (person is null)
            {
                person = new Person
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    EntraObjectId = subjectUserId,
                    DisplayName = subjectDisplayName ?? "Bootstrap Administrator",
                    Email = subjectEmail,
                };
                db.Persons.Add(person);
            }

            var assignment = new OrganizationRoleAssignment
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PersonId = person.Id,
                Role = OrganizationRole.Administrator,
                IsPrimary = true,
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = person.Id,
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedBy = person.Id,
            };
            db.OrganizationRoleAssignments.Add(assignment);

            await db.SaveChangesAsync(ct);

            await _audit.RecordAsync(
                tenantId,
                actorUserId: person.Id,
                action: WizardAuditAction.WizardBootstrapAdminGranted,
                resourceType: nameof(OrganizationRoleAssignment),
                resourceId: assignment.Id,
                beforeJson: null,
                afterJson: System.Text.Json.JsonSerializer.Serialize(new
                {
                    assignment.PersonId,
                    Role = assignment.Role.ToString(),
                    assignment.CreatedAt,
                }),
                effectsJson: null,
                correlationId: correlationId,
                ct: ct);

            _logger.LogInformation(
                "Bootstrap admin granted to {SubjectUserId} for tenant {TenantId}",
                subjectUserId, tenantId);

            return new BootstrapAdministratorResult(
                Granted: true,
                AssignmentId: assignment.Id,
                ErrorCode: null,
                Message: null);
        }
        finally
        {
            gate.Release();
        }
    }
}
