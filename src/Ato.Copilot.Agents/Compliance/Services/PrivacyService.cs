using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Implements Privacy Threshold Analysis (PTA) and Privacy Impact Assessment (PIA) lifecycle.
/// E-Government Act §208 / OMB M-03-22 / NIST SP 800-122 compliance.
/// </summary>
public class PrivacyService : IPrivacyService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PrivacyService> _logger;

    /// <summary>
    /// SP 800-60 Vol II prefixes that definitively contain PII (R1 decision).
    /// D.8.x = Personnel, D.17.x = Health/Medical, D.28.x = Financial.
    /// </summary>
    private static readonly string[] KnownPiiPrefixes = ["D.8.", "D.17.", "D.28."];

    /// <summary>
    /// 8 OMB M-03-22 PIA sections per research.md R2.
    /// </summary>
    private static readonly PiaSection[] OmbSectionTemplates =
    [
        new() { SectionId = "1.1", Title = "System Information", Question = "Describe the system, including its name, purpose, and the IT system of records (SOR). What information about individuals is collected?" },
        new() { SectionId = "2.1", Title = "Information Collected", Question = "What information is to be collected? Describe the types of PII collected: SSNs, medical records, financial records, biometric data, etc." },
        new() { SectionId = "2.2", Title = "Purpose of Collection", Question = "Why is this information being collected? What is the intended use? How is the information to be used?" },
        new() { SectionId = "3.1", Title = "Information Sharing", Question = "With whom will the information be shared? Identify any external organizations or parties." },
        new() { SectionId = "4.1", Title = "Notice and Consent", Question = "How is notice provided to individuals regarding the collection of PII? Is consent obtained and how?" },
        new() { SectionId = "5.1", Title = "Individual Access", Question = "How can individuals access their own data? What procedures are in place for individuals to request corrections?" },
        new() { SectionId = "6.1", Title = "Security Safeguards", Question = "What security controls and safeguards protect the PII? Include administrative, technical, and physical safeguards." },
        new() { SectionId = "7.1", Title = "Retention and Disposal", Question = "How long is the information retained? What is the disposal method after the retention period?" }
    ];

    public PrivacyService(
        IServiceScopeFactory scopeFactory,
        ILogger<PrivacyService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PtaResult> CreatePtaAsync(
        string systemId,
        string analyzedBy,
        bool manualMode = false,
        bool collectsPii = false,
        bool maintainsPii = false,
        bool disseminatesPii = false,
        List<string>? piiCategories = null,
        int? estimatedRecordCount = null,
        string? exemptionRationale = null,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var system = await context.RegisteredSystems
            .FirstOrDefaultAsync(s => s.Id == systemId, cancellationToken)
            ?? throw new InvalidOperationException($"System '{systemId}' not found.");

        // Remove existing PTA (replace, not duplicate)
        var existing = await context.PrivacyThresholdAnalyses
            .FirstOrDefaultAsync(p => p.RegisteredSystemId == systemId, cancellationToken);
        if (existing != null)
        {
            context.PrivacyThresholdAnalyses.Remove(existing);
            await context.SaveChangesAsync(cancellationToken);
        }

        var pta = new PrivacyThresholdAnalysis
        {
            RegisteredSystemId = systemId,
            AnalyzedBy = analyzedBy,
            AnalyzedAt = DateTime.UtcNow,
            EstimatedRecordCount = estimatedRecordCount,
            ExemptionRationale = exemptionRationale
        };

        if (!string.IsNullOrWhiteSpace(exemptionRationale))
        {
            // Exempt path — rationale provided
            pta.Determination = PtaDetermination.Exempt;
            pta.Rationale = $"Exempt: {exemptionRationale}";
        }
        else if (manualMode)
        {
            // T016: Manual mode — use explicit flags
            pta.CollectsPii = collectsPii;
            pta.MaintainsPii = maintainsPii;
            pta.DisseminatesPii = disseminatesPii;
            pta.PiiCategories = piiCategories ?? [];

            if (collectsPii || maintainsPii || disseminatesPii ||
                (estimatedRecordCount.HasValue && estimatedRecordCount.Value >= 10))
            {
                pta.Determination = PtaDetermination.PiaRequired;
                pta.Rationale = "Manual assessment: system processes PII.";
            }
            else
            {
                pta.Determination = PtaDetermination.PiaNotRequired;
                pta.Rationale = "Manual assessment: system does not process PII.";
            }
        }
        else
        {
            // T015: Auto-detection from SecurityCategorization info types
            var categorization = await context.SecurityCategorizations
                .Include(sc => sc.InformationTypes)
                .FirstOrDefaultAsync(sc => sc.RegisteredSystemId == systemId, cancellationToken);

            var infoTypes = categorization?.InformationTypes ?? [];
            var piiInfoTypeIds = new List<string>();
            var detectedCategories = new List<string>();
            bool hasAmbiguous = false;

            foreach (var it in infoTypes)
            {
                if (KnownPiiPrefixes.Any(prefix => it.Sp80060Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                {
                    piiInfoTypeIds.Add(it.Sp80060Id);
                    detectedCategories.Add(it.Name);
                }
                else
                {
                    // Check if info type could be ambiguous (non-standard prefix)
                    // Known non-PII: D.1-D.6 (defense), D.9-D.16, D.18-D.27 (most govt ops)
                    // Ambiguous: anything not clearly PII or clearly non-PII
                    var prefix = GetPrefix(it.Sp80060Id);
                    if (!IsKnownNonPii(prefix))
                        hasAmbiguous = true;
                }
            }

            pta.PiiSourceInfoTypes = piiInfoTypeIds;
            pta.PiiCategories = detectedCategories;

            if (piiInfoTypeIds.Count > 0)
            {
                pta.CollectsPii = true;
                pta.MaintainsPii = true;
                pta.Determination = PtaDetermination.PiaRequired;
                pta.Rationale = $"Auto-detected PII in {piiInfoTypeIds.Count} information type(s): {string.Join(", ", piiInfoTypeIds)}.";
            }
            else if (hasAmbiguous)
            {
                pta.Determination = PtaDetermination.PendingConfirmation;
                pta.Rationale = "Ambiguous information types detected — human confirmation required.";
            }
            else
            {
                pta.Determination = PtaDetermination.PiaNotRequired;
                pta.Rationale = "No PII-containing information types detected.";
            }
        }

        context.PrivacyThresholdAnalyses.Add(pta);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("PTA created for system {SystemId}: {Determination}", systemId, pta.Determination);

        return new PtaResult(
            pta.Id,
            pta.Determination,
            pta.CollectsPii,
            pta.MaintainsPii,
            pta.DisseminatesPii,
            pta.PiiCategories,
            pta.PiiSourceInfoTypes,
            pta.Rationale ?? string.Empty);
    }

    /// <inheritdoc />
    public async Task<PiaResult> GeneratePiaAsync(
        string systemId,
        string createdBy,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var system = await context.RegisteredSystems
            .FirstOrDefaultAsync(s => s.Id == systemId, cancellationToken)
            ?? throw new InvalidOperationException($"System '{systemId}' not found.");

        var pta = await context.PrivacyThresholdAnalyses
            .FirstOrDefaultAsync(p => p.RegisteredSystemId == systemId, cancellationToken)
            ?? throw new InvalidOperationException("PTA must be completed before generating a PIA.");

        if (pta.Determination != PtaDetermination.PiaRequired)
            throw new InvalidOperationException(
                $"PIA generation requires PtaDetermination = PiaRequired (current: {pta.Determination}).");

        // Check for existing PIA — increment version on resubmission
        var existingPia = await context.PrivacyImpactAssessments
            .FirstOrDefaultAsync(p => p.RegisteredSystemId == systemId, cancellationToken);

        int version = 1;
        if (existingPia != null)
        {
            version = existingPia.Version + 1;
            context.PrivacyImpactAssessments.Remove(existingPia);
            await context.SaveChangesAsync(cancellationToken);
        }

        // Generate 8 OMB M-03-22 sections
        var sections = new List<PiaSection>();
        int prePopulated = 0;
        foreach (var template in OmbSectionTemplates)
        {
            var section = new PiaSection
            {
                SectionId = template.SectionId,
                Title = template.Title,
                Question = template.Question
            };

            // Pre-populate where system data is available
            switch (template.SectionId)
            {
                case "1.1":
                    section.Answer = system.Description ?? $"System: {system.Name}";
                    section.IsPrePopulated = true;
                    section.SourceField = "RegisteredSystem.Description";
                    prePopulated++;
                    break;
                case "2.1":
                    if (pta.PiiCategories.Count > 0)
                    {
                        section.Answer = $"PII categories: {string.Join(", ", pta.PiiCategories)}. " +
                                         $"Source information types: {string.Join(", ", pta.PiiSourceInfoTypes)}.";
                        section.IsPrePopulated = true;
                        section.SourceField = "PrivacyThresholdAnalysis.PiiCategories";
                        prePopulated++;
                    }
                    break;
            }

            sections.Add(section);
        }

        var pia = new PrivacyImpactAssessment
        {
            RegisteredSystemId = systemId,
            PtaId = pta.Id,
            Status = PiaStatus.Draft,
            Version = version,
            SystemDescription = system.Description,
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow,
            Sections = sections
        };

        context.PrivacyImpactAssessments.Add(pia);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("PIA v{Version} generated for system {SystemId}", version, systemId);

        return new PiaResult(
            pia.Id,
            pia.Status,
            pia.Version,
            pia.NarrativeDocument ?? string.Empty,
            sections,
            prePopulated,
            sections.Count);
    }

    /// <inheritdoc />
    public async Task<PiaReviewResult> ReviewPiaAsync(
        string systemId,
        PiaReviewDecision decision,
        string reviewerComments,
        string reviewedBy,
        List<string>? deficiencies = null,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var pia = await context.PrivacyImpactAssessments
            .FirstOrDefaultAsync(p => p.RegisteredSystemId == systemId, cancellationToken)
            ?? throw new InvalidOperationException($"No PIA found for system '{systemId}'.");

        if (pia.Status != PiaStatus.Draft && pia.Status != PiaStatus.UnderReview)
            throw new InvalidOperationException(
                $"PIA can only be reviewed when in Draft or UnderReview status (current: {pia.Status}).");

        pia.ReviewerComments = reviewerComments;
        pia.ModifiedAt = DateTime.UtcNow;

        DateTime? expirationDate = null;

        if (decision == PiaReviewDecision.Approved)
        {
            pia.Status = PiaStatus.Approved;
            pia.ApprovedBy = reviewedBy;
            pia.ApprovedAt = DateTime.UtcNow;
            pia.ExpirationDate = DateTime.UtcNow.AddYears(1);
            pia.ReviewDeficiencies = [];
            expirationDate = pia.ExpirationDate;
        }
        else
        {
            pia.Status = PiaStatus.Draft;
            pia.ReviewDeficiencies = deficiencies ?? [];
        }

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("PIA reviewed for system {SystemId}: {Decision}", systemId, decision);

        return new PiaReviewResult(
            pia.Id,
            decision,
            pia.Status,
            reviewerComments,
            pia.ReviewDeficiencies,
            expirationDate);
    }

    /// <inheritdoc />
    public async Task InvalidatePtaAsync(
        string systemId,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var pta = await context.PrivacyThresholdAnalyses
            .FirstOrDefaultAsync(p => p.RegisteredSystemId == systemId, cancellationToken);

        if (pta == null)
        {
            _logger.LogInformation("No PTA to invalidate for system {SystemId}", systemId);
            return;
        }

        // Remove any PIA linked to this PTA first (PtaId is a required FK)
        var pia = await context.PrivacyImpactAssessments
            .FirstOrDefaultAsync(p => p.PtaId == pta.Id, cancellationToken);

        if (pia != null)
        {
            // PIA requires a non-null PtaId FK, so it must be removed before PTA deletion.
            context.PrivacyImpactAssessments.Remove(pia);
        }

        // Delete PTA
        context.PrivacyThresholdAnalyses.Remove(pta);

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("PTA invalidated for system {SystemId}", systemId);
    }

    /// <inheritdoc />
    public async Task<PrivacyComplianceResult> GetPrivacyComplianceAsync(
        string systemId,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var system = await context.RegisteredSystems
            .FirstOrDefaultAsync(s => s.Id == systemId, cancellationToken)
            ?? throw new InvalidOperationException($"System '{systemId}' not found.");

        var pta = await context.PrivacyThresholdAnalyses
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.RegisteredSystemId == systemId, cancellationToken);

        var pia = await context.PrivacyImpactAssessments
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.RegisteredSystemId == systemId, cancellationToken);

        // Interconnection counts
        var interconnections = await context.SystemInterconnections
            .AsNoTracking()
            .Where(i => i.RegisteredSystemId == systemId)
            .ToListAsync(cancellationToken);

        var activeInterconnections = interconnections.Count(i => i.Status == InterconnectionStatus.Active);
        var withAgreements = interconnections.Count(i =>
            context.InterconnectionAgreements.Any(a =>
                a.SystemInterconnectionId == i.Id && a.Status == AgreementStatus.Signed));

        var now = DateTime.UtcNow;
        var agreements = await context.InterconnectionAgreements
            .AsNoTracking()
            .Where(a => interconnections.Select(i => i.Id).Contains(a.SystemInterconnectionId))
            .ToListAsync(cancellationToken);

        var expiredAgreements = agreements.Count(a =>
            a.ExpirationDate.HasValue && a.ExpirationDate.Value < now);
        var expiringWithin90Days = agreements.Count(a =>
            a.ExpirationDate.HasValue && a.ExpirationDate.Value >= now &&
            a.ExpirationDate.Value <= now.AddDays(90));

        // Calculate gate satisfaction
        bool privacyGateSatisfied = pta?.Determination switch
        {
            PtaDetermination.PiaRequired => pia?.Status == PiaStatus.Approved,
            PtaDetermination.PiaNotRequired => true,
            PtaDetermination.Exempt => true,
            _ => false
        };

        bool interconnectionGateSatisfied = system.HasNoExternalInterconnections ||
            (interconnections.Count > 0 && expiredAgreements == 0);

        string overallStatus;
        if (pta == null)
            overallStatus = "NotStarted";
        else if (privacyGateSatisfied && interconnectionGateSatisfied)
            overallStatus = "Compliant";
        else
            overallStatus = "ActionRequired";

        _logger.LogInformation(
            "Privacy compliance for system '{SystemId}': status={Status}, privacyGate={PrivacyGate}, interconnectionGate={IcGate}",
            systemId, overallStatus, privacyGateSatisfied, interconnectionGateSatisfied);

        return new PrivacyComplianceResult(
            systemId,
            system.Name,
            pta?.Determination,
            pia?.Status,
            privacyGateSatisfied,
            activeInterconnections,
            withAgreements,
            expiredAgreements,
            expiringWithin90Days,
            interconnectionGateSatisfied,
            system.HasNoExternalInterconnections,
            overallStatus);
    }

    /// <summary>
    /// Extracts the prefix portion of an SP 800-60 identifier (e.g., "D.8." from "D.8.1").
    /// </summary>
    private static string GetPrefix(string sp80060Id)
    {
        var parts = sp80060Id.Split('.');
        return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}." : sp80060Id;
    }

    /// <summary>
    /// Returns true if the prefix is definitively non-PII (government operations, defense, etc.).
    /// </summary>
    private static bool IsKnownNonPii(string prefix)
    {
        // Known non-PII prefixes: D.1-D.6 (defense/security ops), D.9-D.16, D.18-D.27, D.29+
        string[] nonPiiPrefixes =
        [
            "D.1.", "D.2.", "D.3.", "D.4.", "D.5.", "D.6.",
            "D.9.", "D.10.", "D.11.", "D.12.", "D.13.", "D.14.", "D.15.", "D.16.",
            "D.18.", "D.19.", "D.20.", "D.21.", "D.22.", "D.23.", "D.24.", "D.25.",
            "D.26.", "D.27.", "D.29.", "D.30.", "D.31.", "D.32.", "D.33.", "D.34.",
            "D.35.", "D.36.", "D.37.", "D.38.", "D.39.", "D.40.",
            "C.1.", "C.2.", "C.3.", "C.4.", "C.5.", "C.6."
        ];
        return nonPiiPrefixes.Any(p => prefix.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }
}
