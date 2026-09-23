using System.Text.Json;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Services.Roles;
using Ato.Copilot.Mcp.Services.Tenancy;
using Ato.Copilot.State.Abstractions;

namespace Ato.Copilot.Mcp.Server;

/// <summary>Auditable native-tool permission and persisted-target contract.</summary>
public sealed record WorkspaceToolPolicyDescriptor(
    string ToolName, string Permission, string PrimaryTarget, string LinkedTargets, string Constraints);

/// <summary>
/// Explicit tool policy. Each entry is tied to the concrete tool's target/operation contract.
/// Unmapped tools and operation selectors are denied instead of inferred from name prefixes.
/// </summary>
public sealed partial class WorkspaceToolAuthorizer(HttpContext http, bool conversation, ILogger logger)
{
    private enum Operation { Reference, Read, AuthorNarratives, ReviewNarratives, ManageEvidence, ManageRemediation, DecideAuthorization, VerifyEvidenceIntegrity }
    private enum Target { None, System, Evidence, Poam, Import, InventoryItem, Component, Sap, CollectedEvidence }
    private enum References { None, Boundary, Component, Imports, Assessment, OptionalAssessment, Finding, RiskAcceptances }
    private sealed record Rule(Operation Operation, Target Target, References References = References.None);

    private static readonly IReadOnlyDictionary<string, Rule> Rules = new Dictionary<string, Rule>(StringComparer.Ordinal)
    {
        ["kb_search_nist_controls"] = new(Operation.Reference, Target.None),
        ["kb_explain_nist_control"] = new(Operation.Reference, Target.None),
        ["kb_explain_stig"] = new(Operation.Reference, Target.None),
        ["kb_search_stigs"] = new(Operation.Reference, Target.None),
        ["kb_explain_rmf"] = new(Operation.Reference, Target.None),
        ["kb_explain_impact_level"] = new(Operation.Reference, Target.None),
        ["kb_get_fedramp_template_guidance"] = new(Operation.Reference, Target.None),
        ["compliance_get_system"] = new(Operation.Read, Target.System),
        ["compliance_narrative_history"] = new(Operation.Read, Target.System),
        ["compliance_narrative_diff"] = new(Operation.Read, Target.System),
        ["narrative_set_policy"] = new(Operation.AuthorNarratives, Target.System),
        ["narrative_set_technical"] = new(Operation.AuthorNarratives, Target.System),
        ["compliance_write_narrative"] = new(Operation.AuthorNarratives, Target.System),
        ["compliance_rollback_narrative"] = new(Operation.AuthorNarratives, Target.System),
        ["compliance_submit_narrative"] = new(Operation.AuthorNarratives, Target.System),
        ["compliance_review_narrative"] = new(Operation.ReviewNarratives, Target.System),
        ["evidence_classify"] = new(Operation.ManageEvidence, Target.Evidence),
        ["compliance_get_poam"] = new(Operation.Read, Target.Poam),
        ["compliance_update_poam"] = new(Operation.ManageRemediation, Target.Poam),
        ["compliance_get_baseline"] = new(Operation.Read, Target.System),
        ["compliance_get_categorization"] = new(Operation.Read, Target.System),
        ["compliance_get_system_profile"] = new(Operation.Read, Target.System),
        ["compliance_get_profile_completeness"] = new(Operation.Read, Target.System),
        ["compliance_get_roadmap"] = new(Operation.Read, Target.System),
        ["compliance_get_roadmap_progress"] = new(Operation.Read, Target.System),
        ["compliance_get_control_validation"] = new(Operation.Read, Target.System),
        ["compliance_list_imports"] = new(Operation.Read, Target.System),
        ["compliance_get_import_summary"] = new(Operation.Read, Target.Import),
        ["compliance_list_nessus_imports"] = new(Operation.Read, Target.System),
        ["compliance_list_prisma_policies"] = new(Operation.Read, Target.System),
        ["compliance_prisma_trend"] = new(Operation.Read, Target.System, References.Imports),
        ["compliance_list_boundary_definitions"] = new(Operation.Read, Target.System),
        ["compliance_list_boundary_components"] = new(Operation.Read, Target.System, References.Boundary),
        ["compliance_boundary_gap_analysis"] = new(Operation.Read, Target.System, References.Boundary),
        ["compliance_list_rmf_roles"] = new(Operation.Read, Target.System),
        ["inventory_get"] = new(Operation.Read, Target.InventoryItem),
        ["inventory_list"] = new(Operation.Read, Target.System),
        ["inventory_completeness"] = new(Operation.Read, Target.System),
        ["compliance_list_interconnections"] = new(Operation.Read, Target.System),
        ["compliance_check_privacy_compliance"] = new(Operation.Read, Target.System),
        ["compliance_list_poam"] = new(Operation.Read, Target.System, References.Component),
        ["compliance_poam_metrics"] = new(Operation.Read, Target.System),
        ["compliance_poam_trend"] = new(Operation.Read, Target.System),
        ["compliance_poam_by_component"] = new(Operation.Read, Target.Component),
        ["compliance_list_saps"] = new(Operation.Read, Target.System),
        ["compliance_get_sap"] = new(Operation.Read, Target.Sap),
        ["compliance_narrative_progress"] = new(Operation.Read, Target.System),
        ["compliance_narrative_approval_progress"] = new(Operation.Read, Target.System),
        ["compliance_ssp_completeness"] = new(Operation.Read, Target.System),
        ["compliance_generate_ssp"] = new(Operation.Read, Target.System),
        ["compliance_export_oscal_ssp"] = new(Operation.Read, Target.System),
        ["compliance_generate_sar"] = new(Operation.Read, Target.System, References.Assessment),
        ["compliance_generate_rar"] = new(Operation.Read, Target.System, References.Assessment),
        ["compliance_issue_authorization"] = new(Operation.DecideAuthorization, Target.System, References.RiskAcceptances),
        ["compliance_accept_risk"] = new(Operation.DecideAuthorization, Target.System, References.Finding),
        ["compliance_verify_evidence"] = new(Operation.VerifyEvidenceIntegrity, Target.CollectedEvidence),
        ["compliance_check_evidence_completeness"] = new(Operation.Read, Target.System, References.OptionalAssessment),
    };

