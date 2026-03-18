using Microsoft.EntityFrameworkCore;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Services;

/// <summary>
/// Generates an actionable to-do list for a system based on its current RMF phase,
/// compliance posture, and outstanding work items.
/// </summary>
public class TodoService
{
    private readonly AtoCopilotContext _db;
    private string _systemName = "";

    public TodoService(AtoCopilotContext db)
    {
        _db = db;
    }

    private string SystemName(string _) => _systemName;

    /// <summary>
    /// Build a phase-aware todo list for the given system.
    /// </summary>
    public async Task<TodoListDto?> GetTodoListAsync(
        string systemId, CancellationToken ct = default)
    {
        var system = await _db.RegisteredSystems
            .Include(s => s.ControlBaseline)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == systemId && s.IsActive, ct);

        if (system is null) return null;

        _systemName = system.Name;

        var currentPhase = system.CurrentRmfStep;
        var nextPhase = currentPhase < RmfPhase.Monitor
            ? (RmfPhase?)((int)currentPhase + 1)
            : null;

        var items = new List<TodoItemDto>();

        // ── Phase-specific items ────────────────────────────────────────────
        switch (currentPhase)
        {
            case RmfPhase.Prepare:
                await AddPrepareItems(items, systemId, ct);
                break;
            case RmfPhase.Categorize:
                await AddCategorizeItems(items, systemId, ct);
                break;
            case RmfPhase.Select:
                await AddSelectItems(items, systemId, system, ct);
                break;
            case RmfPhase.Implement:
                await AddImplementItems(items, systemId, system, ct);
                break;
            case RmfPhase.Assess:
                await AddAssessItems(items, systemId, ct);
                break;
            case RmfPhase.Authorize:
                await AddAuthorizeItems(items, systemId, ct);
                break;
            case RmfPhase.Monitor:
                await AddMonitorItems(items, systemId, ct);
                break;
        }

        // ── Cross-phase items (always applicable) ───────────────────────────
        await AddPoamItems(items, systemId, ct);
        await AddFindingItems(items, systemId, ct);
        await AddDeviationItems(items, systemId, ct);
        await AddOutstandingInfoItems(items, systemId, ct);

        // ── Deferred prerequisites from force-advances ──────────────────────
        try { await AddDeferredPrerequisiteItems(items, systemId, ct); }
        catch { /* table may not exist yet — non-critical */ }

        // ── Next phase teaser ───────────────────────────────────────────────
        if (nextPhase.HasValue)
        {
            items.Add(new TodoItemDto
            {
                Id = "next-phase",
                Label = $"Next Phase: {nextPhase.Value}",
                Detail = GetPhaseDescription(nextPhase.Value),
                Category = "phase-action",
                Prompt = $"What do I need to complete before advancing {_systemName} to {nextPhase.Value}?",
            });
        }

