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
    private readonly ICapabilityHistoryService _history;
    private readonly ITenantContext _tenantContext;

    public CspInheritedComponentService(
        IDbContextFactory<AtoCopilotContext> contextFactory,
        ICspCapabilityMappingService mappingService,
        IOptions<CspInheritedOptions> options,
        ILogger<CspInheritedComponentService> logger,
        ICapabilityHistoryService history,
        ITenantContext tenantContext)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _mappingService = mappingService ?? throw new ArgumentNullException(nameof(mappingService));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
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
        bool markMappedImmediately = false,
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

        // Feature 050 FR-001: default behavior persists the row as NeedsReview
        // so even self-mapped capabilities pass through a vetting step. The
        // optional `markMappedImmediately` flag opts back into auto-mapped-on-
        // create, which writes a second `Reviewed` history row in the same
        // transaction (contracts/internal-services.md § 2.1.3).
        var tenantId = _tenantContext.EffectiveTenantId;
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
            Status = CspInheritedCapabilityStatus.NeedsReview,
            MappedBy = MappedBy.User,
            MappingFailureReason = null,
            CreatedAt = now,
            CreatedBy = actor,
            ReviewedAt = null,
            ReviewedBy = null,
            ReviewerNote = null,
        };

        if (markMappedImmediately)
        {
            capability.Status = CspInheritedCapabilityStatus.Mapped;
            capability.ReviewedAt = now;
            capability.ReviewedBy = actor;
            capability.ReviewerNote = "Mapped on create by creator.";
        }

        // Begin a transaction so the capability INSERT and the 1–2 history
        // INSERTs commit atomically. SQLite InMemory provider ignores
        // transactions; SqlServer / file-backed SQLite honors them per FR-004.
        var supportsTransactions = db.Database.ProviderName?.Contains("InMemory", StringComparison.OrdinalIgnoreCase) != true;
        await using var tx = supportsTransactions
            ? await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false)
            : null;

        await db.CspInheritedCapabilities.AddAsync(capability, ct).ConfigureAwait(false);

        // History row #1: Created. Metadata only when the override was used.
        await _history.AppendAsync(
            db, capability.Id, tenantId,
            CapabilityHistoryEventType.Created,
            actorOid: actor,
            summary: "Capability manually created.",
            metadata: markMappedImmediately
                ? new { markedMappedImmediately = true }
                : null,
            ct).ConfigureAwait(false);

        // History row #2: Reviewed — only when the creator opted to skip
        // review at creation time (FR-001 override path).
        if (markMappedImmediately)
        {
            await _history.AppendAsync(
                db, capability.Id, tenantId,
                CapabilityHistoryEventType.Reviewed,
                actorOid: actor,
                summary: "Reviewed and approved at creation time.",
                metadata: new { reviewerNote = "Mapped on create by creator." },
                ct).ConfigureAwait(false);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (tx is not null)
        {
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Manually added CspInheritedCapability {CapabilityId} ('{Name}') to component {ComponentId} by {Actor}; markMappedImmediately={Override}",
            capability.Id, capability.Name, componentId, actor, markMappedImmediately);
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

        // Feature 050 / US3 (R11) — generate one correlator GUID per run so
        // an auditor can group all events emitted by this Remap call.
        var remapRunId = Guid.NewGuid();
        var tenantId = _tenantContext.EffectiveTenantId;

        // Index existing AI rows by normalized name so we can classify
        // incoming pipeline output as created / edited / unchanged / removed.
        // Lowercase + trim matches the natural-key semantics used by the AI
        // pipeline (names are the only stable identity across remaps; ids
        // are freshly minted each run by the mapper).
        var existingAiByName = component.Capabilities
            .Where(c => c.MappedBy == MappedBy.AI)
            .GroupBy(c => NormalizeName(c.Name))
            .ToDictionary(g => g.Key, g => g.First());

        // Track which existing names were matched by the new output so we
        // can soft-archive the unmatched ones afterwards.
        var matchedNames = new HashSet<string>();

        foreach (var incoming in result.Mapped.Concat(result.NeedsReview))
        {
            var key = NormalizeName(incoming.Name);
            if (existingAiByName.TryGetValue(key, out var existing))
            {
                // Same logical capability — either unchanged or edited.
                matchedNames.Add(key);

                if (RowsAreEquivalent(existing, incoming))
                {
                    // No-op: no DB write, no audit row (R11 — preserved AI
                    // row identical to AI re-output writes ZERO events).
                    continue;
                }

                // Edited — update in place, preserve identity.
                existing.Description = incoming.Description;
                existing.MappedNistControlIds = incoming.MappedNistControlIds.ToList();
                existing.MappingConfidence = incoming.MappingConfidence;
                existing.Status = incoming.Status;
                existing.MappingFailureReason = incoming.MappingFailureReason;

                await _history.AppendAsync(
                    db, existing.Id, tenantId,
                    CapabilityHistoryEventType.Edited,
                    actorOid: actor,
                    summary: "Capability edited.",
                    metadata: new { remapRunId, source = "Remap" },
                    ct).ConfigureAwait(false);
            }
            else
            {
                // Created — fresh AI row.
                incoming.CspInheritedComponentId = component.Id;
                incoming.CreatedBy = actor;
                db.CspInheritedCapabilities.Add(incoming);

                await _history.AppendAsync(
                    db, incoming.Id, tenantId,
                    CapabilityHistoryEventType.Created,
                    actorOid: actor,
                    summary: "Capability created.",
                    metadata: new { remapRunId, source = "Remap" },
                    ct).ConfigureAwait(false);
            }
        }

        // Soft-archive any unmatched existing AI rows. R11 says "set Status =
        // Archived" rather than hard-delete so the history rows stay
        // referentially anchored to a row the auditor can still look up.
        // (Hard-delete is still safe per data-model.md FR-015 — history
        // outlives capability — but soft-archive gives a better UX.)
        foreach (var orphan in existingAiByName
            .Where(kvp => !matchedNames.Contains(kvp.Key))
            .Select(kvp => kvp.Value))
        {
            if (orphan.Status == CspInheritedCapabilityStatus.Archived) continue;

            orphan.Status = CspInheritedCapabilityStatus.Archived;
            orphan.MappingFailureReason = "Removed by Remap.";

            await _history.AppendAsync(
                db, orphan.Id, tenantId,
                CapabilityHistoryEventType.Archived,
                actorOid: actor,
                summary: "Capability archived.",
                metadata: new { remapRunId, source = "Remap" },
                ct).ConfigureAwait(false);
        }

        // User-mapped rows: when `preserveHumanMappings = false`, the legacy
        // contract removed them. We preserve that behavior — but since R11
        // says "no history row for preserved User row", removed-via-flag
        // User rows ALSO write no history row (the operator's intent was
        // already audited at the component-level Remap action). This
        // matches the spec's "preserved" wording: from the audit-trail
        // viewpoint the User row is invisible to Remap regardless.
        if (!preserveHumanMappings)
        {
            var userRows = component.Capabilities.Where(c => c.MappedBy == MappedBy.User).ToList();
            db.CspInheritedCapabilities.RemoveRange(userRows);
        }

        component.UpdatedAt = DateTimeOffset.UtcNow;
        component.UpdatedBy = actor;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Remapped CspInheritedComponent {ComponentId}: remapRunId={RemapRunId}, "
            + "{Mapped} mapped + {NeedsReview} needs-review (aiMappingAvailable={Available})",
            component.Id, remapRunId, result.Mapped.Count,
            result.NeedsReview.Count, result.AiMappingAvailable);

        return result;
    }

    /// <summary>
    /// Whitespace-trim + lowercase invariant for capability-name diff keys.
    /// Two AI runs that emit the same capability should match here so the
    /// Remap audit pipeline classifies them as Edited rather than
    /// Created+Archived.
    /// </summary>
    private static string NormalizeName(string name)
        => (name ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>
    /// Identity check for the "AI row identical to AI re-output" R11 case.
    /// Compares Description, MappedNistControlIds (order-insensitive),
    /// MappingConfidence, Status, MappingFailureReason. Name is the diff
    /// key — equal names are a precondition for calling this.
    /// </summary>
    private static bool RowsAreEquivalent(CspInheritedCapability existing, CspInheritedCapability incoming)
    {
        if (!string.Equals(existing.Description ?? string.Empty,
                incoming.Description ?? string.Empty, StringComparison.Ordinal))
        {
            return false;
        }
        if (existing.Status != incoming.Status) return false;
        if (existing.MappingConfidence != incoming.MappingConfidence) return false;
        if (!string.Equals(existing.MappingFailureReason ?? string.Empty,
                incoming.MappingFailureReason ?? string.Empty, StringComparison.Ordinal))
        {
            return false;
        }
        var a = (existing.MappedNistControlIds ?? new List<string>()).OrderBy(x => x, StringComparer.Ordinal);
        var b = (incoming.MappedNistControlIds ?? new List<string>()).OrderBy(x => x, StringComparer.Ordinal);
        return a.SequenceEqual(b, StringComparer.Ordinal);
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

        // Feature 050 / US3: capability-level Reviewed history row inside
        // the same SaveChangesAsync — atomic with the state change above.
        await _history.AppendAsync(
            db, capability.Id, _tenantContext.EffectiveTenantId,
            CapabilityHistoryEventType.Reviewed,
            actorOid: actor,
            summary: "Reviewed and approved.",
            metadata: reviewerNote is null
                ? null
                : new { reviewerNote },
            ct).ConfigureAwait(false);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _logger.LogInformation(
            "Reviewed CspInheritedCapability {CapabilityId} on component {ComponentId} by {Actor}",
            capability.Id, componentId, actor);
        return capability;
    }

    /// <inheritdoc />
    public async Task<CspInheritedCapability> UpdateCapabilityAsync(
        Guid componentId,
        Guid capabilityId,
        string name,
        string description,
        IReadOnlyList<string> mappedNistControlIds,
        byte[]? rowVersion,
        string actor,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ArgumentNullException.ThrowIfNull(mappedNistControlIds);

        var trimmedName = TruncateOrThrow(nameof(name), name, 256);
        var trimmedDesc = TruncateOrThrow(nameof(description), description, 2000);
        if (mappedNistControlIds.Count == 0)
        {
            throw new ArgumentException(
                "mappedNistControlIds must be non-empty.",
                nameof(mappedNistControlIds));
        }

        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var capability = await db.CspInheritedCapabilities
            .FirstOrDefaultAsync(c => c.Id == capabilityId, ct)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException(
                $"CspInheritedCapability '{capabilityId}' not found.");

        if (capability.CspInheritedComponentId != componentId)
        {
            throw new KeyNotFoundException(
                $"CspInheritedCapability '{capabilityId}' does not belong to "
                + $"CspInheritedComponent '{componentId}'.");
        }

        // Optimistic concurrency — caller pins a row version if it has one;
        // we feed it to EF Core's tracker so SaveChanges throws
        // DbUpdateConcurrencyException on a mismatch.
        if (rowVersion is not null && rowVersion.Length > 0)
        {
            db.Entry(capability).Property(c => c.RowVersion).OriginalValue = rowVersion;
        }

        // Feature 050 / US3 diff hint — capture the fields that changed
        // BEFORE applying the new values so the history metadata can
        // pinpoint what an auditor should compare. Field-name format:
        // camelCase to match the existing JSON wire envelope.
        var changedFields = new List<string>();
        if (capability.Name != trimmedName) changedFields.Add("name");
        if (capability.Description != trimmedDesc) changedFields.Add("description");
        if (!capability.MappedNistControlIds.SequenceEqual(mappedNistControlIds))
        {
            changedFields.Add("mappedNistControlIds");
        }

        var previousStatus = capability.Status;
        capability.Name = trimmedName;
        capability.Description = trimmedDesc;
        capability.MappedNistControlIds = mappedNistControlIds.ToList();
        capability.MappedBy = MappedBy.User;
        capability.ReviewedAt = DateTimeOffset.UtcNow;
        capability.ReviewedBy = actor;
        // A manual edit implicitly resolves a NeedsReview row — the CSP-Admin
        // has explicitly chosen the mapped control IDs by editing.
        if (capability.Status == CspInheritedCapabilityStatus.NeedsReview)
        {
            capability.Status = CspInheritedCapabilityStatus.Mapped;
            capability.MappingFailureReason = null;
        }

        // Audit row — parallel to ReviewAsync (FR-105). Captures the
        // before/after status so an auditor can see the implicit resolution.
        db.AuditLogs.Add(new AuditLogEntry
        {
            Id = Guid.NewGuid().ToString(),
            UserId = actor,
            UserRole = "CSP.Admin",
            Action = "CspInheritedCapability.Update",
            Outcome = AuditOutcome.Success,
            Timestamp = DateTime.UtcNow,
            AffectedControls = capability.MappedNistControlIds.ToList(),
            Details = JsonSerializer.Serialize(new
            {
                componentId,
                capabilityId,
                previousStatus = previousStatus.ToString(),
                newStatus = capability.Status.ToString(),
                name = capability.Name,
            }),
        });

        // Feature 050 / US3 — capability-level Edited history row inside the
        // same SaveChangesAsync. The auditor sees an explicit diff list even
        // when no fields changed (zero-length array — still a click-trail).
        await _history.AppendAsync(
            db, capability.Id, _tenantContext.EffectiveTenantId,
            CapabilityHistoryEventType.Edited,
            actorOid: actor,
            summary: "Capability edited.",
            metadata: new { fields = changedFields },
            ct).ConfigureAwait(false);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _logger.LogInformation(
            "Updated CspInheritedCapability {CapabilityId} on component {ComponentId} by {Actor}",
            capability.Id, componentId, actor);
        return capability;
    }

    /// <inheritdoc />
    public async Task<CspInheritedCapability> ArchiveCapabilityAsync(
        Guid componentId,
        Guid capabilityId,
        string actor,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var capability = await db.CspInheritedCapabilities
            .FirstOrDefaultAsync(c => c.Id == capabilityId, ct)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException(
                $"CspInheritedCapability '{capabilityId}' not found.");

        if (capability.CspInheritedComponentId != componentId)
        {
            throw new KeyNotFoundException(
                $"CspInheritedCapability '{capabilityId}' does not belong to "
                + $"CspInheritedComponent '{componentId}'.");
        }

        // Idempotent — already Archived rows return unchanged so the
        // archive button stays safe to re-click after a stale reload.
        if (capability.Status == CspInheritedCapabilityStatus.Archived)
        {
            return capability;
        }

        var previousStatus = capability.Status;
        capability.Status = CspInheritedCapabilityStatus.Archived;
        capability.MappedBy = MappedBy.User;
        capability.ReviewedAt = DateTimeOffset.UtcNow;
        capability.ReviewedBy = actor;

        db.AuditLogs.Add(new AuditLogEntry
        {
            Id = Guid.NewGuid().ToString(),
            UserId = actor,
            UserRole = "CSP.Admin",
            Action = "CspInheritedCapability.Archive",
            Outcome = AuditOutcome.Success,
            Timestamp = DateTime.UtcNow,
            AffectedControls = capability.MappedNistControlIds.ToList(),
            Details = JsonSerializer.Serialize(new
            {
                componentId,
                capabilityId,
                previousStatus = previousStatus.ToString(),
            }),
        });

        // Feature 050 / US3 — capability-level Archived history row inside
        // the same SaveChangesAsync. Idempotent: the early-return above means
        // an already-Archived capability writes NO new row.
        await _history.AppendAsync(
            db, capability.Id, _tenantContext.EffectiveTenantId,
            CapabilityHistoryEventType.Archived,
            actorOid: actor,
            summary: "Capability archived.",
            metadata: null,
            ct).ConfigureAwait(false);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _logger.LogInformation(
            "Archived CspInheritedCapability {CapabilityId} on component {ComponentId} by {Actor}",
            capability.Id, componentId, actor);
        return capability;
    }

    /// <inheritdoc />
    public async Task<CspInheritedCapability> ReparentCapabilityAsync(
        Guid componentId,
        Guid capabilityId,
        Guid targetComponentId,
        byte[] rowVersion,
        string actor,
        CancellationToken ct = default)
    {
        // Feature 050 FR-002 / FR-012. Strict contract: rowVersion is required
        // (reparent is destructive — never last-write-wins) and target must be
        // distinct from source. Both are explicit fail-fast guards before any
        // DB I/O so the endpoint surface can map them deterministically.
        ArgumentNullException.ThrowIfNull(rowVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if (targetComponentId == componentId)
        {
            throw new ArgumentException(
                "Target component is the capability's current component.",
                nameof(targetComponentId));
        }

        var tenantId = _tenantContext.EffectiveTenantId;
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        // SQLite/SqlServer honor transactions; InMemory does not. Keep parity
        // with AddCapabilityAsync so unit tests stay green on InMemory while
        // SQL providers get true atomicity.
        var supportsTransactions = db.Database.ProviderName?.Contains("InMemory", StringComparison.OrdinalIgnoreCase) != true;
        await using var tx = supportsTransactions
            ? await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false)
            : null;

        // 1. Target eligibility — tenant-scoped on read via EF query filters
        //    (CspInheritedComponent is [GlobalReference] today, so the actual
        //    tenant guard is at the application layer; an archived target
        //    masquerades as "not found" so cross-tenant existence does not
        //    leak — same shape as 404).
        var target = await db.CspInheritedComponents
            .AsNoTracking()
            .Where(c => c.Id == targetComponentId
                     && c.Status != CspInheritedComponentStatus.Archived)
            .Select(c => new { c.Id, c.Name })
            .SingleOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException(
                $"Target CspInheritedComponent '{targetComponentId}' not found or archived.");

        // 2. Source capability — must live under the supplied componentId.
        var capability = await db.CspInheritedCapabilities
            .Include(c => c.CspInheritedComponent)
            .Where(c => c.Id == capabilityId
                     && c.CspInheritedComponentId == componentId)
            .SingleOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException(
                $"CspInheritedCapability '{capabilityId}' not found under component '{componentId}'.");

        var fromComponentName = capability.CspInheritedComponent?.Name ?? "(unknown)";

        // 3. Pin OriginalValue for the concurrency token so EF surfaces a
        //    DbUpdateConcurrencyException when the caller's If-Match is stale.
        db.Entry(capability).Property(c => c.RowVersion).OriginalValue = rowVersion;

        // 4. Apply the reparent + review reset.
        capability.CspInheritedComponentId = targetComponentId;
        capability.Status = CspInheritedCapabilityStatus.NeedsReview;
        capability.ReviewedAt = null;
        capability.ReviewedBy = null;
        capability.ReviewerNote = null;
        capability.MappingFailureReason = "Moved to a new component; re-review required.";

        // 5. Audit row — same transaction; no SaveChanges inside AppendAsync.
        await _history.AppendAsync(
            db, capability.Id, tenantId,
            CapabilityHistoryEventType.Moved,
            actorOid: actor,
            summary: $"Moved from '{fromComponentName}' to '{target.Name}'.",
            metadata: new
            {
                fromComponentId = componentId,
                toComponentId = targetComponentId,
            },
            ct).ConfigureAwait(false);

        // 6. Commit — DbUpdateConcurrencyException bubbles to the endpoint as 412.
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (tx is not null)
        {
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Reparented CspInheritedCapability {CapabilityId} from {FromComponentId} to {ToComponentId} by {Actor}",
            capability.Id, componentId, targetComponentId, actor);
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
