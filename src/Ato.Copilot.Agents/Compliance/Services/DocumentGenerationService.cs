using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Generates FedRAMP-style compliance documents (SSP, SAR, POA&amp;M) in Markdown format
/// from the latest assessment data stored in the database.
/// </summary>
public class DocumentGenerationService : IDocumentGenerationService
{
    private readonly IDbContextFactory<AtoCopilotContext> _dbFactory;
    private readonly INistControlsService _nistService;
    private readonly ILogger<DocumentGenerationService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentGenerationService"/> class.
    /// </summary>
    public DocumentGenerationService(
        IDbContextFactory<AtoCopilotContext> dbFactory,
        INistControlsService nistService,
        ILogger<DocumentGenerationService> logger)
    {
        _dbFactory = dbFactory;
        _nistService = nistService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ComplianceDocument> GenerateDocumentAsync(
        string documentType,
        string? subscriptionId = null,
        string? framework = null,
        string? systemName = null,
        CancellationToken cancellationToken = default)
    {
        var docType = NormalizeDocumentType(documentType);
        var resolvedFramework = framework ?? "NIST80053";
        var resolvedSystemName = systemName ?? "Azure Government System";

        _logger.LogInformation(
            "Generating {DocType} document for {System} (framework: {Framework})",
            docType, resolvedSystemName, resolvedFramework);

        // Fetch latest assessment and findings
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var assessment = await GetLatestAssessmentAsync(db, subscriptionId, cancellationToken);
        var findings = assessment != null
            ? await db.Findings
                .Where(f => f.AssessmentId == assessment.Id)
                .OrderBy(f => f.ControlFamily)
                .ThenBy(f => f.ControlId)
                .ToListAsync(cancellationToken)
            : new List<ComplianceFinding>();

        // Fetch active compliance alerts for monitoring integration (FR-038)
        var activeAlerts = await db.ComplianceAlerts
            .Where(a => a.Status != AlertStatus.Resolved && a.Status != AlertStatus.Dismissed)
            .OrderByDescending(a => a.Severity)
            .ThenBy(a => a.ControlFamily)
            .ToListAsync(cancellationToken);

        var content = docType switch
        {
            "SSP" => await GenerateSspAsync(db, resolvedSystemName, resolvedFramework, assessment, findings, cancellationToken),
            "SAR" => GenerateSar(resolvedSystemName, resolvedFramework, assessment, findings, activeAlerts),
            "POAM" => GeneratePoam(resolvedSystemName, resolvedFramework, findings, activeAlerts),
            _ => throw new ArgumentException($"Unsupported document type: {docType}")
        };

        var document = new ComplianceDocument
        {
            DocumentType = docType,
            SystemName = resolvedSystemName,
            Framework = resolvedFramework,
            Content = content,
            AssessmentId = assessment?.Id,
            GeneratedBy = "ATO Copilot (automated)",
            Owner = resolvedSystemName,
            Metadata = new DocumentMetadata
            {
                SystemDescription = $"{resolvedSystemName} — {resolvedFramework} Compliance Documentation",
                DateRange = assessment != null
                    ? $"{assessment.AssessedAt:yyyy-MM-dd} to {(assessment.CompletedAt ?? DateTime.UtcNow):yyyy-MM-dd}"
                    : $"{DateTime.UtcNow:yyyy-MM-dd}",
                PreparedBy = "ATO Copilot",
                AuthorizationBoundary = $"{resolvedSystemName} Azure Government boundary"
            }
        };

        // Persist
        db.Documents.Add(document);
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Generated {DocType} document {Id} ({Len} chars)",
            docType, document.Id, content.Length);

        return document;
    }

    // ─── SSP Generation ──────────────────────────────────────────────────

