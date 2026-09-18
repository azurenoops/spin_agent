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
using System.Text.Json;
using System.Text.RegularExpressions;

using KanbanTaskStatus = Ato.Copilot.Core.Models.Kanban.TaskStatus;

namespace Ato.Copilot.Mcp.Endpoints;

// ─── #648 Decomposition: Categorization domain routes ─────────────────────────────
public static partial class DashboardEndpoints
{
    private static void MapCategorizationRoutes(IEndpointRouteBuilder group, IEndpointRouteBuilder app, ICurrentUserService currentUser)
    {
        group.MapPost("/systems/{systemId}/categorization", async (
                string systemId,
                SetCategorizationRequest body,
                ICategorizationService categorizationService,
                IBaselineService baselineService,
                ComplianceTrendSnapshotService trendSnapshotService,
                AtoCopilotContext context,
                CancellationToken ct) =>
            {
                if (body.InformationTypes is not { Count: > 0 })
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "At least one information type is required",
                        ErrorCode = "INVALID_INPUT"
                    });

                try
                {
                    // Check current baseline level before categorization change
                    var existingBaseline = await context.ControlBaselines
                        .FirstOrDefaultAsync(b => b.RegisteredSystemId == systemId, ct);
                    var previousBaselineLevel = existingBaseline?.BaselineLevel;

                    var infoTypes = body.InformationTypes.Select(it => new InformationTypeInput
                    {
                        Sp80060Id = it.Sp80060Id,
                        Name = it.Name,
                        Category = it.Category,
                        ConfidentialityImpact = it.ConfidentialityImpact,
                        IntegrityImpact = it.IntegrityImpact,
                        AvailabilityImpact = it.AvailabilityImpact,
                        UsesProvisional = it.UsesProvisional,
                        AdjustmentJustification = it.AdjustmentJustification,
                    });

                    var result = await categorizationService.CategorizeSystemAsync(
                        systemId,
                        infoTypes,
                        currentUser.CurrentUserId,
                        body.IsNationalSecuritySystem,
                        body.Justification,
                        ct);

                    context.DashboardActivities.Add(new DashboardActivity
                    {
                        RegisteredSystemId = systemId,
                        EventType = "CategorizationSet",
                        Actor = currentUser.CurrentUserId,
                        Summary = $"Security categorization set to {result.OverallCategorization} (C:{result.ConfidentialityImpact} I:{result.IntegrityImpact} A:{result.AvailabilityImpact})",
                        RelatedEntityType = "SecurityCategorization",
                        RelatedEntityId = result.Id,
                    });
                    await context.SaveChangesAsync(ct);

                    // Auto-reselect baseline if the derived level changed
                    string? baselineReselected = null;
                    int? baselineControls = null;
                    int? inheritancesReapplied = null;
                    var newBaselineLevel = result.NistBaseline;

                    if (previousBaselineLevel != null &&
                        !string.Equals(previousBaselineLevel, newBaselineLevel, StringComparison.OrdinalIgnoreCase))
                    {
                        var newBaseline = await baselineService.SelectBaselineAsync(
                            systemId,
                            applyOverlay: true,
                            selectedBy: currentUser.CurrentUserId,
                            cancellationToken: ct);

                        baselineReselected = newBaseline.BaselineLevel;
                        baselineControls = newBaseline.TotalControls;
                        inheritancesReapplied = newBaseline.InheritedControls + newBaseline.SharedControls + newBaseline.CustomerControls;

                        context.DashboardActivities.Add(new DashboardActivity
                        {
                            RegisteredSystemId = systemId,
                            EventType = "BaselineAutoReselected",
                            Actor = currentUser.CurrentUserId,
                            Summary = $"Baseline auto-reselected from {previousBaselineLevel} to {newBaseline.BaselineLevel} ({newBaseline.TotalControls} controls) due to categorization change",
                            RelatedEntityType = "ControlBaseline",
                            RelatedEntityId = newBaseline.Id,
                        });
                        await context.SaveChangesAsync(ct);
                    }

                    try { await trendSnapshotService.CaptureSnapshotAsync(systemId, ct); }
                    catch { /* non-fatal */ }

                    return Results.Ok(new
                    {
                        id = result.Id,
                        overallCategorization = result.OverallCategorization.ToString(),
                        confidentialityImpact = result.ConfidentialityImpact.ToString(),
                        integrityImpact = result.IntegrityImpact.ToString(),
                        availabilityImpact = result.AvailabilityImpact.ToString(),
                        dodImpactLevel = result.DoDImpactLevel,
                        nistBaseline = result.NistBaseline,
                        informationTypeCount = result.InformationTypes.Count,
                        baselineReselected,
                        baselineControls,
                        inheritancesReapplied,
                    });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new ErrorResponse { Error = ex.Message, ErrorCode = "INVALID_INPUT" });
                }
            })
            .WithName("SetCategorization");

        group.MapGet("/systems/{systemId}/categorization/history", async (
                string systemId,
                ICategorizationService categorizationService,
                CancellationToken ct) =>
            {
                var history = await categorizationService.GetCategorizationHistoryAsync(systemId, ct);
                return Results.Ok(history.Select(entry => new
                {
                    entry.Id,
                    entry.Version,
                    entry.IsCurrent,
                    entry.ChangedBy,
                    entry.ChangedAt,
                    entry.Justification,
                    previousConfidentialityImpact = entry.PreviousConfidentialityImpact?.ToString(),
                    previousIntegrityImpact = entry.PreviousIntegrityImpact?.ToString(),
                    previousAvailabilityImpact = entry.PreviousAvailabilityImpact?.ToString(),
                    previousOverallImpact = entry.PreviousOverallImpact?.ToString(),
                    newConfidentialityImpact = entry.NewConfidentialityImpact.ToString(),
                    newIntegrityImpact = entry.NewIntegrityImpact.ToString(),
                    newAvailabilityImpact = entry.NewAvailabilityImpact.ToString(),
                    newOverallImpact = entry.NewOverallImpact.ToString(),
                    previousInformationTypes = JsonSerializer.Deserialize<JsonElement>(entry.PreviousInformationTypesJson),
                    newInformationTypes = JsonSerializer.Deserialize<JsonElement>(entry.NewInformationTypesJson),
                }));
            })
            .WithName("GetCategorizationHistory");

        // ─── Select Baseline ──────────────────────────────────────────────────
        group.MapPost("/systems/{systemId}/baseline", async (
                string systemId,
                SelectBaselineRequest body,
                IBaselineService baselineService,
                ComplianceTrendSnapshotService trendSnapshotService,
                AtoCopilotContext context,
                CancellationToken ct) =>
            {
                try
                {
                    var baseline = await baselineService.SelectBaselineAsync(
                        systemId,
                        applyOverlay: body.ApplyOverlay,
                        overlayName: body.OverlayName,
                        selectedBy: currentUser.CurrentUserId,
                        cancellationToken: ct);

                    context.DashboardActivities.Add(new DashboardActivity
                    {
                        RegisteredSystemId = systemId,
                        EventType = "BaselineSelected",
                        Actor = currentUser.CurrentUserId,
                        Summary = $"Control baseline selected: {baseline.BaselineLevel} ({baseline.TotalControls} controls)",
                        RelatedEntityType = "ControlBaseline",
                        RelatedEntityId = baseline.Id,
                    });
                    await context.SaveChangesAsync(ct);

                    try { await trendSnapshotService.CaptureSnapshotAsync(systemId, ct); }
                    catch { /* non-fatal */ }

                    return Results.Ok(new
                    {
                        baselineId = baseline.Id,
                        baselineLevel = baseline.BaselineLevel,
                        totalControls = baseline.TotalControls,
                        overlayApplied = baseline.OverlayApplied,
                    });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new ErrorResponse { Error = ex.Message, ErrorCode = "INVALID_INPUT" });
                }
            })
            .WithName("SelectBaseline");

        // ─── GET Baseline Detail ─────────────────────────────────────────────
        group.MapGet("/systems/{systemId}/baseline", async (
                string systemId,
                IBaselineService baselineService,
                CancellationToken ct) =>
            {
                var baseline = await baselineService.GetBaselineAsync(systemId, includeDetails: true, cancellationToken: ct);
                if (baseline == null)
                    return Results.NotFound(new ErrorResponse { Error = "No baseline configured for this system", ErrorCode = "BASELINE_NOT_FOUND" });

                // Compute counts from live inheritance records to avoid stale aggregate fields.
                var inheritedControls = baseline.Inheritances.Count(i => i.InheritanceType == InheritanceType.Inherited);
                var sharedControls = baseline.Inheritances.Count(i => i.InheritanceType == InheritanceType.Shared);
                var customerControls = baseline.Inheritances.Count(i => i.InheritanceType == InheritanceType.Customer);

                // Build family breakdown from ControlIds
                var familyBreakdown = baseline.ControlIds
                    .GroupBy(c => ComplianceFrameworks.ExtractControlFamily(c))
                    .OrderBy(g => g.Key)
                    .Select(g => new { family = g.Key, count = g.Count() })
                    .ToList();

                // Tailoring records
                var tailorings = baseline.Tailorings
                    .OrderByDescending(t => t.TailoredAt)
                    .Select(t => new
                    {
                        id = t.Id,
                        controlId = t.ControlId,
                        action = t.Action.ToString(),
                        rationale = t.Rationale,
                        isOverlayRequired = t.IsOverlayRequired,
                        tailoredBy = t.TailoredBy,
                        tailoredAt = t.TailoredAt,
                    })
                    .ToList();

                return Results.Ok(new
                {
                    baselineId = baseline.Id,
                    baselineLevel = baseline.BaselineLevel,
                    totalControls = baseline.TotalControls,
                    overlayApplied = baseline.OverlayApplied,
                    inheritedControls,
                    sharedControls,
                    customerControls,
                    tailoredInControls = baseline.TailoredInControls,
                    tailoredOutControls = baseline.TailoredOutControls,
                    createdAt = baseline.CreatedAt,
                    createdBy = baseline.CreatedBy,
                    modifiedAt = baseline.ModifiedAt,
                    familyBreakdown,
                    tailorings,
                    controlIds = baseline.ControlIds,
                });
            })
            .WithName("GetBaselineDetail");

        // ─── Advance RMF Step ────────────────────────────────────────────────
        group.MapPost("/systems/{systemId}/advance-rmf-step", async (
                string systemId,
                AdvanceRmfStepRequest body,
                IRmfLifecycleService lifecycleService,
                AtoCopilotContext context,
                CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(body.TargetStep))
                    return Results.BadRequest(new ErrorResponse { Error = "targetStep is required", ErrorCode = "INVALID_INPUT" });

                if (!Enum.TryParse<RmfPhase>(body.TargetStep, true, out var targetStep))
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = $"Invalid target step '{body.TargetStep}'",
                        ErrorCode = "INVALID_INPUT",
                        Suggestion = "Use: Prepare, Categorize, Select, Implement, Assess, Authorize, Monitor"
                    });

                var result = await lifecycleService.AdvanceRmfStepAsync(
                    systemId, targetStep, body.Force ?? false, currentUser.CurrentUserId, body.Notes, ct);

                if (!result.Success)
                {
                    return Results.Json(new
                    {
                        success = false,
                        previousStep = result.PreviousStep.ToString(),
                        newStep = result.NewStep.ToString(),
                        error = result.ErrorMessage,
                        gateResults = result.GateResults.Select(g => new
                        {
                            gateName = g.GateName,
                            passed = g.Passed,
                            message = g.Message,
                            severity = g.Severity,
                        }),
                    }, statusCode: 422);
                }

                // Save failed gates as deferred prerequisites when force-advancing
                if (result.WasForced)
                {
                    var failedGates = result.GateResults.Where(g => !g.Passed).ToList();
                    foreach (var gate in failedGates)
                    {
                        context.DeferredPrerequisites.Add(new DeferredPrerequisite
                        {
                            RegisteredSystemId = systemId,
                            GateName = gate.GateName,
                            Message = gate.Message,
                            SkippedFromPhase = result.PreviousStep.ToString(),
                            AdvancedToPhase = result.NewStep.ToString(),
                            CreatedBy = currentUser.CurrentUserId,
                        });
                    }
                    if (failedGates.Count > 0)
                        await context.SaveChangesAsync(ct);
                }

                context.DashboardActivities.Add(new DashboardActivity
                {
                    RegisteredSystemId = systemId,
                    EventType = "RmfPhaseAdvanced",
                    Actor = currentUser.CurrentUserId,
                    Summary = $"RMF phase advanced from {result.PreviousStep} to {result.NewStep}{(result.WasForced ? " (force-advanced)" : "")}",
                    RelatedEntityType = "RegisteredSystem",
                    RelatedEntityId = systemId,
                });
                await context.SaveChangesAsync(ct);

                return Results.Ok(new
                {
                    success = true,
                    previousStep = result.PreviousStep.ToString(),
                    newStep = result.NewStep.ToString(),
                    wasForced = result.WasForced,
                    gateResults = result.GateResults.Select(g => new
                    {
                        gateName = g.GateName,
                        passed = g.Passed,
                        message = g.Message,
                        severity = g.Severity,
                    }),
                });
            })
            .WithName("AdvanceRmfStep");

        // ─── Phase Readiness Preflight ──────────────────────────────────────
        group.MapGet("/systems/{systemId}/phase-readiness", async (
                string systemId,
                IRmfLifecycleService lifecycleService,
                AtoCopilotContext db,
                CancellationToken ct) =>
            {
                var system = await db.RegisteredSystems
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Id == systemId, ct);

                if (system == null)
                    return Results.NotFound(new ErrorResponse { Error = "System not found", ErrorCode = "NOT_FOUND" });

                var currentPhase = system.CurrentRmfStep.ToString();
                var phases = Enum.GetValues<RmfPhase>();
                var currentIdx = Array.IndexOf(phases, system.CurrentRmfStep);
                var nextPhase = currentIdx >= 0 && currentIdx < phases.Length - 1
                    ? phases[currentIdx + 1]
                    : (RmfPhase?)null;

                if (nextPhase == null)
                {
                    return Results.Ok(new
                    {
                        currentPhase,
                        nextPhase = (string?)null,
                        ready = true,
                        gateResults = Array.Empty<object>(),
                    });
                }

                var gates = await lifecycleService.CheckGateConditionsAsync(systemId, nextPhase.Value, ct);
                var allPassed = gates.All(g => g.Passed || g.Severity != "Error");

                return Results.Ok(new
                {
                    currentPhase,
                    nextPhase = nextPhase.Value.ToString(),
                    ready = allPassed,
                    gateResults = gates.Select(g => new
                    {
                        gateName = g.GateName,
                        passed = g.Passed,
                        message = g.Message,
                        severity = g.Severity,
                    }),
                });
            })
            .WithName("GetPhaseReadiness");

        // ─── Quick Action: Create PTA ──────────────────────────────────────
        group.MapPost("/systems/{systemId}/pta", async (
                string systemId,
                CreatePtaRequest body,
                IPrivacyService privacyService,
                AtoCopilotContext context,
                CancellationToken ct) =>
            {
                var result = await privacyService.CreatePtaAsync(
                    systemId,
                    analyzedBy: currentUser.CurrentUserId,
                    manualMode: true,
                    collectsPii: body.CollectsPii,
                    maintainsPii: body.MaintainsPii,
                    disseminatesPii: body.DisseminatesPii,
                    piiCategories: body.PiiCategories,
                    estimatedRecordCount: body.EstimatedRecordCount,
                    cancellationToken: ct);

                context.DashboardActivities.Add(new DashboardActivity
                {
                    RegisteredSystemId = systemId,
                    EventType = "PtaCreated",
                    Actor = currentUser.CurrentUserId,
                    Summary = $"Privacy Threshold Analysis completed — determination: {result.Determination}",
                    RelatedEntityType = "PrivacyThresholdAnalysis",
                    RelatedEntityId = result.PtaId,
                });
                await context.SaveChangesAsync(ct);

                return Results.Ok(new
                {
                    ptaId = result.PtaId,
                    determination = result.Determination.ToString(),
                    collectsPii = result.CollectsPii,
                    piiCategories = result.PiiCategories,
                    rationale = result.Rationale,
                });
            })
            .WithName("CreatePta");

        // ─── Quick Action: Add Interconnection ─────────────────────────────
        group.MapPost("/systems/{systemId}/interconnections", async (
                string systemId,
                AddInterconnectionRequest body,
                IInterconnectionService interconnectionService,
                AtoCopilotContext context,
                CancellationToken ct) =>
            {
                if (!Enum.TryParse<DataFlowDirection>(body.Direction, true, out var direction))
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = $"Invalid direction '{body.Direction}'",
                        ErrorCode = "INVALID_INPUT",
                        Suggestion = "Use: Inbound, Outbound, Bidirectional"
                    });

                if (!Enum.TryParse<InterconnectionType>(body.Type, true, out var connType))
                    connType = InterconnectionType.Direct;

                var result = await interconnectionService.AddInterconnectionAsync(
                    systemId,
                    body.RemoteSystem,
                    connType,
                    direction,
                    body.DataClassification ?? "CUI",
                    createdBy: currentUser.CurrentUserId,
                    protocolsUsed: string.IsNullOrWhiteSpace(body.Protocol) ? null : new List<string> { body.Protocol },
                    portsUsed: string.IsNullOrWhiteSpace(body.Port) ? null : new List<string> { body.Port },
                    cancellationToken: ct);

                context.DashboardActivities.Add(new DashboardActivity
                {
                    RegisteredSystemId = systemId,
                    EventType = "InterconnectionAdded",
                    Actor = currentUser.CurrentUserId,
                    Summary = $"Interconnection added to {result.TargetSystemName} ({body.Direction})",
                    RelatedEntityType = "SystemInterconnection",
                    RelatedEntityId = result.InterconnectionId,
                });
                await context.SaveChangesAsync(ct);

                return Results.Ok(new
                {
                    interconnectionId = result.InterconnectionId,
                    targetSystemName = result.TargetSystemName,
                    status = result.Status.ToString(),
                });
            })
            .WithName("AddInterconnection");

        // ─── Quick Action: Certify No Interconnections ─────────────────────
        group.MapPost("/systems/{systemId}/certify-no-interconnections", async (
                string systemId,
                IInterconnectionService interconnectionService,
                AtoCopilotContext context,
                CancellationToken ct) =>
            {
                await interconnectionService.CertifyNoInterconnectionsAsync(systemId, true, ct);

                context.DashboardActivities.Add(new DashboardActivity
                {
                    RegisteredSystemId = systemId,
                    EventType = "NoInterconnectionsCertified",
                    Actor = currentUser.CurrentUserId,
                    Summary = "Certified that system has no external interconnections",
                    RelatedEntityType = "RegisteredSystem",
                    RelatedEntityId = systemId,
                });
                await context.SaveChangesAsync(ct);

                return Results.Ok(new { certified = true });
            })
            .WithName("CertifyNoInterconnections");

        // ─── Quick Action: Generate & Approve PIA ─────────────────────────
        group.MapPost("/systems/{systemId}/generate-approve-pia", async (
                string systemId,
                IPrivacyService privacyService,
                AtoCopilotContext context,
                CancellationToken ct) =>
            {
                var piaResult = await privacyService.GeneratePiaAsync(
                    systemId,
                    createdBy: currentUser.CurrentUserId,
                    cancellationToken: ct);

                var reviewResult = await privacyService.ReviewPiaAsync(
                    systemId,
                    PiaReviewDecision.Approved,
                    reviewerComments: "Approved via dashboard.",
                    reviewedBy: currentUser.CurrentUserId,
                    cancellationToken: ct);

                context.DashboardActivities.Add(new DashboardActivity
                {
                    RegisteredSystemId = systemId,
                    EventType = "PiaApproved",
                    Actor = currentUser.CurrentUserId,
                    Summary = $"Privacy Impact Assessment generated and approved (expires {reviewResult.ExpirationDate:yyyy-MM-dd})",
                    RelatedEntityType = "PrivacyImpactAssessment",
                    RelatedEntityId = piaResult.PiaId,
                });
                await context.SaveChangesAsync(ct);

                return Results.Ok(new
                {
                    piaId = piaResult.PiaId,
                    status = reviewResult.NewStatus.ToString(),
                    expirationDate = reviewResult.ExpirationDate,
                });
            })
            .WithName("GenerateAndApprovePia");

        // ─── Document Catalog ──────────────────────────────────────────────
        group.MapGet("/systems/{systemId}/documents", async (
                string systemId,
                AtoCopilotContext context,
                CancellationToken ct) =>
            {
                var system = await context.RegisteredSystems
                    .Include(s => s.PrivacyThresholdAnalysis)
                    .Include(s => s.PrivacyImpactAssessment)
                    .Include(s => s.SystemInterconnections)
                    .FirstOrDefaultAsync(s => s.Id == systemId, ct);

                if (system is null)
                    return Results.NotFound(new ErrorResponse { Error = "System not found", ErrorCode = "NOT_FOUND" });

                // SSP narrative progress
                var totalNarratives = await context.ControlImplementations
                    .CountAsync(ci => ci.RegisteredSystemId == systemId, ct);
                var completedNarratives = await context.ControlImplementations
                    .CountAsync(ci => ci.RegisteredSystemId == systemId &&
                        ci.ImplementationStatus != ImplementationStatus.Planned &&
                        ci.Narrative != null && ci.Narrative != "", ct);
                var narrativePct = totalNarratives > 0 ? Math.Round((double)completedNarratives / totalNarratives * 100, 1) : 0;

                // SAP (latest)
                var sap = await context.SecurityAssessmentPlans
                    .Where(s => s.RegisteredSystemId == systemId)
                    .OrderByDescending(s => s.Status == SapStatus.Finalized ? 1 : 0)
                    .ThenByDescending(s => s.GeneratedAt)
                    .FirstOrDefaultAsync(ct);

                // SAR (latest)
                var sar = await context.SecurityAssessmentReports
                    .Where(s => s.RegisteredSystemId == systemId)
                    .OrderByDescending(s => s.Status == SarStatus.Approved ? 2 : s.Status == SarStatus.UnderReview ? 1 : 0)
                    .ThenByDescending(s => s.CreatedAt)
                    .FirstOrDefaultAsync(ct);

                // Authorization decision (latest)
                var authDecision = await context.AuthorizationDecisions
                    .Where(d => d.RegisteredSystemId == systemId)
                    .OrderByDescending(d => d.DecisionDate)
                    .FirstOrDefaultAsync(ct);

                // POA&M
                var poamCount = await context.PoamItems
                    .CountAsync(p => p.RegisteredSystemId == systemId && p.Status != PoamStatus.Completed && p.Status != PoamStatus.RiskAccepted, ct);
                var poamOverdue = await context.PoamItems
                    .CountAsync(p => p.RegisteredSystemId == systemId &&
                        p.Status != PoamStatus.Completed && p.Status != PoamStatus.RiskAccepted &&
                        p.ScheduledCompletionDate < DateTime.UtcNow, ct);

                // Baseline
                var baseline = await context.ControlBaselines
                    .FirstOrDefaultAsync(b => b.RegisteredSystemId == systemId, ct);

                // Interconnections with agreements
                var interconnections = await context.SystemInterconnections
                    .Where(ic => ic.RegisteredSystemId == systemId)
                    .ToListAsync(ct);
                var interconnectionIds = interconnections.Select(ic => ic.Id).ToList();
                var agreements = await context.InterconnectionAgreements
                    .Where(a => interconnectionIds.Contains(a.SystemInterconnectionId))
                    .ToListAsync(ct);

                // ConMon
                var conMonPlan = await context.ConMonPlans
                    .FirstOrDefaultAsync(p => p.RegisteredSystemId == systemId, ct);
                var conMonReportCount = await context.ConMonReports
                    .CountAsync(r => r.RegisteredSystemId == systemId, ct);
                var lastReport = await context.ConMonReports
                    .Where(r => r.RegisteredSystemId == systemId)
                    .OrderByDescending(r => r.GeneratedAt)
                    .FirstOrDefaultAsync(ct);

                // SSP Sections
                var sspSections = await context.SspSections
                    .Where(s => s.RegisteredSystemId == systemId)
                    .OrderBy(s => s.SectionNumber)
                    .ToListAsync(ct);

                // Active waivers (Feature 035)
                int activeWaiverCount;
                try
                {
                    activeWaiverCount = await context.Deviations
                        .CountAsync(d => d.RegisteredSystemId == systemId
                            && d.DeviationType == DeviationType.Waiver
                            && (d.Status == DeviationStatus.Pending || d.Status == DeviationStatus.Approved), ct);
                }
                catch (Microsoft.Data.SqlClient.SqlException)
                {
                    activeWaiverCount = 0;
                }

                // Narrative governance
                var narrativeStatuses = await context.ControlImplementations
                    .Where(ci => ci.RegisteredSystemId == systemId)
                    .Select(ci => ci.ApprovalStatus)
                    .ToListAsync(ct);

                // Scan imports
                var imports = await context.ScanImportRecords
                    .Where(i => i.RegisteredSystemId == systemId)
                    .OrderByDescending(i => i.ImportedAt)
                    .Take(20)
                    .ToListAsync(ct);

                // Inventory
                var inventoryCount = await context.InventoryItems
                    .CountAsync(i => i.RegisteredSystemId == systemId, ct);

                var now = DateTime.UtcNow;

                return Results.Ok(new SystemDocumentsResponse
                {
                    SystemId = systemId,
                    SystemName = system.Name,
                    CurrentPhase = system.CurrentRmfStep.ToString(),

                    Ssp = new SspDocumentInfo
                    {
                        NarrativeCompletionPct = narrativePct,
                        TotalNarratives = totalNarratives,
                        CompletedNarratives = completedNarratives,
                    },

                    Sap = sap is null ? null : new SapDocumentInfo
                    {
                        SapId = sap.Id,
                        Status = sap.Status.ToString(),
                        Title = sap.Title,
                        ContentHash = sap.ContentHash,
                        TotalControls = sap.TotalControls,
                        FinalizedAt = sap.Status == SapStatus.Finalized ? sap.GeneratedAt : null,
                        ScheduleStart = sap.ScheduleStart,
                        ScheduleEnd = sap.ScheduleEnd,
                    },

                    Sar = sar is null ? null : new SarDocumentInfo
                    {
                        SarId = sar.Id,
                        Status = sar.Status.ToString(),
                        Title = sar.Title,
                        TotalControlsAssessed = sar.TotalControlsAssessed,
                        SatisfiedCount = sar.SatisfiedCount,
                        NotSatisfiedCount = sar.NotSatisfiedCount,
                        CreatedBy = sar.CreatedBy,
                        CreatedAt = sar.CreatedAt,
                        ApprovedBy = sar.ApprovedBy,
                        ApprovedAt = sar.ApprovedAt,
                    },

                    Authorization = authDecision is null ? null : new AuthDecisionInfo
                    {
                        DecisionId = authDecision.Id,
                        DecisionType = authDecision.DecisionType.ToString(),
                        DecisionDate = authDecision.DecisionDate,
                        ExpirationDate = authDecision.ExpirationDate,
                        ResidualRisk = authDecision.ResidualRiskLevel.ToString(),
                        IssuedBy = authDecision.IssuedBy,
                        DaysUntilExpiration = authDecision.ExpirationDate.HasValue
                            ? (int)(authDecision.ExpirationDate.Value - now).TotalDays
                            : null,
                    },

                    PoamCount = poamCount,
                    PoamOverdueCount = poamOverdue,
                    HasBaseline = baseline != null,
                    BaselineControlCount = baseline?.TotalControls ?? 0,

                    Pta = system.PrivacyThresholdAnalysis is null ? null : new PtaDocumentInfo
                    {
                        PtaId = system.PrivacyThresholdAnalysis.Id,
                        Determination = system.PrivacyThresholdAnalysis.Determination.ToString(),
                        CollectsPii = system.PrivacyThresholdAnalysis.CollectsPii,
                        PiiCategories = system.PrivacyThresholdAnalysis.PiiCategories,
                        AnalyzedAt = system.PrivacyThresholdAnalysis.AnalyzedAt,
                        AnalyzedBy = system.PrivacyThresholdAnalysis.AnalyzedBy,
                    },

                    Pia = system.PrivacyImpactAssessment is null ? null : new PiaDocumentInfo
                    {
                        PiaId = system.PrivacyImpactAssessment.Id,
                        Status = system.PrivacyImpactAssessment.Status.ToString(),
                        Version = system.PrivacyImpactAssessment.Version,
                        ApprovedBy = system.PrivacyImpactAssessment.ApprovedBy,
                        ApprovedAt = system.PrivacyImpactAssessment.ApprovedAt,
                        ExpirationDate = system.PrivacyImpactAssessment.ExpirationDate,
                        DaysUntilExpiration = system.PrivacyImpactAssessment.ExpirationDate.HasValue
                            ? (int)(system.PrivacyImpactAssessment.ExpirationDate.Value - now).TotalDays
                            : null,
                    },

                    Interconnections = interconnections.Select(ic =>
                    {
                        var agreement = agreements.FirstOrDefault(a =>
                            a.SystemInterconnectionId == ic.Id);
                        return new InterconnectionDocInfo
                        {
                            InterconnectionId = ic.Id,
                            TargetSystem = ic.TargetSystemName,
                            Direction = ic.DataFlowDirection.ToString(),
                            Status = ic.Status.ToString(),
                            HasAgreement = agreement != null,
                            AgreementType = agreement?.AgreementType.ToString(),
                            AgreementStatus = agreement?.Status.ToString(),
                        };
                    }).ToList(),

                    ConMon = conMonPlan is null ? null : new ConMonInfo
                    {
                        PlanId = conMonPlan.Id,
                        Frequency = conMonPlan.AssessmentFrequency,
                        ReportCount = conMonReportCount,
                        LastReportDate = lastReport?.GeneratedAt,
                    },

                    SspSections = sspSections.Select(s => new SspSectionInfo
                    {
                        SectionNumber = s.SectionNumber,
                        Title = s.SectionTitle,
                        Status = s.Status.ToString(),
                        AuthoredBy = s.AuthoredBy,
                        AuthoredAt = s.AuthoredAt,
                        ReviewedBy = s.ReviewedBy,
                        ReviewedAt = s.ReviewedAt,
                        Version = s.Version,
                    }).ToList(),

                    ActiveWaiverCount = activeWaiverCount,

                    NarrativeGovernance = totalNarratives == 0 ? null : new NarrativeGovernanceInfo
                    {
                        TotalNarratives = totalNarratives,
                        Draft = narrativeStatuses.Count(s => s == SspSectionStatus.Draft || s == SspSectionStatus.NotStarted),
                        InReview = narrativeStatuses.Count(s => s == SspSectionStatus.UnderReview),
                        Approved = narrativeStatuses.Count(s => s == SspSectionStatus.Approved),
                        NeedsRevision = narrativeStatuses.Count(s => s == SspSectionStatus.NeedsRevision),
                        ApprovalPct = totalNarratives > 0
                            ? Math.Round((double)narrativeStatuses.Count(s => s == SspSectionStatus.Approved) / totalNarratives * 100, 1)
                            : 0,
                    },

                    ImportHistory = imports.Select(i => new ScanImportInfo
                    {
                        ImportId = i.Id,
                        ImportType = i.ImportType.ToString(),
                        FileName = i.FileName,
                        ImportedAt = i.ImportedAt,
                        TotalEntries = i.TotalEntries,
                        OpenCount = i.OpenCount,
                        PassCount = i.PassCount,
                        BenchmarkTitle = i.BenchmarkTitle,
                    }).ToList(),

                    InventoryItemCount = inventoryCount,
                });
            })
            .WithName("GetSystemDocuments");

        // ─── Continuous Monitoring Overview ───────────────────────────────
    }
}
