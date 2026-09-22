using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Mcp.Server;

public sealed partial class WorkspaceToolAuthorizer
{
    private async Task<string> ResolveOwnerAsync(Target target, Dictionary<string, object?> arguments,
        Guid? tenantId, CancellationToken ct)
    {
        var parameter = target switch
        {
            Target.Evidence => "evidence_artifact_id",
            Target.Poam => "poam_id",
            Target.Import => "import_id",
            Target.InventoryItem => "item_id",
            Target.Component => "component_id",
            Target.Sap => "sap_id",
            Target.CollectedEvidence => "evidence_id",
            _ => throw Denied("WORKSPACE_TOOL_NOT_SUPPORTED", "The tool has no approved target resolver.")
        };
        var id = Value(arguments, parameter)
            ?? throw Denied("WORKSPACE_RESOURCE_NOT_FOUND", "The requested resource is not accessible.", 404);
        arguments[parameter] = id;
        await using var db = await OpenAsync(ct);
        var owner = target switch
        {
            Target.Evidence => await db.EvidenceArtifacts.AsNoTracking()
                .Where(e => e.Id == id && !e.IsDeleted && (tenantId == null || e.TenantId == tenantId))
                .Select(e => e.RegisteredSystemId).SingleOrDefaultAsync(ct),
            Target.Poam => await db.PoamItems.AsNoTracking()
                .Where(p => p.Id == id && (tenantId == null || p.TenantId == tenantId))
                .Select(p => p.RegisteredSystemId).SingleOrDefaultAsync(ct),
            Target.Import => await db.ScanImportRecords.AsNoTracking()
                .Where(p => p.Id == id && (tenantId == null || p.TenantId == tenantId))
                .Select(p => p.RegisteredSystemId).SingleOrDefaultAsync(ct),
            Target.InventoryItem => await db.InventoryItems.AsNoTracking()
                .Where(p => p.Id == id && (tenantId == null || p.TenantId == tenantId))
                .Select(p => p.RegisteredSystemId).SingleOrDefaultAsync(ct),
            Target.Component => await db.SystemComponents.AsNoTracking()
                .Where(p => p.Id == id && (tenantId == null || p.TenantId == tenantId))
                .Select(p => p.RegisteredSystemId).SingleOrDefaultAsync(ct),
            Target.Sap => await db.SecurityAssessmentPlans.AsNoTracking()
                .Where(p => p.Id == id && (tenantId == null || p.TenantId == tenantId))
                .Select(p => p.RegisteredSystemId).SingleOrDefaultAsync(ct),
            Target.CollectedEvidence => await (
                from evidence in db.Evidence.AsNoTracking()
                join assessment in db.Assessments.AsNoTracking() on evidence.AssessmentId equals assessment.Id
                where evidence.Id == id && evidence.TenantId == assessment.TenantId
                    && (tenantId == null || evidence.TenantId == tenantId)
                select assessment.RegisteredSystemId).SingleOrDefaultAsync(ct),
            _ => null
        };
        return RequireOwner(owner);
    }

    private async Task ValidateReferencesAsync(References references, string tool, Dictionary<string, object?> arguments,
        string systemId, Guid? tenantId, CancellationToken ct)
    {
        if (references == References.None) return;
        await using var db = await OpenAsync(ct);
        switch (references)
        {
            case References.Boundary:
                var boundary = Value(arguments, "boundary_id");
                if (boundary is null)
                {
                    if (tool == "compliance_list_boundary_components")
                        throw Denied("INVALID_TOOL_TARGET", "A boundary target is required.", 400);
                    return;
                }
                arguments["boundary_id"] = boundary;
                MatchOwner(systemId, await db.AuthorizationBoundaryDefinitions.AsNoTracking()
                    .Where(b => b.Id == boundary && (tenantId == null || b.TenantId == tenantId))
                    .Select(b => b.RegisteredSystemId).SingleOrDefaultAsync(ct));
                break;
            case References.Component:
                var component = Value(arguments, "component_id");
                if (component is null) return;
                arguments["component_id"] = component;
                MatchOwner(systemId, await db.SystemComponents.AsNoTracking()
                    .Where(c => c.Id == component && (tenantId == null || c.TenantId == tenantId))
                    .Select(c => c.RegisteredSystemId).SingleOrDefaultAsync(ct));
                break;
            case References.Assessment:
            case References.OptionalAssessment:
                var assessment = Value(arguments, "assessment_id");
                if (assessment is null)
                {
                    if (references == References.OptionalAssessment) return;
                    throw Denied("INVALID_TOOL_TARGET", "An assessment target is required.", 400);
                }
                arguments["assessment_id"] = assessment;
                MatchOwner(systemId, await db.Assessments.AsNoTracking()
                    .Where(a => a.Id == assessment && (tenantId == null || a.TenantId == tenantId))
                    .Select(a => a.RegisteredSystemId).SingleOrDefaultAsync(ct));
                break;
            case References.Finding:
                var finding = Value(arguments, "finding_id")
                    ?? throw Denied("INVALID_TOOL_TARGET", "A finding target is required.", 400);
                arguments["finding_id"] = finding;
                MatchOwner(systemId, await FindingOwnerAsync(db, finding, tenantId, ct));
                break;
            case References.Imports:
                var importsJson = Value(arguments, "import_ids");
                if (importsJson is null) return;
                var imports = ParseArray<string>(importsJson);
                for (var index = 0; index < imports.Count; index++)
                {
                    var id = RequireIdentifier(imports[index]);
                    imports[index] = id;
                    MatchOwner(systemId, await db.ScanImportRecords.AsNoTracking()
                        .Where(r => r.Id == id && (tenantId == null || r.TenantId == tenantId))
                        .Select(r => r.RegisteredSystemId).SingleOrDefaultAsync(ct));
                }
                arguments["import_ids"] = JsonSerializer.Serialize(imports);
                break;
            case References.RiskAcceptances:
                var decision = Value(arguments, "decision_type");
                if (decision is null || !Enum.GetNames<AuthorizationDecisionType>()
                        .Contains(decision, StringComparer.OrdinalIgnoreCase))
                    throw Denied("WORKSPACE_TOOL_NOT_SUPPORTED", "The authorization decision type is not supported.", 400);
                var risksJson = Value(arguments, "risk_acceptances");
                if (risksJson is null) return;
                var risks = ParseArray<RiskAcceptanceInput>(risksJson);
                foreach (var risk in risks)
                {
                    if (risk is null)
                        throw Denied("INVALID_TOOL_TARGET", "Every risk acceptance requires a finding.", 400);
                    risk.FindingId = RequireIdentifier(risk.FindingId);
                    MatchOwner(systemId, await FindingOwnerAsync(db, risk.FindingId, tenantId, ct));
                }
                arguments["risk_acceptances"] = JsonSerializer.Serialize(risks);
                break;
            default:
                throw Denied("WORKSPACE_TOOL_NOT_SUPPORTED", "The tool has no approved linked-target resolver.");
        }
    }

