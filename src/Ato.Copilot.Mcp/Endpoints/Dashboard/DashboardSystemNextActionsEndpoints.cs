using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Mcp.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Mcp.Endpoints;

/// <summary>A navigation-only task supported by current persisted state and effective system authority.</summary>
public sealed record SystemNextAction(string Id, string Title, string Description, string Path,
    string ActionLabel, string ResponsibleRole);

/// <summary>Submitted work that requires another effective role.</summary>
public sealed record SystemNextActionWaitingRole(string Role, int Count);

/// <summary>Fresh role-scoped task projection; not a package readiness certification.</summary>
public sealed record SystemNextActionsResponse(string SystemId, DateTime CheckedAt,
    IReadOnlyList<string> EffectiveRoles, IReadOnlyList<SystemNextAction> Items,
    IReadOnlyList<SystemNextActionWaitingRole> WaitingOnOtherRoles);

public static partial class DashboardEndpoints
{
    private static readonly (ProfileSectionType Type, string Label)[] NextActionProfileSections =
    [
        (ProfileSectionType.MissionAndPurpose, "mission and purpose"),
        (ProfileSectionType.UsersAndAccess, "access context"),
        (ProfileSectionType.EnvironmentAndDeployment, "environment and deployment"),
        (ProfileSectionType.DataTypes, "data types"),
        (ProfileSectionType.PortsProtocolsAndServices, "ports, protocols and services"),
    ];

