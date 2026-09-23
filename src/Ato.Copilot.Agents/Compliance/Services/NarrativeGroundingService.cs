using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Agents.Compliance.Services;

internal sealed class NarrativeGroundingService(
    AtoCopilotContext db, Guid tenantId, NarrativeLibraryService library)
{
    public async Task<string> CaptureAsync(ControlImplementation implementation, string type, CancellationToken ct)
    {
        var systemId = implementation.RegisteredSystemId;
        var system = await db.RegisteredSystems.AsNoTracking()
            .SingleAsync(item => item.TenantId == tenantId && item.Id == systemId, ct);
        var controlId = implementation.ControlId.ToUpperInvariant();
        var definition = await db.NistControls.AsNoTracking().Where(item => item.Id.ToUpper() == controlId)
            .Select(item => new { item.Id, item.Title, item.Description }).FirstOrDefaultAsync(ct);
        var baselines = await db.ControlBaselines.AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.RegisteredSystemId == systemId).ToListAsync(ct);
        var baselineIds = baselines.Select(item => item.Id).ToArray();
        var responsibilities = await db.ControlInheritances.AsNoTracking().Where(item => item.TenantId == tenantId &&
            baselineIds.Contains(item.ControlBaselineId) && item.ControlId == implementation.ControlId).OrderBy(item => item.Id)
            .Select(item => new { item.Id, item.InheritanceType, item.Provider, item.CustomerResponsibility, item.DesignationSource }).ToListAsync(ct);
        var applicability = baselines.Select(item => new { item.Id, item.BaselineLevel, item.OverlayApplied,
            IsApplicable = item.ControlIds.Contains(implementation.ControlId, StringComparer.OrdinalIgnoreCase) }).ToArray();
        var mappings = await ApplicableMappingsAsync(implementation, ct);
        var capabilityIds = mappings.Select(item => item.SecurityCapabilityId).Distinct().ToArray();
        var published = await library.PublishedForSystemAsync(systemId, ct);
        var providerReferences = await ProviderNarrativeLibraryService.ReadApplicableAsync(db, tenantId, systemId, implementation.ControlId, ct);
        var references = published.Concat(providerReferences).Where(item =>
            (item.Scope != "Capability" || capabilityIds.Contains(item.ScopeId)))
            .GroupBy(item => item.ReferenceKey).Select(group => group.MaxBy(item => item.Version)!)
            .SelectMany(reference => reference.Passages.Where(passage =>
                string.Equals(passage.ControlId, implementation.ControlId, StringComparison.OrdinalIgnoreCase) && passage.NarrativeType == type)
                .Select(passage => new { reference.Id, reference.Title, reference.Version, reference.Scope,
                    reference.ScopeId, reference.SourceSha256, passage.Content }))
            .OrderBy(item => item.Id).ThenBy(item => item.Content, StringComparer.Ordinal).ToArray();
        var artifactType = type == "Policy" ? EvidenceNarrativeType.Policy : EvidenceNarrativeType.Technical;
        var artifacts = await db.EvidenceArtifacts.AsNoTracking().Where(item => item.TenantId == tenantId &&
            item.RegisteredSystemId == systemId && item.ControlImplementationId == implementation.Id && !item.IsDeleted &&
            (item.NarrativeType == artifactType || item.NarrativeType == EvidenceNarrativeType.Combined))
            .OrderBy(item => item.Id).Select(item => new { item.Id, item.FileName, item.ContentHash,
                item.Description, item.UploadedAt, item.CollectionMethod }).ToListAsync(ct);
        if (type == "Policy")
        {
            var owners = await db.SecurityCapabilities.AsNoTracking().Where(item => item.TenantId == tenantId &&
                capabilityIds.Contains(item.Id)).OrderBy(item => item.Id).Select(item => new { item.Id, item.Name, item.Owner }).ToListAsync(ct);
            return JsonSerializer.Serialize(new { implementation.ControlId, controlDefinition = definition,
                declaredSystem = new { system.Name, system.Description, system.MissionCriticality },
                applicability, declaredControlOwners = owners, declaredResponsibilities = responsibilities, evidenceArtifactMetadata = artifacts,
                referenceClaims = references, observationStatus = "Unknown: reference claims and artifact metadata are not execution observations." });
        }
        var context = await TechnicalContextAsync(implementation, mappings, ct);
        return JsonSerializer.Serialize(new { implementation.ControlId, controlDefinition = definition,
            declaredSystem = new { system.Name, system.HostingEnvironment }, applicability,
            declaredResponsibilities = responsibilities, context.declaredCapabilities, context.declaredComponents,
            context.declaredBoundaries, context.boundaryScope, context.publishedProviderCapabilities,
            context.linkedValidationMetadata, context.observedEvidence, context.observedFindings,
            evidenceArtifactMetadata = artifacts, referenceClaims = references });
    }

    private async Task<List<CapabilityControlMapping>> ApplicableMappingsAsync(ControlImplementation implementation, CancellationToken ct)
    {
        var systemCapabilities = db.SystemCapabilityLinks.Where(item => item.TenantId == tenantId &&
            item.RegisteredSystemId == implementation.RegisteredSystemId).Select(item => item.SecurityCapabilityId);
        var boundaries = db.AuthorizationBoundaryDefinitions.Where(item => item.TenantId == tenantId &&
            item.RegisteredSystemId == implementation.RegisteredSystemId).Select(item => item.Id);
        return await db.CapabilityControlMappings.AsNoTracking().Where(item => item.TenantId == tenantId &&
            item.ControlId == implementation.ControlId &&
            (item.RegisteredSystemId == implementation.RegisteredSystemId ||
                item.RegisteredSystemId == null && systemCapabilities.Contains(item.SecurityCapabilityId)) &&
            (item.AuthorizationBoundaryDefinitionId == null || boundaries.Contains(item.AuthorizationBoundaryDefinitionId)))
            .OrderBy(item => item.Id).ToListAsync(ct);
    }

    private sealed record TechnicalContext(
        object declaredCapabilities, object declaredComponents, object declaredBoundaries, object boundaryScope,
        object publishedProviderCapabilities, object linkedValidationMetadata, object observedEvidence, object observedFindings);

    private async Task<TechnicalContext> TechnicalContextAsync(
        ControlImplementation implementation, IReadOnlyList<CapabilityControlMapping> mappings, CancellationToken ct)
    {
        var systemId = implementation.RegisteredSystemId;
        var capabilityIds = mappings.Select(item => item.SecurityCapabilityId).Distinct().ToArray();
        var capabilities = await db.SecurityCapabilities.AsNoTracking().Where(item => item.TenantId == tenantId && capabilityIds.Contains(item.Id))
            .OrderBy(item => item.Id).Select(item => new { item.Id, item.Name, item.Provider, item.Description, item.ImplementationStatus }).ToListAsync(ct);
        var componentScope = await ComponentScopeAsync(systemId, mappings, ct);
        var validation = await db.ControlValidationLinks.AsNoTracking().Where(item => item.TenantId == tenantId &&
            item.ControlImplementationId == implementation.Id).OrderBy(item => item.Id)
            .Select(item => new { item.Id, item.LinkType, item.LinkTarget, item.Description, item.ValidatedAt,
                HasValidation = item.ValidatedAt != null, item.IsAutomated }).ToListAsync(ct);
        var assessments = db.Assessments.Where(item => item.TenantId == tenantId && item.RegisteredSystemId == systemId).Select(item => item.Id);
        var evidence = await db.Evidence.AsNoTracking().Where(item => item.TenantId == tenantId &&
            item.ControlId == implementation.ControlId && item.AssessmentId != null && assessments.Contains(item.AssessmentId))
            .OrderBy(item => item.Id).Select(item => new { item.Id, item.EvidenceType, item.Content, item.ContentHash,
                item.ResourceId, item.CollectedAt, item.CollectionMethod, HasVerifiedIntegrity = item.IntegrityVerifiedAt != null }).ToListAsync(ct);
        var findings = await db.Findings.AsNoTracking().Where(item => item.TenantId == tenantId &&
            item.ControlId == implementation.ControlId && assessments.Contains(item.AssessmentId))
            .OrderBy(item => item.Id).Select(item => new { item.Id, item.Title, item.Description, item.Status,
                item.Severity, item.ResourceId, item.Source, item.DiscoveredAt }).ToListAsync(ct);
        var subscribedIds = await db.CapabilitySubscriptions.Where(item => item.RegisteredSystemId == systemId && item.IsActive)
            .Select(item => item.CspInheritedCapabilityId).ToListAsync(ct);
        var providerIds = ProviderNarrativeLibraryService.ParseCapabilityIds(subscribedIds);
        var providers = await db.CspInheritedCapabilities.AsNoTracking().Where(item => providerIds.Contains(item.Id) &&
            item.Status == CspInheritedCapabilityStatus.Mapped && item.CspInheritedComponent.Status == CspInheritedComponentStatus.Published)
            .OrderBy(item => item.Id).Select(item => new { item.Id, item.CspInheritedComponentId,
                item.Name, item.Description, item.MappedNistControlIds }).ToListAsync(ct);
        return new(capabilities, componentScope.Components, componentScope.Boundaries, componentScope.Scope,
            providers.Where(item => item.MappedNistControlIds.Contains(implementation.ControlId, StringComparer.OrdinalIgnoreCase))
                .Select(item => new { item.Id, item.CspInheritedComponentId, item.Name, item.Description }).ToArray(),
            validation, evidence, findings);
    }

    private async Task<(object Components, object Boundaries, object Scope)> ComponentScopeAsync(
        string systemId, IReadOnlyList<CapabilityControlMapping> mappings, CancellationToken ct)
    {
        var capabilities = mappings.Select(item => item.SecurityCapabilityId).Distinct().ToArray();
        var links = await db.ComponentCapabilityLinks.AsNoTracking().Where(item => item.TenantId == tenantId &&
            capabilities.Contains(item.SecurityCapabilityId)).ToListAsync(ct);
        var linkedIds = links.Select(item => item.SystemComponentId).ToArray();
        var boundaries = await db.AuthorizationBoundaryDefinitions.AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.RegisteredSystemId == systemId).ToListAsync(ct);
        var boundaryIds = boundaries.Select(item => item.Id).ToArray();
        var scope = await db.BoundaryComponentAssignments.AsNoTracking().Where(item => item.TenantId == tenantId &&
            boundaryIds.Contains(item.AuthorizationBoundaryDefinitionId) && linkedIds.Contains(item.SystemComponentId!)).ToListAsync(ct);
        var assignments = await db.ComponentSystemAssignments.AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.RegisteredSystemId == systemId && linkedIds.Contains(item.SystemComponentId)).ToListAsync(ct);
        var candidates = await db.SystemComponents.AsNoTracking().Where(item => item.TenantId == tenantId && linkedIds.Contains(item.Id))
            .OrderBy(item => item.Id).ToListAsync(ct);
        var relevantScope = scope.Where(item => links.Where(link => link.SystemComponentId == item.SystemComponentId)
            .Any(link => mappings.Any(mapping => mapping.SecurityCapabilityId == link.SecurityCapabilityId &&
                (mapping.AuthorizationBoundaryDefinitionId == null ||
                 mapping.AuthorizationBoundaryDefinitionId == item.AuthorizationBoundaryDefinitionId)))).ToArray();
        var components = candidates.Where(component =>
        {
            var explicitScope = relevantScope.Where(item => item.SystemComponentId == component.Id).ToArray();
            if (explicitScope.Length > 0) return explicitScope.Any(item => item.IsInScope);
            return links.Where(link => link.SystemComponentId == component.Id).Any(link =>
                mappings.Where(mapping => mapping.SecurityCapabilityId == link.SecurityCapabilityId).Any(mapping =>
                    mapping.AuthorizationBoundaryDefinitionId == null
                        ? component.RegisteredSystemId == systemId || assignments.Any(item => item.SystemComponentId == component.Id)
                        : component.RegisteredSystemId == systemId && component.AuthorizationBoundaryDefinitionId == mapping.AuthorizationBoundaryDefinitionId ||
                          assignments.Any(item => item.SystemComponentId == component.Id && item.AuthorizationBoundaryDefinitionId == mapping.AuthorizationBoundaryDefinitionId)));
        }).ToArray();
        var usedBoundaries = mappings.Select(item => item.AuthorizationBoundaryDefinitionId)
            .Concat(relevantScope.Select(item => item.AuthorizationBoundaryDefinitionId))
            .Concat(components.Select(item => item.AuthorizationBoundaryDefinitionId)).ToHashSet();
        return (components.Select(item => new { item.Id, item.Name, item.Description, item.ComponentType,
                item.Status, item.AzureResourceId, item.AzureResourceType }).ToArray(),
            boundaries.Where(item => usedBoundaries.Contains(item.Id)).OrderBy(item => item.Id)
                .Select(item => new { item.Id, item.Name, item.Description, item.BoundaryType }).ToArray(),
            relevantScope.OrderBy(item => item.Id).Select(item => new { item.SystemComponentId,
                item.AuthorizationBoundaryDefinitionId, item.IsInScope, item.ExclusionRationale, item.InheritanceProvider }).ToArray());
    }
}
