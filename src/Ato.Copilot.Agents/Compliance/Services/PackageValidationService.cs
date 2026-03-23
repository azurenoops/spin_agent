using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Pre-submission validation for authorization packages.
/// Checks artifact presence, OSCAL version/schema consistency, SSP completeness,
/// SAR status, cross-artifact control ID matching, and evidence coverage.
/// </summary>
public class PackageValidationService : IPackageValidationService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOscalSchemaValidationService _schemaValidator;
    private readonly IEvidenceArtifactService _evidenceService;
    private readonly ILogger<PackageValidationService> _logger;

    public PackageValidationService(
        IServiceScopeFactory scopeFactory,
        IOscalSchemaValidationService schemaValidator,
        IEvidenceArtifactService evidenceService,
        ILogger<PackageValidationService> logger)
    {
        _scopeFactory = scopeFactory;
        _schemaValidator = schemaValidator;
        _evidenceService = evidenceService;
        _logger = logger;
    }

    public async Task<PackageValidationResult> ValidateAsync(
        string systemId,
        string validatedBy = "mcp-user",
        CancellationToken cancellationToken = default)
    {
        var findings = new List<ValidationFinding>();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        // ─── 1. Authorization Boundary (FR-020a — block if missing) ─────────
        var hasBoundary = await db.AuthorizationBoundaryDefinitions
            .AnyAsync(b => b.RegisteredSystemId == systemId, cancellationToken);
        if (!hasBoundary)
        {
            findings.Add(Error("boundary", null,
                "No authorization boundary definition found. An authorization boundary is required.",
                "Go to Boundaries and define at least one authorization boundary for this system."));
        }

        // ─── 2. SSP Section Completeness ────────────────────────────────────
        var sspSections = await db.SspSections
            .Where(s => s.RegisteredSystemId == systemId)
            .Select(s => new { s.SectionNumber, s.SectionTitle, s.Status })
            .ToListAsync(cancellationToken);

        if (sspSections.Count == 0)
        {
            findings.Add(Error("ssp", "ssp",
                "No SSP sections found for this system.",
                "Go to Narratives to author SSP control narrative sections, or use the 'compliance_author_narrative' tool."));
        }
        else
        {
            var notApproved = sspSections.Where(s => s.Status != SspSectionStatus.Approved).ToList();
            foreach (var section in notApproved)
            {
                findings.Add(Error("ssp", "ssp",
                    $"SSP section §{section.SectionNumber} ({section.SectionTitle}) is '{section.Status}' — must be Approved.",
                    $"Go to Narratives and submit section §{section.SectionNumber} for review, then approve it."));
            }
        }

        // ─── 3. SAR Status ──────────────────────────────────────────────────
        var sar = await db.SecurityAssessmentReports
            .Where(s => s.RegisteredSystemId == systemId)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (sar == null)
        {
            findings.Add(Error("sar", "sar",
                "No Security Assessment Report (SAR) found.",
                "Go to Assessments and generate a SAR, or use the 'compliance_generate_sar' tool in Copilot chat."));
        }
        else if (sar.Status != SarStatus.Approved)
        {
            findings.Add(Error("sar", "sar",
                $"SAR is '{sar.Status}' — must be Approved before package generation.",
                "Go to Assessments and complete the SAR review/approval workflow."));
        }

        // ─── 4. SAP Status ──────────────────────────────────────────────────
        var sap = await db.SecurityAssessmentPlans
            .Where(s => s.RegisteredSystemId == systemId)
            .OrderByDescending(s => s.GeneratedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (sap == null)
        {
            findings.Add(Error("sap", "assessment-plan",
                "No Security Assessment Plan (SAP) found.",
                "Go to Assessments to create and finalize an Assessment Plan, or use the 'compliance_export_oscal' tool with model 'assessment-plan'."));
        }
        else if (sap.Status != SapStatus.Finalized)
        {
            findings.Add(Error("sap", "assessment-plan",
                $"SAP is '{sap.Status}' — must be Finalized before package generation.",
                "Go to Assessments and finalize the SAP to lock its contents and integrity hash."));
        }

        // ─── 5. POA&M Items Exist ───────────────────────────────────────────
        var poamCount = await db.PoamItems
            .CountAsync(p => p.RegisteredSystemId == systemId, cancellationToken);
        // POA&M is required for the package, but having zero items is a warning, not a blocker
        if (poamCount == 0)
        {
            findings.Add(Warning("poam", "poam",
                "No POA&M items found. The OSCAL POA&M artifact will be empty.",
                "Go to POA&M to create items from assessment findings, or import scan results from Assessments."));
        }

        // ─── 6. Cross-Artifact Control ID Matching ──────────────────────────
        var sspControlIds = await db.ControlImplementations
            .Where(ci => ci.RegisteredSystemId == systemId)
            .Select(ci => ci.ControlId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var poamControlIds = await db.PoamItems
            .Where(p => p.RegisteredSystemId == systemId)
            .Select(p => p.SecurityControlNumber)
            .Distinct()
            .ToListAsync(cancellationToken);

        var sspSet = sspControlIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // POA&M items should reference controls that exist in the SSP
        var orphanedPoam = poamControlIds.Where(c => !sspSet.Contains(c)).ToList();
        foreach (var controlId in orphanedPoam)
        {
            findings.Add(Warning("cross-reference", "poam",
                $"POA&M references control '{controlId}' which is not in the SSP control implementations.",
                $"Go to POA&M and verify that '{controlId}' is the correct control ID, or add it to the SSP via Gap Analysis."));
        }

        // ─── 7. OSCAL Schema Validation ─────────────────────────────────────
        var models = new[] { "ssp", "poam", "assessment-results", "assessment-plan" };
        foreach (var model in models)
        {
            try
            {
                var schemaResult = await _schemaValidator.ValidateForSystemAsync(systemId, model, cancellationToken);
                if (!schemaResult.IsValid)
                {
                    var violationSummary = schemaResult.Violations.Count > 3
                        ? string.Join("; ", schemaResult.Violations.Take(3).Select(v => v.Message)) + $" ... and {schemaResult.Violations.Count - 3} more"
                        : string.Join("; ", schemaResult.Violations.Select(v => v.Message));

                    var schemaHint = model switch
                    {
                        "ssp" => "Review SSP data in Narratives and Gap Analysis to fix the violations.",
                        "poam" => "Go to POA&M and verify item data to fix the violations.",
                        "assessment-results" => "Go to Assessments and verify scan import data to fix the violations.",
                        "assessment-plan" => "Go to Assessments and ensure the SAP data is complete and valid.",
                        _ => $"Review the {model} data sources to fix the violations."
                    };
                    findings.Add(Error("schema", model,
                        $"OSCAL {model} schema validation failed with {schemaResult.Violations.Count} violation(s): {violationSummary}",
                        schemaHint));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Schema validation for {Model} could not be completed", model);
                findings.Add(Warning("schema", model,
                    $"OSCAL {model} schema validation could not be completed: {ex.Message}",
                    $"Ensure the {model} data exists. Check the relevant page (Narratives for SSP, POA&M, or Assessments) and verify data can be exported."));
            }
        }

        // ─── 8. Evidence Coverage (Warnings) ────────────────────────────────
        try
        {
            var evidenceSummary = await _evidenceService.GetSummaryAsync(systemId, cancellationToken);
            if (evidenceSummary.CoveragePercentage < 100)
            {
                findings.Add(Warning("evidence", null,
                    $"Evidence coverage is {evidenceSummary.CoveragePercentage:F0}% — some controls lack supporting evidence.",
                    "Go to Evidence to upload artifacts for controls with missing coverage."));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Evidence coverage check could not be completed for system {SystemId}", systemId);
        }

        // ─── Build Result ───────────────────────────────────────────────────
        var errorCount = findings.Count(f => f.Severity == ValidationSeverity.Error);
        var warningCount = findings.Count(f => f.Severity == ValidationSeverity.Warning);

        return new PackageValidationResult
        {
            IsValid = errorCount == 0,
            ErrorCount = errorCount,
            WarningCount = warningCount,
            ValidatedAt = DateTimeOffset.UtcNow,
            ValidatedBy = validatedBy,
            Findings = findings
        };
    }

    private static ValidationFinding Error(string category, string? artifactType, string description, string remediation) =>
        new()
        {
            Severity = ValidationSeverity.Error,
            Category = category,
            ArtifactType = artifactType,
            Description = description,
            Remediation = remediation
        };

    private static ValidationFinding Warning(string category, string? artifactType, string description, string remediation) =>
        new()
        {
            Severity = ValidationSeverity.Warning,
            Category = category,
            ArtifactType = artifactType,
            Description = description,
            Remediation = remediation
        };
}
