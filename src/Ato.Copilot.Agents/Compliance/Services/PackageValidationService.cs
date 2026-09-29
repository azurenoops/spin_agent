using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
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

    public Task<PackageValidationResult> ValidateAsync(
        string systemId,
        string validatedBy = "mcp-user",
        CancellationToken cancellationToken = default) =>
        ValidateAsync(systemId, PackagePurpose.Legacy, validatedBy, cancellationToken);

    public async Task<PackageValidationResult> ValidateAsync(
        string systemId,
        PackagePurpose purpose,
        string validatedBy = "mcp-user",
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(purpose)) throw new ArgumentOutOfRangeException(nameof(purpose));
        var findings = new List<ValidationFinding>();
        var checks = new List<PackageReadinessCheck>();
        if (purpose is PackagePurpose.AuthorizedBaselineArchive or PackagePurpose.ChangeSubmission)
            return new PackageValidationResult
            {
                ValidatedBy = validatedBy, IsValid = false, ErrorCount = 1,
                Findings = [Error("retained-baseline", null, "Select a completed retained package, its content hash, and a recorded authorization decision.",
                    "Use retained-context validation for archive/change purposes.")]
            };

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var system = await db.RegisteredSystems.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == systemId, cancellationToken)
            ?? throw new InvalidOperationException("System not found in the current workspace.");
        var providerGaps = new List<string>();
        await ProviderDocumentProvenance.ResolveAsync(db, system, providerGaps, cancellationToken);
        checks.Add(Check("provider-authorization", "Provider authorization provenance",
            providerGaps.Count == 0 ? "Passed" : "Blocking", true,
            providerGaps.Count == 0 ? "Selected provider adoption provenance was evaluated; absence does not assert inherited responsibility." : string.Join("\n", providerGaps),
            "Review selected provider source metadata.", "Issm"));
        findings.AddRange(providerGaps.Select(gap => Error("provider-authorization", "ssp", gap,
            "Review the selected provider adoption and its recorded source metadata before generating a final package.")));
        var profileGaps = new List<string>();
        await ApprovedProfileDocumentData.LoadAsync(db, systemId, profileGaps, cancellationToken);
        var hasProfiles = await db.SystemProfileSections.AnyAsync(x => x.RegisteredSystemId == systemId, cancellationToken);
        checks.Add(Check("profile-approval", "Retained approved profile snapshots", !hasProfiles ? "NotApplicable" : profileGaps.Count == 0 ? "Passed" : "Blocking",
            hasProfiles, !hasProfiles ? "There are no recorded profile snapshots to verify; mandatory SSP sections remain separately evaluated."
                : profileGaps.Count == 0 ? "Existing profile snapshots passed the canonical integrity checks; missing SSP sections are checked separately."
                : string.Join("\n", profileGaps), "Review the exact profile scalar and child-row snapshots.", "Issm"));
        findings.AddRange(profileGaps.Select(gap => Error("profile-approval", "ssp", gap,
            "Review and approve the scalar values and structured rows together before final generation.")));

        // ─── 1. AO Authorization Decision ──────────────────────────────────
        var now = DateTime.UtcNow;
        var hasActiveDecision = await db.AuthorizationDecisions
            .AnyAsync(decision => decision.RegisteredSystemId == systemId
                && decision.IsActive
                && (decision.ExpirationDate == null || decision.ExpirationDate > now),
                cancellationToken);
        checks.Add(Check("authorization-decision", "Recorded authorization decision",
            purpose == PackagePurpose.InitialSubmission ? "NotApplicable" : hasActiveDecision ? "Passed" : "Blocking",
            purpose != PackagePurpose.InitialSubmission,
            purpose == PackagePurpose.InitialSubmission ? "Initial submission seeks a decision and does not require a pre-existing AO decision."
                : hasActiveDecision ? "A current active decision is recorded." : "No current active decision is recorded.",
            "Open recorded decisions and the authorization workflow.", "AuthorizingOfficial"));
        if (purpose == PackagePurpose.Legacy && !hasActiveDecision)
        {
            findings.Add(Error("authorization-decision", "ato-letter",
                "No active authorization decision found, or the latest decision has expired.",
                "Go to Authorize and have an Authorizing Official issue a current decision before generating the package."));
        }

        // ─── 2. Authorization Boundary (FR-020a — block if missing) ─────────
        var hasBoundary = await db.AuthorizationBoundaryDefinitions
            .AnyAsync(b => b.RegisteredSystemId == systemId, cancellationToken);
        checks.Add(Check("boundary", "Authorization boundary definition", hasBoundary ? "Passed" : "Blocking", true,
            hasBoundary ? "A modern boundary definition exists; this check does not certify inventory or component completeness."
                : "No authorization boundary definition is recorded.", "Define the authorization boundary.", "Issm"));
        if (!hasBoundary)
        {
            findings.Add(Error("boundary", null,
                "No authorization boundary definition found. An authorization boundary is required.",
                "Go to Boundaries and define at least one authorization boundary for this system."));
        }

        // ─── 3. SSP Section Completeness ────────────────────────────────────
        var sspSections = await db.SspSections
            .Where(s => s.RegisteredSystemId == systemId)
            .Select(s => new { s.SectionNumber, s.SectionTitle, s.Status })
            .ToListAsync(cancellationToken);

        if (purpose == PackagePurpose.InitialSubmission)
        {
            foreach (var number in Enumerable.Range(1, 13).Except(sspSections.Select(s => s.SectionNumber)))
            {
                checks.Add(Check($"ssp-section-{number}", $"SSP section {number}", "Blocking", true,
                    "Required initial-submission SSP section is missing.", "Author and review the SSP section.", "Issm", "ssp"));
                findings.Add(Error("ssp", "ssp", $"Required SSP section §{number} is missing.",
                    $"Author and approve SSP section §{number} before preparing the initial submission."));
            }
        }
        checks.Add(Check("ssp", "SSP section records", sspSections.Count > 0 ? "Passed" : "Blocking", true,
            sspSections.Count > 0 ? "SSP section records exist; each recorded section's review status is checked independently." : "No SSP sections are recorded.",
            "Open SSP narratives and section authoring.", "Isso"));
        foreach (var section in sspSections)
            checks.Add(Check($"ssp-section-{section.SectionNumber}", $"SSP section {section.SectionNumber}: {section.SectionTitle}",
                section.Status == SspSectionStatus.Approved ? "Passed" : "Blocking", true,
                $"Recorded section status: {section.Status}. Required status: Approved.", "Complete the section's author/reviewer workflow.", "Issm", "ssp"));

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

        // ─── 4. SAR Status ──────────────────────────────────────────────────
        var sar = await db.SecurityAssessmentReports
            .Where(s => s.RegisteredSystemId == systemId)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        checks.Add(Check("sar", "Security Assessment Report", sar?.Status == SarStatus.Approved ? "Passed" : "Blocking", true,
            sar == null ? "No SAR is recorded." : $"Recorded SAR status: {sar.Status}. Required status: Approved.",
            "Open the assessment report and its review workflow.", "Sca"));

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

        // ─── 5. SAP Status ──────────────────────────────────────────────────
        var sap = await db.SecurityAssessmentPlans
            .Where(s => s.RegisteredSystemId == systemId)
            .OrderByDescending(s => s.GeneratedAt)
            .FirstOrDefaultAsync(cancellationToken);
        checks.Add(Check("sap", "Security Assessment Plan", sap?.Status == SapStatus.Finalized ? "Passed" : "Blocking", true,
            sap == null ? "No SAP is recorded." : $"Recorded SAP status: {sap.Status}. Required status: Finalized.",
            "Complete and finalize the assessment plan.", "Sca"));

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

        // ─── 6. POA&M Items Exist ───────────────────────────────────────────
        var poamCount = await db.PoamItems
            .CountAsync(p => p.RegisteredSystemId == systemId, cancellationToken);
        checks.Add(Check("poam", "POA&M records", poamCount > 0 ? "Passed" : "FollowUp", false,
            poamCount > 0 ? "POA&M records exist. Open items do not inherently prohibit submission."
                : "The POA&M artifact will contain no items.", "Review recorded risks and POA&M applicability.", "Issm"));
        // POA&M is required for the package, but having zero items is a warning, not a blocker
        if (poamCount == 0)
        {
            findings.Add(Warning("poam", "poam",
                "No POA&M items found. The OSCAL POA&M artifact will be empty.",
                "Go to POA&M to create items from assessment findings, or import scan results from Assessments."));
        }

        // ─── 7. Cross-Artifact Control ID Matching ──────────────────────────
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
        checks.Add(Check("cross-reference", "POA&M control references", orphanedPoam.Count == 0 ? "Passed" : "FollowUp", false,
            orphanedPoam.Count == 0 ? "All recorded POA&M control references match SSP implementations."
                : $"Unmatched control references: {string.Join(", ", orphanedPoam)}.", "Reconcile POA&M control references.", "Isso"));
        foreach (var controlId in orphanedPoam)
        {
            findings.Add(Warning("cross-reference", "poam",
                $"POA&M references control '{controlId}' which is not in the SSP control implementations.",
                $"Go to POA&M and verify that '{controlId}' is the correct control ID, or add it to the SSP via Gap Analysis."));
        }

        // ─── 8. OSCAL Schema Validation ─────────────────────────────────────
        var models = new[] { "ssp", "poam", "assessment-results", "assessment-plan" };
        foreach (var model in models)
        {
            try
            {
                var schemaResult = await _schemaValidator.ValidateForSystemAsync(systemId, model, cancellationToken);
                checks.Add(Check($"schema-{model}", $"OSCAL {model} schema", schemaResult.IsValid ? "Passed" : "Blocking", true,
                    schemaResult.IsValid ? $"The evaluated {model} document conforms to OSCAL {schemaResult.SchemaVersion}."
                        : string.Join("; ", schemaResult.Violations.Select(x => x.Message)),
                    "Correct the source data and revalidate the emitted document.", null, "schema"));
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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Schema validation for {Model} could not be completed", model);
                checks.Add(Check($"schema-{model}", $"OSCAL {model} schema", "Unavailable", true,
                    "Schema evaluation could not be completed.", "Restore the source document/schema service and retry.", null, "schema"));
                findings.Add(Error("schema", model,
                    $"OSCAL {model} schema validation could not be completed: {ex.Message}",
                    $"Ensure the {model} data exists. Check the relevant page (Narratives for SSP, POA&M, or Assessments) and verify data can be exported."));
            }
        }

        // ─── 9. Evidence Coverage (Warnings) ────────────────────────────────
        try
        {
            var evidenceSummary = await _evidenceService.GetSummaryAsync(systemId, cancellationToken);
            checks.Add(Check("evidence", "Recorded evidence coverage", evidenceSummary.CoveragePercentage >= 100 ? "Passed" : "FollowUp",
                false, $"Canonical evidence coverage is {evidenceSummary.CoveragePercentage:F0}%; coverage alone does not establish implementation effectiveness.",
                "Review missing supporting evidence.", "Isso"));
            if (evidenceSummary.CoveragePercentage < 100)
            {
                findings.Add(Warning("evidence", null,
                    $"Evidence coverage is {evidenceSummary.CoveragePercentage:F0}% — some controls lack supporting evidence.",
                    "Go to Evidence to upload artifacts for controls with missing coverage."));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Evidence coverage check could not be completed for system {SystemId}", systemId);
            checks.Add(Check("evidence", "Recorded evidence coverage", "Unavailable", true,
                "Evidence coverage could not be verified.", "Restore evidence availability and retry.", "Isso"));
            findings.Add(Error("evidence", null, "Evidence coverage could not be verified.",
                "Restore evidence availability and retry validation before generating a final package."));
        }

        var pta = await db.PrivacyThresholdAnalyses.AsNoTracking().SingleOrDefaultAsync(x => x.RegisteredSystemId == systemId, cancellationToken);
        var privacyUndetermined = pta == null || pta.Determination == PtaDetermination.PendingConfirmation;
        checks.Add(Check("privacy-pta", "Privacy applicability determination", privacyUndetermined ? "Unavailable" : "Passed", true,
            privacyUndetermined ? "No final PTA determination establishes whether a PIA is required." : $"Recorded PTA determination: {pta!.Determination}.",
            "Complete the canonical Privacy Threshold Analysis.", "Issm", "privacy"));
        var pia = await db.PrivacyImpactAssessments.AsNoTracking().SingleOrDefaultAsync(x => x.RegisteredSystemId == systemId, cancellationToken);
        var piaRequired = pta?.Determination == PtaDetermination.PiaRequired;
        checks.Add(Check("privacy-pia", "Conditional Privacy Impact Assessment",
            privacyUndetermined ? "Unavailable" : !piaRequired ? "NotApplicable" : pia?.Status == PiaStatus.Approved ? "Passed" : "Blocking",
            piaRequired || privacyUndetermined, privacyUndetermined ? "PIA applicability is undetermined until the PTA is confirmed."
                : !piaRequired ? "The recorded PTA does not require a PIA." : $"Required PIA status: {pia?.Status.ToString() ?? "Not recorded"}.",
            "Use the privacy assessment and independent review workflow.", "Issm", "privacy"));
        foreach (var check in checks.Where(x => x.Category == "privacy" && (x.Outcome == "Blocking" || x.Required && x.Outcome == "Unavailable")))
            findings.Add(Error("privacy", null, check.Why, check.NextSteps[0]));

        var agreements = await scope.ServiceProvider.GetRequiredService<IInterconnectionService>().ValidateAgreementsAsync(systemId, cancellationToken);
        checks.Add(Check("interconnection-agreements", "Active interconnection agreements",
            system.HasNoExternalInterconnections || agreements.TotalInterconnections == 0 ? "NotApplicable"
                : !agreements.IsFullyCompliant ? "Blocking" : agreements.ExpiringWithin90DaysCount > 0 ? "FollowUp" : "Passed",
            agreements.TotalInterconnections > 0,
            system.HasNoExternalInterconnections ? "The system has a recorded no-external-interconnections certification."
                : agreements.TotalInterconnections == 0 ? "No active interconnections are recorded; this is not a certification of absence."
                : $"Active: {agreements.TotalInterconnections}; missing agreements: {agreements.MissingAgreementCount}; expired: {agreements.ExpiredAgreementCount}; expiring soon: {agreements.ExpiringWithin90DaysCount}.",
            "Review the retained interconnection agreements; editing details never approves an agreement.", "Issm", "interconnection"));
        if (!agreements.IsFullyCompliant)
            findings.Add(Error("interconnection", null, checks[^1].Why, checks[^1].NextSteps[0]));

        var linkedEvidence = await db.EvidenceArtifacts.AsNoTracking().Where(x => x.RegisteredSystemId == systemId
            && !x.IsDeleted && x.ControlImplementationId != null
            && db.ControlImplementations.Any(c => c.Id == x.ControlImplementationId && c.RegisteredSystemId == systemId)).ToListAsync(cancellationToken);
        foreach (var evidence in linkedEvidence)
        {
            var actual = await PackageReadinessSources.EvidenceHashAsync(_evidenceService, evidence.Id, cancellationToken);
            var outcome = actual == null ? "Unavailable" : actual.Equals(evidence.ContentHash, StringComparison.OrdinalIgnoreCase) ? "Passed" : "Blocking";
            var check = Check($"evidence-bytes-{evidence.Id}", $"Evidence integrity: {evidence.FileName}", outcome, true,
                actual == null ? "Required evidence bytes are unavailable." : outcome == "Passed" ? "Evidence bytes match their recorded hash."
                    : "Evidence bytes do not match the recorded hash.", "Restore the retained evidence or upload a reviewed replacement.", "Isso", "evidence");
            checks.Add(check);
            if (outcome != "Passed") findings.Add(Error("evidence", null, check.Why, check.NextSteps[0]));
        }
        if (!await db.CapabilitySubscriptions.AnyAsync(x => x.RegisteredSystemId == systemId && x.IsActive, cancellationToken))
            checks.Add(Check("responsibility", "Inherited responsibility review", "NotApplicable", false,
                "No active provider subscriptions require responsibility review.", "Review provider applicability.", "Issm"));
        else
        {
            var responsibilities = await PackageReadinessSources.PreviewResponsibilitiesAsync(db, scope.ServiceProvider, systemId, cancellationToken);
            var pending = responsibilities.Items.Count(x => x.State != "Ready" && x.State is not ("Inactive" or "OutsideBaseline"));
            var outcome = responsibilities.BaselineId == null ? "Unavailable" : responsibilities.Items.Count == 0 ? "NotApplicable"
                : pending == 0 && responsibilities.PendingImpacts.Count == 0 ? "Passed" : "FollowUp";
            checks.Add(Check("responsibility", "Inherited responsibility review", outcome,
                outcome == "Unavailable", $"Canonical unresolved allocations: {pending}; pending source impacts: {responsibilities.PendingImpacts.Count}. Catalog/CRM availability is not confirmation.",
                "Review the actual baseline-bound responsibility allocations and source impacts.", "Issm"));
            if (checks[^1].Outcome == "FollowUp") findings.Add(Warning("responsibility", null, checks[^1].Why, checks[^1].NextSteps[0]));
            if (checks[^1].Outcome == "Unavailable") findings.Add(Error("responsibility", null, checks[^1].Why, checks[^1].NextSteps[0]));
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
            Findings = findings,
            Checks = checks
        };
    }

    internal static PackageReadinessCheck Check(string id, string title, string outcome, bool required,
        string why, string next, string? role, string? category = null) =>
        new(id, id, title, outcome, category ?? id, required,
            outcome == "NotApplicable" ? "NotApplicable" : outcome == "Unavailable" ? "Undetermined" : "Applicable",
            why, outcome is "Blocking" or "Unavailable" ? why : null, [],
            outcome is "Passed" or "NotApplicable" ? [] : [next], role, null,
            new(false, false, null, null, "Current source access must be resolved for this caller."));

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
