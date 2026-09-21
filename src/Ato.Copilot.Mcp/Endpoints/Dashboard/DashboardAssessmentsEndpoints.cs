using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Agents.Document.Tools;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Kanban;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Kanban;
using Ato.Copilot.Core.Models.Poam;
using Ato.Copilot.Core.Services;
using Ato.Copilot.Mcp.Authorization;
using Ato.Copilot.Mcp.Services;
using System.Text.RegularExpressions;

using KanbanTaskStatus = Ato.Copilot.Core.Models.Kanban.TaskStatus;

namespace Ato.Copilot.Mcp.Endpoints;

// ─── #648 Decomposition: Assessments domain routes ─────────────────────────────
public static partial class DashboardEndpoints
{
    private static void MapAssessmentRoutes(IEndpointRouteBuilder group, ICurrentUserService currentUser)
    {
        MapAssessmentEnvironmentRoutes(group, currentUser);

        group.MapGet("/assessments", async (
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            var assessments = await context.Assessments
                .OrderByDescending(a => a.AssessedAt)
                .Take(100)
                .AsNoTracking()
                .ToListAsync(ct);

            var systemIds = assessments
                .Where(a => a.RegisteredSystemId != null)
                .Select(a => a.RegisteredSystemId!)
                .Distinct()
                .ToList();

            var systemNames = await context.RegisteredSystems
                .Where(s => systemIds.Contains(s.Id))
                .Select(s => new { s.Id, s.Name })
                .AsNoTracking()
                .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

            // Check which systems have categorization
            var categorizedSystemIds = await context.SecurityCategorizations
                .Where(sc => systemIds.Contains(sc.RegisteredSystemId))
                .Select(sc => sc.RegisteredSystemId)
                .AsNoTracking()
                .ToListAsync(ct);

            var findingCounts = await context.Findings
                .Where(f => assessments.Select(a => a.Id).Contains(f.AssessmentId))
                .GroupBy(f => f.AssessmentId)
                .Select(g => new { AssessmentId = g.Key, Count = g.Count() })
                .AsNoTracking()
                .ToDictionaryAsync(x => x.AssessmentId, x => x.Count, ct);

            var items = assessments.Select(a => new AssessmentListItemDto
            {
                AssessmentId = a.Id,
                SystemId = a.RegisteredSystemId,
                SystemName = a.RegisteredSystemId != null && systemNames.TryGetValue(a.RegisteredSystemId, out var name) ? name : null,
                Framework = a.Framework,
                Status = a.Status.ToString(),
                ScanType = a.ScanType,
                ComplianceScore = Math.Round(a.ComplianceScore, 1),
                TotalControls = a.TotalControls,
                PassedControls = a.PassedControls,
                FailedControls = a.FailedControls,
                TotalFindings = findingCounts.GetValueOrDefault(a.Id, 0),
                AssessedAt = a.AssessedAt,
                InitiatedBy = a.InitiatedBy,
                HasCategorization = a.RegisteredSystemId != null && categorizedSystemIds.Contains(a.RegisteredSystemId),
            }).ToList();

            return Results.Ok(items);
        })
        .WithName("ListAssessments");

        group.MapGet("/assessments/{assessmentId}", async (
            string assessmentId,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            var assessment = await context.Assessments
                .Include(a => a.Findings)
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == assessmentId, ct);
            if (assessment is null)
                return Results.NotFound(new { error = "Assessment not found" });

            string? systemName = null;
            if (assessment.RegisteredSystemId is not null)
            {
                systemName = await context.RegisteredSystems
                    .Where(s => s.Id == assessment.RegisteredSystemId)
                    .Select(s => s.Name)
                    .FirstOrDefaultAsync(ct);
            }

            // Build per-family breakdown from stored ControlFamilyResults or derive from findings
            var familyResults = assessment.ControlFamilyResults is { Count: > 0 }
                ? assessment.ControlFamilyResults.Select(f => new AssessmentFamilyDto
                {
                    FamilyCode = f.FamilyCode,
                    FamilyName = f.FamilyName,
                    TotalControls = f.TotalControls,
                    PassedControls = f.PassedControls,
                    FailedControls = f.FailedControls,
                    ComplianceScore = Math.Round(f.ComplianceScore, 1),
                }).ToList()
                : assessment.Findings
                    .GroupBy(f => f.ControlId?.Split('-').FirstOrDefault() ?? "Unknown")
                    .Select(g => new AssessmentFamilyDto
                    {
                        FamilyCode = g.Key,
                        FamilyName = g.Key,
                        TotalControls = 0,
                        PassedControls = 0,
                        FailedControls = g.Count(),
                        ComplianceScore = 0,
                    }).ToList();

            var findingDeviationIds = assessment.Findings
                .Where(f => f.DeviationId != null)
                .Select(f => f.DeviationId!)
                .Distinct()
                .ToList();
            Dictionary<string, string> deviationTypes;
            try
            {
                deviationTypes = findingDeviationIds.Count > 0
                    ? await context.Deviations
                        .Where(d => findingDeviationIds.Contains(d.Id))
                        .Select(d => new { d.Id, Type = d.DeviationType.ToString() })
                        .ToDictionaryAsync(d => d.Id, d => d.Type, ct)
                    : new Dictionary<string, string>();
            }
            catch (Microsoft.Data.SqlClient.SqlException)
            {
                deviationTypes = new Dictionary<string, string>();
            }

            var findingDtos = assessment.Findings
                .OrderBy(f => f.ControlId)
                .Select(f => new AssessmentFindingDto
                {
                    FindingId = f.Id,
                    ControlId = f.ControlId,
                    ControlFamily = f.ControlId?.Split('-').FirstOrDefault() ?? "",
                    Title = f.Title,
                    Description = f.Description,
                    Severity = f.Severity.ToString(),
                    Status = f.Status.ToString(),
                    ResourceType = f.ResourceType,
                    ResourceId = f.ResourceId,
                    RemediationGuidance = f.RemediationGuidance,
                    DiscoveredAt = f.DiscoveredAt,
                    DeviationId = f.DeviationId,
                    DeviationType = f.DeviationId != null && deviationTypes.TryGetValue(f.DeviationId, out var dt) ? dt : null,
                }).ToList();

            // Compute severity counts
            int criticalCount = assessment.Findings.Count(f => f.Severity == FindingSeverity.Critical);
            int highCount = assessment.Findings.Count(f => f.Severity == FindingSeverity.High);
            int mediumCount = assessment.Findings.Count(f => f.Severity == FindingSeverity.Medium);
            int lowCount = assessment.Findings.Count(f => f.Severity == FindingSeverity.Low);

            return Results.Ok(new AssessmentDetailDto
            {
                AssessmentId = assessment.Id,
                SystemId = assessment.RegisteredSystemId,
                SystemName = systemName,
                Framework = assessment.Framework,
                ScanType = assessment.ScanType,
                Status = assessment.Status.ToString(),
                ComplianceScore = Math.Round(assessment.ComplianceScore, 1),
                TotalControls = assessment.TotalControls,
                PassedControls = assessment.PassedControls,
                FailedControls = assessment.FailedControls,
                NotAssessedControls = assessment.NotAssessedControls,
                AssessedAt = assessment.AssessedAt,
                CompletedAt = assessment.CompletedAt,
                InitiatedBy = assessment.InitiatedBy,
                ExecutiveSummary = assessment.ExecutiveSummary,
                CriticalCount = criticalCount,
                HighCount = highCount,
                MediumCount = mediumCount,
                LowCount = lowCount,
                FamilyResults = familyResults,
                Findings = findingDtos,
            });
        })
        .WithName("GetAssessmentDetail");

