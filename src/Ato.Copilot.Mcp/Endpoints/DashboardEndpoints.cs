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

/// <summary>
/// Maps all /api/dashboard/* REST endpoints for the Visual Compliance Dashboard.
/// #648: God-object decomposed into domain-specific partial class files under Endpoints/Dashboard/.
/// This spine registers the route group and delegates to each domain Map*Routes method.
/// </summary>
public static partial class DashboardEndpoints
{
    /// <summary>
    /// Registers dashboard route group and all dashboard API endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dashboard")
            .WithTags("Dashboard");

        MapSystemRoutes(group, app);
        MapComponentRoutes(group, app);
        MapCapabilityRoutes(group, app);
        MapRoadmapRoutes(group, app);
        MapProfileRoutes(group, app);
        MapBoundaryRoutes(group, app);
        MapCategorizationRoutes(group, app);
        MapConMonRoutes(group, app);
        MapAssessmentRoutes(group, app);
        MapAuthorizationRoutes(group, app);
        MapDeviationRoutes(group, app);
        MapExportRoutes(group, app);
        MapEvidenceRoutes(group, app);
        MapPoamRoutes(group, app);
        MapAzureDiscoveryRoutes(group, app);
        MapInheritanceRoutes(group, app);
        MapControlRoutes(group, app);

