using System.Text.Json.Serialization;

namespace Ato.Copilot.Core.Authorization;

public static class AdminPortalActions
{
    public const string OrganizationOverviewView = "organization.overview.view";
    public const string OrganizationProfileView = "organization.profile.view";
    public const string OrganizationProfileEdit = "organization.profile.edit";
    public const string OrganizationPeopleView = "organization.people.view";
    public const string OrganizationMembershipsManage = "organization.memberships.manage";
    public const string OrganizationAdministratorsManage = "organization.administrators.manage";
    public const string OrganizationSubscriptionsManage = "organization.subscriptions.manage";
    public const string OrganizationSetupManage = "organization.setup.manage";
    public const string OrganizationImportsManage = "organization.imports.manage";
    public const string OrganizationTemplatesManage = "organization.templates.manage";
    public const string OrganizationAuditView = "organization.audit.view";

    public const string ProviderProfileView = "provider.profile.view";
    public const string ProviderProfileEdit = "provider.profile.edit";
    public const string ProviderOrganizationsView = "provider.organizations.view";
    public const string ProviderOrganizationsManage = "provider.organizations.manage";
    public const string ProviderOfferingsManage = "provider.offerings.manage";
    public const string ProviderAuditView = "provider.audit.view";

    public const string PlatformMigrationPreview = "platform.migration.preview";
    public const string PlatformMigrationExecute = "platform.migration.execute";

    public const string SystemView = "system.view";
    public const string SystemEdit = "system.edit";
    public const string SystemAssess = "system.assess";
    public const string SystemApprove = "system.approve";
    public const string SystemAdminister = "system.administer";
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WorkspaceKind
{
    Administration,
    System,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AccessScopeKind
{
    Organization,
    Provider,
    Platform,
    System,
}

public sealed record EffectiveAccessSubject(
    Guid ObjectId,
    string DisplayName,
    Guid TenantId,
    bool IsCspAdmin);

public sealed record AccessBadge(string Label, string Source);

public sealed record EffectiveAccessDestination(
    string Id,
    WorkspaceKind Workspace,
    AccessScopeKind ScopeKind,
    string ScopeId,
    string DisplayName,
    IReadOnlyList<string> Actions,
    IReadOnlyList<AccessBadge> Badges,
    string Availability);

public sealed record EffectiveAccessResult(
    string Version,
    DateTimeOffset GeneratedAt,
    EffectiveAccessSubject Subject,
    string? DefaultDestinationId,
    IReadOnlyList<EffectiveAccessDestination> Destinations);

public interface IEffectiveAccessService
{
    Task<EffectiveAccessResult> ResolveAsync(
        EffectiveAccessSubject subject,
        CancellationToken cancellationToken = default);
}
