using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Queries;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services.Roles;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>Read-only projections and focused commands over the retained SAP lifecycle.</summary>
public sealed class AssessmentPlanWorkspaceService(
    AtoCopilotContext db, ISapService sapService, IDocumentTemplateService templates)
{
    public async Task<AssessmentPlanWorkspace> GetAsync(Guid tenantId, string systemId, string? planId,
        AssessmentPlanPermissions authority, CancellationToken ct)
    {
        var system = await SystemAsync(tenantId, systemId, ct);
        var baseline = await db.ControlBaselines.AsNoTracking()
            .SingleOrDefaultAsync(b => b.TenantId == tenantId && b.RegisteredSystemId == systemId, ct);
        var plans = await Plans(tenantId, systemId).OrderWorkingFirst().LoadRetainedDetailsAsync(ct);
        var plan = planId is null ? plans.FirstOrDefault()
            : plans.SingleOrDefault(p => p.Id == planId) ?? throw new KeyNotFoundException();
        var hasBaseline = baseline?.ControlIds.Count > 0;
        var editReason = !authority.CanEditPlan ? authority.EditReason : plan is null ? "Create a plan first."
            : plan.Status != SapStatus.Draft ? "Finalized plans are immutable. Create a revision to continue planning." : null;
        var finalizeReason = !authority.CanFinalizePlan ? authority.FinalizeReason : plan is null ? "Create a plan first."
            : plan.Status != SapStatus.Draft ? "This plan is already finalized." : null;
        var createReason = !authority.CanCreatePlan ? authority.CreateReason
            : !hasBaseline && plans.All(p => p.Status != SapStatus.Draft) ? "Select a nonempty control baseline first." : null;
        var permissions = new AssessmentPlanPermissions(createReason is null, editReason is null,
            finalizeReason is null, createReason, editReason, finalizeReason);
        var warnings = plan is null ? new List<string>()
            : (await sapService.ValidateSapAsync(plan.Id, ct)).Warnings;
        var controls = plan?.ControlEntries.Where(c => !c.IsExcluded).ToList() ?? [];
        var tasks = new List<AssessmentPlanningTask>
        {
            new("title", "Name your plan", "Give the retained assessment plan a recognizable title.",
                !string.IsNullOrWhiteSpace(plan?.Title), false, "Edit title"),
            new("lead", "Choose an assessment lead", "Select a named system person or an existing team member. This does not grant a role.",
                !string.IsNullOrWhiteSpace(plan?.AssessmentLead), false, "Choose lead"),
            new("scope", "Define assessment scope", "Include or exclude retained controls without discarding their procedures.",
                controls.Count > 0, false, "Edit scope"),
            new("approach", "Describe the assessment approach", "Record the approach and rules of engagement.",
                !string.IsNullOrWhiteSpace(plan?.AssessmentApproach), false, "Edit approach"),
            new("team", "Review the assessment team", "Record team members separately from authorization roles.",
                plan?.TeamMembers.Count > 0, false, "Edit team"),
            new("schedule", "Set the assessment schedule", "Record planned start and end dates.",
                plan?.ScheduleStart is not null && plan.ScheduleEnd is not null, false, "Edit schedule"),
            new("procedures", "Review assessment procedures", "Review the retained objectives and methods for included controls.",
                controls.Count > 0 && controls.All(c => c.AssessmentMethods.Count > 0 && c.AssessmentObjectives.Count > 0),
                false, "Review procedures")
        };
        return new(systemId, system.Name, baseline?.BaselineLevel, baseline?.ControlIds.Count ?? 0,
            plan is null ? null : Detail(plan), plans.Select(p => new AssessmentPlanSummary(p.Id, p.Title,
                p.Status.ToString(), p.Revision, p.GeneratedAt, p.FinalizedAt)).ToList(),
            await LeadOptionsAsync(tenantId, systemId, plan, ct), tasks, warnings,
            finalizeReason is null ? [] : [finalizeReason], permissions);
    }

    public async Task<AssessmentPlanPreview> PreviewAsync(Guid tenantId, string systemId, string planId, CancellationToken ct)
    {
        await SystemAsync(tenantId, systemId, ct);
        var plan = await PlanAsync(tenantId, systemId, planId, ct);
        return new(systemId, plan.Id, plan.Revision, SapService.HashContent(plan.Content), plan.Content);
    }

    public async Task<byte[]> ExportAsync(Guid tenantId, string systemId, string planId,
        string format, CancellationToken ct)
    {
        await SystemAsync(tenantId, systemId, ct);
        await PlanAsync(tenantId, systemId, planId, ct);
        return format switch
        {
            "docx" => await templates.RenderDocxAsync(systemId, "sap", null, ct, planId),
            "pdf" => await templates.RenderPdfAsync(systemId, "sap", null, ct, planId),
            _ => throw new ArgumentException("Export format must be docx or pdf.")
        };
    }

    public async Task<string> CreateAsync(Guid tenantId, string systemId, CreateAssessmentPlanRequest request,
        string actor, CancellationToken ct)
    {
        await SystemAsync(tenantId, systemId, ct);
        if (string.IsNullOrWhiteSpace(request.RequestId) || request.RequestId.Length > 200)
            throw new ArgumentException("A stable requestId is required (maximum 200 characters).");
        if (request.PreviousPlanId is not null)
        {
            var previous = await PlanAsync(tenantId, systemId, request.PreviousPlanId, ct);
            if (previous.Status != SapStatus.Finalized) throw new InvalidOperationException("Only finalized plans can be revised.");
            CheckVersion(previous, request.ExpectedContentHash, previous.Revision);
        }
        var generated = await sapService.GenerateSapAsync(new SapGenerationInput(systemId,
            GenerationRequestId: request.RequestId, PreviousPlanId: request.PreviousPlanId,
            ExpectedContentHash: request.ExpectedContentHash), actor, ct);
        return generated.SapId;
    }

    public async Task UpdateAsync(Guid tenantId, string systemId, string planId, UpdateAssessmentPlanRequest request,
        string actor, CancellationToken ct)
    {
        await SystemAsync(tenantId, systemId, ct);
        var plan = await PlanAsync(tenantId, systemId, planId, ct);
        CheckVersion(plan, request.ExpectedContentHash, request.ExpectedRevision);
        var input = new SapUpdateInput(planId, ExpectedContentHash: request.ExpectedContentHash,
            ExpectedRevision: request.ExpectedRevision, UpdatedBy: actor);
        switch (request.Task)
        {
            case "title":
                if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 500)
                    throw new ArgumentException("A title of at most 500 characters is required.");
                input = input with { Title = request.Title };
                break;
            case "lead":
                var selected = (await LeadOptionsAsync(tenantId, systemId, plan, ct))
                    .SingleOrDefault(p => p.Id == request.AssessmentLeadId)
                    ?? throw new ArgumentException("Choose a named lead from this system's people or retained team.");
                input = input with { AssessmentLead = selected.Name,
                    AssessmentLeadUserId = selected.Kind == "Legacy" ? plan.AssessmentLeadUserId : selected.Id, ReplaceLead = true };
                break;
            case "scope":
                if (request.IncludedControlIds is null) throw new ArgumentException("includedControlIds is required for scope changes.");
                Length(request.ScopeNotes, 4000);
                foreach (var rationale in request.ExclusionReasons?.Values ?? Enumerable.Empty<string>()) Length(rationale, 4000);
                input = input with { ScopeNotes = request.ScopeNotes, IncludedControlIds = request.IncludedControlIds,
                    ExclusionReasons = request.ExclusionReasons };
                break;
            case "approach":
                Length(request.AssessmentApproach, 4000);
                Length(request.RulesOfEngagement, 4000);
                input = input with { AssessmentApproach = request.AssessmentApproach, RulesOfEngagement = request.RulesOfEngagement };
                break;
            case "schedule":
                input = input with { ScheduleStart = request.ScheduleStart, ScheduleEnd = request.ScheduleEnd, ReplaceSchedule = true };
                break;
            case "team":
                if (request.TeamMembers is null) throw new ArgumentException("teamMembers is required.");
                foreach (var member in request.TeamMembers)
                {
                    if (member is null || string.IsNullOrWhiteSpace(member.Name)
                        || member.Organization is null || member.Role is null)
                        throw new ArgumentException("Each team member needs a name, organization and role.");
                    Length(member.Name, 200); Length(member.Organization, 200); Length(member.Role, 50); Length(member.ContactInfo, 500);
                }
                input = input with { TeamMembers = request.TeamMembers };
                break;
            case "procedures":
                if (request.MethodOverrides is null) throw new ArgumentException("methodOverrides is required.");
                foreach (var method in request.MethodOverrides)
                {
                    if (method is null || method.Methods is null
                        || method.Methods.Any(m => m is not ("Examine" or "Interview" or "Test")))
                        throw new ArgumentException("Methods must be Examine, Interview or Test.");
                    Length(method.Rationale, 2000);
                }
                input = input with { MethodOverrides = request.MethodOverrides };
                break;
            default: throw new ArgumentException("Unknown assessment planning task.");
        }
        await sapService.UpdateSapAsync(input, ct);
    }

    public async Task FinalizeAsync(Guid tenantId, string systemId, string planId,
        FinalizeAssessmentPlanRequest request, string actor, CancellationToken ct)
    {
        await SystemAsync(tenantId, systemId, ct);
        var plan = await PlanAsync(tenantId, systemId, planId, ct);
        CheckVersion(plan, request.ExpectedContentHash, request.ExpectedRevision);
        await sapService.FinalizeSapAsync(planId, actor, ct, request.ExpectedContentHash, request.ExpectedRevision);
    }

    private IQueryable<SecurityAssessmentPlan> Plans(Guid tenantId, string systemId) =>
        db.SecurityAssessmentPlans.AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.RegisteredSystemId == systemId);

    private async Task<SecurityAssessmentPlan> PlanAsync(Guid tenantId, string systemId, string id, CancellationToken ct) =>
        (await Plans(tenantId, systemId).Where(p => p.Id == id).LoadRetainedDetailsAsync(ct))
            .SingleOrDefault() ?? throw new KeyNotFoundException();

    private async Task<RegisteredSystem> SystemAsync(Guid tenantId, string systemId, CancellationToken ct) =>
        await db.RegisteredSystems.AsNoTracking().SingleOrDefaultAsync(s => s.TenantId == tenantId
            && s.Id == systemId && s.IsActive, ct) ?? throw new KeyNotFoundException();

    private static void CheckVersion(SecurityAssessmentPlan plan, string? hash, long revision)
    {
        if (string.IsNullOrWhiteSpace(hash) || revision < 1) throw new ArgumentException("Expected content hash and revision are required.");
        if (revision != plan.Revision || hash != SapService.HashContent(plan.Content))
            throw new DbUpdateConcurrencyException("The plan changed. Reload and review before saving.");
    }

    private static void Length(string? value, int maximum)
    {
        if (value?.Length > maximum) throw new ArgumentException($"Field exceeds {maximum} characters.");
    }

    private async Task<List<AssessmentLeadOption>> LeadOptionsAsync(Guid tenantId, string systemId,
        SecurityAssessmentPlan? plan, CancellationToken ct)
    {
        var candidates = await db.Persons.AsNoTracking().Where(p => p.TenantId == tenantId &&
            (db.SystemRoleAssignments.Any(r => r.TenantId == tenantId && r.RegisteredSystemId == systemId
                && r.PersonId == p.Id && r.RemovedAt == null)
             || db.OrganizationRoleAssignments.Any(r => r.TenantId == tenantId && r.PersonId == p.Id && r.RemovedAt == null)
             || db.RmfRoleAssignments.Any(r => r.TenantId == tenantId && r.RegisteredSystemId == systemId
                 && r.IsActive && (r.UserId == p.Id.ToString() || r.UserId == p.Email))))
            .OrderBy(p => p.DisplayName).ToListAsync(ct);
        var options = new List<AssessmentLeadOption>();
        foreach (var person in candidates)
        {
            var roles = await SystemWorkspaceAccessPolicy.ResolveRolesAsync(db, tenantId, person.Id, systemId, ct);
            if (roles.Count > 0) options.Add(new(person.Id.ToString(), person.DisplayName, "Person", null));
        }
        if (plan is not null)
        {
            options.AddRange(plan.TeamMembers.OrderBy(m => m.Name)
                .Select(m => new AssessmentLeadOption("team:" + m.Id, m.Name, "TeamMember", m.Organization)));
            if (!string.IsNullOrWhiteSpace(plan.AssessmentLead)
                && !options.Any(o => o.Id == plan.AssessmentLeadUserId))
                options.Add(new(plan.AssessmentLeadUserId ?? "legacy:" + plan.Id, plan.AssessmentLead, "Legacy", null));
        }
        return options;
    }

    private static AssessmentPlanDetail Detail(SecurityAssessmentPlan plan) => new(
        plan.Id, plan.Title, plan.Status.ToString(), plan.Revision, SapService.HashContent(plan.Content),
        plan.GeneratedAt, plan.UpdatedAt, plan.FinalizedAt, plan.AssessmentLead, plan.AssessmentLeadUserId,
        plan.ScopeNotes, plan.AssessmentApproach, plan.RulesOfEngagement, plan.ScheduleStart, plan.ScheduleEnd,
        plan.ControlEntries.Count(e => !e.IsExcluded),
        plan.ControlEntries.OrderBy(e => e.ControlId).Select(e => new AssessmentPlanControl(e.ControlId,
            e.ControlTitle, e.ControlFamily, !e.IsExcluded, e.ExclusionRationale, e.AssessmentMethods,
            e.OverrideRationale, e.AssessmentObjectives)).ToList(),
        plan.TeamMembers.Select(m => new SapTeamMemberInput(m.Name, m.Organization, m.Role, m.ContactInfo)).ToList());
}