    private async Task ValidateComponentLinksAsync(Dictionary<string, object?> arguments, string systemId,
        Guid? tenantId, CancellationToken ct)
    {
        var id = Value(arguments, "component_id")!;
        await using var db = await OpenAsync(ct);
        var owners = await (from link in db.PoamComponentLinks.AsNoTracking()
                            join poam in db.PoamItems.AsNoTracking() on link.PoamItemId equals poam.Id
                            where link.SystemComponentId == id
                                && (tenantId == null || link.TenantId == tenantId)
                            select new { poam.RegisteredSystemId, LinkTenant = link.TenantId, PoamTenant = poam.TenantId })
            .Distinct().ToListAsync(ct);
        foreach (var owner in owners)
        {
            if (owner.LinkTenant != owner.PoamTenant)
                throw Denied("WORKSPACE_TOOL_TARGET_MISMATCH", "A linked resource belongs to another organization.");
            MatchOwner(systemId, owner.RegisteredSystemId);
        }
    }

    private async Task ValidateInventoryLinksAsync(string tool, Dictionary<string, object?> arguments,
        string systemId, Guid? tenantId, CancellationToken ct)
    {
        var itemId = tool == "inventory_get" ? Value(arguments, "item_id") : null;
        await using var db = await OpenAsync(ct);
        var selected = db.InventoryItems.AsNoTracking().Where(i => i.RegisteredSystemId == systemId
            && (tenantId == null || i.TenantId == tenantId) && (itemId == null || i.Id == itemId));
        var foreignChildren = await (from parent in selected
                                     join child in db.InventoryItems on parent.Id equals child.ParentHardwareId
                                     where child.RegisteredSystemId != systemId || child.TenantId != parent.TenantId
                                     select child.Id).AnyAsync(ct);
        var foreignParent = await (from child in selected
                                   join parent in db.InventoryItems on child.ParentHardwareId equals parent.Id
                                   where parent.RegisteredSystemId != systemId || parent.TenantId != child.TenantId
                                   select parent.Id).AnyAsync(ct);
        if (foreignChildren || foreignParent)
            throw Denied("WORKSPACE_TOOL_TARGET_MISMATCH", "An inventory relationship crosses the selected system.");
    }

    private Task<AtoCopilotContext> OpenAsync(CancellationToken ct) =>
        http.RequestServices.GetRequiredService<IDbContextFactory<AtoCopilotContext>>().CreateDbContextAsync(ct);

    private static Task<string?> FindingOwnerAsync(AtoCopilotContext db, string id, Guid? tenantId, CancellationToken ct) =>
        (from finding in db.Findings.AsNoTracking()
         join assessment in db.Assessments.AsNoTracking() on finding.AssessmentId equals assessment.Id
         where finding.Id == id && finding.TenantId == assessment.TenantId
             && (tenantId == null || finding.TenantId == tenantId)
         select assessment.RegisteredSystemId).SingleOrDefaultAsync(ct);

    private static List<T> ParseArray<T>(string json)
    {
        try
        {
            var values = JsonSerializer.Deserialize<List<T>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (values is null || values.Count > 100)
                throw Denied("INVALID_TOOL_TARGET", "Provide a JSON array containing at most 100 target references.", 400);
            return values;
        }
        catch (JsonException)
        {
            throw Denied("INVALID_TOOL_TARGET", "Target references must use the native JSON array contract.", 400);
        }
    }

    private static string RequireIdentifier(string? value) => string.IsNullOrWhiteSpace(value)
        ? throw Denied("INVALID_TOOL_TARGET", "Every referenced target must have a nonempty identifier.", 400)
        : value.Trim();

    private static string RequireOwner(string? owner) => string.IsNullOrWhiteSpace(owner)
        ? throw Denied("WORKSPACE_RESOURCE_NOT_FOUND", "The requested resource has no accessible system owner.", 404)
        : owner;

    private static void MatchOwner(string expected, string? actual)
    {
        if (RequireOwner(actual) != expected)
            throw Denied("WORKSPACE_TOOL_TARGET_MISMATCH", "A linked resource belongs to a different system.");
    }
}
