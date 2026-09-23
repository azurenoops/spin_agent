using System.Text.Json.Serialization;

namespace Ato.Copilot.Core.Interfaces.Workspaces;

/// <summary>Atomic organization-owned catalog authoring or provider adoption; never a system operation.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OrganizationCatalogAdditionRequest(
    string IdempotencyKey, string Source, string RecordType, string? RecordId,
    OrganizationCatalogCapabilityRequest? Capability,
    OrganizationCatalogComponentRequest? Component,
    IReadOnlyList<OrganizationCatalogComponentReference> Components,
    IReadOnlyList<OrganizationCatalogComponentRequest> NewComponents,
    string OrganizationContribution, string Owner);

/// <summary>Organization-wide local capability fields.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OrganizationCatalogCapabilityRequest(
    string Name, string Provider, string Category, string Description, string ImplementationStatus, string Owner);

/// <summary>Organization-wide local component fields; no system or boundary assignment.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OrganizationCatalogComponentRequest(
    string Name, string ComponentType, string Description, string Owner);

/// <summary>An eligible organization-local or published provider supporting component.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OrganizationCatalogComponentReference(string Source, string RecordId);

/// <summary>The durable result of an addition; Existing indicates same-intent idempotency replay.</summary>
public sealed record OrganizationCatalogAdditionResult(
    string Source, string RecordType, string RecordId, string Name, bool Existing);