                return app;
    }

    /// <summary>
    /// Maps only evidence dashboard routes under <c>/api/dashboard</c>.
    /// Used by evidence integration tests that do not boot the full dashboard DI graph.
    /// </summary>
    public static IEndpointRouteBuilder MapDashboardEvidenceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dashboard")
            .WithTags("Dashboard");
        MapEvidenceRoutes(group, app);
        return app;
    }

    // ─── NIST Family Name Lookup ────────────────────────────────────────────
    private static readonly Dictionary<string, string> NistFamilyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AC"] = "Access Control",
        ["AT"] = "Awareness and Training",
        ["AU"] = "Audit and Accountability",
        ["CA"] = "Assessment, Authorization, and Monitoring",
        ["CM"] = "Configuration Management",
        ["CP"] = "Contingency Planning",
        ["IA"] = "Identification and Authentication",
        ["IR"] = "Incident Response",
        ["MA"] = "Maintenance",
        ["MP"] = "Media Protection",
        ["PE"] = "Physical and Environmental Protection",
        ["PL"] = "Planning",
        ["PM"] = "Program Management",
        ["PS"] = "Personnel Security",
        ["PT"] = "PII Processing and Transparency",
        ["RA"] = "Risk Assessment",
        ["SA"] = "System and Services Acquisition",
        ["SC"] = "System and Communications Protection",
        ["SI"] = "System and Information Integrity",
        ["SR"] = "Supply Chain Risk Management",
    };

    private static readonly Regex OscalInsertParamPattern =
        new("\\{\\{\\s*insert:\\s*param\\s*,\\s*([^}]+?)\\s*\\}\\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string? ResolveControlCatalogDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return description;

        // OSCAL control prose often includes unresolved organization-defined parameters.
        // Keep the canonical text but render placeholders in a readable form.
        var resolved = OscalInsertParamPattern.Replace(description, match =>
        {
            var paramId = match.Groups[1].Value.Trim();
            return string.IsNullOrWhiteSpace(paramId)
                ? "[organization-defined parameter]"
                : $"[organization-defined parameter: {paramId}]";
        });

        return resolved;
    }

    // ─── Feature 043: Import helpers ────────────────────────────────────────

    private static readonly Dictionary<string, Ato.Copilot.Mcp.Services.CrmExportService.ImportParseResult> ImportPreviewCache = new();

    private static string MapImportType(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Customer";
        var lower = raw.ToLowerInvariant().Trim();
        return lower switch
        {
            "inherited" or "system" or "csp" => "Inherited",
            "shared" or "hybrid" => "Shared",
            "customer" or "system-specific" or "not implemented" => "Customer",
            _ => "Customer"
        };
    }

    // ─── Feature 039: POA&M mapping helpers ──────────────────────────────────

    private static object MapToListItem(PoamItem p) => new
    {
        id = p.Id,
        controlId = p.SecurityControlNumber,
        weakness = p.Weakness,
        catSeverity = p.CatSeverity.ToString().Replace("Cat", ""),
        status = p.Status.ToString(),
        components = p.ComponentLinks.Select(cl => new
        {
            id = cl.SystemComponentId,
            name = cl.SystemComponent?.Name ?? "",
            type = cl.SystemComponent?.ComponentType.ToString() ?? ""
        }),
        poc = p.PointOfContact,
        dueDate = p.ScheduledCompletionDate.ToString("o"),
        daysRemaining = (p.ScheduledCompletionDate - DateTime.UtcNow).Days,
        milestoneProgress = new
        {
            completed = p.Milestones.Count(m => m.CompletedDate.HasValue),
            total = p.Milestones.Count
        },
        deviationType = p.DeviationId != null ? "linked" : (string?)null,
        externalTicketRef = p.ExternalTicketRef,
        remediationTaskId = p.RemediationTaskId,
        remediationTaskStatus = (string?)null,
        isOverdue = p.ScheduledCompletionDate < DateTime.UtcNow &&
                    p.Status != PoamStatus.Completed &&
                    p.Status != PoamStatus.RiskAccepted,
        systemId = p.RegisteredSystemId,
        systemName = p.RegisteredSystem?.Name ?? ""
    };

    private static object MapToDetail(PoamItem p) => new
    {
        id = p.Id,
        controlId = p.SecurityControlNumber,
        weakness = p.Weakness,
        weaknessSource = p.WeaknessSource,
        catSeverity = p.CatSeverity.ToString().Replace("Cat", ""),
        status = p.Status.ToString(),
        components = p.ComponentLinks.Select(cl => new
        {
            id = cl.SystemComponentId,
            name = cl.SystemComponent?.Name ?? "",
            type = cl.SystemComponent?.ComponentType.ToString() ?? ""
        }),
        poc = p.PointOfContact,
        pocEmail = p.PocEmail,
        dueDate = p.ScheduledCompletionDate.ToString("o"),
        scheduledCompletionDate = p.ScheduledCompletionDate.ToString("o"),
        actualCompletionDate = p.ActualCompletionDate?.ToString("o"),
        daysRemaining = (p.ScheduledCompletionDate - DateTime.UtcNow).Days,
        milestoneProgress = new
        {
            completed = p.Milestones.Count(m => m.CompletedDate.HasValue),
            total = p.Milestones.Count
        },
        deviationType = p.DeviationId != null ? "linked" : (string?)null,
        externalTicketRef = p.ExternalTicketRef,
        remediationTaskId = p.RemediationTaskId,
        remediationTaskStatus = (string?)null,
        isOverdue = p.ScheduledCompletionDate < DateTime.UtcNow &&
                    p.Status != PoamStatus.Completed &&
                    p.Status != PoamStatus.RiskAccepted,
        systemId = p.RegisteredSystemId,
        systemName = p.RegisteredSystem?.Name ?? "",
        resourcesRequired = p.ResourcesRequired,
        costEstimate = p.CostEstimate,
        comments = p.Comments,
        findingId = p.FindingId,
        deviationId = p.DeviationId,
        createdAt = p.CreatedAt.ToString("o"),
        modifiedAt = p.ModifiedAt?.ToString("o"),
        createdBy = p.CreatedBy,
        rowVersion = p.RowVersion.ToString(),
        milestones = p.Milestones.OrderBy(m => m.Sequence).Select(m => new
        {
            id = m.Id,
            description = m.Description,
            targetDate = m.TargetDate.ToString("o"),
            completedDate = m.CompletedDate?.ToString("o"),
            sequence = m.Sequence,
            isOverdue = m.IsOverdue
        }),
        history = p.History.OrderByDescending(h => h.Timestamp).Select(h => new
        {
            id = h.Id,
            eventType = h.EventType.ToString(),
            oldValue = h.OldValue,
            newValue = h.NewValue,
            actingUserName = h.ActingUserName,
            timestamp = h.Timestamp.ToString("o"),
            details = h.Details,
            cascadeOrigin = h.CascadeOrigin?.ToString()
        }),
        ticketSync = (object?)null
    };

    // ═══════════════════════════════════════════════════════════════════════════
    // Request DTOs for new Authorize/Monitor endpoints
    // ═══════════════════════════════════════════════════════════════════════════

    private record IssueAuthorizationRequest(
        string DecisionType,
        DateTime? ExpirationDate,
        string? ResidualRiskLevel,
        string? TermsAndConditions,
        string? ResidualRiskJustification,
        List<RiskAcceptanceInput>? RiskAcceptances,
        string? IssuedBy,
        string? IssuedByName);

    private record CreatePoamRequest(
        string Weakness,
        string ControlId,
        string CatSeverity,
        string PointOfContact,
        DateTime ScheduledCompletionDate,
        string? FindingId,
        string? ResourcesRequired,
        List<MilestoneInput>? Milestones);

    private record AcceptRiskRequest(
        string FindingId,
        string ControlId,
        string CatSeverity,
        string Justification,
        DateTime ExpirationDate,
        string? CompensatingControl,
        string? AcceptedBy);

    private record CreateConMonPlanRequest(
        string? AssessmentFrequency,
        DateTime? AnnualReviewDate,
        List<string>? ReportDistribution,
        List<string>? SignificantChangeTriggers);

    private record GenerateConMonReportRequest(
        string? ReportType,
        string? Period);

    private record ReportSignificantChangeRequest(
        string? ChangeType,
        string? Description,
        string? DetectedBy);

    private record ReauthorizationCheckRequest(
        bool InitiateIfTriggered = false);

    private record UpdatePoamStatusRequest(
        string Status,
        string? RowVersion = null,
        string? DelayReason = null,
        string? RevisedDate = null,
        string? DeviationId = null,
        string? Comments = null);

    private static ConMonExpirationInfo BuildConMonExpirationInfo(
        AuthorizationDecision activeAuth,
        DateTime now)
    {
        var daysUntilExpiration = (int)(activeAuth.ExpirationDate!.Value.Date - now.Date).TotalDays;

        var (alertLevel, alertMessage, isExpired) = daysUntilExpiration switch
        {
            < 0 => (
                "Expired",
                $"Authorization EXPIRED {Math.Abs(daysUntilExpiration)} days ago on {activeAuth.ExpirationDate.Value:yyyy-MM-dd}. System is operating without authorization. Initiate reauthorization immediately.",
                true),
            <= 30 => (
                "Urgent",
                $"Authorization expires in {daysUntilExpiration} days on {activeAuth.ExpirationDate.Value:yyyy-MM-dd}. Begin reauthorization process immediately.",
                false),
            <= 60 => (
                "Warning",
                $"Authorization expires in {daysUntilExpiration} days on {activeAuth.ExpirationDate.Value:yyyy-MM-dd}. Schedule reauthorization activities.",
                false),
            <= 90 => (
                "Info",
                $"Authorization expires in {daysUntilExpiration} days on {activeAuth.ExpirationDate.Value:yyyy-MM-dd}. Plan for upcoming reauthorization.",
                false),
            _ => (
                "None",
                $"Authorization valid for {daysUntilExpiration} more days (expires {activeAuth.ExpirationDate.Value:yyyy-MM-dd}).",
                false)
        };

        return new ConMonExpirationInfo
        {
            HasActiveAuthorization = true,
            DecisionType = activeAuth.DecisionType.ToString(),
            DecisionDate = activeAuth.DecisionDate,
            ExpirationDate = activeAuth.ExpirationDate,
            DaysUntilExpiration = daysUntilExpiration,
            AlertLevel = alertLevel,
            AlertMessage = alertMessage,
            IsExpired = isExpired,
        };
    }

    private record BulkPoamStatusRequest(
        List<string> PoamIds,
        string Status,
        string? DelayReason = null,
        string? RevisedDate = null,
        string? Comments = null);

    private record MoveTaskRequest(
        string Status);

    // Fix #554: DTO for POST /api/dashboard/remediation/tasks
    private record CreateRemediationTaskRequest(
        string SystemId,
        string Title,
        string? Description = null,
        string? FindingId = null,
        string? Severity = null,
        string? ControlId = null,
        string? DueDate = null);

    // ─── Feature 039: POA&M request DTOs ─────────────────────────────────────

    private record Feature039CreatePoamRequest(
        string Weakness,
        string? WeaknessSource,
        string ControlId,
        string CatSeverity,
        string Poc,
        DateTime ScheduledCompletionDate,
        string? PocEmail = null,
        string? ResourcesRequired = null,
        decimal? CostEstimate = null,
        string? Comments = null,
        string? FindingId = null,
        List<string>? ComponentIds = null,
        List<Feature039MilestoneInput>? Milestones = null);

    private record Feature039MilestoneInput(
        string Description,
        DateTime TargetDate);

    private record Feature039UpdatePoamRequest(
        string RowVersion,
        string? Weakness = null,
        string? ControlId = null,
        string? Poc = null,
        string? PocEmail = null,
        string? Comments = null,
        string? ResourcesRequired = null,
        DateTime? ScheduledCompletionDate = null,
        decimal? CostEstimate = null);

    private record Feature039LinkComponentsRequest(
        List<string> ComponentIds);

    private record Feature039UnlinkComponentsRequest(
        List<string> ComponentIds);

    private record Feature039CreateTaskRequest(
        string BoardId);

    private record Feature039LinkTaskRequest(
        string TaskId);

    private record Feature039BulkCreateRequest(
        List<string> FindingIds,
        List<string>? ComponentIds = null,
        bool LinkRemediationTasks = false);

    private record Feature039StatusUpdateRequest(
        string Status,
        string RowVersion,
        string? DelayReason = null,
        DateTime? RevisedDate = null,
        string? DeviationId = null,
        string? Comments = null,
        bool CascadeToTask = false);

    private record Feature039BulkStatusRequest(
        List<string> PoamIds,
        string Status,
        string? DelayReason = null,
        DateTime? RevisedDate = null,
        string? Comments = null);

    // ─── Feature 039: Ticketing DTOs ────────────────────────────────────────

    private record ConfigureTicketingRequest(
        string Provider,
        string BaseUrl,
        string ProjectKey,
        string ApiKeySecretName,
        bool SyncEnabled = true);

    private record SyncTicketRequest(
        string Direction = "push");

    // ─── Feature 040: Component Discovery DTOs ──────────────────────────────

    private record DiscoverAzureComponentsRequest(
        string SubscriptionId,
        string? ResourceGroupFilter = null,
        string? ResourceTypeFilter = null,
        string? SearchFilter = null,
        string? Cursor = null);

    private record ImportAzureResourceItem(
        string ResourceId,
        string Name,
        string Type,
        string ResourceGroup,
        string Location);

    private record ImportAzureComponentsRequest(
        List<ImportAzureResourceItem> Resources);

    private record ImportSystemAzureComponentsRequest(
        List<ImportAzureResourceItem> Resources,
        List<string>? AssignExistingOrgComponents = null);

    // ─── Feature 040: Boundary Component Assignment DTOs ────────────────────

    private record AssignComponentToBoundaryRequest(
        string ComponentId,
        bool IsInScope = true,
        string? ExclusionRationale = null,
        string? InheritanceProvider = null,
        string? CreatedBy = null);

    private record UpdateBoundaryAssignmentRequest(
        bool IsInScope,
        string? ExclusionRationale = null,
        string? InheritanceProvider = null,
        string? ModifiedBy = null);

    private record AcquireLockRequest(
        string UserId,
        string UserDisplayName);

    // ─── Feature 042: System Capability Link DTOs ───────────────────────────

    private record LinkCapabilitiesRequest(
        List<string> CapabilityIds);

    // ─── Feature 043: Control Inheritance DTOs ──────────────────────────────

    private record Feature043DesignationInput(
        string ControlId,
        string InheritanceType,
        string? Provider = null,
        string? CustomerResponsibility = null);

    private record Feature043SetInheritanceRequest(
        List<Feature043DesignationInput> Designations,
        string? ChangeSource = "Manual");

    private record Feature043ApplyProfileRequest(
        string ProfileId,
        string? ConflictResolution = "skip",
        bool Preview = false);

    private record Feature043ImportApplyRequest(
        string PreviewToken,
        Feature043ColumnMapping ColumnMapping,
        string? ConflictResolution = "overwrite");

    private record Feature043ColumnMapping(
        string ControlId,
        string InheritanceType,
        string Provider,
        string CustomerResponsibility);

    // Feature 044 request types
    private record Feature044RevertRequest(
        List<string> ControlIds,
        string? RevertedBy = null);

    // Issue #418 — Enhanced Evidence Automation request types
    private record EvidenceCorrelateRequest(
        string ControlId,
        string EvidenceReferenceId,
        EvidenceSourceType SourceType,
        string? SubscriptionId = null,
        string? Note = null);
}