    /// <summary>Generates a System Security Plan (SSP) document from the latest assessment data.</summary>
    private async Task<string> GenerateSspAsync(
        AtoCopilotContext db,
        string systemName,
        string framework,
        ComplianceAssessment? assessment,
        List<ComplianceFinding> findings,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();

        // Header
        sb.AppendLine($"# System Security Plan (SSP)");
        sb.AppendLine();
        sb.AppendLine($"**System Name**: {systemName}");
        sb.AppendLine($"**Framework**: {framework}");
        sb.AppendLine($"**Date**: {DateTime.UtcNow:yyyy-MM-dd}");
        sb.AppendLine($"**Prepared By**: ATO Copilot (automated)");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        // 1. System Identification
        sb.AppendLine("## 1. System Identification");
        sb.AppendLine();
        sb.AppendLine($"- **System Name**: {systemName}");
        sb.AppendLine($"- **Cloud Environment**: Azure Government");
        sb.AppendLine($"- **Compliance Framework**: {framework}");
        sb.AppendLine($"- **Impact Level**: High");
        sb.AppendLine();

        // 2. Assessment Summary
        sb.AppendLine("## 2. Assessment Summary");
        sb.AppendLine();
        if (assessment != null)
        {
            sb.AppendLine($"- **Assessment Date**: {assessment.AssessedAt:yyyy-MM-dd HH:mm} UTC");
            sb.AppendLine($"- **Compliance Score**: {assessment.ComplianceScore:F1}%");
            sb.AppendLine($"- **Total Controls**: {assessment.TotalControls}");
            sb.AppendLine($"- **Passed**: {assessment.PassedControls}");
            sb.AppendLine($"- **Failed**: {assessment.FailedControls}");
            sb.AppendLine($"- **Not Assessed**: {assessment.NotAssessedControls}");
            sb.AppendLine($"- **Scan Type**: {assessment.ScanType}");
        }
        else
        {
            sb.AppendLine("*No assessment data available. Run a compliance assessment first.*");
        }
        sb.AppendLine();

        // 3. Control Implementation Status
        sb.AppendLine("## 3. Control Implementation Status");
        sb.AppendLine();

        // Group findings by family
        var familyGroups = findings
            .GroupBy(f => f.ControlFamily)
            .OrderBy(g => g.Key);

        if (familyGroups.Any())
        {
            sb.AppendLine("| Control Family | Controls Evaluated | Findings | Status |");
            sb.AppendLine("|---|---|---|---|");

            foreach (var group in familyGroups)
            {
                var controlCount = group.Select(f => f.ControlId).Distinct().Count();
                var findingCount = group.Count();
                var status = group.Any(f => f.Severity == FindingSeverity.Critical || f.Severity == FindingSeverity.High)
                    ? "⚠ Action Required"
                    : findingCount > 0 ? "⚡ Review Needed" : "✓ Compliant";
                sb.AppendLine($"| {group.Key} | {controlCount} | {findingCount} | {status} |");
            }
        }
        else
        {
            sb.AppendLine("*No findings recorded.*");
        }
        sb.AppendLine();

        // 4. Control Details
        sb.AppendLine("## 4. Control Family Details");
        sb.AppendLine();

        // NIST catalog is loaded lazily on first query; no explicit call needed

        foreach (var group in familyGroups)
        {
            sb.AppendLine($"### {group.Key} — {GetFamilyName(group.Key)}");
            sb.AppendLine();

            foreach (var finding in group.OrderBy(f => f.ControlId))
            {
                sb.AppendLine($"#### {finding.ControlId}: {finding.Title}");
                sb.AppendLine();
                sb.AppendLine($"- **Severity**: {finding.Severity}");
                sb.AppendLine($"- **Status**: {finding.Status}");
                sb.AppendLine($"- **Resource**: `{finding.ResourceId}`");
                sb.AppendLine($"- **Description**: {finding.Description}");
                if (!string.IsNullOrEmpty(finding.RemediationGuidance))
                    sb.AppendLine($"- **Remediation**: {finding.RemediationGuidance}");
                sb.AppendLine();
            }
        }

        // 5. Appendix
        sb.AppendLine("## 5. Appendix");
        sb.AppendLine();

        // Appendix A — System Component Inventory
        sb.AppendLine("### Appendix A — System Component Inventory");
        sb.AppendLine();

        var registeredSystemId = assessment?.RegisteredSystemId;

        if (registeredSystemId is not null)
        {
            var components = await db.SystemComponents
                .Where(c => c.RegisteredSystemId == registeredSystemId)
                .Include(c => c.CapabilityLinks).ThenInclude(cl => cl.SecurityCapability)
                .OrderBy(c => c.ComponentType).ThenBy(c => c.Name)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (components.Count > 0)
            {
                sb.AppendLine("| Name | Type | Description | Owner | Capabilities | Status |");
                sb.AppendLine("|------|------|-------------|-------|-------------|--------|");
                foreach (var comp in components)
                {
                    var caps = string.Join(", ", comp.CapabilityLinks.Select(cl => cl.SecurityCapability.Name));
                    sb.AppendLine($"| {comp.Name} | {comp.ComponentType} | {comp.Description ?? "—"} | {comp.Owner ?? "—"} | {caps} | {comp.Status} |");
                }
            }
            else
            {
                sb.AppendLine("*No system components registered.*");
            }
        }
        else
        {
            sb.AppendLine("*System component inventory not available — register components via the dashboard.*");
        }

        sb.AppendLine();
        sb.AppendLine($"*This SSP was auto-generated on {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC by ATO Copilot.*");
        sb.AppendLine("*Review and approval by the System Owner and Authorizing Official is required.*");

        return sb.ToString();
    }