    /// <summary>Auditable, explicit policy coverage; a listed name still requires its target and operation checks.</summary>
    public static IReadOnlyCollection<string> SupportedToolNames { get; } =
        Array.AsReadOnly(Rules.Keys.Order(StringComparer.Ordinal).ToArray());

    public static IReadOnlyCollection<WorkspaceToolPolicyDescriptor> ReviewedPolicies { get; } =
        Array.AsReadOnly(Rules.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r =>
            new WorkspaceToolPolicyDescriptor(r.Key,
                r.Value.Operation == Operation.Reference ? "ValidatedWorkspace" : $"Can{r.Value.Operation}",
                r.Value.Target.ToString(), r.Value.References.ToString(),
                r.Key == "compliance_get_control_validation" ? "Only action=get or omitted; add/delete denied."
                : r.Key == "compliance_verify_evidence" ? "CanManageEvidence OR effective assigned Sca; persisted evidence/assessment owner and qualified verifier required. Integrity only, not approval or authoring."
                : r.Key == "compliance_poam_metrics" ? "Explicit system_id required; unbounded aggregates denied."
                : r.Key == "compliance_issue_authorization" ? "Defined decision type; every inline finding must belong to the selected system; native JSON property names."
                : r.Value.Operation == Operation.Reference ? "Public reference data only."
                : "CanRead required; every persisted reference must match; chat requires the same selected system."))
            .ToArray());

    private WorkspaceException? _failure;

    /// <summary>Re-raises a denial even if an agent catches it and produces a success-shaped answer.</summary>
    public void ThrowIfDenied()
    {
        if (_failure is { } failure) throw failure;
    }

    public async Task AuthorizeAsync(string tool, Dictionary<string, object?> arguments, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            ThrowIfDenied();
            var identity = WorkspaceChatScope.Resolve(http);
            var tenant = http.RequestServices.GetService<ITenantContext>();
            var oversight = identity.Kind == "csp" || identity.Mode == "support";
            if (tenant is null || !tenant.IsWorkspaceRequest
                || tenant.PersonId != identity.PersonId
                || tenant.EffectiveTenantId != (identity.TenantId ?? Guid.Empty)
                || tenant.IsCspAdmin != oversight)
                throw Denied("WORKSPACE_CONTEXT_INVALID", "The validated workspace is not bound to this tool request.");

            if (!Rules.TryGetValue(tool, out var rule))
                throw Denied("WORKSPACE_TOOL_NOT_SUPPORTED", "This tool has no approved workspace operation mapping.");
            if (arguments.Keys.Any(k => k.Equals("operation", StringComparison.OrdinalIgnoreCase)
                || k.Equals("action", StringComparison.OrdinalIgnoreCase) && tool != "compliance_get_control_validation"))
                throw Denied("WORKSPACE_TOOL_NOT_SUPPORTED", "This tool does not authorize caller-selected sub-operations.");
            if (tool == "compliance_get_control_validation")
            {
                var action = Value(arguments, "action") ?? "get";
                if (!action.Equals("get", StringComparison.OrdinalIgnoreCase))
                    throw Denied("WORKSPACE_TOOL_NOT_SUPPORTED", "Control-validation mutations need an explicit domain permission.");
                arguments["action"] = "get";
            }

            var explicitSystem = Value(arguments, "system_id", "systemId");
            string? target = rule.Target switch
            {
                Target.None => null,
                Target.System => explicitSystem
                    ?? throw Denied("SYSTEM_TARGET_REQUIRED", "The tool requires an explicit system target.", 400),
                Target.Sap when Value(arguments, "sap_id") is null => explicitSystem
                    ?? throw Denied("SYSTEM_TARGET_REQUIRED", "The tool requires a SAP or system target.", 400),
                _ => await ResolveOwnerAsync(rule.Target, arguments, identity.TenantId, ct)
            };
            if (target is not null && explicitSystem is not null && target != explicitSystem)
                throw Denied("WORKSPACE_TOOL_TARGET_MISMATCH", "The supplied system does not own the requested resource.");
            if (target is not null && conversation)
            {
                var selected = http.Items.TryGetValue(WorkspaceChatScope.IdentityItem, out var selectedIdentity)
                    && selectedIdentity is ConversationIdentity scope ? scope.SystemId : null;
                if (selected is null)
                    throw Denied("SYSTEM_CONTEXT_REQUIRED", "Select a system before using system tools in this conversation.", 400);
                if (selected != target)
                    throw Denied("WORKSPACE_TOOL_TARGET_MISMATCH", "The tool target differs from the selected conversation system.");
            }

            if (target is not null)
            {
                var access = await http.RequestServices.GetRequiredService<ISystemWorkspaceAccessService>()
                    .GetAccessAsync(identity.TenantId ?? Guid.Empty, identity.PersonId, target, oversight, ct);
                var allowed = rule.Operation == Operation.VerifyEvidenceIntegrity
                    ? EvidenceIntegrityVerificationPolicy.IsAllowed(access)
                    : Permits(rule.Operation, access.Permissions);
                if (!access.Permissions.CanRead || !allowed)
                    throw Denied("WORKSPACE_OPERATION_NOT_AUTHORIZED", "Your applicable system assignments do not authorize this operation.");
                await ValidateReferencesAsync(rule.References, tool, arguments, target, identity.TenantId, ct);
                if (rule.Target == Target.Component)
                    await ValidateComponentLinksAsync(arguments, target, identity.TenantId, ct);
                if (tool is "inventory_get" or "inventory_list")
                    await ValidateInventoryLinksAsync(tool, arguments, target, identity.TenantId, ct);
                if (tool == "compliance_review_narrative"
                    && Value(arguments, "decision")?.ToLowerInvariant() is not ("approve" or "request_revision"))
                    throw Denied("WORKSPACE_TOOL_NOT_SUPPORTED", "The review decision must be approve or request_revision.", 400);
                arguments.Remove("systemId");
                arguments["system_id"] = target;
            }

            foreach (var key in arguments.Keys.ToArray())
            {
                var normalized = key.Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
                if (normalized is "userid" or "userrole" or "roles" or "permissions" or "personid"
                    or "tenantid" or "organizationid" or "iscspadmin" or "isadmin" or "oid" or "tid")
                    arguments.Remove(key);
            }
            arguments["user_id"] = identity.ActorId;
            logger.LogInformation(
                "Workspace tool authorized | Tool: {Tool} | Operation: {Operation} | SystemId: {SystemId} | DirectoryTenantId: {DirectoryTenantId} | ActorObjectId: {ActorObjectId} | WorkspaceTenantId: {WorkspaceTenantId}",
                tool, rule.Operation, target, identity.DirectoryId, identity.ObjectId, identity.TenantId);
        }
        catch (WorkspaceException exception)
        {
            Interlocked.CompareExchange(ref _failure, exception, null);
            logger.LogWarning("Workspace tool denied | Tool: {Tool} | Code: {Code} | TraceId: {TraceId}",
                tool, exception.Code, http.TraceIdentifier);
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Workspace tool authorization unavailable | Tool: {Tool} | TraceId: {TraceId}",
                tool, http.TraceIdentifier);
            var failure = Denied("WORKSPACE_AUTHORIZATION_UNAVAILABLE", "Tool authorization could not be completed.", 503);
            Interlocked.CompareExchange(ref _failure, failure, null);
            throw failure;
        }
    }

    private static string? Value(Dictionary<string, object?> arguments, params string[] names)
    {
        var values = new List<string>();
        foreach (var pair in arguments.Where(p => names.Contains(p.Key, StringComparer.OrdinalIgnoreCase)))
        {
            if (pair.Value is null || pair.Value is JsonElement { ValueKind: JsonValueKind.Null }) continue;
            var value = pair.Value switch
            {
                string text => text,
                JsonElement { ValueKind: JsonValueKind.String } json => json.GetString(),
                _ => throw Denied("INVALID_TOOL_TARGET", "Tool targets must be nonempty strings.", 400)
            };
            if (string.IsNullOrWhiteSpace(value))
                throw Denied("INVALID_TOOL_TARGET", "Tool targets must be nonempty strings.", 400);
            values.Add(value.Trim());
        }
        return values.Distinct(StringComparer.Ordinal).Count() > 1
            ? throw Denied("WORKSPACE_TOOL_TARGET_MISMATCH", "Conflicting tool targets are not allowed.")
            : values.FirstOrDefault();
    }

    private static bool Permits(Operation operation, SystemWorkspacePermissions permissions) => operation switch
    {
        Operation.Read => permissions.CanRead,
        Operation.AuthorNarratives => permissions.CanAuthorNarratives,
        Operation.ReviewNarratives => permissions.CanReviewNarratives,
        Operation.ManageEvidence => permissions.CanManageEvidence,
        Operation.ManageRemediation => permissions.CanManageRemediation,
        Operation.DecideAuthorization => permissions.CanDecideAuthorization,
        _ => false
    };

    private static WorkspaceException Denied(string code, string message, int status = 403) => new(status, code, message);
}
