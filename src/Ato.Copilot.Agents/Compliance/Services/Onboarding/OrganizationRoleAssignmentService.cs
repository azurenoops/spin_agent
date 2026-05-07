using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Onboarding;

namespace Ato.Copilot.Agents.Compliance.Services.Onboarding;

/// <summary>
/// EF-backed organization-level role assignment service (FR-020..FR-026 / FR-002).
/// </summary>
public class OrganizationRoleAssignmentService : IOrganizationRoleAssignmentService
{
    private readonly IDbContextFactory<AtoCopilotContext> _contextFactory;
    private readonly IWizardAuditService _audit;
    private readonly ILogger<OrganizationRoleAssignmentService> _logger;

    public OrganizationRoleAssignmentService(
        IDbContextFactory<AtoCopilotContext> contextFactory,
        IWizardAuditService audit,
        ILogger<OrganizationRoleAssignmentService> logger)
    {
        _contextFactory = contextFactory;
        _audit = audit;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OrganizationRoleAssignment>> ListAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        return await db.OrganizationRoleAssignments
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.RemovedAt == null)
            .OrderBy(r => r.Role)
            .ThenBy(r => r.CreatedAt)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<RoleAssignmentResult> AddAsync(
        Guid tenantId,
        OrganizationRole role,
        Guid personId,
        Guid actorUserId,
        Guid correlationId,
        CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);

        var person = await db.Persons
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == personId && p.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException(
                $"Person {personId} not found for tenant {tenantId}.");

        var activeForRole = await db.OrganizationRoleAssignments
            .Where(r => r.TenantId == tenantId && r.Role == role && r.RemovedAt == null)
            .ToListAsync(ct);

        // Reject duplicate person-role pairings.
        if (activeForRole.Any(r => r.PersonId == personId))
        {
            throw new InvalidOperationException(
                $"Person {personId} is already assigned the {role} role for this tenant.");
        }

        var warnings = new List<string>();
        var isPrimary = activeForRole.Count == 0;

        // Singleton-warn semantics for ISSM and Administrator (FR-023).
        if ((role == OrganizationRole.Issm || role == OrganizationRole.Administrator)
            && activeForRole.Count > 0)
        {
            warnings.Add(
                $"Multiple {role} holders are unusual — confirm this is the intended structure.");
        }

        var now = DateTimeOffset.UtcNow;
        var assignment = new OrganizationRoleAssignment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Role = role,
            PersonId = personId,
            IsPrimary = isPrimary,
            CreatedAt = now,
            CreatedBy = actorUserId,
            UpdatedAt = now,
            UpdatedBy = actorUserId,
        };
        db.OrganizationRoleAssignments.Add(assignment);
        await db.SaveChangesAsync(ct);

        await _audit.RecordAsync(
            tenantId, actorUserId, WizardAuditAction.RoleAssigned,
            nameof(OrganizationRoleAssignment), assignment.Id,
            beforeJson: null,
            afterJson: JsonSerializer.Serialize(Project(assignment, person)),
            effectsJson: warnings.Count == 0 ? null : JsonSerializer.Serialize(new { warnings }),
            correlationId: correlationId,
            ct: ct);

        return new RoleAssignmentResult(assignment, warnings);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(
        Guid tenantId,
        Guid assignmentId,
        Guid actorUserId,
        Guid correlationId,
        CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var assignment = await db.OrganizationRoleAssignments
            .FirstOrDefaultAsync(r => r.Id == assignmentId && r.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException(
                $"Role assignment {assignmentId} not found for tenant {tenantId}.");
        if (assignment.RemovedAt != null)
        {
            return;
        }

        // Last-Administrator invariant (FR-002).
        if (assignment.Role == OrganizationRole.Administrator)
        {
            var remainingAdmins = await db.OrganizationRoleAssignments
                .CountAsync(r => r.TenantId == tenantId &&
                                 r.Role == OrganizationRole.Administrator &&
                                 r.RemovedAt == null &&
                                 r.Id != assignmentId, ct);
            if (remainingAdmins == 0)
            {
                throw new InvalidOperationException(WizardErrorCodes.LastAdminProtected);
            }
        }

        var beforeJson = JsonSerializer.Serialize(Project(assignment, null));
        assignment.RemovedAt = DateTimeOffset.UtcNow;
        assignment.UpdatedAt = DateTimeOffset.UtcNow;
        assignment.UpdatedBy = actorUserId;
        await db.SaveChangesAsync(ct);

        await _audit.RecordAsync(
            tenantId, actorUserId, WizardAuditAction.RoleRemoved,
            nameof(OrganizationRoleAssignment), assignment.Id,
            beforeJson: beforeJson,
            afterJson: null,
            effectsJson: null,
            correlationId: correlationId,
            ct: ct);
    }

    private static object Project(OrganizationRoleAssignment a, Person? person) => new
    {
        a.Id,
        Role = a.Role.ToString(),
        a.PersonId,
        a.IsPrimary,
        person?.DisplayName,
        person?.Email,
    };
}