    // ─── SAR Generation ──────────────────────────────────────────────────

    /// <summary>Generates a Security Assessment Report (SAR) from the latest assessment data.</summary>
    private static string GenerateSar(
        string systemName,
        string framework,
        ComplianceAssessment? assessment,
        List<ComplianceFinding> findings,
        List<ComplianceAlert> activeAlerts)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# Security Assessment Report (SAR)");
        sb.AppendLine();
        sb.AppendLine($"**System Name**: {systemName}");
        sb.AppendLine($"**Framework**: {framework}");
        sb.AppendLine($"**Date**: {DateTime.UtcNow:yyyy-MM-dd}");
        sb.AppendLine($"**Prepared By**: ATO Copilot (automated)");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        // Executive Summary
        sb.AppendLine("## 1. Executive Summary");
        sb.AppendLine();
        if (assessment != null)
        {
            var riskLevel = assessment.ComplianceScore >= 90 ? "Low"
                : assessment.ComplianceScore >= 70 ? "Moderate" : "High";

            sb.AppendLine($"An automated security assessment of **{systemName}** was conducted on " +
                          $"{assessment.AssessedAt:yyyy-MM-dd}. The overall compliance score is " +
                          $"**{assessment.ComplianceScore:F1}%**, indicating a **{riskLevel}** risk posture.");
            sb.AppendLine();
            sb.AppendLine($"- **Controls Evaluated**: {assessment.TotalControls}");
            sb.AppendLine($"- **Passed**: {assessment.PassedControls}");
            sb.AppendLine($"- **Failed**: {assessment.FailedControls}");
        }
        else
        {
            sb.AppendLine("*No assessment data available.*");
        }
        sb.AppendLine();

        // Methodology
        sb.AppendLine("## 2. Assessment Methodology");
        sb.AppendLine();
        sb.AppendLine("The assessment was conducted using automated tools:");
        sb.AppendLine("- **Azure Policy**: Policy compliance state evaluation");
        sb.AppendLine("- **Microsoft Defender for Cloud**: Security assessment and secure score");
        sb.AppendLine("- **NIST 800-53 Rev. 5**: Control framework mapping");
        sb.AppendLine();

        // Findings Summary
        sb.AppendLine("## 3. Findings Summary");
        sb.AppendLine();

        if (findings.Count > 0)
        {
            var critical = findings.Count(f => f.Severity == FindingSeverity.Critical);
            var high = findings.Count(f => f.Severity == FindingSeverity.High);
            var medium = findings.Count(f => f.Severity == FindingSeverity.Medium);
            var low = findings.Count(f => f.Severity == FindingSeverity.Low);

            sb.AppendLine("| Severity | Count |");
            sb.AppendLine("|----------|-------|");
            sb.AppendLine($"| Critical | {critical} |");
            sb.AppendLine($"| High | {high} |");
            sb.AppendLine($"| Medium | {medium} |");
            sb.AppendLine($"| Low | {low} |");
            sb.AppendLine($"| **Total** | **{findings.Count}** |");
            sb.AppendLine();

            // Detailed findings
            sb.AppendLine("## 4. Detailed Findings");
            sb.AppendLine();

            int idx = 1;
            foreach (var finding in findings.OrderByDescending(f => f.Severity))
            {
                sb.AppendLine($"### Finding {idx}: {finding.Title}");
                sb.AppendLine();
                sb.AppendLine($"- **Control**: {finding.ControlId}");
                sb.AppendLine($"- **Severity**: {finding.Severity}");
                sb.AppendLine($"- **Resource**: `{finding.ResourceId}`");
                sb.AppendLine($"- **Description**: {finding.Description}");
                sb.AppendLine($"- **Remediation**: {finding.RemediationGuidance}");
                sb.AppendLine();
                idx++;
            }
        }
        else
        {
            sb.AppendLine("No findings were identified during this assessment.");
            sb.AppendLine();
        }

        // Recommendation
        sb.AppendLine("## 5. Recommendation");
        sb.AppendLine();
        if (assessment != null && assessment.ComplianceScore >= 90)
        {
            sb.AppendLine($"Based on the assessment results (**{assessment.ComplianceScore:F1}%** compliance), " +
                          "the system is recommended for **Authorization to Operate (ATO)** with the condition " +
                          "that remaining findings are addressed per the POA&M.");
        }
        else
        {
            sb.AppendLine("The system requires additional remediation before Authorization to Operate (ATO). " +
                          "Refer to the POA&M for required actions.");
        }
        sb.AppendLine();

