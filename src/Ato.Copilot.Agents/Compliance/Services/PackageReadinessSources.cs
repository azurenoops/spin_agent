using System.Security.Cryptography;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Workspaces;
using Ato.Copilot.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>Deterministic input identity, not generated export timestamps or page visits.</summary>
internal static class PackageReadinessSources
{
    internal sealed record Snapshot(string Hash, IReadOnlyList<PackageReadinessSource> Sources, DateTime? ValidUntil);

    internal static async Task<Snapshot> CaptureAsync(AtoCopilotContext db, IServiceProvider services,
        RegisteredSystem system, PackageReadinessSelection selection, string actor, CancellationToken ct)
    {
        if (selection.Purpose is PackagePurpose.AuthorizedBaselineArchive or PackagePurpose.ChangeSubmission)
        {
            var context = await RetainedPackageContext.ResolveAsync(db, services, system.Id, selection.Purpose,
                selection.RetainedContext, actor, ct);
            return new(AuthorizationPackageContextOptions.SourceContextHash(context),
                [new("retained-baseline", context.BaselinePackageId, null, context.BaselineContentHash, "Retained baseline"),
                 new("authorization-decision", context.AuthorizationDecisionId, null, context.DecisionSnapshotHash, "Recorded decision"),
                 .. context.ChangePreviewId is { } preview
                     ? new[] { new PackageReadinessSource("reviewed-change", preview.ToString(), null, context.ChangeContentHash, "Reviewed SSP change") } : []],
                (await db.AuthorizationPackages.AsNoTracking().SingleAsync(x => x.Id == context.BaselinePackageId, ct)).ExpiresAt.UtcDateTime);
        }

        var sources = new List<PackageReadinessSource>();
        await Add(db.RegisteredSystems.Where(x => x.Id == system.Id), "system", sources, db, ct);
        await Add(db.SystemProfileSections.Where(x => x.RegisteredSystemId == system.Id), "profile-approval", sources, db, ct);
        var profileIds = db.SystemProfileSections.Where(x => x.RegisteredSystemId == system.Id).Select(x => x.Id);
        await Add(db.UserCategories.Where(x => profileIds.Contains(x.SystemProfileSectionId)), "profile-approval", sources, db, ct);
        await Add(db.DataTypeEntries.Where(x => profileIds.Contains(x.SystemProfileSectionId)), "profile-approval", sources, db, ct);
        await Add(db.PpsEntries.Where(x => profileIds.Contains(x.SystemProfileSectionId)), "profile-approval", sources, db, ct);
        await Add(db.LeveragedAuthorizations.Where(x => profileIds.Contains(x.SystemProfileSectionId)), "profile-approval", sources, db, ct);
        await Add(db.ProfileAuditEntries.Where(x => profileIds.Contains(x.SystemProfileSectionId)), "profile-approval", sources, db, ct);
        await Add(db.AuthorizationBoundaryDefinitions.Where(x => x.RegisteredSystemId == system.Id), "boundary", sources, db, ct);
        await Add(db.AuthorizationBoundaries.Where(x => x.RegisteredSystemId == system.Id), "boundary", sources, db, ct);
        await Add(db.BoundaryComponentAssignments.Where(x => x.AuthorizationBoundaryDefinition.RegisteredSystemId == system.Id), "boundary", sources, db, ct);
        await Add(db.ComponentSystemAssignments.Where(x => x.RegisteredSystemId == system.Id), "boundary", sources, db, ct);
        var components = db.ComponentSystemAssignments.Where(x => x.RegisteredSystemId == system.Id).Select(x => x.SystemComponentId);
        var placed = db.BoundaryComponentAssignments.Where(x => x.AuthorizationBoundaryDefinition.RegisteredSystemId == system.Id).Select(x => x.SystemComponentId);
        await Add(db.SystemComponents.Where(x => components.Contains(x.Id) || placed.Contains(x.Id)), "inventory", sources, db, ct);
        await Add(db.InventoryItems.Where(x => x.RegisteredSystemId == system.Id), "inventory", sources, db, ct);
        await Add(db.ControlBaselines.Where(x => x.RegisteredSystemId == system.Id), "baseline", sources, db, ct);
        var baselines = db.ControlBaselines.Where(x => x.RegisteredSystemId == system.Id).Select(x => x.Id);
        var bindingIds = await db.BaselineCatalogBindings.AsNoTracking().Where(x => baselines.Contains(x.ControlBaselineId)
            && x.TenantId == system.TenantId).Select(x => x.Id).ToListAsync(ct);
        foreach (var bindingId in bindingIds)
        {
            var binding = await CatalogSourceReader.ReadBindingAsync(db, x => x.Id == bindingId
                && x.TenantId == system.TenantId && baselines.Contains(x.ControlBaselineId), ct)
                ?? throw new DbUpdateConcurrencyException("A retained catalog source changed during readiness capture.");
            AddRows(new[] { binding }, "requirement-catalog", sources, db);
        }
        await Add(db.ControlInheritances.Where(x => baselines.Contains(x.ControlBaselineId)), "responsibility", sources, db, ct);
        await Add(db.ControlTailorings.Where(x => baselines.Contains(x.ControlBaselineId)), "baseline", sources, db, ct);
        await Add(db.ControlImplementations.Where(x => x.RegisteredSystemId == system.Id), "ssp", sources, db, ct);
        var implementations = db.ControlImplementations.Where(x => x.RegisteredSystemId == system.Id).Select(x => x.Id);
        await Add(db.NarrativeVersions.Where(x => implementations.Contains(x.ControlImplementationId)), "ssp", sources, db, ct);
        await Add(db.ControlValidationLinks.Where(x => implementations.Contains(x.ControlImplementationId)), "evidence", sources, db, ct);
        await Add(db.SspSections.Where(x => x.RegisteredSystemId == system.Id), "ssp", sources, db, ct);
        await Add(db.Set<Ato.Copilot.Core.Models.Compliance.SystemDesignWorkspace>().Where(x => x.SystemId == system.Id), "system-design", sources, db, ct);
        await Add(db.Set<Ato.Copilot.Core.Models.Compliance.SystemDesignRevision>().Where(x => x.SystemId == system.Id), "system-design", sources, db, ct);
        await Add(db.SecurityAssessmentPlans.Where(x => x.RegisteredSystemId == system.Id), "sap", sources, db, ct);
        var plans = db.SecurityAssessmentPlans.Where(x => x.RegisteredSystemId == system.Id).Select(x => x.Id);
        await Add(db.SapControlEntries.Where(x => plans.Contains(x.SecurityAssessmentPlanId)), "sap", sources, db, ct);
        await Add(db.SapTeamMembers.Where(x => plans.Contains(x.SecurityAssessmentPlanId)), "sap", sources, db, ct);
        await Add(db.SecurityAssessmentReports.Where(x => x.RegisteredSystemId == system.Id), "sar", sources, db, ct);
        var reports = db.SecurityAssessmentReports.Where(x => x.RegisteredSystemId == system.Id).Select(x => x.Id);
        await Add(db.SarSections.Where(x => reports.Contains(x.SecurityAssessmentReportId)), "sar", sources, db, ct);
        await Add(db.ControlEffectivenessRecords.Where(x => x.RegisteredSystemId == system.Id), "assessment-results", sources, db, ct);
        var assessments = db.ControlEffectivenessRecords.Where(x => x.RegisteredSystemId == system.Id).Select(x => x.AssessmentId);
        await Add(db.Assessments.Where(x => x.RegisteredSystemId == system.Id || assessments.Contains(x.Id)), "assessment-results", sources, db, ct);
        await Add(db.PoamItems.Where(x => x.RegisteredSystemId == system.Id), "poam", sources, db, ct);
        var poams = db.PoamItems.Where(x => x.RegisteredSystemId == system.Id).Select(x => x.Id);
        await Add(db.PoamMilestones.Where(x => poams.Contains(x.PoamItemId)), "poam", sources, db, ct);
        await Add(db.Deviations.Where(x => x.RegisteredSystemId == system.Id), "poam", sources, db, ct);
        await Add(db.ContingencyPlanReferences.Where(x => x.RegisteredSystemId == system.Id), "ssp", sources, db, ct);
        await Add(db.PrivacyThresholdAnalyses.Where(x => x.RegisteredSystemId == system.Id), "privacy", sources, db, ct);
        await Add(db.PrivacyImpactAssessments.Where(x => x.RegisteredSystemId == system.Id), "privacy", sources, db, ct);
        await Add(db.SystemInterconnections.Where(x => x.RegisteredSystemId == system.Id), "interconnection", sources, db, ct);
        var connections = db.SystemInterconnections.Where(x => x.RegisteredSystemId == system.Id).Select(x => x.Id);
        await Add(db.InterconnectionAgreements.Where(x => connections.Contains(x.SystemInterconnectionId)), "interconnection", sources, db, ct);
        await Add(db.EvidenceArtifacts.Where(x => x.RegisteredSystemId == system.Id), "evidence", sources, db, ct);
        var baseline = await db.ControlBaselines.AsNoTracking().SingleOrDefaultAsync(x => x.RegisteredSystemId == system.Id, ct);
        var coverage = await RequirementCoverageDocumentData.LoadAsync(db, system.Id, baseline,
            await db.ControlImplementations.AsNoTracking().Where(x => x.RegisteredSystemId == system.Id).ToListAsync(ct), ct);
        var requirementEvidenceIds = coverage.Controls.SelectMany(x => x.Responses).SelectMany(x => x.Evidence ?? [])
            .Select(x => x.ArtifactId).Distinct().ToArray();
        foreach (var evidence in await db.EvidenceArtifacts.AsNoTracking().Where(x => x.RegisteredSystemId == system.Id
            && !x.IsDeleted && (requirementEvidenceIds.Contains(x.Id)
                || x.ControlImplementationId != null && implementations.Contains(x.ControlImplementationId))).ToListAsync(ct))
        {
            var bytes = await EvidenceHashAsync(services.GetRequiredService<IEvidenceArtifactService>(), evidence.Id, ct);
            sources.Add(new("evidence-bytes", evidence.Id, null, bytes ?? Hash("Unavailable"), evidence.FileName));
        }
        await Add(db.CapabilitySubscriptions.Where(x => x.RegisteredSystemId == system.Id), "provider-authorization", sources, db, ct);
        await Add(db.Set<CapabilityResponsibilityConfirmation>().Where(x => x.RegisteredSystemId == system.Id), "responsibility", sources, db, ct);
        await Add(db.SecurityCategorizations.Where(x => x.RegisteredSystemId == system.Id), "categorization", sources, db, ct);
        var categorizations = db.SecurityCategorizations.Where(x => x.RegisteredSystemId == system.Id).Select(x => x.Id);
        await Add(db.Set<InformationType>().Where(x => categorizations.Contains(x.SecurityCategorizationId)), "categorization", sources, db, ct);
        await Add(db.RmfRoleAssignments.Where(x => x.RegisteredSystemId == system.Id), "roles", sources, db, ct);
        await Add(db.SystemRoleAssignments.Where(x => x.RegisteredSystemId == system.Id), "roles", sources, db, ct);
        await Add(db.OrganizationRoleAssignments.Where(x => x.TenantId == system.TenantId), "roles", sources, db, ct);
        var systemPeople = db.SystemRoleAssignments.Where(x => x.RegisteredSystemId == system.Id).Select(x => x.PersonId);
        var organizationPeople = db.OrganizationRoleAssignments.Where(x => x.TenantId == system.TenantId).Select(x => x.PersonId);
        await Add(db.Persons.Where(x => systemPeople.Contains(x.Id) || organizationPeople.Contains(x.Id)), "roles", sources, db, ct);
        var providerGaps = new List<string>();
        var providers = await ProviderDocumentProvenance.ResolveAsync(db, system, providerGaps, ct);
        sources.AddRange(providers.Select(x => new PackageReadinessSource("provider-authorization",
            x.Adoption.Id.ToString(), x.Release.Revision.ToString(),
            Hash(new { adoption = x.Adoption.SnapshotHash, revision = x.Revision.SnapshotHash,
                release = x.Release.SnapshotHash, context = x.Context.SnapshotHash }), "Pinned provider adoption")));
        if (providerGaps.Count > 0)
            sources.Add(new("provider-authorization", system.Id, null, Hash(providerGaps.Order(StringComparer.Ordinal)), "Unresolved provider provenance"));
        if (await db.CapabilitySubscriptions.AnyAsync(x => x.RegisteredSystemId == system.Id && x.IsActive, ct))
        {
            var responsibility = await PreviewResponsibilitiesAsync(db, services, system.Id, ct);
            sources.Add(new("responsibility", system.Id, null,
                Hash(new { responsibility.BaselineId,
                    items = responsibility.Items.OrderBy(x => x.SubscriptionId, StringComparer.Ordinal).ThenBy(x => x.ControlId, StringComparer.Ordinal),
                    pendingImpacts = responsibility.PendingImpacts.OrderBy(x => x.Id) }), "Canonical responsibility review state"));
        }

        DateTime? validUntil = null;
        if (selection.Purpose == PackagePurpose.Legacy)
        {
            await Add(db.AuthorizationDecisions.Where(x => x.RegisteredSystemId == system.Id), "authorization-decision", sources, db, ct);
            var expiries = await db.AuthorizationDecisions.Where(x => x.RegisteredSystemId == system.Id && x.IsActive)
                .Select(x => x.ExpirationDate).ToListAsync(ct);
            validUntil = expiries.Where(x => x > DateTime.UtcNow).Min();
        }
        var agreementDates = await db.InterconnectionAgreements.Where(x => connections.Contains(x.SystemInterconnectionId)
            && x.Status == AgreementStatus.Signed).Select(x => x.ExpirationDate).ToListAsync(ct);
        var thresholds = agreementDates.OfType<DateTime>().SelectMany(x => new[] { x, x.AddDays(-90) })
            .Where(x => x > DateTime.UtcNow).Select(x => (DateTime?)x).Append(validUntil);
        validUntil = thresholds.Min();
        sources = sources.OrderBy(x => x.Kind, StringComparer.Ordinal).ThenBy(x => x.RecordId, StringComparer.Ordinal).ToList();
        return new(Hash(new { selection.Purpose, sources }), sources, validUntil);
    }

