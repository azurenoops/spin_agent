using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.SystemDesign;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services;
using Ato.Copilot.Core.Services.Roles;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Agents.Compliance.Services;

public sealed partial class SystemDesignService
{
    private async Task ProjectGovernanceAsync(AtoCopilotContext db, string systemId, List<DesignNode> nodes,
        List<DesignEdge> edges, Action<string, DesignNode> add, List<string> fingerprints, CancellationToken ct)
    {
        var roles = await new UnifiedRoleReader(factory).GetSystemRolesAsync(TenantId, systemId, ct);
        var organization = await db.Tenants.AsNoTracking().Where(t => t.Id == TenantId).Select(t => t.DisplayName).SingleOrDefaultAsync(ct);
        fingerprints.Add(Json(roles));
        var system = nodes.Single(n => n.Id == $"system:{systemId}");
        foreach (var role in roles.Roles.Where(r => r.Source != RoleAssignmentSource.NotAssigned))
        {
            var roleName = role.Role switch
            {
                RmfRole.Issm => "ISSM", RmfRole.Isso => "ISSO", RmfRole.Sca => "Security Control Assessor",
                RmfRole.AuthorizingOfficial => "Authorizing Official", RmfRole.MissionOwner => "Mission Owner",
                _ => "System Owner"
            };
            var key = $"{systemId}:{role.Role}";
            var actor = new DesignNode
            {
                Id = $"rmf-contact:{key}", Label = $"{roleName} · {role.PersonDisplayName ?? "Name not recorded"}",
                Kind = "ActorGroup", ProjectionStatus = "Recorded", ReviewState = "Recorded",
                Source = Source("ResolvedRmfRole", key, new { roles.TenantId, roles.RegisteredSystemId, role, organization },
                    $"/systems/{Uri.EscapeDataString(systemId)}/roles", "Recorded", 7,
                    "Unified System team assignment; not technical access, design approval or an authorization decision"),
                Properties = new()
                {
                    ["contextEntityClass"] = "Performer", ["contextEntityCategory"] = "SecurityCompliance",
                    ["contextRole"] = roleName, ["contextOrganization"] = organization,
                    ["personId"] = role.PersonId?.ToString(), ["assignmentSource"] = role.Source.ToString(),
                    ["organizationRoleId"] = role.OrgRoleId?.ToString()
                }
            };
            add("Mission", actor);
            edges.Add(RecordedRelationship("GovernanceAssignment", actor, system, $"Assigned {roleName} responsibility; not network traffic", role));
        }
    }

    private void ProjectPolicyReference(string systemId, SystemComponent component,
        IReadOnlyList<ComponentSystemAssignment> assignments, Action<string, DesignNode> add)
    {
        var references = assignments.Where(a => a.SystemComponentId == component.Id).ToArray();
        if (references.Length == 0)
        {
            add("InventoryBoundary", new()
            {
                Id = $"policy:{component.Id}", Kind = "PolicyReference", Label = component.Name,
                Source = Source("SystemComponent", component.Id, Fields(component, "Name", "Description", "SubType", "Status", "ModifiedAt"),
                    $"/systems/{Uri.EscapeDataString(systemId)}/legal", "Recorded"),
                ProjectionStatus = "Canonical", ReviewState = "Recorded",
                Properties = new() { ["policyId"] = component.Id, ["retention"] = "LegacyUnretainedOrIndirect",
                    ["referenceType"] = component.SubType, ["description"] = component.Description,
                    ["applicabilityReview"] = "Not recorded" }
            });
            return;
        }
        foreach (var reference in references)
        {
            var retained = RetainedPolicySource.Read(reference);
            if (reference.PolicySourceSnapshotJson is not null && retained is null
                || retained is not null && (retained.Id != component.Id || retained.Revision != reference.PolicySourceRevision))
                throw new InvalidOperationException("The retained policy reference identity/version could not be verified.");
            add("InventoryBoundary", new()
            {
                Id = $"policy-reference:{reference.Id}", Kind = "PolicyReference", Label = retained?.Name ?? component.Name,
                Source = Source("PolicyReference", reference.Id,
                    new { reference = Scalars(reference), current = Fields(component, "Name", "Description", "SubType", "Status", "ModifiedAt") },
                    $"/systems/{Uri.EscapeDataString(systemId)}/legal?reference={Uri.EscapeDataString(reference.Id)}", "Recorded", 7,
                    "Retained system policy reference; applicability approval is not established"),
                ProjectionStatus = retained is null ? "LegacyUnretained" : "RetainedReference", ReviewState = "Recorded",
                Properties = new() { ["policyId"] = component.Id, ["rationale"] = reference.PolicyRationale,
                    ["referenceType"] = retained?.SubType ?? component.SubType,
                    ["description"] = retained?.Description ?? (retained is null ? component.Description : null),
                    ["retainedVersion"] = reference.PolicySourceRevision, ["versionLabel"] = retained?.VersionLabel,
                    ["retention"] = retained is null ? "LegacyUnretained" : "Retained", ["applicabilityReview"] = "Not recorded" }
            });
        }
    }
}