        // Monitoring Coverage (FR-038)
        sb.AppendLine("## 6. Continuous Monitoring Status");
        sb.AppendLine();
        if (activeAlerts.Count > 0)
        {
            sb.AppendLine($"The system currently has **{activeAlerts.Count}** active compliance monitoring alerts:");
            sb.AppendLine();
            sb.AppendLine($"- **Critical**: {activeAlerts.Count(a => a.Severity == AlertSeverity.Critical)}");
            sb.AppendLine($"- **High**: {activeAlerts.Count(a => a.Severity == AlertSeverity.High)}");
            sb.AppendLine($"- **Medium**: {activeAlerts.Count(a => a.Severity == AlertSeverity.Medium)}");
            sb.AppendLine($"- **Low**: {activeAlerts.Count(a => a.Severity == AlertSeverity.Low)}");
            sb.AppendLine();
            sb.AppendLine("These alerts represent active compliance drift or violations detected by the " +
                          "continuous monitoring system and should be addressed alongside assessment findings.");
        }
        else
        {
            sb.AppendLine("No active monitoring alerts. Continuous monitoring is operational with no outstanding issues.");
        }
        sb.AppendLine();

        sb.AppendLine($"*This SAR was auto-generated on {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC by ATO Copilot.*");

        return sb.ToString();
    }

    // ─── POA&M Generation ────────────────────────────────────────────────

    /// <summary>Generates a Plan of Action and Milestones (POA&amp;M) from the latest assessment data.</summary>
    private static string GeneratePoam(
        string systemName,
        string framework,
        List<ComplianceFinding> findings,
        List<ComplianceAlert> activeAlerts)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# Plan of Action and Milestones (POA&M)");
        sb.AppendLine();
        sb.AppendLine($"**System Name**: {systemName}");
        sb.AppendLine($"**Framework**: {framework}");
        sb.AppendLine($"**Date**: {DateTime.UtcNow:yyyy-MM-dd}");
        sb.AppendLine($"**Prepared By**: ATO Copilot (automated)");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        sb.AppendLine("## Open Findings");
        sb.AppendLine();

        var openFindings = findings
            .Where(f => f.Status != FindingStatus.Remediated && f.Status != FindingStatus.Accepted)
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.ControlId)
            .ToList();

        if (openFindings.Count > 0)
        {
            sb.AppendLine("| # | Control | Severity | Finding | Resource | Estimated Completion | Status |");
            sb.AppendLine("|---|---------|----------|---------|----------|---------------------|--------|");

            int idx = 1;
            foreach (var finding in openFindings)
            {
                var est = EstimateCompletion(finding.Severity);
                var truncatedTitle = finding.Title.Length > 40
                    ? finding.Title[..37] + "..."
                    : finding.Title;
                var truncatedResource = finding.ResourceId.Length > 30
                    ? "..." + finding.ResourceId[^27..]
                    : finding.ResourceId;

                sb.AppendLine($"| {idx} | {finding.ControlId} | {finding.Severity} | {truncatedTitle} | {truncatedResource} | {est} | {finding.Status} |");
                idx++;
            }
            sb.AppendLine();

            // Summary statistics
            sb.AppendLine("## Summary");
            sb.AppendLine();
            sb.AppendLine($"- **Total Open Items**: {openFindings.Count}");
            sb.AppendLine($"- **Critical**: {openFindings.Count(f => f.Severity == FindingSeverity.Critical)}");
            sb.AppendLine($"- **High**: {openFindings.Count(f => f.Severity == FindingSeverity.High)}");
            sb.AppendLine($"- **Medium**: {openFindings.Count(f => f.Severity == FindingSeverity.Medium)}");
            sb.AppendLine($"- **Low**: {openFindings.Count(f => f.Severity == FindingSeverity.Low)}");
        }
        else
        {
            sb.AppendLine("*No open findings. All controls are compliant or accepted.*");
        }
        sb.AppendLine();

        // Remediation Details
        if (openFindings.Count > 0)
        {
            sb.AppendLine("## Remediation Details");
            sb.AppendLine();

            int idx = 1;
            foreach (var finding in openFindings)
            {
                sb.AppendLine($"### {idx}. {finding.ControlId} — {finding.Title}");
                sb.AppendLine();
                sb.AppendLine($"- **Description**: {finding.Description}");
                sb.AppendLine($"- **Remediation Guidance**: {finding.RemediationGuidance}");
                sb.AppendLine($"- **Auto-Remediable**: {(finding.AutoRemediable ? "Yes" : "No")}");
                if (!string.IsNullOrEmpty(finding.RemediationScript))
                {
                    sb.AppendLine($"- **Remediation Script**:");
                    sb.AppendLine("```powershell");
                    sb.AppendLine(finding.RemediationScript);
                    sb.AppendLine("```");
                }
                sb.AppendLine();
                idx++;
            }
        }

        // Monitoring Alerts Section (FR-038)
        if (activeAlerts.Count > 0)
        {
            sb.AppendLine("## Active Monitoring Alerts");
            sb.AppendLine();
            sb.AppendLine("| # | Alert ID | Type | Severity | Control | Title | Created |");
            sb.AppendLine("|---|----------|------|----------|---------|-------|---------|");

            int alertIdx = 1;
            foreach (var alert in activeAlerts)
            {
                var truncatedTitle = alert.Title.Length > 35
                    ? alert.Title[..32] + "..."
                    : alert.Title;
                sb.AppendLine($"| {alertIdx} | {alert.AlertId} | {alert.Type} | {alert.Severity} | {alert.ControlFamily} | {truncatedTitle} | {alert.CreatedAt:yyyy-MM-dd} |");
                alertIdx++;
            }
            sb.AppendLine();
            sb.AppendLine($"- **Total Active Alerts**: {activeAlerts.Count}");
            sb.AppendLine($"- **Critical**: {activeAlerts.Count(a => a.Severity == AlertSeverity.Critical)}");
            sb.AppendLine($"- **High**: {activeAlerts.Count(a => a.Severity == AlertSeverity.High)}");
            sb.AppendLine();
        }

        sb.AppendLine($"*This POA&M was auto-generated on {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC by ATO Copilot.*");

        return sb.ToString();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    /// <summary>Retrieves the most recent compliance assessment from the database.</summary>
    private async Task<ComplianceAssessment?> GetLatestAssessmentAsync(
        AtoCopilotContext db,
        string? subscriptionId,
        CancellationToken cancellationToken)
    {
        var query = db.Assessments.AsQueryable();

        if (!string.IsNullOrEmpty(subscriptionId))
            query = query.Where(a => a.SubscriptionId == subscriptionId);

        return await query
            .OrderByDescending(a => a.AssessedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>Normalizes a document type string to a canonical form (ssp, poam, sar).</summary>
    public static string NormalizeDocumentType(string documentType)
    {
        return documentType.Trim().ToUpperInvariant() switch
        {
            "SSP" or "SYSTEM SECURITY PLAN" or "SYSTEMSECURITYPLAN" => "SSP",
            "SAR" or "SECURITY ASSESSMENT REPORT" or "SECURITYASSESSMENTREPORT" => "SAR",
            "POAM" or "POA&M" or "PLAN OF ACTION" or "PLANOFACTION" => "POAM",
            _ => documentType.Trim().ToUpperInvariant()
        };
    }

    /// <summary>Estimates the completion timeframe based on finding severity.</summary>
    private static string EstimateCompletion(FindingSeverity severity) =>
        severity switch
        {
            FindingSeverity.Critical => DateTime.UtcNow.AddDays(7).ToString("yyyy-MM-dd"),
            FindingSeverity.High => DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd"),
            FindingSeverity.Medium => DateTime.UtcNow.AddDays(90).ToString("yyyy-MM-dd"),
            FindingSeverity.Low => DateTime.UtcNow.AddDays(180).ToString("yyyy-MM-dd"),
            _ => DateTime.UtcNow.AddDays(90).ToString("yyyy-MM-dd")
        };

    /// <summary>Returns the full display name for a NIST control family abbreviation.</summary>
    private static string GetFamilyName(string family) =>
        family switch
        {
            "AC" => "Access Control",
            "AU" => "Audit and Accountability",
            "AT" => "Awareness and Training",
            "CM" => "Configuration Management",
            "CP" => "Contingency Planning",
            "IA" => "Identification and Authentication",
            "IR" => "Incident Response",
            "MA" => "Maintenance",
            "MP" => "Media Protection",
            "PE" => "Physical and Environmental Protection",
            "PL" => "Planning",
            "PM" => "Program Management",
            "PS" => "Personnel Security",
            "PT" => "Personally Identifiable Information Processing and Transparency",
            "RA" => "Risk Assessment",
            "SA" => "System and Services Acquisition",
            "SC" => "System and Communications Protection",
            "SI" => "System and Information Integrity",
            "SR" => "Supply Chain Risk Management",
            _ => family
        };
}