    private static async Task Add<T>(IQueryable<T> query, string kind, List<PackageReadinessSource> sources,
        AtoCopilotContext db, CancellationToken ct) where T : class
    {
        AddRows(await query.AsNoTracking().ToListAsync(ct), kind, sources, db);
    }

    private static void AddRows<T>(IReadOnlyList<T> rows, string kind, List<PackageReadinessSource> sources,
        AtoCopilotContext db) where T : class
    {
        var type = db.Model.FindEntityType(typeof(T)) ?? throw new InvalidOperationException("Readiness source model is unavailable.");
        var properties = type.GetProperties().Where(x => x.PropertyInfo != null).OrderBy(x => x.Name).ToArray();
        var key = type.FindPrimaryKey() ?? throw new InvalidOperationException("Readiness source identity is unavailable.");
        foreach (var row in rows)
        {
            var values = properties.ToDictionary(x => x.Name, x => x.PropertyInfo!.GetValue(row));
            var id = string.Join("/", key.Properties.Select(x => x.PropertyInfo!.GetValue(row)?.ToString()));
            var revision = new[] { "Revision", "VersionNumber", "CurrentVersion", "RowVersion" }
                .Select(name => values.GetValueOrDefault(name)).FirstOrDefault(value => value != null);
            var revisionText = revision is byte[] bytes ? bytes.Length == 0 ? null : Convert.ToBase64String(bytes)
                : Convert.ToString(revision, System.Globalization.CultureInfo.InvariantCulture);
            sources.Add(new(kind, id, string.IsNullOrEmpty(revisionText) ? null : revisionText, Hash(values), typeof(T).Name));
        }
    }

    internal static string Hash<T>(T value) => ApprovedProfileDocumentData.Hash(JsonSerializer.Serialize(value, PackageReadinessService.Json));

    internal static Task<CapabilityResponsibilityResponse> PreviewResponsibilitiesAsync(
        AtoCopilotContext db, IServiceProvider services, string systemId, CancellationToken ct)
    {
        // A nested DI scope has an unpopulated scoped TenantContext. Use the same established
        // ambient identity as EF, without constructing an identity or changing its privileges.
        var tenant = services.GetRequiredService<ITenantContextAccessor>().Current
            ?? throw new UnauthorizedAccessException("An established tenant context is required to read readiness responsibility sources.");
        return ActivatorUtilities.CreateInstance<CapabilityResponsibilityService>(services, db, tenant)
            .PreviewAsync(systemId, ct);
    }

    internal static async Task<string?> EvidenceHashAsync(IEvidenceArtifactService evidence, string id, CancellationToken ct)
    {
        var download = await evidence.DownloadAsync(id, ct);
        if (download is null) return null;
        await using var content = download.Value.Content;
        return Convert.ToHexString(await SHA256.HashDataAsync(content, ct)).ToLowerInvariant();
    }
}
