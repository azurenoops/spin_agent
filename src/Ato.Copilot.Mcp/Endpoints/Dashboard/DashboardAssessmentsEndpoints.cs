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
using Ato.Copilot.Mcp.Services;
using System.Text.RegularExpressions;

using KanbanTaskStatus = Ato.Copilot.Core.Models.Kanban.TaskStatus;

namespace Ato.Copilot.Mcp.Endpoints;

// ─── #648 Decomposition: Assessments domain routes ─────────────────────────────
public static partial class DashboardEndpoints
{
    private static void MapAssessmentRoutes(IEndpointRouteBuilder group, IEndpointRouteBuilder app)
    {
        app.MapGet("/api/dashboard/assessments", async (
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

        app.MapGet("/api/dashboard/assessments/{assessmentId}", async (
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

        app.MapGet("/api/dashboard/systems/{systemId}/assessments/{assessmentId}/component-risks", async (
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

        app.MapGet("/api/dashboard/systems/{systemId}/assessments/{assessmentId}/findings", async (
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

        app.MapPost("/api/dashboard/systems/{systemId}/resolve-finding-components", async (
            string systemId,
            ComponentService componentService,
            CancellationToken ct) =>
        {
            var linked = await componentService.ResolveFindingComponentsAsync(systemId, ct);
            return Results.Ok(new { linkedCount = linked });
        })
        .WithName("ResolveFindingComponents");

        app.MapPost("/api/dashboard/systems/{systemId}/components/{componentId}/relink-findings", async (
            string systemId,
            string componentId,
            ComponentService componentService,
            CancellationToken ct) =>
        {
            var linked = await componentService.RelinkComponentFindingsAsync(systemId, componentId, ct);
            return Results.Ok(new { linkedCount = linked });
        })
        .WithName("RelinkComponentFindings");

        app.MapPost("/api/dashboard/systems/{systemId}/run-assessment", async (
            string systemId,
            IAtoComplianceEngine complianceEngine,
            ComplianceTrendSnapshotService trendSnapshotService,
            IAuthorizationService authorizationService,
            IKanbanService kanbanService,
            IRemediationEngine remediationEngine,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            var system = await context.RegisteredSystems
                .FirstOrDefaultAsync(s => s.Id == systemId && s.IsActive, ct);
            if (system is null)
                return Results.NotFound(new { error = "System not found" });

            var hasCategorization = await context.SecurityCategorizations
                .AnyAsync(sc => sc.RegisteredSystemId == systemId, ct);
            if (!hasCategorization)
                return Results.BadRequest(new { error = "System must be categorized before running an assessment." });

            var subscriptionId = system.AzureProfile?.SubscriptionIds.FirstOrDefault();

            ComplianceAssessment assessment;

            if (!string.IsNullOrWhiteSpace(subscriptionId))
            {
                // Use the real compliance engine (same as chat) when Azure subscription exists
                assessment = await complianceEngine.RunComprehensiveAssessmentAsync(
                    subscriptionId, resourceGroup: null, progress: null, cancellationToken: ct);
                assessment.RegisteredSystemId = systemId;
                assessment.InitiatedBy = "dashboard-user";

                // The engine persists assessment via its own DbContext, so update
                // RegisteredSystemId and InitiatedBy in our context
                var existingAssessment = await context.Assessments
                    .FirstOrDefaultAsync(a => a.Id == assessment.Id, ct);
                if (existingAssessment is not null)
                {
                    existingAssessment.RegisteredSystemId = systemId;
                    existingAssessment.InitiatedBy = "dashboard-user";
                    await context.SaveChangesAsync(ct);
                }

                // Create ControlEffectiveness records so the heatmap updates
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
                            AssessorId = "dashboard-user",
                            AssessedAt = DateTime.UtcNow,
                            CatSeverity = failed && finding?.CatSeverity != null
                                ? finding.CatSeverity
                                : (failed ? Ato.Copilot.Core.Models.Compliance.CatSeverity.CatII : null),
                        });
                    }
                    context.ControlEffectivenessRecords.AddRange(azEffRecords);
                    await context.SaveChangesAsync(ct);
                }
            }
            else
            {
                // Fallback: evaluate control implementations against baseline
                var baseline = await context.ControlBaselines
                    .AsNoTracking()
                    .FirstOrDefaultAsync(b => b.RegisteredSystemId == systemId, ct);
                if (baseline is null)
                    return Results.BadRequest(new { error = "System must have a control baseline selected before running an assessment." });

                var implementations = await context.ControlImplementations
                    .Where(ci => ci.RegisteredSystemId == systemId)
                    .ToListAsync(ct);

                var implByControl = implementations.ToDictionary(ci => ci.ControlId, StringComparer.OrdinalIgnoreCase);

                // Load valid NIST control IDs to avoid FK violations on Findings
                var validControlIds = await context.NistControls
                    .Select(nc => nc.Id)
                    .AsNoTracking()
                    .ToListAsync(ct);
                var validSet = new HashSet<string>(validControlIds, StringComparer.OrdinalIgnoreCase);

                int totalControls = baseline.ControlIds.Count;
                int passedControls = 0;
                int failedControls = 0;
                var findings = new List<ComplianceFinding>();

                // Evaluate each control and update implementation status based on narrative content
                foreach (var controlId in baseline.ControlIds)
                {
                    if (implByControl.TryGetValue(controlId, out var impl))
                    {
                        // Skip controls already marked NotApplicable
                        if (impl.ImplementationStatus == ImplementationStatus.NotApplicable)
                        {
                            passedControls++;
                            continue;
                        }

                        bool hasNarrative = !string.IsNullOrWhiteSpace(impl.Narrative);
                        bool isReviewed = impl.ReviewedBy is not null;

                        if (hasNarrative && (isReviewed || !impl.AiSuggested))
                        {
                            // Reviewed narrative or manually-authored → Implemented
                            impl.ImplementationStatus = ImplementationStatus.Implemented;
                            passedControls++;
                        }
                        else if (hasNarrative)
                        {
                            // AI-generated narrative not yet reviewed → PartiallyImplemented
                            impl.ImplementationStatus = ImplementationStatus.PartiallyImplemented;
                            failedControls++;
                            findings.Add(new ComplianceFinding
                            {
                                AssessmentId = "",
                                ControlId = controlId,
                                Title = $"Control {controlId} pending review",
                                Description = $"Control {controlId} has an auto-generated narrative that has not been reviewed. Mark as reviewed to achieve full compliance.",
                                Severity = FindingSeverity.Medium,
                                CatSeverity = Ato.Copilot.Core.Models.Compliance.CatSeverity.CatII,
                                Status = FindingStatus.Open,
                                ResourceType = "ControlImplementation",
                                ResourceId = controlId,
                                DiscoveredAt = DateTime.UtcNow,
                            });
                        }
                        else
                        {
                            // No narrative → stays Planned
                            impl.ImplementationStatus = ImplementationStatus.Planned;
                            failedControls++;
                            findings.Add(new ComplianceFinding
                            {
                                AssessmentId = "",
                                ControlId = controlId,
                                Title = $"Control {controlId} not implemented",
                                Description = $"Control {controlId} has no implementation narrative. Add a narrative to demonstrate compliance.",
                                Severity = FindingSeverity.High,
                                CatSeverity = Ato.Copilot.Core.Models.Compliance.CatSeverity.CatI,
                                Status = FindingStatus.Open,
                                ResourceType = "ControlImplementation",
                                ResourceId = controlId,
                                DiscoveredAt = DateTime.UtcNow,
                            });
                        }
                    }
                    else
                    {
                        // No implementation record at all
                        failedControls++;
                        findings.Add(new ComplianceFinding
                        {
                            AssessmentId = "",
                            ControlId = controlId,
                            Title = $"Control {controlId} not implemented",
                            Description = $"No control implementation record exists for {controlId}.",
                            Severity = FindingSeverity.High,
                            CatSeverity = Ato.Copilot.Core.Models.Compliance.CatSeverity.CatI,
                            Status = FindingStatus.Open,
                            ResourceType = "ControlImplementation",
                            ResourceId = controlId,
                            DiscoveredAt = DateTime.UtcNow,
                        });
                    }
                }

                // Persist updated implementation statuses
                await context.SaveChangesAsync(ct);

                // Build per-family breakdown
                var familyStats = new Dictionary<string, (int Total, int Passed, int Failed)>(StringComparer.OrdinalIgnoreCase);
                foreach (var controlId in baseline.ControlIds)
                {
                    var family = controlId.Contains('-') ? controlId[..controlId.IndexOf('-')] : controlId;
                    if (!familyStats.ContainsKey(family))
                        familyStats[family] = (0, 0, 0);
                    var s = familyStats[family];
                    bool passed = implByControl.TryGetValue(controlId, out var ci) &&
                        ci.ImplementationStatus is ImplementationStatus.Implemented or ImplementationStatus.NotApplicable;
                    familyStats[family] = (s.Total + 1, s.Passed + (passed ? 1 : 0), s.Failed + (passed ? 0 : 1));
                }

                var familyResults = familyStats
                    .OrderBy(kvp => kvp.Key)
                    .Select(kvp => new ControlFamilyAssessment
                    {
                        FamilyCode = kvp.Key,
                        FamilyName = ControlFamilies.FamilyNames.GetValueOrDefault(kvp.Key, kvp.Key),
                        TotalControls = kvp.Value.Total,
                        PassedControls = kvp.Value.Passed,
                        FailedControls = kvp.Value.Failed,
                        ComplianceScore = kvp.Value.Total > 0
                            ? Math.Round((double)kvp.Value.Passed / kvp.Value.Total * 100, 1)
                            : 0,
                        Status = FamilyAssessmentStatus.Completed,
                    }).ToList();

                assessment = new ComplianceAssessment
                {
                    SubscriptionId = "",
                    Framework = "NIST 800-53",
                    ScanType = "combined",
                    Status = AssessmentStatus.Completed,
                    InitiatedBy = "dashboard-user",
                    AssessedAt = DateTime.UtcNow,
                    CompletedAt = DateTime.UtcNow,
                    RegisteredSystemId = systemId,
                    ComplianceScore = totalControls > 0
                        ? Math.Round((double)passedControls / totalControls * 100, 1)
                        : 0,
                    TotalControls = totalControls,
                    PassedControls = passedControls,
                    FailedControls = failedControls,
                    ControlFamilyResults = familyResults,
                };

                // Persist findings
                context.Assessments.Add(assessment);
                await context.SaveChangesAsync(ct);

                foreach (var finding in findings)
                    finding.AssessmentId = assessment.Id;

                // Only persist findings whose ControlId exists in NistControls (FK constraint)
                var persistableFindings = findings.Where(f => validSet.Contains(f.ControlId)).ToList();
                if (persistableFindings.Count > 0)
                {
                    context.Findings.AddRange(persistableFindings);
                    await context.SaveChangesAsync(ct);
                }

                // Create ControlEffectiveness records so the heatmap updates
                var effectivenessRecords = new List<ControlEffectiveness>();
                foreach (var controlId in baseline.ControlIds)
                {
                    var passed = implByControl.TryGetValue(controlId, out var ci) &&
                        ci.ImplementationStatus is ImplementationStatus.Implemented or ImplementationStatus.NotApplicable;
                    effectivenessRecords.Add(new ControlEffectiveness
                    {
                        AssessmentId = assessment.Id,
                        RegisteredSystemId = systemId,
                        ControlId = controlId,
                        Determination = passed
                            ? EffectivenessDetermination.Satisfied
                            : EffectivenessDetermination.OtherThanSatisfied,
                        AssessmentMethod = "Examine",
                        AssessorId = "dashboard-user",
                        AssessedAt = DateTime.UtcNow,
                        CatSeverity = passed ? null
                            : (implByControl.TryGetValue(controlId, out var imp) && imp.ImplementationStatus == ImplementationStatus.PartiallyImplemented
                                ? Ato.Copilot.Core.Models.Compliance.CatSeverity.CatII
                                : Ato.Copilot.Core.Models.Compliance.CatSeverity.CatI),
                    });
                }
                context.ControlEffectivenessRecords.AddRange(effectivenessRecords);
                await context.SaveChangesAsync(ct);

                assessment.Findings = persistableFindings;
            }

            // Log activity
            context.DashboardActivities.Add(new DashboardActivity
            {
                RegisteredSystemId = systemId,
                EventType = "AssessmentCompleted",
                Actor = assessment.InitiatedBy ?? "dashboard-user",
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
                        "dashboard-user",
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
                    system.AzureProfile?.SubscriptionIds.FirstOrDefault() ?? systemId,
                    assessment.InitiatedBy ?? "dashboard-user",
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
        .WithName("RunAssessment");

        // ───────────── Narratives ─────────────────────────────────────────────

        // List NIST controls that don't yet have a narrative for this system
        app.MapGet("/api/dashboard/systems/{systemId}/available-controls", async (
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
        app.MapPost("/api/dashboard/systems/{systemId}/narratives", async (
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
                Narrative = request.Narrative,
                AiSuggested = false,
                AuthoredBy = "dashboard-user",
                AuthoredAt = now,
                CurrentVersion = 1,
            };

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

        app.MapGet("/api/dashboard/systems/{systemId}/narratives", async (
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
                    (ci.Narrative != null && ci.Narrative.Contains(search)));

            var items = await query
                .OrderBy(ci => ci.ControlId)
                .Select(ci => new NarrativeListItemDto
                {
                    Id = ci.Id,
                    ControlId = ci.ControlId,
                    Family = ci.ControlId.Length >= 2 ? ci.ControlId.Substring(0, ci.ControlId.IndexOf('-') > 0 ? ci.ControlId.IndexOf('-') : 2) : ci.ControlId,
                    Narrative = ci.Narrative,
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

        app.MapPut("/api/dashboard/systems/{systemId}/narratives/bulk-update", async (
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

            var updatedBy = request.UpdatedBy ?? "dashboard-user";
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
        app.MapPatch("/api/dashboard/systems/{systemId}/controls/{controlId}/narrative", async (
            string systemId,
            string controlId,
            SaveNarrativeRequest request,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            var impl = await context.ControlImplementations
                .FirstOrDefaultAsync(ci => ci.RegisteredSystemId == systemId && ci.ControlId == controlId, ct);
            if (impl is null)
                return Results.NotFound(new ErrorResponse { Error = "Control implementation not found", ErrorCode = "CONTROL_NOT_FOUND" });

            impl.Narrative = request.Narrative;
            impl.AiSuggested = false;
            impl.ModifiedAt = DateTime.UtcNow;
            await context.SaveChangesAsync(ct);

            return Results.Ok(new { controlId, narrative = impl.Narrative });
        })
        .WithName("SaveNarrativeText");

        // ───────────── Deferred Prerequisites ─────────────────────────────────

        app.MapPost("/api/dashboard/systems/{systemId}/deferred-prerequisites/{id}/resolve", async (
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
            item.ResolvedBy = "dashboard-user";
            await context.SaveChangesAsync(ct);

            return Results.Ok(new { id = item.Id, resolved = true });
        })
        .WithName("ResolveDeferredPrerequisite");

        // ───────────── Authorization & Monitor Phase Endpoints ────────────────

        // ─── Issue Authorization Decision (ATO/ATOwC/IATT/DATO) ─────────────
    }
}