        return new TodoListDto
        {
            SystemId = systemId,
            SystemName = system.Name,
            CurrentPhase = currentPhase.ToString(),
            NextPhase = nextPhase?.ToString(),
            Items = items,
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Phase-specific builders
    // ═══════════════════════════════════════════════════════════════════════

    private async Task AddPrepareItems(List<TodoItemDto> items, string systemId, CancellationToken ct)
    {
        var hasRoles = await _db.RmfRoleAssignments
            .AnyAsync(r => r.RegisteredSystemId == systemId, ct);

        if (!hasRoles)
        {
            items.Add(new TodoItemDto
            {
                Id = "assign-roles",
                Label = "Assign RMF Roles",
                Detail = "Designate ISSO, ISSM, SCA, and AO for this system",
                Category = "phase-action",
                Prompt = $"Assign RMF roles (ISSO, ISSM, SCA, AO) for {SystemName(systemId)}",
            });
        }

        var hasBoundary = await _db.AuthorizationBoundaries
            .AnyAsync(b => b.RegisteredSystemId == systemId, ct);

        if (!hasBoundary)
        {
            items.Add(new TodoItemDto
            {
                Id = "define-boundary",
                Label = "Define Authorization Boundary",
                Detail = "Identify all resources and interconnections in scope",
                Category = "phase-action",
                Prompt = $"Define the authorization boundary for {SystemName(systemId)}",
            });
        }

        if (hasRoles && hasBoundary)
        {
            items.Add(new TodoItemDto
            {
                Id = "advance-categorize",
                Label = "Advance to Categorize",
                Detail = "Preparation complete — proceed to FIPS 199 categorization",
                Category = "phase-action",
                Prompt = $"Advance {SystemName(systemId)} to the Categorize phase",
            });
        }
    }

    private async Task AddCategorizeItems(List<TodoItemDto> items, string systemId, CancellationToken ct)
    {
        var hasCategorization = await _db.SecurityCategorizations
            .AnyAsync(c => c.RegisteredSystemId == systemId, ct);

        if (!hasCategorization)
        {
            items.Add(new TodoItemDto
            {
                Id = "perform-categorization",
                Label = "Perform FIPS 199 Categorization",
                Detail = "Determine confidentiality, integrity, and availability impact levels",
                Category = "phase-action",
                Prompt = $"Perform FIPS 199 security categorization for {SystemName(systemId)}",
            });
        }

        var hasInfoTypes = await _db.InformationTypes
            .AnyAsync(i => i.SecurityCategorization != null &&
                           i.SecurityCategorization.RegisteredSystemId == systemId, ct);

        if (!hasInfoTypes)
        {
            items.Add(new TodoItemDto
            {
                Id = "identify-info-types",
                Label = "Identify Information Types",
                Detail = "Map SP 800-60 information types processed by this system",
                Category = "phase-action",
                Prompt = $"Identify SP 800-60 information types for {SystemName(systemId)}",
            });
        }

        if (hasCategorization && hasInfoTypes)
        {
            items.Add(new TodoItemDto
            {
                Id = "advance-select",
                Label = "Advance to Select",
                Detail = "Categorization complete — proceed to baseline selection",
                Category = "phase-action",
                Prompt = $"Advance {SystemName(systemId)} to the Select phase",
            });
        }
    }

    private async Task AddSelectItems(List<TodoItemDto> items, string systemId,
        RegisteredSystem system, CancellationToken ct)
    {
        if (system.ControlBaseline is null)
        {
            items.Add(new TodoItemDto
            {
                Id = "select-baseline",
                Label = "Select Control Baseline",
                Detail = "Choose NIST 800-53 baseline (Low / Moderate / High) based on impact level",
                Category = "phase-action",
                Prompt = $"Select the NIST 800-53 control baseline for {SystemName(systemId)}",
            });
        }
        else
        {
            var hasTailoring = await _db.ControlTailorings
                .AnyAsync(t => t.ControlBaselineId == system.ControlBaseline.Id, ct);

            if (!hasTailoring)
            {
                items.Add(new TodoItemDto
                {
                    Id = "tailor-controls",
                    Label = "Tailor Control Baseline",
                    Detail = $"Review and tailor {system.ControlBaseline.TotalControls} controls — apply scoping and compensation",
                    Category = "phase-action",
                    Prompt = $"Tailor the control baseline for {SystemName(systemId)}",
                });
            }

            items.Add(new TodoItemDto
            {
                Id = "advance-implement",
                Label = "Advance to Implement",
                Detail = "Baseline selected — proceed to control implementation",
                Category = "phase-action",
                Prompt = $"Advance {SystemName(systemId)} to the Implement phase",
            });
        }
    }

    private async Task AddImplementItems(List<TodoItemDto> items, string systemId,
        RegisteredSystem system, CancellationToken ct)
    {
        var baselineControlCount = system.ControlBaseline?.TotalControls ?? 0;
        if (baselineControlCount == 0) return;

        var narrativeCount = await _db.ControlImplementations
            .Where(ci => ci.RegisteredSystemId == systemId &&
                         ci.Narrative != null && ci.Narrative != "")
            .CountAsync(ct);

        var coverage = Math.Round(100.0 * narrativeCount / baselineControlCount, 1);
        var remaining = baselineControlCount - narrativeCount;

        if (remaining > 0)
        {
            items.Add(new TodoItemDto
            {
                Id = "write-narratives",
                Label = $"{remaining} Control Narratives to Write",
                Detail = $"SSP narrative coverage is {coverage}% — {remaining} controls remaining",
                Category = "narrative",
                Prompt = $"Show narrative progress for {SystemName(systemId)} and help write missing narratives",
            });
        }

        if (coverage >= 80)
        {
            items.Add(new TodoItemDto
            {
                Id = "advance-assess",
                Label = "Advance to Assess",
                Detail = $"Narrative coverage at {coverage}% — ready for SCA assessment",
                Category = "phase-action",
                Prompt = $"Advance {SystemName(systemId)} to the Assess phase",
            });
        }
    }

    private async Task AddAssessItems(List<TodoItemDto> items, string systemId, CancellationToken ct)
    {
        var latestAssessment = await _db.Assessments
            .Where(a => a.RegisteredSystemId == systemId)
            .OrderByDescending(a => a.AssessedAt)
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);

        if (latestAssessment is null)
        {
            items.Add(new TodoItemDto
            {
                Id = "run-assessment",
                Label = "Run Compliance Assessment",
                Detail = "No assessment on record — SCA must evaluate control effectiveness",
                Category = "phase-action",
                Prompt = $"Run a compliance assessment for {SystemName(systemId)}",
            });
        }
        else if (latestAssessment.Status != AssessmentStatus.Completed)
        {
            items.Add(new TodoItemDto
            {
                Id = "complete-assessment",
                Label = "Complete In-Progress Assessment",
                Detail = $"Assessment started {latestAssessment.AssessedAt:MMM d} is {latestAssessment.Status}",
                Category = "phase-action",
                Prompt = $"Show assessment status for {SystemName(systemId)}",
            });
        }
        else
        {
            var openFindings = await _db.Findings
                .Where(f => f.AssessmentId == latestAssessment.Id && f.Status == FindingStatus.Open)
                .CountAsync(ct);

            if (openFindings > 0)
            {
                items.Add(new TodoItemDto
                {
                    Id = "remediate-findings",
                    Label = $"{openFindings} Open Finding{(openFindings > 1 ? "s" : "")} to Resolve",
                    Detail = "Address findings before requesting authorization",
                    Category = "finding",
                    Prompt = $"Show open findings for {SystemName(systemId)} and suggest remediation steps",
                });
            }

            items.Add(new TodoItemDto
            {
                Id = "advance-authorize",
                Label = "Advance to Authorize",
                Detail = $"Assessment complete (score: {latestAssessment.ComplianceScore:F1}%) — submit to AO",
                Category = "phase-action",
                Prompt = $"Advance {SystemName(systemId)} to the Authorize phase",
            });
        }
    }

