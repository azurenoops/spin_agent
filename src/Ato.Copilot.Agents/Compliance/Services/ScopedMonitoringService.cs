using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Workspaces;
using Ato.Copilot.Core.Services.Monitoring;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Agents.Compliance.Services;

public sealed record SaveMonitoringRuleRequest(string Name, string BoundaryDefinitionId, string BaselineReference,
    string OwnerId, string Signal, MonitoringCondition Condition, int CadenceMinutes, string Severity,
    bool IsEnabled, long? ExpectedVersion = null);
public sealed record DispositionMonitoringImpactRequest(long ExpectedVersion, string Disposition, string Rationale);
public sealed record MonitoringObservedChange(string SourceId, string Kind, string Title, string? ControlId,
    string? ChangeDetails, string Attribution, DateTimeOffset ObservedAt, ComplianceAlert Alert, Guid? ProviderComponentId = null);
public sealed record MonitoringCoverage(string AssignmentId, string BoundaryId, string? ResourceId,
    Guid? ProviderComponentId, string Health, DateTimeOffset? LastSuccessAt, string? Error);

/// <summary>Tenant-bound projection over the existing watch engine and published-provider delivery ledger.</summary>
public sealed class ScopedMonitoringService(AtoCopilotContext db, INarrativeChangeImpactService narratives)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<List<MonitoringScopeResource>> ScopeAsync(string systemId, string? boundaryId, CancellationToken ct)
        => await db.BoundaryComponentAssignments.AsNoTracking()
            .Where(x => x.IsInScope && x.AuthorizationBoundaryDefinition.RegisteredSystemId == systemId
                && (boundaryId == null || x.AuthorizationBoundaryDefinitionId == boundaryId))
            .Select(x => new MonitoringScopeResource(x.Id, x.AuthorizationBoundaryDefinitionId, x.SystemComponentId,
                x.SystemComponent == null ? null : x.SystemComponent.AzureResourceId, x.CspInheritedComponentId))
            .ToListAsync(ct);

    public async Task<AlertRule> SaveRuleAsync(string systemId, Guid? id, SaveMonitoringRuleRequest input,
        string actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 200 ||
            string.IsNullOrWhiteSpace(input.OwnerId) || input.OwnerId.Length > 200 ||
            string.IsNullOrWhiteSpace(input.BaselineReference) || input.BaselineReference.Length > 500 ||
            input.CadenceMinutes is < 1 or > 10080 || input.Signal is not ("Alert" or "ProviderRelease") ||
            !Enum.TryParse<AlertSeverity>(input.Severity, out var severity) || !Enum.IsDefined(severity))
            throw new ArgumentException("Name, owner, reviewed baseline, supported signal/severity and a 1–10080 minute cadence are required.");
        var condition = JsonSerializer.Serialize(input.Condition, Json);
        MonitoringConditionEvaluator.Parse(condition);
        if (!await db.AuthorizationBoundaryDefinitions.AnyAsync(
            x => x.Id == input.BoundaryDefinitionId && x.RegisteredSystemId == systemId, ct))
            throw new KeyNotFoundException("Boundary not found in this system.");
        var scope = await ScopeAsync(systemId, input.BoundaryDefinitionId, ct);
        if (scope.Count == 0) throw new ArgumentException("Review an in-scope boundary assignment before creating a rule.");
        if (input.Signal == "ProviderRelease" && !scope.Any(x => x.ProviderComponentId != null))
            throw new ArgumentException("A provider-release rule requires an explicit provider-component boundary dependency.");
        AlertRule rule;
        if (id.HasValue)
        {
            rule = await db.AlertRules.SingleOrDefaultAsync(x => x.Id == id && x.RegisteredSystemId == systemId, ct)
                ?? throw new KeyNotFoundException("Rule not found.");
            if (input.ExpectedVersion != rule.Version) throw new DbUpdateConcurrencyException("Rule changed; reload before saving.");
            rule.Version++;
        }
        else
        {
            var system = await db.RegisteredSystems.SingleOrDefaultAsync(x => x.Id == systemId, ct)
                ?? throw new KeyNotFoundException("System not found.");
            rule = new AlertRule { Id = Guid.NewGuid(), TenantId = system.TenantId, RegisteredSystemId = systemId,
                CreatedBy = actor, CreatedAt = DateTimeOffset.UtcNow };
            db.AlertRules.Add(rule);
        }
        rule.Name = input.Name.Trim();
        rule.OwnerId = input.OwnerId.Trim();
        rule.LastModifiedBy = actor;
        rule.Signal = input.Signal;
        rule.BoundaryDefinitionId = input.BoundaryDefinitionId;
        rule.BaselineReference = input.BaselineReference.Trim();
        rule.ReviewedScopeJson = JsonSerializer.Serialize(scope.OrderBy(x => x.AssignmentId), Json);
        rule.TriggerCondition = condition;
        rule.CadenceMinutes = input.CadenceMinutes;
        rule.SeverityOverride = severity;
        rule.IsEnabled = input.IsEnabled;
        rule.UpdatedAt = DateTimeOffset.UtcNow;
        rule.NextEvaluationUtcTicks = 0;
        // Retain every authored version even before the first evaluation.
        db.Set<MonitoringRuleEvaluation>().Add(new()
        {
            TenantId = rule.TenantId, RuleId = rule.Id, RuleVersion = rule.Version, RegisteredSystemId = systemId,
            Outcome = "Configured", InputFingerprint = Hash("configured"), RuleSnapshotJson = JsonSerializer.Serialize(rule, Json),
            InputSnapshotJson = "{}"
        });
        await db.SaveChangesAsync(ct);
        return rule;
    }

    public async Task<List<MonitoringObservedChange>> ChangesAsync(string systemId, CancellationToken ct)
    {
        var changes = await CurrentChangesAsync(systemId, ct);
        var retained = await db.Set<ScopedMonitoringObservation>().AsNoTracking()
            .Where(x => x.RegisteredSystemId == systemId).ToListAsync(ct);
        foreach (var row in retained.OrderByDescending(x => x.AttributedAt))
            if (!changes.Any(x => x.SourceId == row.SourceId))
            {
                var observation = JsonSerializer.Deserialize<MonitoringObservedChange>(row.SnapshotJson, Json)
                    ?? throw new InvalidOperationException("Retained monitoring observation is invalid.");
                changes.Add(observation);
            }
        return changes.OrderByDescending(x => x.ObservedAt).ToList();
    }

    private async Task<List<MonitoringObservedChange>> CurrentChangesAsync(string systemId, CancellationToken ct)
    {
        var scope = await ScopeAsync(systemId, null, ct);
        var ids = scope.Where(x => x.ResourceId != null).Select(x => Normalize(x.ResourceId!)).ToHashSet();
        // Resource lists are JSON; materialize tenant-filtered alerts, then attribute exact ARM IDs.
        var alerts = await db.ComplianceAlerts.AsNoTracking().ToListAsync(ct);
        var changes = alerts.Where(x => x.AffectedResources.Any(r => ids.Contains(Normalize(r))))
            .Select(x => new MonitoringObservedChange(x.Id.ToString(), "Alert",
                x.AffectedResources.All(r => ids.Contains(Normalize(r))) ? x.Title : "Observation includes resources outside this scope",
                x.ControlId, x.AffectedResources.All(r => ids.Contains(Normalize(r))) ? x.ChangeDetails : null,
                x.AffectedResources.All(r => ids.Contains(Normalize(r))) ? "Boundary" : "Partial",
                x.CreatedAt, new ComplianceAlert
                {
                    Id = x.Id, Type = x.Type, Severity = x.Severity, Status = x.Status, ControlId = x.ControlId,
                    ControlFamily = x.ControlFamily,
                    ChangeDetails = x.AffectedResources.All(r => ids.Contains(Normalize(r))) ? x.ChangeDetails : null,
                    AffectedResources = x.AffectedResources.Where(r => ids.Contains(Normalize(r))).ToList(),
                    RegisteredSystemId = systemId
                })).ToList();
        var tenant = await db.RegisteredSystems.Where(x => x.Id == systemId).Select(x => x.TenantId).SingleAsync(ct);
        // This is a global routing ledger: both tenant and system predicates are mandatory.
        var provider = await (from impact in db.Set<ProviderReleaseImpact>().AsNoTracking()
            join release in db.Set<ProviderCapabilityRelease>() on impact.ReleaseId equals release.Id
            join capability in db.CspInheritedCapabilities on release.CapabilityId equals capability.Id
            where impact.TenantId == tenant && impact.RegisteredSystemId == systemId && impact.DeliveredAt != null
            select new { Impact = impact, capability.CspInheritedComponentId }).ToListAsync(ct);
        changes.AddRange(provider.Select(x => new MonitoringObservedChange(x.Impact.Id.ToString(), "ProviderRelease",
            $"Published provider revision: {x.Impact.ChangeKind}", x.Impact.ControlId,
            JsonSerializer.Serialize(new { releaseId = x.Impact.ReleaseId, sourceRevision = x.Impact.SourceRevision, changeKind = x.Impact.ChangeKind }, Json),
            "ProviderDependency", x.Impact.CreatedAt, new ComplianceAlert
            {
                Id = x.Impact.Id, Type = AlertType.Drift, Severity = AlertSeverity.Medium, ControlId = x.Impact.ControlId,
                ControlFamily = x.Impact.ControlId.Split('-')[0], RegisteredSystemId = systemId
            }, x.CspInheritedComponentId)));
        return changes.OrderByDescending(x => x.ObservedAt).ToList();
    }

    public async Task<List<MonitoringCoverage>> CoverageAsync(string systemId, CancellationToken ct)
    {
        var scope = await ScopeAsync(systemId, null, ct);
        var configs = await db.MonitoringConfigurations.AsNoTracking().ToListAsync(ct);
        return scope.Select(resource =>
        {
            var config = configs.Where(c => ResourceInConfiguration(resource.ResourceId, c))
                .OrderByDescending(c => c.ResourceGroupName != null).FirstOrDefault();
            return new MonitoringCoverage(resource.AssignmentId, resource.BoundaryId, resource.ResourceId,
                resource.ProviderComponentId, resource.ProviderComponentId.HasValue ? "ProviderDependency"
                    : CollectionHealth(config), config?.LastRunAt, config?.CollectionError);
        }).ToList();
    }

    public static string CollectionHealth(MonitoringConfiguration? config)
    {
        if (config == null) return "Missing";
        if (!config.IsEnabled) return "Disabled";
        if (config.CollectionError != null) return "Failed";
        if (config.LastRunAt == null) return "Missing";
        var cadence = config.Frequency switch
        {
            MonitoringFrequency.FifteenMinutes => TimeSpan.FromMinutes(15),
            MonitoringFrequency.Hourly => TimeSpan.FromHours(1),
            MonitoringFrequency.Daily => TimeSpan.FromDays(1),
            MonitoringFrequency.Weekly => TimeSpan.FromDays(7),
            _ => TimeSpan.Zero
        };
        var now = DateTimeOffset.UtcNow;
        if (cadence == TimeSpan.Zero || config.LastRunAt > now.AddMinutes(5)) return "Unknown";
        if (config.NextRunAt < now.AddMinutes(-5) || config.LastRunAt < now - cadence - TimeSpan.FromMinutes(5)) return "Stale";
        return "Healthy";
    }

    public async Task EvaluateDueAsync(CancellationToken ct)
    {
        // Background TenantContext has no workspace-person filter. Requests never disable
        // filters to read unlinked/shared alerts; only this attributed ledger reaches the UI.
        var systems = await db.RegisteredSystems.AsNoTracking().Select(x => new { x.Id, x.TenantId }).ToListAsync(ct);
        foreach (var system in systems)
        {
            foreach (var change in await CurrentChangesAsync(system.Id, ct))
            {
                var snapshot = JsonSerializer.Serialize(change, Json);
                var fingerprint = Hash(snapshot);
                if (!await db.Set<ScopedMonitoringObservation>().AnyAsync(x => x.RegisteredSystemId == system.Id &&
                    x.SourceId == change.SourceId && x.Fingerprint == fingerprint, ct))
                    db.Set<ScopedMonitoringObservation>().Add(new()
                    {
                        TenantId = system.TenantId, RegisteredSystemId = system.Id, SourceId = change.SourceId,
                        Fingerprint = fingerprint, SnapshotJson = snapshot
                    });
            }
        }
        await db.SaveChangesAsync(ct);
        var nowTicks = DateTime.UtcNow.Ticks;
        var rules = await db.AlertRules.Where(x => x.RegisteredSystemId != null && x.IsEnabled
            && x.NextEvaluationUtcTicks <= nowTicks).ToListAsync(ct);
        foreach (var rule in rules) await EvaluateAsync(rule, false, ct);
    }

    public async Task<IReadOnlyList<MonitoringRuleEvaluation>> TestAsync(string systemId, Guid ruleId, CancellationToken ct)
    {
        var rule = await db.AlertRules.SingleOrDefaultAsync(x => x.Id == ruleId && x.RegisteredSystemId == systemId, ct)
            ?? throw new KeyNotFoundException("Rule not found.");
        return await EvaluateAsync(rule, true, ct);
    }

    private async Task<IReadOnlyList<MonitoringRuleEvaluation>> EvaluateAsync(AlertRule rule, bool dryRun, CancellationToken ct)
    {
        if (!rule.IsEnabled) return Array.Empty<MonitoringRuleEvaluation>();
        var systemId = rule.RegisteredSystemId!;
        var currentScope = await ScopeAsync(systemId, rule.BoundaryDefinitionId, ct);
        var snapshot = JsonSerializer.Serialize(currentScope.OrderBy(x => x.AssignmentId), Json);
        var scopeChanged = snapshot != rule.ReviewedScopeJson;
        var changes = (await ChangesAsync(systemId, ct)).Where(x => x.Kind == rule.Signal).ToList();
        var ids = currentScope.Where(x => x.ResourceId != null).Select(x => Normalize(x.ResourceId!)).ToHashSet();
        changes = changes.Where(x => x.Kind == "ProviderRelease" ? currentScope.Any(s => s.ProviderComponentId == x.ProviderComponentId)
            : x.Alert.AffectedResources.Any(r => ids.Contains(Normalize(r)))).ToList();
        var coverage = (await CoverageAsync(systemId, ct)).Where(x => x.BoundaryId == rule.BoundaryDefinitionId).ToList();
        var unhealthy = rule.Signal == "Alert" &&
            coverage.Any(x => x.ProviderComponentId == null && x.Health != "Healthy");
        var results = new List<MonitoringRuleEvaluation>();
        var observations = changes.Select(x => (Fingerprint: Hash(x.SourceId + x.ChangeDetails), Change: (MonitoringObservedChange?)x)).ToList();
        // A failed collection is not an evaluation of a previously stored observation.
        // Do not consume its replay key; recovery must still evaluate that observation.
        if (unhealthy || scopeChanged) observations.Clear();
        if (observations.Count == 0 || unhealthy || scopeChanged)
            observations.Add((Hash($"health:{DateTime.UtcNow.Ticks / TimeSpan.FromMinutes(rule.CadenceMinutes).Ticks}"), null));
        foreach (var observation in observations)
        {
            if (!dryRun && await db.Set<MonitoringRuleEvaluation>().AnyAsync(x => x.RuleId == rule.Id &&
                x.RuleVersion == rule.Version && x.InputFingerprint == observation.Fingerprint, ct)) continue;
            var outcome = scopeChanged ? "ScopeReviewRequired" : unhealthy ? "CollectionUnavailable"
                : observation.Change == null ? "NoMatch"
                : observation.Change.Attribution == "Partial" ? "UnknownAttribution"
                : MonitoringConditionEvaluator.Matches(rule.TriggerCondition, observation.Change.Alert) ? "Matched" : "NoMatch";
            var evaluation = new MonitoringRuleEvaluation
            {
                TenantId = rule.TenantId, RuleId = rule.Id, RuleVersion = rule.Version, RegisteredSystemId = systemId,
                InputFingerprint = observation.Fingerprint, Outcome = outcome,
                RuleSnapshotJson = JsonSerializer.Serialize(rule, Json),
                InputSnapshotJson = JsonSerializer.Serialize(new { change = observation.Change, coverage }, Json)
            };
            results.Add(evaluation);
            if (dryRun) continue;
            db.Set<MonitoringRuleEvaluation>().Add(evaluation);
            if (outcome == "Matched")
            {
                var control = observation.Change!.ControlId;
                var implementations = await db.ControlImplementations.AsNoTracking()
                    .Where(x => x.RegisteredSystemId == systemId && (control == null || x.ControlId == control))
                    .Select(x => new { x.Id, x.ControlId, x.CurrentVersion, x.ApprovedVersionId }).ToListAsync(ct);
                var duties = await db.Set<CapabilityResponsibilityConfirmation>().AsNoTracking()
                    .Where(x => x.RegisteredSystemId == systemId && x.IsCurrent && (control == null || x.ControlId == control))
                    .Select(x => new { x.Id, x.ControlId, x.CustomerResponsibility, x.SourceRevision, x.ConfirmedBy }).ToListAsync(ct);
                var evidence = await db.EvidenceArtifacts.AsNoTracking()
                    .Where(x => x.RegisteredSystemId == systemId && !x.IsDeleted &&
                        (control == null || db.ControlImplementations.Any(i => i.Id == x.ControlImplementationId && i.ControlId == control)))
                    .Select(x => new { x.Id, x.FileName, x.ControlImplementationId }).ToListAsync(ct);
                db.Set<MonitoringImpactReview>().Add(new()
                {
                    TenantId = rule.TenantId, RegisteredSystemId = systemId, EvaluationId = evaluation.Id,
                    ControlId = control, OwnerId = rule.OwnerId ?? rule.CreatedBy,
                    AffectedRecordsJson = JsonSerializer.Serialize(new { implementations, duties, evidence,
                        evidenceScope = "Linked evidence candidates; sufficiency requires review",
                        documents = new[] { "SSP", "ConMon plan", "Assessment scope" } }, Json)
                });
            }
        }
        if (!dryRun)
        {
            rule.LastEvaluatedAt = DateTimeOffset.UtcNow;
            rule.NextEvaluationUtcTicks = DateTime.UtcNow.AddMinutes(rule.CadenceMinutes).Ticks;
            // Include the read version in UPDATE so edits/disable racing evaluation roll back its review inserts.
            db.Entry(rule).Property(x => x.Version).IsModified = true;
            await db.SaveChangesAsync(ct);
        }
        return results;
    }

    public async Task<MonitoringImpactReview> DispositionAsync(string systemId, Guid id,
        DispositionMonitoringImpactRequest input, string actor, CancellationToken ct)
    {
        var firstAttempt = true;
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            // A commit failure can leave accepted tracked values that were rolled back.
            // Reload before a transient retry; the retained version/actor handles committed replays.
            if (!firstAttempt) db.ChangeTracker.Clear();
            firstAttempt = false;
            return await DispositionCoreAsync(systemId, id, input, actor, ct);
        });
    }

    private async Task<MonitoringImpactReview> DispositionCoreAsync(string systemId, Guid id,
        DispositionMonitoringImpactRequest input, string actor, CancellationToken ct)
    {
        if (input.Disposition is not ("NoImpact" or "StageNarrativeReview" or "RecommendReassessment") ||
            string.IsNullOrWhiteSpace(input.Rationale) || input.Rationale.Length > 4000)
            throw new ArgumentException("Select a supported disposition and provide a rationale (up to 4000 characters).");
        var impact = await db.Set<MonitoringImpactReview>().SingleOrDefaultAsync(x => x.Id == id && x.RegisteredSystemId == systemId, ct)
            ?? throw new KeyNotFoundException("Impact not found.");
        if (impact.Version == input.ExpectedVersion + 1 && impact.Disposition == input.Disposition &&
            impact.Rationale == input.Rationale.Trim() && impact.ReviewedBy == actor)
            return impact;
        if (impact.Version != input.ExpectedVersion) throw new DbUpdateConcurrencyException("Impact changed; reload before reviewing.");
        if (impact.Disposition != "Pending") throw new ArgumentException("The retained disposition cannot be overwritten.");
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        if (input.Disposition == "StageNarrativeReview")
        {
            if (impact.ControlId == null) throw new ArgumentException("A control must be attributed before narrative review can be staged.");
            var result = await narratives.QueueAsync(new(impact.TenantId, systemId, new[] { impact.ControlId },
                new[] { "Technical" }, "Monitoring", impact.Id.ToString(), actor, impact.Id.ToString()), ct);
            impact.NarrativeProposalIdsJson = JsonSerializer.Serialize(result.ProposalIds);
        }
        impact.Disposition = input.Disposition;
        impact.Rationale = input.Rationale.Trim();
        impact.ReviewedBy = actor;
        impact.ReviewedAt = DateTimeOffset.UtcNow;
        impact.Version++;
        await db.SaveChangesAsync(ct);
        if (transaction != null) await transaction.CommitAsync(ct);
        return impact;
    }

    public static bool ResourceInConfiguration(string? resource, MonitoringConfiguration c) =>
        resource != null && resource.StartsWith($"/subscriptions/{c.SubscriptionId}/", StringComparison.OrdinalIgnoreCase)
        && (c.ResourceGroupName == null || resource.Contains($"/resourceGroups/{c.ResourceGroupName}/", StringComparison.OrdinalIgnoreCase));
    private static string Normalize(string value) => value.TrimEnd('/').ToUpperInvariant();
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
