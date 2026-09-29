using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Services;

/// <summary>
/// Lossless document projections from the canonical SAP, SAR and Dashboard POA&amp;M
/// stores. These are application JSON documents, not OSCAL exports: the OSCAL
/// projections omit saved narrative and/or completed register entries.
/// </summary>
public sealed class WorkingDocumentPreviewService(
    ISapService plans, ISecurityAssessmentReportService reports, PoamService register)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<object> PreviewAsync(RegisteredSystem system, string documentType, CancellationToken ct)
    {
        object document;
        string root;
        string status;
        var sources = new List<WorkingDocumentSourceRecord>();
        var gaps = new List<WorkingDocumentSourceGap>
        {
            new("CANONICAL_JSON_NOT_OSCAL",
                "Complete saved application document JSON; not an OSCAL export or an approved authorization artifact.")
        };
        switch (documentType)
        {
            case "sap":
                var sap = await plans.GetWorkingSapAsync(system.Id, ct);
                if (sap == null) return Missing(system, documentType);
                root = "security-assessment-plan";
                status = sap.Status.ToString();
                document = ProjectSap(sap);
                sources.Add(Source("SecurityAssessmentPlan", sap.Id, document));
                break;
            case "sar":
                var sar = await reports.GetSarForSystemAsync(system.Id, ct);
                if (sar == null) return Missing(system, documentType);
                root = "security-assessment-report";
                status = sar.Status.ToString();
                document = ProjectSar(sar);
                sources.Add(Source("SecurityAssessmentReport", sar.Id, document));
                break;
            case "poam":
                var items = await register.GetWorkingRegisterAsync(system.Id, ct);
                root = "poam-register";
                status = "CurrentRegister";
                var projected = items.Select(ProjectPoam).ToArray();
                document = new { systemId = system.Id, systemName = system.Name, itemCount = items.Count, items = projected };
                sources.AddRange(items.Select((item, i) => Source("PoamItem", item.Id, projected[i])));
                if (items.Count == 0)
                    gaps.Add(new("EMPTY_POAM_REGISTER", "No saved POA&M items exist for this system. The current register contains 0 items."));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(documentType));
        }
        var content = JsonSerializer.Serialize(new Dictionary<string, object> { [root] = document }, JsonOptions);
        return new WorkingDocumentPreviewDto(system.Id, system.Name, documentType, true,
            "json", "application/json", content, Hash(content), DateTimeOffset.UtcNow, gaps, status, sources);
    }

    private static MissingWorkingDocumentPreviewDto Missing(RegisteredSystem system, string type) =>
        new(system.Id, system.Name, type, false, $"{type.ToUpperInvariant()}_NOT_FOUND",
            $"No saved {type.ToUpperInvariant()} exists for this system. Preview does not create a document.");

    private static WorkingDocumentSourceRecord Source(string kind, string id, object document) =>
        new(kind, id, null, Hash(JsonSerializer.Serialize(document, JsonOptions)));

    private static string Hash(string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    private static object ProjectSap(SecurityAssessmentPlan sap) => new
    {
        sap.Id, sap.RegisteredSystemId, sap.AssessmentId, sap.Status, sap.Title, sap.BaselineLevel,
        sap.ScopeNotes, sap.AssessmentLead, sap.AssessmentApproach, sap.RulesOfEngagement,
        sap.ScheduleStart, sap.ScheduleEnd, sap.Content, sap.ContentHash, sap.TotalControls,
        sap.CustomerControls, sap.InheritedControls, sap.SharedControls, sap.StigBenchmarkCount,
        sap.GeneratedBy, sap.GeneratedAt, sap.FinalizedBy, sap.FinalizedAt, sap.Format,
        ControlEntries = sap.ControlEntries.OrderBy(x => x.ControlId).ThenBy(x => x.Id).Select(x => new
        {
            x.Id, x.SecurityAssessmentPlanId, x.ControlId, x.ControlTitle, x.ControlFamily,
            x.InheritanceType, x.Provider, x.AssessmentMethods, x.AssessmentObjectives, x.EvidenceRequirements,
            x.StigBenchmarks, x.EvidenceExpected, x.EvidenceCollected, x.IsMethodOverridden, x.OverrideRationale
        }),
        TeamMembers = sap.TeamMembers.OrderBy(x => x.Id).Select(x => new
        {
            x.Id, x.SecurityAssessmentPlanId, x.Name, x.Organization, x.Role, x.ContactInfo
        })
    };

    private static object ProjectSar(SecurityAssessmentReport sar) => new
    {
        sar.Id, sar.RegisteredSystemId, sar.SapId, sar.Title, sar.Status,
        sar.AssessmentStartDate, sar.AssessmentEndDate, sar.TotalControlsAssessed, sar.TotalControlsPending,
        sar.SatisfiedCount, sar.NotSatisfiedCount, sar.FindingsBySeverity, sar.FindingsByFamily,
        sar.CreatedBy, sar.CreatedAt, sar.ModifiedBy, sar.ModifiedAt, sar.ReviewedBy, sar.ReviewedAt,
        sar.ApprovedBy, sar.ApprovedAt,
        Sections = sar.Sections.OrderBy(x => x.SectionType).ThenBy(x => x.Id).Select(x => new
        {
            x.Id, x.SecurityAssessmentReportId, x.SectionType, x.Title, x.Content,
            x.IsAutoGenerated, x.ModifiedBy, x.ModifiedAt
        })
    };

    private static object ProjectPoam(PoamItem item) => new
    {
        item.Id, item.RegisteredSystemId, item.FindingId, item.RemediationTaskId,
        item.Weakness, item.WeaknessSource, item.SecurityControlNumber, item.CatSeverity,
        item.PointOfContact, item.PocEmail, item.ResourcesRequired, item.CostEstimate,
        item.ScheduledCompletionDate, item.ActualCompletionDate, item.Status, item.Comments,
        item.CreatedAt, item.ModifiedAt, item.DeviationId, item.CreatedBy, item.ModifiedBy, item.ExternalTicketRef,
        Milestones = item.Milestones.OrderBy(x => x.Sequence).ThenBy(x => x.Id).Select(x => new
        {
            x.Id, x.PoamItemId, x.Description, x.TargetDate, x.CompletedDate, x.Sequence
        }),
        ComponentLinks = item.ComponentLinks.OrderBy(x => x.Id).Select(x => new
        {
            x.Id, x.PoamItemId, x.SystemComponentId, x.LinkedAt, x.LinkedBy
        }),
        History = item.History.OrderBy(x => x.Timestamp).ThenBy(x => x.Id).Select(x => new
        {
            x.Id, x.PoamItemId, x.EventType, x.OldValue, x.NewValue, x.ActingUserId,
            x.ActingUserName, x.Timestamp, x.Details, x.CascadeOrigin
        })
    };
}
