namespace Ato.Copilot.Core.Dtos.Dashboard;

public sealed record SystemSetupContactSelection(Guid PersonId, string Responsibility = "preparationContact");

public sealed record SystemSetupDraft
{
    public string Name { get; init; } = "";
    public string Acronym { get; init; } = "";
    public string MissionPurpose { get; init; } = "";
    public string Objective { get; init; } = "";
    public SystemSetupContactSelection? Contact { get; init; }
    public string SourceChoice { get; init; } = "blank";
    public string HostingChoice { get; init; } = "deferred";
    public string MonitoringChoice { get; init; } = "configureLater";
    public string LastScreen { get; init; } = "s-details";
    public string? ExpectedIdentityRevision { get; init; }
}

public sealed record SystemSetupIntent(
    int SchemaVersion, string Objective, SystemSetupContactSelection? Contact,
    string SourceChoice, string HostingChoice, string MonitoringChoice, string LastScreen,
    IReadOnlyList<string> UnconfirmedFields);

public sealed record SystemSetupConfirmRequest(long ReviewRevision, bool Confirmed, string? IdentityRevision = null);
public sealed record SystemSetupContact(Guid PersonId, string DisplayName);
public sealed record SystemSetupTeamRole(string Role, string? DisplayName, string Source);
public sealed record SystemSetupMonitoring(string Configuration, string Access, string ScopeReview, string Collection, string Evaluation);
public sealed record SystemSetupTask(string Id, string Label, string Detail, string State,
    string Contribution, string Link, string OwnerRole, bool CanAct);
public sealed record SystemSetupLinks(string? Documents, string? Hosting, string? Capabilities, string? Monitoring);
public sealed record SystemSetupView(
    string SystemId, Guid TenantId, string DisplayName, string OrganizationName,
    long Revision, string IdentityRevision, DateTimeOffset SavedAt, string SetupState,
    DateTimeOffset? CompletedAt, SystemSetupDraft Draft, bool CanManage,
    IReadOnlyList<SystemSetupContact> Contacts, IReadOnlyList<SystemSetupTeamRole> EffectiveTeam,
    IReadOnlyList<object> Sources, IReadOnlyList<SystemSetupTask> Tasks, SystemSetupMonitoring Monitoring,
    SystemSetupLinks Links, IReadOnlyList<string> UnconfirmedFields);
public sealed record SystemSetupSummary(string SystemId, string DisplayName, long Revision,
    DateTimeOffset SavedAt, string SetupState, SystemSetupResumeLinks Links);
public sealed record SystemSetupResumeLinks(string Resume);