    private async Task AddAuthorizeItems(List<TodoItemDto> items, string systemId, CancellationToken ct)
    {
        var hasDecision = await _db.AuthorizationDecisions
            .AnyAsync(d => d.RegisteredSystemId == systemId && d.IsActive, ct);

        if (!hasDecision)
        {
            items.Add(new TodoItemDto
            {
                Id = "request-authorization",
                Label = "Request AO Authorization",
                Detail = "Submit authorization package for ATO/ATOwC/IATT decision",
                Category = "authorization",
                Prompt = $"Prepare authorization package for {SystemName(systemId)}",
            });
        }
        else
        {
            items.Add(new TodoItemDto
            {
                Id = "advance-monitor",
                Label = "Advance to Monitor",
                Detail = "Authorization granted — begin continuous monitoring",
                Category = "phase-action",
                Prompt = $"Advance {SystemName(systemId)} to the Monitor phase",
            });
        }
    }

    private async Task AddMonitorItems(List<TodoItemDto> items, string systemId, CancellationToken ct)
    {
        var decision = await _db.AuthorizationDecisions
            .Where(d => d.RegisteredSystemId == systemId && d.IsActive)
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);

        if (decision?.ExpirationDate is not null)
        {
            var daysRemaining = (int)(decision.ExpirationDate.Value - DateTime.UtcNow).TotalDays;

            if (daysRemaining < 0)
            {
                items.Add(new TodoItemDto
                {
                    Id = "ato-expired",
                    Label = "ATO Has Expired",
                    Detail = "Reauthorization required — system authorization is no longer valid",
                    Category = "authorization",
                    Prompt = $"Start reauthorization for {SystemName(systemId)}",
                });
            }
            else if (daysRemaining <= 90)
            {
                items.Add(new TodoItemDto
                {
                    Id = "ato-expiring",
                    Label = $"ATO Expires in {daysRemaining} Days",
                    Detail = "Begin reauthorization planning",
                    Category = "authorization",
                    Prompt = $"Check ATO status and begin reauthorization planning for {SystemName(systemId)}",
                });
            }
        }