    private static void MapSystemNextActionsRoutes(IEndpointRouteBuilder group)
    {
        group.MapGet("/systems/{systemId}/next-actions", async (
            string systemId, AtoCopilotContext db, ITenantContext tenant,
            ISystemWorkspaceAccessService accessService, HttpContext http, CancellationToken ct) =>
        {
            var access = await accessService.GetAccessAsync(
                tenant.EffectiveTenantId, tenant.PersonId, systemId, tenant.IsCspAdmin, ct);
            if (!access.Permissions.CanRead) return Results.NotFound();
            var system = await db.RegisteredSystems.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == systemId && x.IsActive, ct);
            if (system is null) return Results.NotFound();
            http.Response.Headers.CacheControl = "no-store";
            return Results.Ok(await ProjectSystemNextActions(db, system, access, ct));
        }).WithName("GetSystemNextActions")
            .RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem);
    }

    private static async Task<SystemNextActionsResponse> ProjectSystemNextActions(
        AtoCopilotContext db, RegisteredSystem system, SystemWorkspaceAccessResponse access, CancellationToken ct)
    {
        var actions = new List<(int Priority, SystemNextAction Item)>();
        var waiting = new Dictionary<string, int>(StringComparer.Ordinal);
        var roles = access.Roles.ToHashSet(StringComparer.Ordinal);
        var canAuthorProfile = roles.Contains(nameof(RmfRole.MissionOwner)) && access.Permissions.CanEditProfile;
        var canReviewProfile = roles.Contains(nameof(RmfRole.Issm)) && access.Permissions.CanReviewNarratives;
        var sections = await db.SystemProfileSections.AsNoTracking().Include(x => x.UserCategories)
            .Where(x => x.RegisteredSystemId == system.Id).ToListAsync(ct);
        AddProfileNextActions(actions, waiting, sections, canAuthorProfile, canReviewProfile);
        if (roles.Contains(nameof(RmfRole.Issm)) && access.Permissions.CanManageSystem)
            await AddBoundaryNextActions(db, system.Id, actions, ct);
        await AddNarrativeNextActions(db, system.Id, access, roles, actions, waiting, ct);
        if (roles.Contains(nameof(RmfRole.Sca)) && system.CurrentRmfStep == RmfPhase.Assess)
            await AddAssessmentNextActions(db, system.Id, access.Permissions, actions, ct);
        if (roles.Contains(nameof(RmfRole.AuthorizingOfficial)) && access.Permissions.CanDecideAuthorization
            && system.CurrentRmfStep == RmfPhase.Authorize)
            AddNextAction(actions, 10, "authorization-review", "Review authorization package",
                "Inspect the current package and decision prerequisites. Opening this page does not issue an authorization.",
                "authorize", RmfRole.AuthorizingOfficial);
        return new(system.Id, DateTime.UtcNow, access.Roles,
            actions.OrderBy(x => x.Priority).ThenBy(x => x.Item.Id, StringComparer.Ordinal).Select(x => x.Item).ToArray(),
            waiting.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new SystemNextActionWaitingRole(x.Key, x.Value)).ToArray());
    }

    private static void AddProfileNextActions(List<(int Priority, SystemNextAction Item)> actions,
        Dictionary<string, int> waiting, IReadOnlyList<SystemProfileSection> sections, bool canAuthor, bool canReview)
    {
        foreach (var (type, label) in NextActionProfileSections)
        {
            var section = sections.SingleOrDefault(x => x.SectionType == type);
            AddProfileGovernanceAction(actions, waiting, $"profile-{type}", label,
                $"profile/{type}", section?.GovernanceStatus ?? SspSectionStatus.NotStarted, canAuthor, canReview);
        }
        var users = sections.SingleOrDefault(x => x.SectionType == ProfileSectionType.UsersAndAccess);
        if (canAuthor && users?.UserCategories.Any(x => !x.PendingDeletion) != true)
            AddNextAction(actions, 40, "user-categories-start", "Define user categories",
                "Add the user categories separately from the access-context description.",
                "profile/UsersAndAccess", RmfRole.MissionOwner);
        foreach (var category in users?.UserCategories ?? [])
        {
            if (category.PendingDeletion && category.GovernanceStatus == SspSectionStatus.Approved) continue;
            var label = category.PendingDeletion ? $"removal of user category {category.CategoryName}" : $"user category {category.CategoryName}";
            AddProfileGovernanceAction(actions, waiting, $"user-category-{category.Id}", label,
                "profile/UsersAndAccess", category.GovernanceStatus, canAuthor, canReview);
        }
    }

    private static void AddProfileGovernanceAction(List<(int Priority, SystemNextAction Item)> actions,
        Dictionary<string, int> waiting, string id, string label, string path, SspSectionStatus status,
        bool canAuthor, bool canReview)
    {
        if (status == SspSectionStatus.UnderReview)
        {
            if (canReview)
                AddNextAction(actions, 10, $"{id}-review", $"Review {label}",
                    "Submitted for ISSM review; open the saved content and its review workflow.", path, RmfRole.Issm);
            else AddNextActionWaiting(waiting, RmfRole.Issm, 1);
        }
        else if (canAuthor)
        {
            switch (status)
            {
                case SspSectionStatus.NotStarted:
                    AddNextAction(actions, 40, $"{id}-start", $"Complete {label}",
                        "No saved profile content exists yet. Open this section to begin.", path, RmfRole.MissionOwner);
                    break;
                case SspSectionStatus.Draft:
                    AddNextAction(actions, 30, $"{id}-submit", $"Submit {label} for review",
                        "A saved draft exists. Check its content before submitting it to the ISSM.", path, RmfRole.MissionOwner);
                    break;
                case SspSectionStatus.NeedsRevision:
                    AddNextAction(actions, 20, $"{id}-revise", $"Revise {label}",
                        "Address the saved reviewer feedback before resubmitting.", path, RmfRole.MissionOwner);
                    break;
            }
        }
    }

    private static async Task AddBoundaryNextActions(AtoCopilotContext db, string systemId,
        List<(int Priority, SystemNextAction Item)> actions, CancellationToken ct)
    {
        var hasDefinition = await db.AuthorizationBoundaryDefinitions.AnyAsync(x => x.RegisteredSystemId == systemId, ct);
        var hasScope = await db.BoundaryComponentAssignments.AnyAsync(
            x => x.AuthorizationBoundaryDefinition.RegisteredSystemId == systemId && x.IsInScope, ct);
        // Match the lifecycle gate's legacy system-wide component-assignment fallback.
        var hasLegacyScope = await db.ComponentSystemAssignments.AnyAsync(x => x.RegisteredSystemId == systemId, ct);
        if (!hasDefinition && !hasLegacyScope)
            AddNextAction(actions, 50, "boundary-define", "Define authorization boundary",
                "No boundary definition or legacy component scope is saved for this system.", "boundaries", RmfRole.Issm);
        else if (hasDefinition && !hasScope && !hasLegacyScope)
            AddNextAction(actions, 50, "boundary-scope", "Define boundary component scope",
                "A boundary definition exists, but no in-scope component assignment is saved.", "boundaries", RmfRole.Issm);
    }

    private static async Task AddNarrativeNextActions(AtoCopilotContext db, string systemId,
        SystemWorkspaceAccessResponse access, HashSet<string> roles,
        List<(int Priority, SystemNextAction Item)> actions, Dictionary<string, int> waiting, CancellationToken ct)
    {
        var implementations = db.ControlImplementations.Where(x => x.RegisteredSystemId == systemId);
        var submitted = await implementations.CountAsync(x => x.ApprovalStatus == SspSectionStatus.UnderReview
            && db.NarrativeVersions.Any(v => v.ControlImplementationId == x.Id && v.VersionNumber == x.CurrentVersion), ct);
        if (submitted > 0)
        {
            if (roles.Contains(nameof(RmfRole.Issm)) && access.Permissions.CanReviewNarratives)
                AddNextAction(actions, 10, "narratives-review", "Review submitted control narratives",
                    $"{submitted} control narratives have a saved current version awaiting review.", "narratives", RmfRole.Issm);
            else AddNextActionWaiting(waiting, RmfRole.Issm, submitted);
        }
        if (!roles.Contains(nameof(RmfRole.Isso))) return;
        if (access.Permissions.CanAuthorNarratives)
        {
            var unfinished = await implementations.CountAsync(x => x.ApprovalStatus == SspSectionStatus.NotStarted
                || x.ApprovalStatus == SspSectionStatus.Draft || x.ApprovalStatus == SspSectionStatus.NeedsRevision, ct);
            if (unfinished > 0)
                AddNextAction(actions, 30, "narratives-author", "Complete control narratives",
                    $"{unfinished} control narratives are not started, in draft, or need revision.", "narratives", RmfRole.Isso);
        }
        if (access.Permissions.CanManageEvidence)
        {
            var unclassified = await db.EvidenceArtifacts.CountAsync(x => x.RegisteredSystemId == systemId
                && !x.IsDeleted && x.NarrativeType == EvidenceNarrativeType.Unclassified, ct);
            if (unclassified > 0)
                AddNextAction(actions, 40, "evidence-classify", "Classify uploaded evidence",
                    $"{unclassified} uploaded evidence artifacts have no narrative classification.", "evidence", RmfRole.Isso);
        }
    }

    private static async Task AddAssessmentNextActions(AtoCopilotContext db, string systemId,
        SystemWorkspacePermissions permissions, List<(int Priority, SystemNextAction Item)> actions, CancellationToken ct)
    {
        var sap = await db.SecurityAssessmentPlans.AsNoTracking().Where(x => x.RegisteredSystemId == systemId)
            .OrderByDescending(x => x.GeneratedAt).ThenBy(x => x.Id).FirstOrDefaultAsync(ct);
        if (sap is null && permissions.CanGenerateSap)
        {
            var baseline = await db.ControlBaselines.AsNoTracking().SingleOrDefaultAsync(x => x.RegisteredSystemId == systemId, ct);
            if (baseline?.ControlIds is { Count: > 0 })
                AddNextAction(actions, 30, "sap-prepare", "Prepare assessment plan",
                    "A control baseline exists, but no Security Assessment Plan is saved.", "assessments?tab=plan", RmfRole.Sca);
        }
        else if (sap?.Status == SapStatus.Draft && permissions.CanFinalizeSap)
            AddNextAction(actions, 30, "sap-complete", "Complete draft assessment plan",
                "The latest saved Security Assessment Plan is a draft. Review its scope, team and schedule.",
                "assessments?tab=plan", RmfRole.Sca);
        if (permissions.CanGenerateSar
            && !await db.SecurityAssessmentReports.AnyAsync(x => x.RegisteredSystemId == systemId, ct)
            && await db.ControlEffectivenessRecords.AnyAsync(x => x.RegisteredSystemId == systemId, ct))
            AddNextAction(actions, 40, "sar-prepare", "Prepare assessment report",
                "Control-effectiveness determinations exist, but no Security Assessment Report is saved.", "assessments", RmfRole.Sca);
    }

    private static void AddNextAction(List<(int Priority, SystemNextAction Item)> actions, int priority,
        string id, string title, string description, string path, RmfRole role) =>
        actions.Add((priority, new(id, title, description, path, "Open", role.ToString())));

    private static void AddNextActionWaiting(Dictionary<string, int> waiting, RmfRole role, int count) =>
        waiting[role.ToString()] = waiting.GetValueOrDefault(role.ToString()) + count;
}
