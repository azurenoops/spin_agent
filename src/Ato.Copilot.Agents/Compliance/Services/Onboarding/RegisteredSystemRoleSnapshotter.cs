using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Agents.Compliance.Services.Onboarding;

/// <summary>
/// EF-backed implementation of <see cref="IRegisteredSystemRoleSnapshotter"/>.
/// Copies <see cref="OrganizationRoleAssignment"/> rows into
/// <see cref="SystemRoleAssignment"/> rows when a system is registered (FR-024).
/// </summary>
public class RegisteredSystemRoleSnapshotter : IRegisteredSystemRoleSnapshotter
{
    private readonly IDbContextFactory<AtoCopilotContext> _contextFactory;
    private readonly ILogger<RegisteredSystemRoleSnapshotter> _logger;

    public RegisteredSystemRoleSnapshotter(
        IDbContextFactory<AtoCopilotContext> contextFactory,
        ILogger<RegisteredSystemRoleSnapshotter> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> SnapshotAsync(
        Guid tenantId,
        string registeredSystemId,
        Guid actorUserId,
        Guid correlationId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(registeredSystemId))
        {
            throw new ArgumentException("RegisteredSystemId is required.", nameof(registeredSystemId));
        }

        await using var db = await _contextFactory.CreateDbContextAsync(ct);

        // Idempotency — if any inherited rows already exist for this system, skip.
        var alreadySnapshotted = await db.SystemRoleAssignments
            .AnyAsync(s => s.RegisteredSystemId == registeredSystemId &&
                           s.IsInherited &&
                           s.RemovedAt == null, ct);
        if (alreadySnapshotted)
        {
            _logger.LogDebug(
                "System {SystemId} already has inherited role snapshots — skipping.",
                registeredSystemId);
            return 0;
        }

        var orgAssignments = await db.OrganizationRoleAssignments
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.RemovedAt == null)
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        foreach (var src in orgAssignments)
        {
            db.SystemRoleAssignments.Add(new SystemRoleAssignment
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                RegisteredSystemId = registeredSystemId,
                Role = src.Role,
                PersonId = src.PersonId,
                IsInherited = true,
                SourceOrganizationRoleAssignmentId = src.Id,
                CreatedAt = now,
                CreatedBy = actorUserId,
                UpdatedAt = now,
                UpdatedBy = actorUserId,
            });
        }
        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Snapshotted {Count} org-level role assignments to system {SystemId} (tenant {TenantId}, correlation {CorrelationId}).",
            orgAssignments.Count, registeredSystemId, tenantId, correlationId);

        return orgAssignments.Count;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SystemRoleAssignment>> ListEffectiveAsync(
        string registeredSystemId, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        return await db.SystemRoleAssignments
            .AsNoTracking()
            .Where(s => s.RegisteredSystemId == registeredSystemId && s.RemovedAt == null)
            .OrderBy(s => s.Role)
            .ThenBy(s => s.IsInherited)
            .ToListAsync(ct);
    }
}