        var hasConMonPlan = await _db.ConMonPlans
            .AnyAsync(p => p.RegisteredSystemId == systemId, ct);

        if (!hasConMonPlan)
        {
            items.Add(new TodoItemDto
            {
                Id = "create-conmon-plan",
                Label = "Create Continuous Monitoring Plan",
                Detail = "Establish monitoring frequency, metrics, and reporting schedule",
                Category = "phase-action",
                Prompt = $"Create a continuous monitoring plan for {SystemName(systemId)}",
            });
        }

        var lastReport = await _db.ConMonReports
            .Where(r => r.RegisteredSystemId == systemId)
            .OrderByDescending(r => r.GeneratedAt)
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);

        if (lastReport is not null)
        {
            var daysSinceReport = (int)(DateTime.UtcNow - lastReport.GeneratedAt).TotalDays;
            if (daysSinceReport > 30)
            {
                items.Add(new TodoItemDto
                {
                    Id = "conmon-report-due",
                    Label = "ConMon Report Overdue",
                    Detail = $"Last report generated {lastReport.GeneratedAt:MMM d, yyyy} — {daysSinceReport} days ago",
                    Category = "phase-action",
                    Prompt = $"Generate a continuous monitoring report for {SystemName(systemId)}",
                });
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Cross-phase items
    // ═══════════════════════════════════════════════════════════════════════

    private async Task AddPoamItems(List<TodoItemDto> items, string systemId, CancellationToken ct)
    {
        var overdueCount = await _db.PoamItems
            .Where(p => p.RegisteredSystemId == systemId &&
                        p.Status == PoamStatus.Ongoing &&
                        p.ScheduledCompletionDate < DateTime.UtcNow)
            .CountAsync(ct);

        if (overdueCount > 0)
        {
            items.Add(new TodoItemDto
            {
                Id = "overdue-poams",
                Label = $"{overdueCount} Overdue POA&M{(overdueCount > 1 ? "s" : "")}",
                Detail = "Past scheduled completion — requires immediate attention",
                Category = "poam",
                Prompt = $"List overdue POA&Ms for {_systemName} and suggest remediation priorities",
            });
        }

        var openCount = await _db.PoamItems
            .Where(p => p.RegisteredSystemId == systemId &&
                        p.Status == PoamStatus.Ongoing &&
                        p.ScheduledCompletionDate >= DateTime.UtcNow)
            .CountAsync(ct);

        if (openCount > 0)
        {
            items.Add(new TodoItemDto
            {
                Id = "open-poams",
                Label = $"{openCount} Open POA&M{(openCount > 1 ? "s" : "")} to Resolve",
                Detail = "Active items awaiting remediation",
                Category = "poam",
                Prompt = $"List open POA&Ms for {_systemName}",
            });
        }
    }

    private async Task AddFindingItems(List<TodoItemDto> items, string systemId, CancellationToken ct)
    {
        var latestAssessment = await _db.Assessments
            .Where(a => a.RegisteredSystemId == systemId && a.Status == AssessmentStatus.Completed)
            .OrderByDescending(a => a.AssessedAt)
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);

        if (latestAssessment is null) return;

        var catICounts = await _db.Findings
            .Where(f => f.AssessmentId == latestAssessment.Id &&
                        f.Status == FindingStatus.Open &&
                        f.CatSeverity == CatSeverity.CatI)
            .CountAsync(ct);

        if (catICounts > 0)
        {
            items.Add(new TodoItemDto
            {
                Id = "cat-i-findings",
                Label = $"{catICounts} CAT I Finding{(catICounts > 1 ? "s" : "")}",
                Detail = "Critical severity — must resolve before authorization",
                Category = "finding",
                Prompt = $"Show CAT I findings for {_systemName} and suggest remediation steps",
            });
        }

        var catIICounts = await _db.Findings
            .Where(f => f.AssessmentId == latestAssessment.Id &&
                        f.Status == FindingStatus.Open &&
                        f.CatSeverity == CatSeverity.CatII)
            .CountAsync(ct);

        if (catIICounts > 0)
        {
            items.Add(new TodoItemDto
            {
                Id = "cat-ii-findings",
                Label = $"{catIICounts} CAT II Finding{(catIICounts > 1 ? "s" : "")}",
                Detail = "High severity — remediate or accept risk",
                Category = "finding",
                Prompt = $"Show CAT II findings for {_systemName}",
            });
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Deviation items (Feature 035 — T024)
    // ═══════════════════════════════════════════════════════════════════════

    private async Task AddDeviationItems(List<TodoItemDto> items, string systemId, CancellationToken ct)
    {
        try
        {
            var pendingCount = await _db.Deviations
                .CountAsync(d => d.RegisteredSystemId == systemId && d.Status == DeviationStatus.Pending, ct);

            if (pendingCount > 0)
            {
                items.Add(new TodoItemDto
                {
                    Id = "deviation-pending",
                    Label = $"Review {pendingCount} pending deviation request{(pendingCount > 1 ? "s" : "")}",
                    Detail = "Deviation requests awaiting ISSM/AO review",
                    Category = "deviation",
                    Prompt = $"Show pending deviation requests for {_systemName}",
                    Link = $"/systems/{systemId}/deviations",
                });
            }

            var expiringCount = await _db.Deviations
                .CountAsync(d => d.RegisteredSystemId == systemId
                    && d.Status == DeviationStatus.Approved
                    && d.ExpirationDate <= DateTime.UtcNow.AddDays(30), ct);

            if (expiringCount > 0)
            {
                items.Add(new TodoItemDto
                {
                    Id = "deviation-expiring",
                    Label = $"Renew {expiringCount} expiring deviation{(expiringCount > 1 ? "s" : "")}",
                    Detail = "Approved deviations expiring within 30 days",
                    Category = "deviation",
                    Prompt = $"Show deviations expiring soon for {_systemName} and help me extend them",
                    Link = $"/systems/{systemId}/deviations",
                });
            }

            var catIAoCount = await _db.Deviations
                .CountAsync(d => d.RegisteredSystemId == systemId
                    && d.Status == DeviationStatus.Pending
                    && d.CatSeverity == CatSeverity.CatI, ct);

            if (catIAoCount > 0)
            {
                items.Add(new TodoItemDto
                {
                    Id = "deviation-cat-i-ao",
                    Label = $"{catIAoCount} CAT I deviation{(catIAoCount > 1 ? "s" : "")} require your approval",
                    Detail = "CAT I deviations require both ISSM recommendation and AO approval",
                    Category = "deviation",
                    Prompt = $"Show CAT I deviation requests for {_systemName} that need AO approval",
                    Link = $"/systems/{systemId}/deviations",
                });
            }
        }
        catch (Microsoft.Data.SqlClient.SqlException) { /* Deviations table may not exist yet */ }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Outstanding-info items (Feature 035 — T025)
    // ═══════════════════════════════════════════════════════════════════════

    private async Task AddOutstandingInfoItems(List<TodoItemDto> items, string systemId, CancellationToken ct)
    {
        // POA&M items missing scheduled completion dates
        var poamMissingDates = await _db.PoamItems
            .CountAsync(p => p.RegisteredSystemId == systemId
                && p.Status != PoamStatus.Completed
                && p.ScheduledCompletionDate == null, ct);

        if (poamMissingDates > 0)
        {
            items.Add(new TodoItemDto
            {
                Id = "outstanding-poam-dates",
                Label = $"{poamMissingDates} POA&M item{(poamMissingDates > 1 ? "s" : "")} missing completion dates",
                Detail = "POA&M entries need scheduled completion dates for authorization review",
                Category = "outstanding-info",
                Prompt = $"Which POA&M items for {_systemName} are missing completion dates?",
                Link = $"/remediation",
            });
        }

        // Deviations without evidence
        try
        {
            var devNoEvidence = await _db.Deviations
                .CountAsync(d => d.RegisteredSystemId == systemId
                    && d.Status == DeviationStatus.Approved
                    && (d.EvidenceReferences == null || d.EvidenceReferences == "[]"), ct);

            if (devNoEvidence > 0)
            {
                items.Add(new TodoItemDto
                {
                    Id = "outstanding-deviation-evidence",
                    Label = $"{devNoEvidence} deviation{(devNoEvidence > 1 ? "s" : "")} without evidence",
                    Detail = "Approved deviations should have supporting evidence attached",
                    Category = "outstanding-info",
                    Prompt = $"Show deviations for {_systemName} that are missing evidence",
                    Link = $"/systems/{systemId}/deviations",
                });
            }
        }
        catch (Microsoft.Data.SqlClient.SqlException) { /* Deviations table may not exist yet */ }

        // Authorization decision missing expiration
        var authNoExpiry = await _db.AuthorizationDecisions
            .CountAsync(a => a.RegisteredSystemId == systemId
                && a.ExpirationDate == null, ct);

        if (authNoExpiry > 0)
        {
            items.Add(new TodoItemDto
            {
                Id = "outstanding-auth-expiry",
                Label = "Authorization decision missing expiration date",
                Detail = "Set an expiration date for the authorization to enable monitoring alerts",
                Category = "outstanding-info",
                Prompt = $"Help me set an expiration date on the authorization decision for {_systemName}",
            });
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Deferred prerequisites from force-advances
    // ═══════════════════════════════════════════════════════════════════════

    private async Task AddDeferredPrerequisiteItems(List<TodoItemDto> items, string systemId, CancellationToken ct)
    {
        var deferred = await _db.DeferredPrerequisites
            .Where(d => d.RegisteredSystemId == systemId && !d.IsResolved)
            .OrderBy(d => d.CreatedAt)
            .AsNoTracking()
            .ToListAsync(ct);

        foreach (var d in deferred)
        {
            items.Insert(0, new TodoItemDto
            {
                Id = $"deferred-{d.Id}",
                Label = $"⚠ {d.GateName}",
                Detail = $"Skipped during force-advance from {d.SkippedFromPhase} → {d.AdvancedToPhase}. {d.Message}",
                Category = "deferred",
                Prompt = $"Help me resolve the {d.GateName} prerequisite for {_systemName}",
                Link = $"/systems/{systemId}",
                DeferredId = d.Id,
            });
        }
    }

    // ═══════════════════════════════════════════════════════════════════════

    private static string GetPhaseDescription(RmfPhase phase) => phase switch
    {
        RmfPhase.Prepare => "Establish context, register system, assign roles",
        RmfPhase.Categorize => "FIPS 199 categorization — determine impact levels",
        RmfPhase.Select => "Choose NIST baseline, apply overlays, tailor controls",
        RmfPhase.Implement => "Write SSP narratives, implement controls",
        RmfPhase.Assess => "SCA evaluates control effectiveness",
        RmfPhase.Authorize => "AO issues ATO/ATOwC/IATT/DATO decision",
        RmfPhase.Monitor => "Continuous monitoring and reauthorization",
        _ => "",
    };
}