        // ─── Component Risk Summary (Feature 040 US6) ─────────────────────────

        group.MapGet("/systems/{systemId}/assessments/{assessmentId}/component-risks", async (
            string systemId,
            string assessmentId,
            ComponentService componentService,
            CancellationToken ct) =>
        {
            var result = await componentService.GetComponentRiskSummaryAsync(systemId, assessmentId, ct);
            return Results.Ok(result);
        })
        .WithName("GetAssessmentComponentRisks");

        // ─── Assessment Findings with optional componentId filter (Feature 040 US6) ──

        group.MapGet("/systems/{systemId}/assessments/{assessmentId}/findings", async (
            string systemId,
            string assessmentId,
            string? componentId,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            var assessment = await context.Assessments
                .Include(a => a.Findings)
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == assessmentId && a.RegisteredSystemId == systemId, ct);
            if (assessment is null)
                return Results.NotFound(new { error = "Assessment not found" });

            IEnumerable<ComplianceFinding> findings = assessment.Findings;

            if (componentId == "unlinked")
                findings = findings.Where(f => f.ComponentId == null);
            else if (!string.IsNullOrEmpty(componentId))
                findings = findings.Where(f => f.ComponentId == componentId);

            var dtos = findings.OrderBy(f => f.ControlId).Select(f => new AssessmentFindingDto
            {
                FindingId = f.Id,
                ControlId = f.ControlId,
                ControlFamily = f.ControlId?.Split('-').FirstOrDefault() ?? "",
                Title = f.Title,
                Description = f.Description,
                Severity = f.Severity.ToString(),
                Status = f.Status.ToString(),
                ResourceType = f.ResourceType,
                ResourceId = f.ResourceId,
                RemediationGuidance = f.RemediationGuidance,
                DiscoveredAt = f.DiscoveredAt,
                DeviationId = f.DeviationId,
                DeviationType = null,
            }).ToList();

            return Results.Ok(new { items = dtos, totalCount = dtos.Count });
        })
        .WithName("GetAssessmentFindings");

        // ─── Resolve Finding Components (Feature 040 US6) ─────────────────────

        group.MapPost("/systems/{systemId}/resolve-finding-components", async (
            string systemId,
            ComponentService componentService,
            CancellationToken ct) =>
        {
            var linked = await componentService.ResolveFindingComponentsAsync(systemId, ct);
            return Results.Ok(new { linkedCount = linked });
        })
        .WithName("ResolveFindingComponents");

        group.MapPost("/systems/{systemId}/components/{componentId}/relink-findings", async (
            string systemId,
            string componentId,
            ComponentService componentService,
            CancellationToken ct) =>
        {
            var linked = await componentService.RelinkComponentFindingsAsync(systemId, componentId, ct);
            return Results.Ok(new { linkedCount = linked });
        })
        .WithName("RelinkComponentFindings");

        group.MapPost("/systems/{systemId}/run-assessment", async (
            string systemId,
            IAssessmentEnvironmentService environmentService,
            IAtoComplianceEngine complianceEngine,
            ComplianceTrendSnapshotService trendSnapshotService,
            IAuthorizationService authorizationService,
            IKanbanService kanbanService,
            IRemediationEngine remediationEngine,
            AtoCopilotContext context,
            ILogger<AssessmentEnvironmentService> logger,
            CancellationToken ct) =>
        {
            var actorId = currentUser.CurrentUserId;
            AssessmentReadinessResponse readiness;
            try { readiness = await environmentService.GetReadinessAsync(systemId, ct); }
            catch (AssessmentEnvironmentException failure)
            {
                return LoggedAssessmentEnvironmentError(failure, logger, systemId);
            }
            if (!readiness.IsReady)
                return AssessmentEnvironmentError(
                    readiness.ErrorCode ?? throw new InvalidOperationException("Blocked assessment readiness requires an error code."),
                    readiness.Message, readiness.Suggestion);

            var system = await context.RegisteredSystems
                .FirstOrDefaultAsync(s => s.Id == systemId && s.IsActive, ct);
            if (system is null)
                return Results.NotFound(new { error = "System not found" });

            var subscriptionId = readiness.Subscriptions.FirstOrDefault()?.SubscriptionId
                ?? throw new InvalidOperationException("Ready assessment requires a validated subscription.");

            var assessment = await complianceEngine.RunComprehensiveAssessmentAsync(
                subscriptionId, resourceGroup: null, progress: null, cancellationToken: ct);
            assessment.RegisteredSystemId = systemId;
            assessment.InitiatedBy = actorId;

            // Keep the established Azure completion pipeline; scope and result integrity are separate fixes (#982/#983).
            var existingAssessment = await context.Assessments
                .FirstOrDefaultAsync(a => a.Id == assessment.Id, ct);
            if (existingAssessment is not null)
            {
                existingAssessment.RegisteredSystemId = systemId;
                existingAssessment.InitiatedBy = actorId;
                await context.SaveChangesAsync(ct);
            }

            var failedControlIds = new HashSet<string>(
                assessment.Findings.Select(f => f.ControlId).Where(id => id != null)!,
                StringComparer.OrdinalIgnoreCase);

            var baseline = await context.ControlBaselines
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.RegisteredSystemId == systemId, ct);

            if (baseline is not null)
            {
                var azEffRecords = new List<ControlEffectiveness>();
                foreach (var controlId in baseline.ControlIds)
                {
                    var failed = failedControlIds.Contains(controlId);
                    var finding = failed
                        ? assessment.Findings.FirstOrDefault(f =>
                            string.Equals(f.ControlId, controlId, StringComparison.OrdinalIgnoreCase))
                        : null;

                    azEffRecords.Add(new ControlEffectiveness
                    {
                        AssessmentId = assessment.Id,
                        RegisteredSystemId = systemId,
                        ControlId = controlId,
                        Determination = failed
                            ? EffectivenessDetermination.OtherThanSatisfied
                            : EffectivenessDetermination.Satisfied,
                        AssessmentMethod = "Examine",
                        AssessorId = actorId,
                        AssessedAt = DateTime.UtcNow,
                        CatSeverity = failed && finding?.CatSeverity != null
                            ? finding.CatSeverity
                            : (failed ? Ato.Copilot.Core.Models.Compliance.CatSeverity.CatII : null),
                    });
                }
                context.ControlEffectivenessRecords.AddRange(azEffRecords);
                await context.SaveChangesAsync(ct);
            }

            // Log activity
            context.DashboardActivities.Add(new DashboardActivity
            {
                RegisteredSystemId = systemId,
                EventType = "AssessmentCompleted",
                Actor = assessment.InitiatedBy ?? actorId,
                Summary = $"Compliance assessment completed — score {assessment.ComplianceScore:F1}%, {assessment.Findings.Count} findings ({assessment.PassedControls}/{assessment.TotalControls} controls passed)",
                RelatedEntityType = "ComplianceAssessment",
                RelatedEntityId = assessment.Id,
            });
            await context.SaveChangesAsync(ct);

            // Capture a trend snapshot after assessment completes
            try { await trendSnapshotService.CaptureSnapshotAsync(systemId, ct); }
            catch { /* non-fatal */ }

            // ─── Auto-create POA&M items from open findings ──────────────────
            var poamCreated = 0;
            var openFindings = assessment.Findings
                .Where(f => f.Status == FindingStatus.Open || f.Status == FindingStatus.InProgress)
                .ToList();

            foreach (var finding in openFindings)
            {
                try
                {
                    var severity = finding.CatSeverity ?? (finding.Severity switch
                    {
                        FindingSeverity.Critical or FindingSeverity.High => Ato.Copilot.Core.Models.Compliance.CatSeverity.CatI,
                        FindingSeverity.Medium => Ato.Copilot.Core.Models.Compliance.CatSeverity.CatII,
                        _ => Ato.Copilot.Core.Models.Compliance.CatSeverity.CatIII,
                    });

                    var dueDate = severity switch
                    {
                        Ato.Copilot.Core.Models.Compliance.CatSeverity.CatI => DateTime.UtcNow.AddDays(30),
                        Ato.Copilot.Core.Models.Compliance.CatSeverity.CatII => DateTime.UtcNow.AddDays(90),
                        _ => DateTime.UtcNow.AddDays(180),
                    };

                    var poam = await authorizationService.CreatePoamAsync(
                        systemId,
                        finding.Title ?? finding.Description ?? $"Finding for {finding.ControlId}",
                        finding.ControlId ?? "Unknown",
                        severity.ToString(),
                        actorId,
                        dueDate,
                        finding.Id,
                        finding.RemediationGuidance,
                        cancellationToken: ct);
                    poamCreated++;
                }
                catch { /* non-fatal — continue creating remaining POA&M items */ }
            }

            // ─── Auto-create Kanban remediation board from assessment ─────────
            string? boardId = null;
            var kanbanTaskCount = 0;
            try
            {
                var board = await kanbanService.CreateBoardFromAssessmentAsync(
                    assessment.Id,
                    $"{system.Name} — Assessment {DateTime.UtcNow:yyyy-MM-dd}",
                    subscriptionId,
                    assessment.InitiatedBy ?? actorId,
                    ct);
                boardId = board.Id;
                kanbanTaskCount = board.Tasks.Count;

                // Link POA&M items to kanban tasks via FindingId
                var poamItems = await context.PoamItems
                    .Where(p => p.RegisteredSystemId == systemId && p.FindingId != null)
                    .ToListAsync(ct);
                var tasksByFinding = board.Tasks
                    .Where(t => t.FindingId != null)
                    .ToDictionary(t => t.FindingId!, t => t);

                foreach (var poam in poamItems)
                {
                    if (poam.FindingId != null && tasksByFinding.TryGetValue(poam.FindingId, out var task))
                    {
                        poam.RemediationTaskId = task.Id;
                        task.PoamItemId = poam.Id;
                    }
                }
                await context.SaveChangesAsync(ct);
            }
            catch { /* non-fatal — board creation failure doesn't block assessment */ }

            // ─── Auto-generate remediation plan ──────────────────────────────
            string? remediationPlanId = null;
            try
            {
                var plan = await remediationEngine.GenerateRemediationPlanAsync(
                    openFindings,
                    null,
                    ct);
                remediationPlanId = plan.Id;
            }
            catch { /* non-fatal */ }

            return Results.Ok(new
            {
                assessmentId = assessment.Id,
                status = assessment.Status.ToString(),
                systemId,
                scanType = assessment.ScanType,
                complianceScore = assessment.ComplianceScore,
                totalControls = assessment.TotalControls,
                passedControls = assessment.PassedControls,
                failedControls = assessment.FailedControls,
                totalFindings = assessment.Findings.Count,
                poamItemsCreated = poamCreated,
                remediationBoardId = boardId,
                remediationTaskCount = kanbanTaskCount,
                remediationPlanId,
            });
        })
        .RequireAuthorization(Policies.ComplianceWriter)
        .WithName("RunAssessment")
        .WithSummary("Run an Azure-backed assessment after independently validating system readiness.")
        .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
        .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
        .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
        .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
        .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable);

        // ───────────── Narratives ─────────────────────────────────────────────

        // List NIST controls that don't yet have a narrative for this system
        group.MapGet("/systems/{systemId}/available-controls", async (
            string systemId,
            string? search,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            var existingControlIds = await context.ControlImplementations
                .Where(ci => ci.RegisteredSystemId == systemId)
                .Select(ci => ci.ControlId)
                .ToListAsync(ct);

            var query = context.NistControls.AsNoTracking().AsQueryable();

            if (!string.IsNullOrEmpty(search))
                query = query.Where(n => n.Id.Contains(search) || n.Title.Contains(search));

            var controls = await query
                .Where(n => !existingControlIds.Contains(n.Id))
                .OrderBy(n => n.Family).ThenBy(n => n.Id)
                .Select(n => new { n.Id, n.Family, n.Title })
                .Take(200)
                .ToListAsync(ct);

            return Results.Ok(controls);
        })
        .WithName("ListAvailableControls");

        // Create a new narrative (ControlImplementation) for a control
        group.MapPost("/systems/{systemId}/narratives", async (
            string systemId,
            CreateNarrativeRequest request,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            // Validate the control exists
            var control = await context.NistControls
                .AsNoTracking()
                .FirstOrDefaultAsync(n => n.Id == request.ControlId, ct);
            if (control is null)
                return Results.NotFound(new ErrorResponse { Error = "NIST control not found", ErrorCode = "CONTROL_NOT_FOUND" });

            // Check for duplicate
            var exists = await context.ControlImplementations
                .AnyAsync(ci => ci.RegisteredSystemId == systemId && ci.ControlId == request.ControlId, ct);
            if (exists)
                return Results.Conflict(new ErrorResponse { Error = "Narrative already exists for this control", ErrorCode = "DUPLICATE" });

            var now = DateTime.UtcNow;
            var impl = new ControlImplementation
            {
                ControlId = request.ControlId,
                RegisteredSystemId = systemId,
                ImplementationStatus = Enum.TryParse<ImplementationStatus>(request.ImplementationStatus, true, out var s)
                    ? s : ImplementationStatus.Planned,
                ApprovalStatus = SspSectionStatus.Draft,
                AiSuggested = false,
                AuthoredBy = currentUser.CurrentUserId,
                AuthoredAt = now,
                CurrentVersion = 1,
            };
            impl.SetCombinedNarrative(request.Narrative);

            context.ControlImplementations.Add(impl);
            await context.SaveChangesAsync(ct);

            return Results.Created($"/api/dashboard/systems/{systemId}/narratives", new
            {
                impl.Id,
                impl.ControlId,
                family = control.Family,
                impl.Narrative,
                implementationStatus = impl.ImplementationStatus.ToString(),
                approvalStatus = impl.ApprovalStatus.ToString(),
            });
        })
        .WithName("CreateNarrative");

        group.MapGet("/systems/{systemId}/narratives", async (
            string systemId,
            string? family,
            string? status,
            string? search,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            var query = context.ControlImplementations
                .Where(ci => ci.RegisteredSystemId == systemId)
                .AsNoTracking();

            if (!string.IsNullOrEmpty(family))
                query = query.Where(ci => ci.ControlId.StartsWith(family));

            if (!string.IsNullOrEmpty(status))
            {
                if (Enum.TryParse<ImplementationStatus>(status, true, out var implStatus))
                    query = query.Where(ci => ci.ImplementationStatus == implStatus);
            }

            if (!string.IsNullOrEmpty(search))
                query = query.Where(ci => ci.ControlId.Contains(search) ||
                    (ci.Narrative != null && ci.Narrative.Contains(search)) ||
                    (ci.PolicyNarrative != null && ci.PolicyNarrative.Contains(search)) ||
                    (ci.TechnicalNarrative != null && ci.TechnicalNarrative.Contains(search)));

            var items = await query
                .OrderBy(ci => ci.ControlId)
                .Select(ci => new NarrativeListItemDto
                {
                    Id = ci.Id,
                    ControlId = ci.ControlId,
                    Family = ci.ControlId.Length >= 2 ? ci.ControlId.Substring(0, ci.ControlId.IndexOf('-') > 0 ? ci.ControlId.IndexOf('-') : 2) : ci.ControlId,
                    Narrative = ci.Narrative,
                    PolicyNarrative = ci.PolicyNarrative,
                    TechnicalNarrative = ci.TechnicalNarrative,
                    MigratedFromLegacy = ci.MigratedFromLegacy,
                    ImplementationStatus = ci.ImplementationStatus.ToString(),
                    ApprovalStatus = ci.ApprovalStatus.ToString(),
                    AuthoredBy = ci.AuthoredBy,
                    AuthoredAt = ci.AuthoredAt,
                    Version = ci.CurrentVersion,
                    IsAutoPopulated = ci.IsAutoPopulated,
                    AiSuggested = ci.AiSuggested,
                })
                .ToListAsync(ct);

            return Results.Ok(items);
        })
        .WithName("ListNarratives");

        group.MapPut("/systems/{systemId}/narratives/bulk-update", async (
            string systemId,
            BulkNarrativeUpdateRequest request,
            ComplianceTrendSnapshotService trendSnapshotService,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            var narratives = await context.ControlImplementations
                .Where(ci => ci.RegisteredSystemId == systemId &&
                    request.ControlIds.Contains(ci.ControlId))
                .ToListAsync(ct);

            if (narratives.Count == 0)
                return Results.NotFound(new { error = "No matching narratives found" });

            var updatedBy = currentUser.CurrentUserId;
            var now = DateTime.UtcNow;

            foreach (var ci in narratives)
            {
                if (!string.IsNullOrEmpty(request.ImplementationStatus) &&
                    Enum.TryParse<ImplementationStatus>(request.ImplementationStatus, true, out var newStatus))
                {
                    ci.ImplementationStatus = newStatus;
                }

                if (!string.IsNullOrEmpty(request.ApprovalStatus) &&
                    Enum.TryParse<SspSectionStatus>(request.ApprovalStatus, true, out var newApproval))
                {
                    ci.ApprovalStatus = newApproval;
                }

                ci.ModifiedAt = now;
            }

            context.DashboardActivities.Add(new DashboardActivity
            {
                RegisteredSystemId = systemId,
                EventType = "NarrativesUpdated",
                Actor = updatedBy,
                Summary = $"Bulk updated {narratives.Count} narratives",
                RelatedEntityType = "ControlImplementation",
                RelatedEntityId = systemId,
            });
            await context.SaveChangesAsync(ct);

            try { await trendSnapshotService.CaptureSnapshotAsync(systemId, ct); }
            catch { /* non-fatal */ }

            return Results.Ok(new { updatedCount = narratives.Count, controlIds = narratives.Select(n => n.ControlId).ToList() });
        })
        .WithName("BulkUpdateNarratives");

        // ─── Save single narrative text ────────────────────────────────────
        group.MapPatch("/systems/{systemId}/controls/{controlId}/narrative", async (
            string systemId,
            string controlId,
            PatchDualNarrativeRequest request,
            IDualNarrativeService service,
            Ato.Copilot.Core.Interfaces.Auth.IUserContext userContext,
            CancellationToken ct) =>
        {
            try
            {
                var result = await service.UpdateAsync(
                    systemId, controlId,
                    request.PolicyNarrative, request.PolicyNarrative is not null,
                    request.TechnicalNarrative, request.TechnicalNarrative is not null,
                    userContext.Role, userContext.UserId, ct);
                return Results.Ok(result);
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new ErrorResponse { Error = exception.Message, ErrorCode = "VALIDATION_ERROR" });
            }
            catch (UnauthorizedAccessException exception)
            {
                return Results.Json(
                    new ErrorResponse { Error = exception.Message, ErrorCode = "FORBIDDEN" },
                    statusCode: StatusCodes.Status403Forbidden);
            }
            catch (InvalidOperationException exception) when (exception.Message.StartsWith("NARRATIVE_NOT_FOUND:"))
            {
                return Results.NotFound(new ErrorResponse { Error = exception.Message, ErrorCode = "CONTROL_NOT_FOUND" });
            }
        })
        .WithName("SaveDualNarrativeText");

        // ───────────── Deferred Prerequisites ─────────────────────────────────

        group.MapPost("/systems/{systemId}/deferred-prerequisites/{id}/resolve", async (
            string systemId,
            string id,
            AtoCopilotContext context,
            IRmfLifecycleService lifecycleService,
            CancellationToken ct) =>
        {
            var item = await context.DeferredPrerequisites
                .FirstOrDefaultAsync(d => d.Id == id && d.RegisteredSystemId == systemId, ct);

            if (item is null)
                return Results.NotFound(new { error = "Deferred prerequisite not found" });

            if (item.IsResolved)
                return Results.Ok(new { id = item.Id, alreadyResolved = true });

            // Verify the gate is actually satisfied before allowing resolution
            if (Enum.TryParse<RmfPhase>(item.AdvancedToPhase, true, out var targetPhase))
            {
                try
                {
                    var gates = await lifecycleService.CheckGateConditionsAsync(systemId, targetPhase, ct);
                    var matchingGate = gates.FirstOrDefault(g =>
                        g.GateName.Equals(item.GateName, StringComparison.OrdinalIgnoreCase));

                    if (matchingGate is not null && !matchingGate.Passed)
                    {
                        // Gate still failing — determine an action link based on gate name
                        var gateLower = item.GateName.ToLowerInvariant();
                        string actionLink = $"/systems/{systemId}";
                        string actionLabel = "Go to System";

                        if (gateLower.Contains("categorization") || gateLower.Contains("information type"))
                        {
                            actionLabel = "Set Categorization in Phase Readiness";
                        }
                        else if (gateLower.Contains("privacy"))
                        {
                            actionLabel = "Create PTA in Phase Readiness";
                        }
                        else if (gateLower.Contains("boundary"))
                        {
                            actionLink = $"/systems/{systemId}/boundaries";
                            actionLabel = "Manage Boundaries";
                        }
                        else if (gateLower.Contains("interconnection"))
                        {
                            actionLabel = "Add Interconnection in Phase Readiness";
                        }
                        else if (gateLower.Contains("role"))
                        {
                            actionLabel = "Assign Roles";
                        }
                        else if (gateLower.Contains("baseline"))
                        {
                            actionLink = $"/systems/{systemId}/gap-analysis";
                            actionLabel = "Select Baseline";
                        }
                        else if (gateLower.Contains("narrative"))
                        {
                            actionLink = $"/systems/{systemId}/narratives";
                            actionLabel = "Write Narratives";
                        }

                        return Results.Json(new
                        {
                            resolved = false,
                            gateName = item.GateName,
                            message = matchingGate.Message,
                            severity = matchingGate.Severity,
                            actionLink,
                            actionLabel,
                        }, statusCode: 422);
                    }
                }
                catch
                {
                    // If gate check fails, still allow manual resolution
                }
            }

            item.IsResolved = true;
            item.ResolvedAt = DateTime.UtcNow;
            item.ResolvedBy = currentUser.CurrentUserId;
            await context.SaveChangesAsync(ct);

            return Results.Ok(new { id = item.Id, resolved = true });
        })
        .WithName("ResolveDeferredPrerequisite");

        // ───────────── Authorization & Monitor Phase Endpoints ────────────────

        // ─── Issue Authorization Decision (ATO/ATOwC/IATT/DATO) ─────────────
    }
}
