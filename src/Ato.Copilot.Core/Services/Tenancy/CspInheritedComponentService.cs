using System.Text.Json;
using Ato.Copilot.Core.Configuration.Tenancy;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ato.Copilot.Core.Services.Tenancy;

/// <summary>
/// Feature 048 T205 — lifecycle service for <see cref="CspInheritedComponent"/>
/// rows (FR-007 / FR-100 / FR-104 / FR-105).
/// </summary>
/// <remarks>
/// <para>
/// The endpoint surface (T207 / T208) gates mutations to <c>CSP.Admin</c>;
/// this service assumes that gating has already been applied and focuses on
/// the lifecycle invariants:
/// <list type="bullet">
///   <item><see cref="PublishAsync"/> transitions <c>Draft → Published</c>;
///         already-<c>Published</c> is idempotent; <c>Archived</c> rejects.</item>
///   <item><see cref="ArchiveAsync"/> transitions any non-<c>Archived</c>
///         state to <c>Archived</c>; idempotent.</item>
///   <item><see cref="RemapAsync"/> re-runs the AI mapper via
///         <see cref="ICspCapabilityMappingService"/>; existing
///         <see cref="MappedBy.User"/> rows are preserved when requested.</item>
///   <item><see cref="ReviewCapabilityAsync"/> requires
///         <see cref="CspInheritedCapabilityStatus.NeedsReview"/>; sets
///         <see cref="MappedBy.User"/> and emits an audit row with
///         <c>Action = "CspInheritedCapability.Review"</c>.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class CspInheritedComponentService : ICspInheritedComponentService
{
    private readonly IDbContextFactory<AtoCopilotContext> _contextFactory;
    private readonly ICspCapabilityMappingService _mappingService;
    private readonly IOptions<CspInheritedOptions> _options;
    private readonly ILogger<CspInheritedComponentService> _logger;

    public CspInheritedComponentService(
        IDbContextFactory<AtoCopilotContext> contextFactory,
        ICspCapabilityMappingService mappingService,
        IOptions<CspInheritedOptions> options,
        ILogger<CspInheritedComponentService> logger)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _mappingService = mappingService ?? throw new ArgumentNullException(nameof(mappingService));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<CspInheritedComponent?> GetAsync(Guid componentId, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var component = await db.CspInheritedComponents
            .Include(c => c.Capabilities)
            .FirstOrDefaultAsync(c => c.Id == componentId, ct)
            .ConfigureAwait(false);
        if (component is not null)
        {
            PopulateCounts(component);
        }
        return component;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CspInheritedComponent>> ListAsync(
        Guid cspProfileId,
        CspInheritedComponentStatus? status = null,
        CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var query = db.CspInheritedComponents
            .Include(c => c.Capabilities)
            .Where(c => c.CspProfileId == cspProfileId);
        if (status is { } s)
        {
            query = query.Where(c => c.Status == s);
        }
        var rows = await query
            .OrderBy(c => c.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        foreach (var row in rows)
        {
            PopulateCounts(row);
        }
        return rows;
    }

    /// <inheritdoc />
    public async Task<CspInheritedComponent> CreateAsync(
        Guid cspProfileId,
        string name,
        string description,
        CspComponentType componentType,
        string actor,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if (cspProfileId == Guid.Empty)
        {
            throw new ArgumentException(
                "cspProfileId must be a non-empty Guid.", nameof(cspProfileId));
        }

        var trimmedName = TruncateOrThrow(nameof(name), name, 256);
        var trimmedDesc = TruncateOrThrow(nameof(description), description, 2000);

        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        var component = new CspInheritedComponent
        {
            Id = Guid.NewGuid(),
            CspProfileId = cspProfileId,
            Name = trimmedName,
            Description = trimmedDesc,
            ComponentType = componentType,
            // Manual-create rows have no source artifact; provenance is the
            // CSP-Admin actor recorded on ImportedBy/UpdatedBy.
            SourceFormat = SourceFormat.Manual,
            SourceFileName = null,
            SourceArtifactReference = null,
            // Skip Draft — there is no extraction step to defer publishing
            // for, and the CSP-Admin is explicitly publishing by clicking
            // "Create component".
            Status = CspInheritedComponentStatus.Published,
            ImportedAt = now,
            ImportedBy = actor,
            UpdatedAt = now,
            UpdatedBy = actor,
        };
        db.CspInheritedComponents.Add(component);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Manually created CspInheritedComponent {ComponentId} ('{Name}') by {Actor}",
            component.Id, component.Name, actor);

        // PopulateCounts looks at component.Capabilities, which is empty
        // here — keep parity with the rest of the service surface so
        // callers can read the (zero) counts off the returned row.
        PopulateCounts(component);
        return component;
    }

    /// <inheritdoc />
    public async Task<CspInheritedCapability> AddCapabilityAsync(
        Guid componentId,
        string name,
        string description,
        IReadOnlyList<string> mappedNistControlIds,
        string actor,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ArgumentNullException.ThrowIfNull(mappedNistControlIds);

        var trimmedName = TruncateOrThrow(nameof(name), name, 256);
        var trimmedDesc = TruncateOrThrow(nameof(description), description, 2000);

        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var componentExists = await db.CspInheritedComponents
            .AnyAsync(c => c.Id == componentId, ct)
            .ConfigureAwait(false);
        if (!componentExists)
        {
            throw new KeyNotFoundException(
                $"CspInheritedComponent '{componentId}' not found.");
        }

        var now = DateTimeOffset.UtcNow;
        var capability = new CspInheritedCapability
        {
            Id = Guid.NewGuid(),
            CspInheritedComponentId = componentId,
            Name = trimmedName,
            Description = trimmedDesc,
            MappedNistControlIds = mappedNistControlIds.ToList(),
            // No AI involvement → no confidence score; mark the row as a
            // human-authored mapping so a future remap preserves it.
            MappingConfidence = null,
            Status = CspInheritedCapabilityStatus.Mapped,
            MappedBy = MappedBy.User,
            MappingFailureReason = null,
            CreatedAt = now,
            CreatedBy = actor,
            ReviewedAt = now,
            ReviewedBy = actor,
            ReviewerNote = null,
        };
        db.CspInheritedCapabilities.Add(capability);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Manually added CspInheritedCapability {CapabilityId} ('{Name}') to component {ComponentId} by {Actor}",
            capability.Id, capability.Name, componentId, actor);
        return capability;
    }

    /// <inheritdoc />
    public async Task<CspInheritedComponent> UpdateAsync(
        Guid componentId,
        string name,
        string description,
        CspComponentType componentType,
        byte[]? rowVersion,
        string actor,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var component = await db.CspInheritedComponents
            .FirstOrDefaultAsync(c => c.Id == componentId, ct)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"CspInheritedComponent '{componentId}' not found.");

        if (rowVersion is not null)
        {
            // Surface concurrency mismatches via DbUpdateConcurrencyException
            // mapped to 412 PRECONDITION_FAILED at the endpoint layer.
            db.Entry(component).Property(c => c.RowVersion).OriginalValue = rowVersion;
        }

        component.Name = TruncateOrThrow(nameof(name), name, 256);
        component.Description = TruncateOrThrow(nameof(description), description, 2000);
        component.ComponentType = componentType;
        component.UpdatedAt = DateTimeOffset.UtcNow;
        component.UpdatedBy = actor;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return component;
    }

    /// <inheritdoc />
    public async Task<CspInheritedComponent> PublishAsync(
        Guid componentId,
        string actor,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var component = await db.CspInheritedComponents
            .FirstOrDefaultAsync(c => c.Id == componentId, ct)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"CspInheritedComponent '{componentId}' not found.");

        switch (component.Status)
        {
            case CspInheritedComponentStatus.Draft:
                component.Status = CspInheritedComponentStatus.Published;
                component.UpdatedAt = DateTimeOffset.UtcNow;
                component.UpdatedBy = actor;
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                _logger.LogInformation(
                    "Published CspInheritedComponent {ComponentId} by {Actor}",
                    component.Id, actor);
                return component;

            case CspInheritedComponentStatus.Published:
                // Idempotent.
                return component;

            case CspInheritedComponentStatus.Archived:
                throw new InvalidOperationException(
                    $"CspInheritedComponent '{componentId}' is Archived; reactivate it before publishing.");

            default:
                throw new InvalidOperationException(
                    $"CspInheritedComponent '{componentId}' has unrecognized Status '{component.Status}'.");
        }
    }

    /// <inheritdoc />
    public async Task<CspInheritedComponent> ArchiveAsync(
        Guid componentId,
        string actor,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var component = await db.CspInheritedComponents
            .FirstOrDefaultAsync(c => c.Id == componentId, ct)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"CspInheritedComponent '{componentId}' not found.");

        if (component.Status == CspInheritedComponentStatus.Archived)
        {
            return component;
        }

        component.Status = CspInheritedComponentStatus.Archived;
        component.UpdatedAt = DateTimeOffset.UtcNow;
        component.UpdatedBy = actor;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _logger.LogInformation(
            "Archived CspInheritedComponent {ComponentId} by {Actor}",
            component.Id, actor);
        return component;
    }

    /// <inheritdoc />
    public async Task<CapabilityMappingResult> RemapAsync(
        Guid componentId,
        bool preserveHumanMappings,
        string actor,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var component = await db.CspInheritedComponents
            .Include(c => c.Capabilities)
            .FirstOrDefaultAsync(c => c.Id == componentId, ct)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"CspInheritedComponent '{componentId}' not found.");

        var threshold = _options.Value.MappingConfidenceThreshold;
        var result = await _mappingService.MapAsync(component, threshold, ct).ConfigureAwait(false);

        // Replace existing AI capabilities with the freshly-mapped ones.
        // Optionally preserve User-mapped rows (default: preserve so a manual
        // review survives a remap pass).
        var existingToRemove = preserveHumanMappings
            ? component.Capabilities.Where(c => c.MappedBy == MappedBy.AI).ToList()
            : component.Capabilities.ToList();
        db.CspInheritedCapabilities.RemoveRange(existingToRemove);

        foreach (var cap in result.Mapped.Concat(result.NeedsReview))
        {
            cap.CspInheritedComponentId = component.Id;
            cap.CreatedBy = actor;
            db.CspInheritedCapabilities.Add(cap);
        }

        component.UpdatedAt = DateTimeOffset.UtcNow;
        component.UpdatedBy = actor;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Remapped CspInheritedComponent {ComponentId}: removed {Removed}, "
            + "added {Mapped} mapped + {NeedsReview} needs-review (aiMappingAvailable={Available})",
            component.Id, existingToRemove.Count, result.Mapped.Count,
            result.NeedsReview.Count, result.AiMappingAvailable);

        return result;
    }

    /// <inheritdoc />
    public async Task<CspInheritedCapability> ReviewCapabilityAsync(
        Guid componentId,
        Guid capabilityId,
        IReadOnlyList<string> mappedControlIds,
        string? reviewerNote,
        string actor,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ArgumentNullException.ThrowIfNull(mappedControlIds);

        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var capability = await db.CspInheritedCapabilities
            .FirstOrDefaultAsync(c => c.Id == capabilityId, ct)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"CspInheritedCapability '{capabilityId}' not found.");

        if (capability.CspInheritedComponentId != componentId)
        {
            throw new KeyNotFoundException(
                $"CspInheritedCapability '{capabilityId}' does not belong to CspInheritedComponent '{componentId}'.");
        }

        if (capability.Status != CspInheritedCapabilityStatus.NeedsReview)
        {
            throw new InvalidOperationException(
                $"CspInheritedCapability '{capabilityId}' has Status='{capability.Status}'; "
                + "only NeedsReview rows can be reviewed.");
        }

        capability.MappedNistControlIds = mappedControlIds.ToList();
        capability.ReviewerNote = reviewerNote;
        capability.MappedBy = MappedBy.User;
        capability.ReviewedAt = DateTimeOffset.UtcNow;
        capability.ReviewedBy = actor;
        capability.Status = CspInheritedCapabilityStatus.Mapped;
        capability.MappingFailureReason = null;

        // Audit row — T192 asserts presence of "CspInheritedCapability.Review"
        // with the capability id in payload AND the reviewed control ids in
        // AuditLogEntry.AffectedControls (FR-105). The interceptor stamps
        // TenantId.
        db.AuditLogs.Add(new AuditLogEntry
        {
            Id = Guid.NewGuid().ToString(),
            UserId = actor,
            UserRole = "CSP.Admin",
            Action = "CspInheritedCapability.Review",
            Outcome = AuditOutcome.Success,
            Timestamp = DateTime.UtcNow,
            AffectedControls = capability.MappedNistControlIds.ToList(),
            Details = JsonSerializer.Serialize(new
            {
                componentId,
                capabilityId,
                mappedControlIds = capability.MappedNistControlIds,
                reviewerNote,
            }),
        });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _logger.LogInformation(
            "Reviewed CspInheritedCapability {CapabilityId} on component {ComponentId} by {Actor}",
            capability.Id, componentId, actor);
        return capability;
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private static void PopulateCounts(CspInheritedComponent component)
    {
        component.CapabilityMappedCount = component.Capabilities
            .Count(c => c.Status == CspInheritedCapabilityStatus.Mapped);
        component.CapabilityNeedsReviewCount = component.Capabilities
            .Count(c => c.Status == CspInheritedCapabilityStatus.NeedsReview);
    }

    private static string TruncateOrThrow(string parameterName, string value, int maxLength)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException(
                $"'{parameterName}' must be non-empty.", parameterName);
        }
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }
}
