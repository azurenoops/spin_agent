using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Services;

/// <summary>
/// Service for managing system-to-capability links.
/// </summary>
public class SystemCapabilityLinkService
{
    private readonly AtoCopilotContext _db;
    private readonly ILogger<SystemCapabilityLinkService> _logger;

    public SystemCapabilityLinkService(AtoCopilotContext db, ILogger<SystemCapabilityLinkService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Links one or more capabilities to a system, skipping duplicates.
    /// </summary>
    public async Task<(int LinkedCount, List<SystemCapabilityLink> Items)> LinkCapabilitiesAsync(
        string systemId,
        IReadOnlyList<string> capabilityIds,
        string user,
        CancellationToken ct = default)
    {
        var system = await _db.RegisteredSystems.FindAsync(new object[] { systemId }, ct);
        if (system is null)
            throw new KeyNotFoundException($"System {systemId} not found");

        // Validate all capability IDs exist
        var validCapIds = await _db.SecurityCapabilities
            .Where(c => capabilityIds.Contains(c.Id))
            .Select(c => c.Id)
            .AsNoTracking()
            .ToListAsync(ct);

        var invalidIds = capabilityIds.Except(validCapIds).ToList();
        if (invalidIds.Count > 0)
            throw new ArgumentException($"Invalid capability IDs: {string.Join(", ", invalidIds)}");

        // Get existing links to skip duplicates
        var existingLinks = await _db.SystemCapabilityLinks
            .Where(l => l.RegisteredSystemId == systemId && capabilityIds.Contains(l.SecurityCapabilityId))
            .Select(l => l.SecurityCapabilityId)
            .AsNoTracking()
            .ToListAsync(ct);

        var newCapIds = capabilityIds.Except(existingLinks).ToList();
        var links = new List<SystemCapabilityLink>();

        foreach (var capId in newCapIds)
        {
            var link = new SystemCapabilityLink
            {
                RegisteredSystemId = systemId,
                SecurityCapabilityId = capId,
                LinkedBy = user,
            };
            _db.SystemCapabilityLinks.Add(link);
            links.Add(link);
        }

        if (links.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "Linked {Count} capabilities to system {SystemId} by {User}",
                links.Count, systemId, user);

            // Auto-create ControlImplementation stubs for org-wide mappings
            // so the Capabilities Coverage page shows correct control counts.
            var orgMappings = await _db.CapabilityControlMappings
                .Where(m => newCapIds.Contains(m.SecurityCapabilityId) && m.RegisteredSystemId == null)
                .Select(m => new { m.SecurityCapabilityId, m.ControlId })
                .ToListAsync(ct);

            if (orgMappings.Count > 0)
            {
                var existingControlIds = await _db.ControlImplementations
                    .Where(ci => ci.RegisteredSystemId == systemId)
                    .Select(ci => ci.ControlId)
                    .ToListAsync(ct);
                var existingSet = new HashSet<string>(existingControlIds, StringComparer.OrdinalIgnoreCase);

                var created = 0;
                foreach (var mapping in orgMappings)
                {
                    if (existingSet.Contains(mapping.ControlId)) continue;

                    _db.ControlImplementations.Add(new ControlImplementation
                    {
                        RegisteredSystemId = systemId,
                        ControlId = mapping.ControlId.ToUpperInvariant(),
                        SecurityCapabilityId = mapping.SecurityCapabilityId,
                        ImplementationStatus = ImplementationStatus.Planned,
                        ApprovalStatus = SspSectionStatus.Draft,
                        IsAutoPopulated = true,
                        AuthoredBy = user,
                        CurrentVersion = 1,
                    });
                    existingSet.Add(mapping.ControlId);
                    created++;
                }

                if (created > 0)
                {
                    await _db.SaveChangesAsync(ct);
                    _logger.LogInformation(
                        "Auto-created {Count} control implementation stubs for system {SystemId}",
                        created, systemId);
                }
            }
        }

        // Load capability names for the response
        var allLinks = await _db.SystemCapabilityLinks
            .Include(l => l.SecurityCapability)
            .Where(l => l.RegisteredSystemId == systemId && capabilityIds.Contains(l.SecurityCapabilityId))
            .AsNoTracking()
            .ToListAsync(ct);

        return (links.Count, allLinks);
    }

    /// <summary>
    /// Returns all capability links for a system with capability details.
    /// </summary>
    public async Task<List<SystemCapabilityLink>> GetLinksForSystemAsync(
        string systemId,
        CancellationToken ct = default)
    {
        return await _db.SystemCapabilityLinks
            .Include(l => l.SecurityCapability)
            .Where(l => l.RegisteredSystemId == systemId)
            .OrderBy(l => l.LinkedAt)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    /// <summary>
    /// Removes a specific capability link.
    /// </summary>
    public async Task<bool> RemoveLinkAsync(
        string systemId,
        string linkId,
        CancellationToken ct = default)
    {
        var link = await _db.SystemCapabilityLinks
            .FirstOrDefaultAsync(l => l.Id == linkId && l.RegisteredSystemId == systemId, ct);

        if (link is null)
        {
            _logger.LogWarning("Capability link {LinkId} not found for system {SystemId}", linkId, systemId);
            return false;
        }

        _db.SystemCapabilityLinks.Remove(link);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Removed capability link {LinkId} from system {SystemId}",
            linkId, systemId);

        return true;
    }
}
