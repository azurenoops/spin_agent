using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Roles;
using Ato.Copilot.State.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Implements assessment artifact operations: per-control effectiveness recording,
/// immutable SHA-256 snapshot creation, snapshot diffing, evidence integrity verification,
/// evidence completeness checking, and SAR generation.
/// </summary>
public class AssessmentArtifactService : IAssessmentArtifactService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AssessmentArtifactService> _logger;

    private static readonly JsonSerializerOptions CanonicalJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public AssessmentArtifactService(
        IServiceScopeFactory scopeFactory,
        ILogger<AssessmentArtifactService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ControlEffectiveness> AssessControlAsync(
        string assessmentId,
        string controlId,
        string determination,
        string? method = null,
        List<string>? evidenceIds = null,
        string? notes = null,
        string? catSeverity = null,
        string assessorId = "mcp-user",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assessmentId, nameof(assessmentId));
        ArgumentException.ThrowIfNullOrWhiteSpace(controlId, nameof(controlId));
        ArgumentException.ThrowIfNullOrWhiteSpace(determination, nameof(determination));

        // Parse determination enum
        if (!Enum.TryParse<EffectivenessDetermination>(determination, ignoreCase: true, out var det))
            throw new InvalidOperationException(
                $"Invalid determination '{determination}'. Must be 'Satisfied' or 'OtherThanSatisfied'.");

        // Parse CAT severity if provided
        CatSeverity? catSev = null;
        if (!string.IsNullOrWhiteSpace(catSeverity))
        {
            if (!Enum.TryParse<CatSeverity>(catSeverity, ignoreCase: true, out var parsed))
                throw new InvalidOperationException(
                    $"Invalid cat_severity '{catSeverity}'. Must be 'CatI', 'CatII', or 'CatIII'.");
            catSev = parsed;
        }

        // Require CAT severity when OtherThanSatisfied
        if (det == EffectivenessDetermination.OtherThanSatisfied && catSev == null)
            throw new InvalidOperationException(
                "cat_severity is required when determination is 'OtherThanSatisfied'.");

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        // Verify assessment exists
        var assessment = await context.Assessments
            .FirstOrDefaultAsync(a => a.Id == assessmentId, cancellationToken)
            ?? throw new InvalidOperationException($"Assessment '{assessmentId}' not found.");

        // Determine RegisteredSystemId from assessment
        var registeredSystemId = assessment.RegisteredSystemId ?? string.Empty;

        // Check for existing determination (upsert)
        var existing = await context.ControlEffectivenessRecords
            .FirstOrDefaultAsync(ce =>
                ce.AssessmentId == assessmentId && ce.ControlId == controlId,
                cancellationToken);

        if (existing != null)
        {
            existing.Determination = det;
            existing.AssessmentMethod = method;
            existing.EvidenceIds = evidenceIds ?? new List<string>();
            existing.Notes = notes;
            existing.CatSeverity = catSev;
            existing.AssessorId = assessorId;
            existing.AssessedAt = DateTime.UtcNow;

            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Updated effectiveness determination for control '{ControlId}' in assessment '{AssessmentId}': {Determination}",
                controlId, assessmentId, det);

            return existing;
        }

        // Create new effectiveness record
        var record = new ControlEffectiveness
        {
            AssessmentId = assessmentId,
            RegisteredSystemId = registeredSystemId,
            ControlId = controlId.Trim(),
            Determination = det,
            AssessmentMethod = method,
            EvidenceIds = evidenceIds ?? new List<string>(),
            Notes = notes,
            CatSeverity = catSev,
            AssessorId = assessorId,
            AssessedAt = DateTime.UtcNow
        };

        context.ControlEffectivenessRecords.Add(record);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created effectiveness determination for control '{ControlId}' in assessment '{AssessmentId}': {Determination}",
            controlId, assessmentId, det);

        return record;
    }

    /// <inheritdoc />
    public async Task<ComplianceSnapshot> TakeSnapshotAsync(
        string systemId,
        string assessmentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId, nameof(systemId));
        ArgumentException.ThrowIfNullOrWhiteSpace(assessmentId, nameof(assessmentId));

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        // Verify system exists
        var system = await context.RegisteredSystems
            .FirstOrDefaultAsync(s => s.Id == systemId, cancellationToken)
            ?? throw new InvalidOperationException($"System '{systemId}' not found.");

        // Verify assessment exists
        var assessment = await context.Assessments
            .Include(a => a.Findings)
            .FirstOrDefaultAsync(a => a.Id == assessmentId, cancellationToken)
            ?? throw new InvalidOperationException($"Assessment '{assessmentId}' not found.");

        // Gather all effectiveness determinations for this assessment
        var effectivenessRecords = await context.ControlEffectivenessRecords
            .Where(ce => ce.AssessmentId == assessmentId)
            .OrderBy(ce => ce.ControlId)
            .ToListAsync(cancellationToken);

        // Gather evidence hashes for controls in this assessment
        var evidenceRecords = await context.Evidence
            .Where(e => e.AssessmentId == assessmentId)
            .OrderBy(e => e.ControlId)
            .Select(e => new { e.ControlId, e.ContentHash })
            .ToListAsync(cancellationToken);

        // Compute control counts
        var satisfied = effectivenessRecords.Count(e => e.Determination == EffectivenessDetermination.Satisfied);
        var otherThan = effectivenessRecords.Count(e => e.Determination == EffectivenessDetermination.OtherThanSatisfied);
        var totalAssessed = effectivenessRecords.Count;

        // Compute compliance score: Satisfied / (Assessed - N/A) * 100
        var complianceScore = totalAssessed > 0
            ? Math.Round((double)satisfied / totalAssessed * 100, 2)
            : assessment.ComplianceScore;

        // Build canonical JSON for hashing
        var canonicalData = new
        {
            systemId,
            assessmentId,
            complianceScore,
            determinations = effectivenessRecords.Select(e => new
            {
                controlId = e.ControlId,
                determination = e.Determination.ToString(),
                catSeverity = e.CatSeverity?.ToString()
            }).ToList(),
            findings = assessment.Findings
                .OrderBy(f => f.ControlId)
                .Select(f => new
                {
                    controlId = f.ControlId,
                    severity = f.Severity.ToString(),
                    status = f.Status.ToString(),
                    catSeverity = f.CatSeverity?.ToString()
                }).ToList(),
            evidenceHashes = evidenceRecords.Select(e => new
            {
                controlId = e.ControlId,
                contentHash = e.ContentHash
            }).ToList()
        };

        var canonicalJson = JsonSerializer.Serialize(canonicalData, CanonicalJsonOpts);
        var integrityHash = ComputeSha256(canonicalJson);

        // Create immutable snapshot
        var snapshot = new ComplianceSnapshot
        {
            Id = Guid.NewGuid(),
            SubscriptionId = systemId, // Use systemId for RMF snapshots
            ComplianceScore = complianceScore,
            TotalControls = totalAssessed,
            PassedControls = satisfied,
            FailedControls = otherThan,
            TotalResources = assessment.Findings.Select(f => f.ResourceId).Distinct().Count(),
            CompliantResources = 0,
            NonCompliantResources = 0,
            ActiveAlertCount = 0,
            CriticalAlertCount = 0,
            HighAlertCount = 0,
            ControlFamilyBreakdown = BuildFamilyBreakdown(effectivenessRecords),
            CapturedAt = DateTimeOffset.UtcNow,
            IsWeeklySnapshot = false,
            IntegrityHash = integrityHash,
            IsImmutable = true
        };

        context.ComplianceSnapshots.Add(snapshot);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created immutable snapshot '{SnapshotId}' for system '{SystemId}', assessment '{AssessmentId}' with hash '{Hash}'",
            snapshot.Id, systemId, assessmentId, integrityHash);

        return snapshot;
    }

    /// <inheritdoc />
    public async Task<SnapshotComparison> CompareSnapshotsAsync(
        string snapshotIdA,
        string snapshotIdB,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotIdA, nameof(snapshotIdA));
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotIdB, nameof(snapshotIdB));

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var guidA = Guid.Parse(snapshotIdA);
        var guidB = Guid.Parse(snapshotIdB);

        var snapshotA = await context.ComplianceSnapshots
            .FirstOrDefaultAsync(s => s.Id == guidA, cancellationToken)
            ?? throw new InvalidOperationException($"Snapshot '{snapshotIdA}' not found.");

        var snapshotB = await context.ComplianceSnapshots
            .FirstOrDefaultAsync(s => s.Id == guidB, cancellationToken)
            ?? throw new InvalidOperationException($"Snapshot '{snapshotIdB}' not found.");

        // Parse family breakdowns for control-level diff
        var breakdownA = ParseFamilyBreakdown(snapshotA.ControlFamilyBreakdown);
        var breakdownB = ParseFamilyBreakdown(snapshotB.ControlFamilyBreakdown);

        // Compute deltas
        var allFamilies = breakdownA.Keys.Union(breakdownB.Keys).ToList();
        var newlySatisfied = new List<string>();
        var newlyOtherThan = new List<string>();

        foreach (var family in allFamilies)
        {
            var passedA = breakdownA.GetValueOrDefault(family, 0);
            var passedB = breakdownB.GetValueOrDefault(family, 0);

            if (passedB > passedA)
                newlySatisfied.Add(family);
            else if (passedB < passedA)
                newlyOtherThan.Add(family);
        }

        var comparison = new SnapshotComparison
        {
            SnapshotA = new SnapshotSummary
            {
                Id = snapshotA.Id.ToString(),
                CapturedAt = snapshotA.CapturedAt,
                ComplianceScore = snapshotA.ComplianceScore,
                TotalControls = snapshotA.TotalControls,
                PassedControls = snapshotA.PassedControls,
                FailedControls = snapshotA.FailedControls,
                IntegrityHash = snapshotA.IntegrityHash
            },
            SnapshotB = new SnapshotSummary
            {
                Id = snapshotB.Id.ToString(),
                CapturedAt = snapshotB.CapturedAt,
                ComplianceScore = snapshotB.ComplianceScore,
                TotalControls = snapshotB.TotalControls,
                PassedControls = snapshotB.PassedControls,
                FailedControls = snapshotB.FailedControls,
                IntegrityHash = snapshotB.IntegrityHash
            },
            ScoreDelta = Math.Round(snapshotB.ComplianceScore - snapshotA.ComplianceScore, 2),
            NewlySatisfied = newlySatisfied,
            NewlyOtherThanSatisfied = newlyOtherThan,
            UnchangedCount = allFamilies.Count - newlySatisfied.Count - newlyOtherThan.Count,
            NewFindings = Math.Max(0, snapshotB.FailedControls - snapshotA.FailedControls),
            ResolvedFindings = Math.Max(0, snapshotA.FailedControls - snapshotB.FailedControls),
            EvidenceAdded = 0,
            EvidenceRemoved = 0
        };

        _logger.LogInformation(
            "Compared snapshots '{A}' and '{B}': score delta {Delta}",
            snapshotIdA, snapshotIdB, comparison.ScoreDelta);

        return comparison;
    }

    /// <inheritdoc />
    public async Task<EvidenceVerificationResult> VerifyEvidenceAsync(
        string evidenceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceId, nameof(evidenceId));

        using var scope = _scopeFactory.CreateScope();
        var actor = scope.ServiceProvider.GetService<IConversationIdentityAccessor>()?.Current;
        // This service is singleton and creates a child scope. Its scoped ITenantContext is
        // not the HTTP request's context; use the already-bound ambient request context.
        var tenant = scope.ServiceProvider.GetService<ITenantContextAccessor>()?.Current;
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        RequireIntegrityVerificationIdentity(actor, tenant, context);
        var auditId = Guid.NewGuid().ToString();

        return await context.Database.CreateExecutionStrategy().ExecuteAsync(async ct =>
        {
            // Retry the complete authorization + commit unit, not an individual write.
            context.ChangeTracker.Clear();
            RequireIntegrityVerificationIdentity(actor, tenant, context);
            return await VerifyEvidenceIntegrityAsync(evidenceId, auditId, actor!, context, scope.ServiceProvider, ct);
        }, cancellationToken);
    }

    private async Task<EvidenceVerificationResult> VerifyEvidenceIntegrityAsync(
        string evidenceId, string auditId, ConversationIdentity actor, AtoCopilotContext context,
        IServiceProvider services, CancellationToken cancellationToken)
    {
        var tenantId = actor.TenantId!.Value;
        var personId = actor.PersonId!.Value;

        // Keep the ownership rows stable from authorization through timestamp + audit commit.
        await using var transaction = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

        if (!await context.OrganizationMemberships.AnyAsync(m =>
                m.DirectoryTenantId == actor.DirectoryId && m.ObjectId == actor.ObjectId
                && m.TenantId == tenantId && m.PersonId == personId && m.RevokedAt == null, cancellationToken)
            || !await context.Tenants.AnyAsync(t => t.Id == tenantId && t.Status == TenantStatus.Active, cancellationToken))
            throw IntegrityVerificationDenied();

        // Resolve only ownership metadata until the effective system permission is known.
        var owner = await (
            from item in context.Evidence.AsNoTracking()
            join assessment in context.Assessments on item.AssessmentId equals assessment.Id
            join system in context.RegisteredSystems on assessment.RegisteredSystemId equals system.Id
            where item.Id == evidenceId && item.TenantId == tenantId
                && assessment.TenantId == tenantId && system.TenantId == tenantId && system.IsActive
            select new { EvidenceId = item.Id, AssessmentId = assessment.Id, SystemId = system.Id })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw IntegrityVerificationDenied();

        var accessService = services.GetService<ISystemWorkspaceAccessService>()
            ?? throw IntegrityVerificationDenied();
        var access = await accessService.GetAccessAsync(tenantId, personId, owner.SystemId,
            isCspOversight: false, cancellationToken);
        if (!string.Equals(access.SystemId, owner.SystemId, StringComparison.Ordinal)
            || !EvidenceIntegrityVerificationPolicy.IsAllowed(access))
            throw IntegrityVerificationDenied();

        var evidence = await context.Evidence
            .SingleOrDefaultAsync(e => e.Id == owner.EvidenceId && e.TenantId == tenantId
                && e.AssessmentId == owner.AssessmentId, cancellationToken)
            ?? throw IntegrityVerificationDenied();

        var recomputedHash = ComputeSha256(evidence.Content);
        var isVerified = string.Equals(evidence.ContentHash, recomputedHash, StringComparison.OrdinalIgnoreCase);
        var verifiedAt = DateTime.UtcNow;
        var status = isVerified ? "verified" : "tampered";

        if (isVerified)
            evidence.IntegrityVerifiedAt = verifiedAt;

        context.AuditLogs.Add(new AuditLogEntry
        {
            Id = auditId,
            TenantId = tenantId,
            ActorTenantId = tenantId,
            UserId = actor.ActorId,
            UserRole = access.Roles.Contains(nameof(RmfRole.Sca), StringComparer.Ordinal) ? nameof(RmfRole.Sca) : "EvidenceManager",
            Action = "EvidenceIntegrityVerification",
            Timestamp = verifiedAt,
            Outcome = isVerified ? AuditOutcome.Success : AuditOutcome.Failure,
            Details = JsonSerializer.Serialize(new
            {
                evidenceId = evidence.Id,
                assessmentId = owner.AssessmentId,
                systemId = owner.SystemId,
                status,
                effectiveRoles = access.Roles,
                canManageEvidence = access.Permissions.CanManageEvidence
            }, CanonicalJsonOpts)
        });
        await context.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);

        var result = new EvidenceVerificationResult
        {
            EvidenceId = evidence.Id,
            ControlId = evidence.ControlId,
            OriginalHash = evidence.ContentHash,
            RecomputedHash = recomputedHash,
            Status = status,
            CollectorIdentity = evidence.CollectorIdentity,
            VerifierIdentity = actor.ActorId,
            CollectionMethod = evidence.CollectionMethod,
            IntegrityVerifiedAt = evidence.IntegrityVerifiedAt
        };

        _logger.LogInformation(
            "Evidence '{EvidenceId}' integrity verification: {Status}",
            evidenceId, result.Status);

        return result;
    }

    private static void RequireIntegrityVerificationIdentity(
        ConversationIdentity? actor, ITenantContext? tenant, AtoCopilotContext context)
    {
        if (actor is null || actor.DirectoryId == Guid.Empty || actor.ObjectId == Guid.Empty
            || actor.Kind != "organization" || actor.Mode != "ordinary"
            || actor.TenantId is null || actor.TenantId == Guid.Empty
            || actor.PersonId is null || actor.PersonId == Guid.Empty
            || tenant is null || !tenant.IsWorkspaceRequest || tenant.IsCspAdmin
            || tenant.ImpersonatedTenantId is not null || tenant.Status != TenantStatus.Active
            || tenant.TenantId != actor.TenantId || tenant.EffectiveTenantId != actor.TenantId
            || tenant.PersonId != actor.PersonId
            || context.TenantFilterDisabled || context.TenantFilterCspAdminAll || !context.IsWorkspaceRequest
            || context.TenantFilterEffectiveId != actor.TenantId || context.WorkspacePersonId != actor.PersonId)
            throw IntegrityVerificationDenied();
    }

    private static UnauthorizedAccessException IntegrityVerificationDenied() =>
        new("Evidence integrity verification is not authorized in this workspace.");

    /// <inheritdoc />
    public async Task<EvidenceCompletenessReport> CheckEvidenceCompletenessAsync(
        string systemId,
        string? assessmentId = null,
        string? familyFilter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId, nameof(systemId));

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        if (!string.IsNullOrWhiteSpace(assessmentId)
            && !await context.Assessments.AnyAsync(a => a.Id == assessmentId
                && a.RegisteredSystemId == systemId, cancellationToken))
            throw new InvalidOperationException("The assessment is not accessible for the requested system.");

        // Get all effectiveness determinations for this system
        var query = context.ControlEffectivenessRecords
            .Where(ce => ce.RegisteredSystemId == systemId);

        if (!string.IsNullOrWhiteSpace(assessmentId))
            query = query.Where(ce => ce.AssessmentId == assessmentId);

        if (!string.IsNullOrWhiteSpace(familyFilter))
            query = query.Where(ce => ce.ControlId.StartsWith(familyFilter));

        var controls = await query
            .Select(ce => ce.ControlId)
            .Distinct()
            .ToListAsync(cancellationToken);

        // Get evidence per control
        var evidenceQuery = from evidence in context.Evidence
                            join assessment in context.Assessments on evidence.AssessmentId equals assessment.Id
                            where assessment.RegisteredSystemId == systemId
                                && evidence.TenantId == assessment.TenantId
                            select evidence;
        if (!string.IsNullOrWhiteSpace(assessmentId))
            evidenceQuery = evidenceQuery.Where(e => e.AssessmentId == assessmentId);

        var evidenceByControl = await evidenceQuery
            .Where(e => controls.Contains(e.ControlId))
            .GroupBy(e => e.ControlId)
            .Select(g => new
            {
                ControlId = g.Key,
                TotalCount = g.Count(),
                VerifiedCount = g.Count(e => e.IntegrityVerifiedAt != null)
            })
            .ToListAsync(cancellationToken);

        var evidenceLookup = evidenceByControl.ToDictionary(e => e.ControlId);

        var controlStatuses = controls.Select(controlId =>
        {
            if (evidenceLookup.TryGetValue(controlId, out var ev))
            {
                return new ControlEvidenceStatus
                {
                    ControlId = controlId,
                    Status = ev.VerifiedCount > 0 ? "verified" : "unverified",
                    EvidenceCount = ev.TotalCount,
                    VerifiedCount = ev.VerifiedCount
                };
            }

            return new ControlEvidenceStatus
            {
                ControlId = controlId,
                Status = "missing",
                EvidenceCount = 0,
                VerifiedCount = 0
            };
        }).OrderBy(s => s.ControlId).ToList();

        var withEvidence = controlStatuses.Count(s => s.Status != "missing");
        var withVerified = controlStatuses.Count(s => s.Status == "verified");
        var withUnverified = controlStatuses.Count(s => s.Status == "unverified");
        var without = controlStatuses.Count(s => s.Status == "missing");

        var completeness = controls.Count > 0
            ? Math.Round((double)withEvidence / controls.Count * 100, 2)
            : 0.0;

        return new EvidenceCompletenessReport
        {
            SystemId = systemId,
            AssessmentId = assessmentId,
            CompletenessPercentage = completeness,
            TotalControls = controls.Count,
            ControlsWithEvidence = withEvidence,
            ControlsWithoutEvidence = without,
            ControlsWithUnverifiedEvidence = withUnverified,
            ControlStatuses = controlStatuses
        };
    }

    /// <inheritdoc />
    public async Task<SarDocument> GenerateSarAsync(
        string systemId,
        string assessmentId,
        string format = "markdown",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId, nameof(systemId));
        ArgumentException.ThrowIfNullOrWhiteSpace(assessmentId, nameof(assessmentId));

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        // Load system
        var system = await context.RegisteredSystems
            .FirstOrDefaultAsync(s => s.Id == systemId, cancellationToken)
            ?? throw new InvalidOperationException($"System '{systemId}' not found.");

        // Load assessment with findings
        var assessment = await context.Assessments
            .Include(a => a.Findings)
            .FirstOrDefaultAsync(a => a.Id == assessmentId, cancellationToken)
            ?? throw new InvalidOperationException($"Assessment '{assessmentId}' not found.");

        // Load effectiveness determinations
        var effectivenessRecords = await context.ControlEffectivenessRecords
            .Where(ce => ce.AssessmentId == assessmentId)
            .ToListAsync(cancellationToken);

        // Compute aggregate counts
        var satisfied = effectivenessRecords.Count(e => e.Determination == EffectivenessDetermination.Satisfied);
        var otherThan = effectivenessRecords.Count(e => e.Determination == EffectivenessDetermination.OtherThanSatisfied);
        var totalAssessed = effectivenessRecords.Count;

        // CAT breakdown
        var catI = effectivenessRecords.Count(e => e.CatSeverity == Core.Models.Compliance.CatSeverity.CatI);
        var catII = effectivenessRecords.Count(e => e.CatSeverity == Core.Models.Compliance.CatSeverity.CatII);
        var catIII = effectivenessRecords.Count(e => e.CatSeverity == Core.Models.Compliance.CatSeverity.CatIII);

        var complianceScore = totalAssessed > 0
            ? Math.Round((double)satisfied / totalAssessed * 100, 2)
            : assessment.ComplianceScore;

        // Per-family results
        var familyGroups = effectivenessRecords
            .GroupBy(e => e.ControlId.Length >= 2 ? e.ControlId[..2] : e.ControlId)
            .OrderBy(g => g.Key)
            .Select(g => new FamilyAssessmentResult
            {
                Family = g.Key,
                ControlsAssessed = g.Count(),
                ControlsSatisfied = g.Count(e => e.Determination == EffectivenessDetermination.Satisfied),
                ControlsOtherThanSatisfied = g.Count(e => e.Determination == EffectivenessDetermination.OtherThanSatisfied),
                CatBreakdown = new Dictionary<string, int>
                {
                    ["CatI"] = g.Count(e => e.CatSeverity == Core.Models.Compliance.CatSeverity.CatI),
                    ["CatII"] = g.Count(e => e.CatSeverity == Core.Models.Compliance.CatSeverity.CatII),
                    ["CatIII"] = g.Count(e => e.CatSeverity == Core.Models.Compliance.CatSeverity.CatIII)
                }
            }).ToList();

        // Generate SAR markdown content
        var sb = new StringBuilder();
        sb.AppendLine("# Security Assessment Report (SAR)");
        sb.AppendLine();
        sb.AppendLine("## 1. Executive Summary");
        sb.AppendLine();
        sb.AppendLine($"- **System**: {system.Name} ({system.Acronym})");
        sb.AppendLine($"- **Assessment ID**: {assessmentId}");
        sb.AppendLine($"- **Assessment Date**: {assessment.AssessedAt:yyyy-MM-dd}");
        sb.AppendLine($"- **Compliance Score**: {complianceScore:F2}%");
        sb.AppendLine($"- **Controls Assessed**: {totalAssessed}");
        sb.AppendLine($"- **Controls Satisfied**: {satisfied}");
        sb.AppendLine($"- **Controls Other Than Satisfied**: {otherThan}");
        sb.AppendLine();
        sb.AppendLine("## 2. CAT Severity Breakdown");
        sb.AppendLine();
        sb.AppendLine($"| Category | Count |");
        sb.AppendLine($"|----------|-------|");
        sb.AppendLine($"| CAT I (Critical) | {catI} |");
        sb.AppendLine($"| CAT II (Medium) | {catII} |");
        sb.AppendLine($"| CAT III (Low) | {catIII} |");
        sb.AppendLine();
        sb.AppendLine("## 3. Control-by-Family Results");
        sb.AppendLine();
        sb.AppendLine("| Family | Assessed | Satisfied | Other Than Satisfied | CAT I | CAT II | CAT III |");
        sb.AppendLine("|--------|----------|-----------|---------------------|-------|--------|---------|");

        foreach (var family in familyGroups)
        {
            sb.AppendLine($"| {family.Family} | {family.ControlsAssessed} | {family.ControlsSatisfied} | " +
                $"{family.ControlsOtherThanSatisfied} | {family.CatBreakdown.GetValueOrDefault("CatI", 0)} | " +
                $"{family.CatBreakdown.GetValueOrDefault("CatII", 0)} | {family.CatBreakdown.GetValueOrDefault("CatIII", 0)} |");
        }

        sb.AppendLine();
        sb.AppendLine("## 4. Risk Summary");
        sb.AppendLine();

        if (catI > 0)
            sb.AppendLine($"**HIGH RISK**: {catI} CAT I finding(s) require immediate remediation.");
        if (catII > 0)
            sb.AppendLine($"**MEDIUM RISK**: {catII} CAT II finding(s) require remediation within 180 days.");
        if (catIII > 0)
            sb.AppendLine($"**LOW RISK**: {catIII} CAT III finding(s) require remediation within 365 days.");

        if (catI == 0 && catII == 0 && catIII == 0)
            sb.AppendLine("No findings — all assessed controls are Satisfied.");

        sb.AppendLine();
        sb.AppendLine("## 5. Detailed Findings");
        sb.AppendLine();

        var otherThanRecords = effectivenessRecords
            .Where(e => e.Determination == EffectivenessDetermination.OtherThanSatisfied)
            .OrderBy(e => e.CatSeverity)
            .ThenBy(e => e.ControlId);

        foreach (var rec in otherThanRecords)
        {
            sb.AppendLine($"### {rec.ControlId} — {rec.CatSeverity}");
            sb.AppendLine();
            sb.AppendLine($"- **Method**: {rec.AssessmentMethod ?? "Not specified"}");
            sb.AppendLine($"- **Assessor**: {rec.AssessorId}");
            sb.AppendLine($"- **Assessed**: {rec.AssessedAt:yyyy-MM-dd}");
            if (!string.IsNullOrWhiteSpace(rec.Notes))
                sb.AppendLine($"- **Notes**: {rec.Notes}");
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine($"*Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC*");

        return new SarDocument
        {
            SystemId = systemId,
            AssessmentId = assessmentId,
            Format = format,
            Content = sb.ToString(),
            ComplianceScore = complianceScore,
            ControlsAssessed = totalAssessed,
            ControlsSatisfied = satisfied,
            ControlsOtherThanSatisfied = otherThan,
            CatIFindings = catI,
            CatIIFindings = catII,
            CatIIIFindings = catIII,
            FamilyResults = familyGroups,
            GeneratedAt = DateTime.UtcNow
        };
    }

    // ─── Private Helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Compute SHA-256 hash of the given input string.
    /// </summary>
    private static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Build a JSON family breakdown from effectiveness records.
    /// </summary>
    private static string BuildFamilyBreakdown(List<ControlEffectiveness> records)
    {
        var breakdown = records
            .GroupBy(e => e.ControlId.Length >= 2 ? e.ControlId[..2] : e.ControlId)
            .ToDictionary(
                g => g.Key,
                g => g.Count(e => e.Determination == EffectivenessDetermination.Satisfied));

        return JsonSerializer.Serialize(breakdown);
    }

    /// <summary>
    /// Parse a family breakdown JSON string into a dictionary.
    /// </summary>
    private static Dictionary<string, int> ParseFamilyBreakdown(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, int>();

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, int>>(json)
                   ?? new Dictionary<string, int>();
        }
        catch
        {
            return new Dictionary<string, int>();
        }
    }
}
