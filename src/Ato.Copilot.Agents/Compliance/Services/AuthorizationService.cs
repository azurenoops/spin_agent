using System.Text;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Implements authorization decision workflows: issuing ATO/ATOwC/IATT/DATO,
/// risk acceptance with auto-expire, POA&amp;M CRUD, RAR generation,
/// and authorization package bundling (SSP + SAR + RAR + POA&amp;M + CRM + ATO Letter).
/// </summary>
public class AuthorizationService : IAuthorizationService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuthorizationService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>Mapping of control family prefixes to display names.</summary>
    private static readonly Dictionary<string, string> FamilyNames = new()
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
        ["SR"] = "Supply Chain Risk Management"
    };

    public AuthorizationService(
        IServiceScopeFactory scopeFactory,
        ILogger<AuthorizationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AuthorizationDecision> IssueAuthorizationAsync(
        string systemId,
        string decisionType,
        DateTime? expirationDate,
        string residualRiskLevel,
        string? termsAndConditions = null,
        string? residualRiskJustification = null,
        List<RiskAcceptanceInput>? riskAcceptances = null,
        string issuedBy = "mcp-user",
        string issuedByName = "MCP User",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId, nameof(systemId));
        ArgumentException.ThrowIfNullOrWhiteSpace(decisionType, nameof(decisionType));
        ArgumentException.ThrowIfNullOrWhiteSpace(residualRiskLevel, nameof(residualRiskLevel));

        // Parse enums
        if (!Enum.TryParse<AuthorizationDecisionType>(decisionType, true, out var parsedDecisionType))
            throw new InvalidOperationException($"Invalid decision_type '{decisionType}'. Must be ATO, AtoWithConditions, IATT, or DATO.");

        if (!Enum.TryParse<ComplianceRiskLevel>(residualRiskLevel, true, out var parsedRiskLevel))
            throw new InvalidOperationException($"Invalid residual_risk_level '{residualRiskLevel}'. Must be Low, Medium, High, or Critical.");

        // Expiration is required for non-DATO decisions
        if (parsedDecisionType != AuthorizationDecisionType.Dato && expirationDate == null)
            throw new InvalidOperationException($"expiration_date is required for {decisionType} decisions.");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        // Verify system exists and load privacy/interconnection data
        var system = await db.RegisteredSystems
            .Include(s => s.PrivacyThresholdAnalysis)
            .Include(s => s.PrivacyImpactAssessment)
            .Include(s => s.SystemInterconnections)
                .ThenInclude(ic => ic.Agreements)
            .FirstOrDefaultAsync(s => s.Id == systemId, cancellationToken)
            ?? throw new InvalidOperationException($"System '{systemId}' not found.");

        // ─── Privacy & Interconnection pre-checks (Feature 021) ────────
        var warnings = new List<string>();

        // Privacy: PTA must exist and PIA must be approved if required
        if (system.PrivacyThresholdAnalysis is { } pta)
        {
            if (pta.Determination == PtaDetermination.PiaRequired
                && system.PrivacyImpactAssessment?.Status != PiaStatus.Approved)
            {
                warnings.Add("PIA is required but not yet approved.");
            }
        }
        else
        {
            warnings.Add("No Privacy Threshold Analysis (PTA) on file.");
        }

        // Interconnections: all active interconnections must have a signed, non-expired agreement
        if (!system.HasNoExternalInterconnections)
        {
            var activeInterconnections = system.SystemInterconnections
                .Where(ic => ic.Status == InterconnectionStatus.Active)
                .ToList();

            foreach (var ic in activeInterconnections)
            {
                var hasSigned = ic.Agreements.Any(a =>
                    a.Status == AgreementStatus.Signed
                    && (a.ExpirationDate == null || a.ExpirationDate > DateTime.UtcNow));

                if (!hasSigned)
                {
                    warnings.Add($"Interconnection with '{ic.TargetSystemName}' lacks a signed, non-expired agreement.");
                }
            }
        }

        if (warnings.Count > 0)
        {
            _logger.LogWarning(
                "Authorization pre-check warnings for system {SystemId}: {Warnings}",
                systemId, string.Join("; ", warnings));
        }

        // Calculate compliance score from latest assessment
        double complianceScore = 0;
        var catBreakdown = new { catI = 0, catII = 0, catIII = 0 };

        var latestEffectiveness = await db.ControlEffectivenessRecords
            .Where(e => e.RegisteredSystemId == systemId)
            .ToListAsync(cancellationToken);

        if (latestEffectiveness.Count > 0)
        {
            var satisfied = latestEffectiveness.Count(e => e.Determination == EffectivenessDetermination.Satisfied);
            var total = latestEffectiveness.Count;
            complianceScore = total > 0 ? Math.Round((double)satisfied / total * 100, 2) : 0;
            catBreakdown = new
            {
                catI = latestEffectiveness.Count(e => e.CatSeverity == Core.Models.Compliance.CatSeverity.CatI),
                catII = latestEffectiveness.Count(e => e.CatSeverity == Core.Models.Compliance.CatSeverity.CatII),
                catIII = latestEffectiveness.Count(e => e.CatSeverity == Core.Models.Compliance.CatSeverity.CatIII)
            };
        }

        // Deactivate previous authorization decisions for this system
        var previousDecisions = await db.AuthorizationDecisions
            .Where(d => d.RegisteredSystemId == systemId && d.IsActive)
            .ToListAsync(cancellationToken);

        var decision = new AuthorizationDecision
        {
            RegisteredSystemId = systemId,
            DecisionType = parsedDecisionType,
            DecisionDate = DateTime.UtcNow,
            ExpirationDate = expirationDate,
            TermsAndConditions = termsAndConditions,
            ResidualRiskLevel = parsedRiskLevel,
            ResidualRiskJustification = residualRiskJustification,
            ComplianceScoreAtDecision = complianceScore,
            FindingsAtDecision = JsonSerializer.Serialize(catBreakdown, JsonOpts),
            IssuedBy = issuedBy,
            IssuedByName = issuedByName,
            IsActive = true
        };

        foreach (var prev in previousDecisions)
        {
            prev.IsActive = false;
            prev.SupersededById = decision.Id;
        }

        db.AuthorizationDecisions.Add(decision);

        // Process inline risk acceptances
        if (riskAcceptances != null)
        {
            foreach (var ra in riskAcceptances)
            {
                if (!Enum.TryParse<Core.Models.Compliance.CatSeverity>(ra.CatSeverity, true, out var raCat))
                    throw new InvalidOperationException($"Invalid cat_severity '{ra.CatSeverity}' in risk acceptance.");

                var acceptance = new RiskAcceptance
                {
                    AuthorizationDecisionId = decision.Id,
                    FindingId = ra.FindingId,
                    ControlId = ra.ControlId,
                    CatSeverity = raCat,
                    Justification = ra.Justification,
                    CompensatingControl = ra.CompensatingControl,
                    ExpirationDate = ra.ExpirationDate,
                    AcceptedBy = issuedBy,
                    AcceptedAt = DateTime.UtcNow,
                    IsActive = true
                };
                decision.RiskAcceptances.Add(acceptance);
                db.RiskAcceptances.Add(acceptance);
            }
        }

        // Advance system to Monitor step if not DATO
        if (parsedDecisionType != AuthorizationDecisionType.Dato)
        {
            system.CurrentRmfStep = RmfPhase.Monitor;
            system.RmfStepUpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Authorization decision {DecisionType} issued for system {SystemId} by {IssuedBy}",
            decisionType, systemId, issuedBy);

        return decision;
    }

    /// <inheritdoc />
    public async Task<RiskAcceptance> AcceptRiskAsync(
        string systemId,
        string findingId,
        string controlId,
        string catSeverity,
        string justification,
        DateTime expirationDate,
        string? compensatingControl = null,
        string acceptedBy = "mcp-user",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId, nameof(systemId));
        ArgumentException.ThrowIfNullOrWhiteSpace(findingId, nameof(findingId));
        ArgumentException.ThrowIfNullOrWhiteSpace(controlId, nameof(controlId));
        ArgumentException.ThrowIfNullOrWhiteSpace(catSeverity, nameof(catSeverity));
        ArgumentException.ThrowIfNullOrWhiteSpace(justification, nameof(justification));

        if (!Enum.TryParse<Core.Models.Compliance.CatSeverity>(catSeverity, true, out var parsedCat))
            throw new InvalidOperationException($"Invalid cat_severity '{catSeverity}'. Must be CatI, CatII, or CatIII.");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        // Find active authorization for the system
        var activeAuth = await db.AuthorizationDecisions
            .Where(d => d.RegisteredSystemId == systemId && d.IsActive)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"No active authorization decision for system '{systemId}'. Issue an authorization first.");

        // Verify finding exists
        var finding = await db.Findings.FindAsync([findingId], cancellationToken)
            ?? throw new InvalidOperationException($"Finding '{findingId}' not found.");

        var acceptance = new RiskAcceptance
        {
            AuthorizationDecisionId = activeAuth.Id,
            FindingId = findingId,
            ControlId = controlId,
            CatSeverity = parsedCat,
            Justification = justification,
            CompensatingControl = compensatingControl,
            ExpirationDate = expirationDate,
            AcceptedBy = acceptedBy,
            AcceptedAt = DateTime.UtcNow,
            IsActive = true
        };

        db.RiskAcceptances.Add(acceptance);
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Risk accepted for finding {FindingId} control {ControlId} by {AcceptedBy}, expires {Expiration}",
            findingId, controlId, acceptedBy, expirationDate);

        return acceptance;
    }

    /// <inheritdoc />
    public async Task<RiskRegister> GetRiskRegisterAsync(
        string systemId,
        string statusFilter = "active",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId, nameof(systemId));

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var query = db.RiskAcceptances
            .Include(r => r.Finding)
            .Include(r => r.AuthorizationDecision)
            .Where(r => r.AuthorizationDecision!.RegisteredSystemId == systemId);

        var allAcceptances = await query.ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;

        // Auto-expire any past-due active acceptances
        foreach (var ra in allAcceptances.Where(r => r.IsActive && r.ExpirationDate < now))
        {
            ra.IsActive = false;
        }
        await db.SaveChangesAsync(cancellationToken);

        // Build details
        var details = allAcceptances.Select(r =>
        {
            string status;
            if (r.RevokedAt != null) status = "revoked";
            else if (!r.IsActive || r.ExpirationDate < now) status = "expired";
            else status = "active";

            return new RiskAcceptanceDetail
            {
                Id = r.Id,
                ControlId = r.ControlId,
                CatSeverity = r.CatSeverity.ToString(),
                Justification = r.Justification,
                CompensatingControl = r.CompensatingControl,
                ExpirationDate = r.ExpirationDate,
                AcceptedAt = r.AcceptedAt,
                AcceptedBy = r.AcceptedBy,
                Status = status,
                FindingTitle = r.Finding?.Title
            };
        }).ToList();

        // Apply filter
        var filtered = statusFilter.ToLowerInvariant() switch
        {
            "active" => details.Where(d => d.Status == "active").ToList(),
            "expired" => details.Where(d => d.Status == "expired").ToList(),
            "revoked" => details.Where(d => d.Status == "revoked").ToList(),
            "all" => details,
            _ => details.Where(d => d.Status == "active").ToList()
        };

        return new RiskRegister
        {
            SystemId = systemId,
            TotalAcceptances = filtered.Count,
            ActiveCount = details.Count(d => d.Status == "active"),
            ExpiredCount = details.Count(d => d.Status == "expired"),
            RevokedCount = details.Count(d => d.Status == "revoked"),
            Acceptances = filtered
        };
    }

    /// <inheritdoc />
    public async Task<PoamItem> CreatePoamAsync(
        string systemId,
        string weakness,
        string controlId,
        string catSeverity,
        string poc,
        DateTime scheduledCompletion,
        string? findingId = null,
        string? resourcesRequired = null,
        List<MilestoneInput>? milestones = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId, nameof(systemId));
        ArgumentException.ThrowIfNullOrWhiteSpace(weakness, nameof(weakness));
        ArgumentException.ThrowIfNullOrWhiteSpace(controlId, nameof(controlId));
        ArgumentException.ThrowIfNullOrWhiteSpace(catSeverity, nameof(catSeverity));
        ArgumentException.ThrowIfNullOrWhiteSpace(poc, nameof(poc));

        if (!Enum.TryParse<Core.Models.Compliance.CatSeverity>(catSeverity, true, out var parsedCat))
            throw new InvalidOperationException($"Invalid cat_severity '{catSeverity}'. Must be CatI, CatII, or CatIII.");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        // Verify system exists
        _ = await db.RegisteredSystems.FindAsync([systemId], cancellationToken)
            ?? throw new InvalidOperationException($"System '{systemId}' not found.");

        var poam = new PoamItem
        {
            RegisteredSystemId = systemId,
            FindingId = findingId,
            Weakness = weakness,
            WeaknessSource = findingId != null ? "SCA Assessment" : "Manual",
            SecurityControlNumber = controlId,
            CatSeverity = parsedCat,
            PointOfContact = poc,
            ResourcesRequired = resourcesRequired,
            ScheduledCompletionDate = scheduledCompletion,
            Status = PoamStatus.Ongoing,
            CreatedAt = DateTime.UtcNow
        };

        // Add milestones
        if (milestones != null)
        {
            int seq = 1;
            foreach (var m in milestones)
            {
                poam.Milestones.Add(new PoamMilestone
                {
                    PoamItemId = poam.Id,
                    Description = m.Description,
                    TargetDate = m.TargetDate,
                    Sequence = seq++
                });
            }
        }

        db.PoamItems.Add(poam);
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "POA&M item created for system {SystemId}, control {ControlId}, severity {CatSeverity}",
            systemId, controlId, catSeverity);

        return poam;
    }

    /// <inheritdoc />
    public async Task<List<PoamItem>> ListPoamAsync(
        string systemId,
        string? statusFilter = null,
        string? severityFilter = null,
        bool overdueOnly = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId, nameof(systemId));

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var query = db.PoamItems
            .Include(p => p.Milestones)
            .Where(p => p.RegisteredSystemId == systemId);

        // Status filter
        if (!string.IsNullOrWhiteSpace(statusFilter) && Enum.TryParse<PoamStatus>(statusFilter, true, out var parsedStatus))
        {
            query = query.Where(p => p.Status == parsedStatus);
        }

        // Severity filter
        if (!string.IsNullOrWhiteSpace(severityFilter) && Enum.TryParse<Core.Models.Compliance.CatSeverity>(severityFilter, true, out var parsedSev))
        {
            query = query.Where(p => p.CatSeverity == parsedSev);
        }

        var items = await query.OrderBy(p => p.ScheduledCompletionDate).ToListAsync(cancellationToken);

        // Overdue filter (applied after materialization due to computed property)
        if (overdueOnly)
        {
            var now = DateTime.UtcNow;
            items = items.Where(p => p.ScheduledCompletionDate < now && p.ActualCompletionDate == null && p.Status == PoamStatus.Ongoing).ToList();
        }

        return items;
    }

    /// <inheritdoc />
    public async Task<RarDocument> GenerateRarAsync(
        string systemId,
        string assessmentId,
        string? format = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId, nameof(systemId));
        ArgumentException.ThrowIfNullOrWhiteSpace(assessmentId, nameof(assessmentId));

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var system = await db.RegisteredSystems.FindAsync([systemId], cancellationToken)
            ?? throw new InvalidOperationException($"System '{systemId}' not found.");

        // Get findings for this assessment
        var findings = await db.Findings
            .Where(f => f.AssessmentId == assessmentId)
            .ToListAsync(cancellationToken);

        // Get effectiveness determinations
        var effectiveness = await db.ControlEffectivenessRecords
            .Where(e => e.RegisteredSystemId == systemId && e.AssessmentId == assessmentId)
            .ToListAsync(cancellationToken);

        // Build CAT breakdown
        var catBreakdown = new CatBreakdown
        {
            CatI = effectiveness.Count(e => e.CatSeverity == Core.Models.Compliance.CatSeverity.CatI),
            CatII = effectiveness.Count(e => e.CatSeverity == Core.Models.Compliance.CatSeverity.CatII),
            CatIII = effectiveness.Count(e => e.CatSeverity == Core.Models.Compliance.CatSeverity.CatIII)
        };

        // Get risk acceptances
        var riskAcceptances = await db.RiskAcceptances
            .Include(r => r.AuthorizationDecision)
            .Where(r => r.AuthorizationDecision!.RegisteredSystemId == systemId && r.IsActive)
            .ToListAsync(cancellationToken);

        // Build per-family risk results
        var familyGroups = findings
            .GroupBy(f => f.ControlFamily)
            .Select(g =>
            {
                var family = g.Key;
                var openCount = g.Count(f => f.Status == FindingStatus.Open || f.Status == FindingStatus.InProgress);
                var acceptedCount = riskAcceptances.Count(r => r.ControlId.StartsWith(family));
                var riskLevel = openCount > 5 ? "High" : openCount > 2 ? "Medium" : "Low";

                return new FamilyRiskResult
                {
                    Family = family,
                    FamilyName = FamilyNames.GetValueOrDefault(family, family),
                    TotalFindings = g.Count(),
                    OpenFindings = openCount,
                    AcceptedFindings = acceptedCount,
                    RiskLevel = riskLevel
                };
            })
            .OrderByDescending(f => f.OpenFindings)
            .ToList();

        // Determine aggregate risk level
        var aggregateRisk = catBreakdown.CatI > 0 ? "Critical"
            : catBreakdown.CatII > 3 ? "High"
            : catBreakdown.CatII > 0 ? "Medium"
            : "Low";

        // Generate RAR content
        var sb = new StringBuilder();
        sb.AppendLine("# Risk Assessment Report (RAR)");
        sb.AppendLine();
        sb.AppendLine($"**System**: {system.Name}");
        sb.AppendLine($"**Assessment ID**: {assessmentId}");
        sb.AppendLine($"**Date**: {DateTime.UtcNow:yyyy-MM-dd}");
        sb.AppendLine($"**Aggregate Risk Level**: {aggregateRisk}");
        sb.AppendLine();
        sb.AppendLine("## Executive Summary");
        sb.AppendLine();
        sb.AppendLine($"This Risk Assessment Report provides a comprehensive risk analysis for {system.Name}. ");
        sb.AppendLine($"The assessment identified {findings.Count} total findings across {familyGroups.Count} control families. ");
        sb.AppendLine($"The aggregate residual risk level is **{aggregateRisk}**.");
        sb.AppendLine();
        sb.AppendLine("## CAT Severity Breakdown");
        sb.AppendLine();
        sb.AppendLine($"| Severity | Count |");
        sb.AppendLine($"|----------|-------|");
        sb.AppendLine($"| CAT I    | {catBreakdown.CatI}     |");
        sb.AppendLine($"| CAT II   | {catBreakdown.CatII}     |");
        sb.AppendLine($"| CAT III  | {catBreakdown.CatIII}     |");
        sb.AppendLine($"| **Total** | **{catBreakdown.Total}** |");
        sb.AppendLine();
        sb.AppendLine("## Risk by Control Family");
        sb.AppendLine();
        sb.AppendLine("| Family | Name | Total | Open | Accepted | Risk |");
        sb.AppendLine("|--------|------|-------|------|----------|------|");
        foreach (var fr in familyGroups)
        {
            sb.AppendLine($"| {fr.Family} | {fr.FamilyName} | {fr.TotalFindings} | {fr.OpenFindings} | {fr.AcceptedFindings} | {fr.RiskLevel} |");
        }
        sb.AppendLine();
        sb.AppendLine("## Risk Acceptances");
        sb.AppendLine();
        sb.AppendLine($"Active risk acceptances: {riskAcceptances.Count}");
        foreach (var ra in riskAcceptances)
        {
            sb.AppendLine($"- **{ra.ControlId}** ({ra.CatSeverity}): {ra.Justification.Substring(0, Math.Min(100, ra.Justification.Length))}...");
        }

        var rar = new RarDocument
        {
            SystemId = systemId,
            AssessmentId = assessmentId,
            GeneratedAt = DateTime.UtcNow,
            Format = format ?? "markdown",
            ExecutiveSummary = $"Assessment identified {findings.Count} findings. Aggregate risk: {aggregateRisk}. CAT I: {catBreakdown.CatI}, CAT II: {catBreakdown.CatII}, CAT III: {catBreakdown.CatIII}.",
            AggregateRiskLevel = aggregateRisk,
            FamilyRisks = familyGroups,
            CatBreakdown = catBreakdown,
            Content = sb.ToString()
        };

        _logger.LogInformation(
            "RAR generated for system {SystemId}, assessment {AssessmentId}, aggregate risk: {Risk}",
            systemId, assessmentId, aggregateRisk);

        return rar;
    }

    /// <inheritdoc />
    public async Task<AuthorizationPackageBundle> BundlePackageAsync(
        string systemId,
        string? format = null,
        bool includeEvidence = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId, nameof(systemId));

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var system = await db.RegisteredSystems.FindAsync([systemId], cancellationToken)
            ?? throw new InvalidOperationException($"System '{systemId}' not found.");

        var docFormat = format ?? "markdown";
        var package = new AuthorizationPackageBundle
        {
            SystemId = systemId,
            GeneratedAt = DateTime.UtcNow,
            Format = docFormat,
            IncludesEvidence = includeEvidence
        };

        // 1. SSP — check if generated
        var ssp = await db.Documents
            .Where(d => d.SystemName == system.Name && d.DocumentType == "SSP")
            .OrderByDescending(d => d.GeneratedAt)
            .FirstOrDefaultAsync(cancellationToken);

        package.Documents.Add(new PackageDocument
        {
            Name = "System Security Plan",
            FileName = $"ssp.{(docFormat == "markdown" ? "md" : docFormat)}",
            DocumentType = "SSP",
            Content = ssp?.Content ?? "# System Security Plan\n\n*Not yet generated. Run compliance_generate_ssp to create.*",
            Status = ssp != null ? "generated" : "not_available"
        });

        // 2. SAR — from documents table
        var sar = await db.Documents
            .Where(d => d.SystemName == system.Name && d.DocumentType == "SAR")
            .OrderByDescending(d => d.GeneratedAt)
            .FirstOrDefaultAsync(cancellationToken);

        package.Documents.Add(new PackageDocument
        {
            Name = "Security Assessment Report",
            FileName = $"sar.{(docFormat == "markdown" ? "md" : docFormat)}",
            DocumentType = "SAR",
            Content = sar?.Content ?? "# Security Assessment Report\n\n*Not yet generated. Run compliance_generate_sar to create.*",
            Status = sar != null ? "generated" : "not_available"
        });

        // 3. RAR — generate inline
        var latestAssessment = await db.Assessments
            .Where(a => a.RegisteredSystemId == systemId)
            .OrderByDescending(a => a.CompletedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestAssessment != null)
        {
            try
            {
                var rar = await GenerateRarAsync(systemId, latestAssessment.Id, format, cancellationToken);
                package.Documents.Add(new PackageDocument
                {
                    Name = "Risk Assessment Report",
                    FileName = $"rar.{(docFormat == "markdown" ? "md" : docFormat)}",
                    DocumentType = "RAR",
                    Content = rar.Content,
                    Status = "generated"
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate RAR for package bundling");
                package.Documents.Add(new PackageDocument
                {
                    Name = "Risk Assessment Report",
                    FileName = $"rar.{(docFormat == "markdown" ? "md" : docFormat)}",
                    DocumentType = "RAR",
                    Content = "# Risk Assessment Report\n\n*Generation failed. Run compliance_generate_rar manually.*",
                    Status = "not_available"
                });
            }
        }
        else
        {
            package.Documents.Add(new PackageDocument
            {
                Name = "Risk Assessment Report",
                FileName = $"rar.{(docFormat == "markdown" ? "md" : docFormat)}",
                DocumentType = "RAR",
                Content = "# Risk Assessment Report\n\n*No assessment found. Run an assessment first.*",
                Status = "not_available"
            });
        }

        // 4. POA&M
        var poamItems = await db.PoamItems
            .Include(p => p.Milestones)
            .Where(p => p.RegisteredSystemId == systemId)
            .OrderBy(p => p.ScheduledCompletionDate)
            .ToListAsync(cancellationToken);

        var poamSb = new StringBuilder();
        poamSb.AppendLine("# Plan of Action and Milestones (POA&M)");
        poamSb.AppendLine();
        poamSb.AppendLine($"**System**: {system.Name}");
        poamSb.AppendLine($"**Date**: {DateTime.UtcNow:yyyy-MM-dd}");
        poamSb.AppendLine($"**Total Items**: {poamItems.Count}");
        poamSb.AppendLine();
        poamSb.AppendLine("| # | Control | Weakness | Severity | POC | Status | Due Date |");
        poamSb.AppendLine("|---|---------|----------|----------|-----|--------|----------|");
        int itemNum = 1;
        foreach (var item in poamItems)
        {
            poamSb.AppendLine($"| {itemNum++} | {item.SecurityControlNumber} | {item.Weakness.Substring(0, Math.Min(50, item.Weakness.Length))}... | {item.CatSeverity} | {item.PointOfContact} | {item.Status} | {item.ScheduledCompletionDate:yyyy-MM-dd} |");
        }

        package.Documents.Add(new PackageDocument
        {
            Name = "Plan of Action and Milestones",
            FileName = $"poam.{(docFormat == "markdown" ? "md" : docFormat)}",
            DocumentType = "POAM",
            Content = poamSb.ToString(),
            Status = poamItems.Count > 0 ? "generated" : "not_available"
        });

        // 5. CRM — check documents table
        var crm = await db.Documents
            .Where(d => d.SystemName == system.Name && d.DocumentType == "CRM")
            .OrderByDescending(d => d.GeneratedAt)
            .FirstOrDefaultAsync(cancellationToken);

        package.Documents.Add(new PackageDocument
        {
            Name = "Customer Responsibility Matrix",
            FileName = $"crm.{(docFormat == "markdown" ? "md" : docFormat)}",
            DocumentType = "CRM",
            Content = crm?.Content ?? "# Customer Responsibility Matrix\n\n*Not yet generated. Run compliance_generate_crm to create.*",
            Status = crm != null ? "generated" : "not_available"
        });

        // 6. ATO Letter
        var activeDecision = await db.AuthorizationDecisions
            .Where(d => d.RegisteredSystemId == systemId && d.IsActive)
            .FirstOrDefaultAsync(cancellationToken);

        if (activeDecision != null)
        {
            var letterSb = new StringBuilder();
            letterSb.AppendLine("# Authorization to Operate Letter");
            letterSb.AppendLine();
            letterSb.AppendLine($"**System**: {system.Name}");
            letterSb.AppendLine($"**Decision**: {activeDecision.DecisionType}");
            letterSb.AppendLine($"**Date**: {activeDecision.DecisionDate:yyyy-MM-dd}");
            letterSb.AppendLine($"**Expiration**: {activeDecision.ExpirationDate?.ToString("yyyy-MM-dd") ?? "N/A"}");
            letterSb.AppendLine($"**Residual Risk**: {activeDecision.ResidualRiskLevel}");
            letterSb.AppendLine($"**Compliance Score**: {activeDecision.ComplianceScoreAtDecision:F2}%");
            letterSb.AppendLine($"**Issued By**: {activeDecision.IssuedByName}");
            letterSb.AppendLine();
            if (!string.IsNullOrWhiteSpace(activeDecision.TermsAndConditions))
            {
                letterSb.AppendLine("## Terms and Conditions");
                letterSb.AppendLine();
                letterSb.AppendLine(activeDecision.TermsAndConditions);
            }

            package.Documents.Add(new PackageDocument
            {
                Name = "Authorization Letter",
                FileName = $"ato-letter.{(docFormat == "markdown" ? "md" : docFormat)}",
                DocumentType = "ATO_LETTER",
                Content = letterSb.ToString(),
                Status = "generated"
            });
        }
        else
        {
            package.Documents.Add(new PackageDocument
            {
                Name = "Authorization Letter",
                FileName = $"ato-letter.{(docFormat == "markdown" ? "md" : docFormat)}",
                DocumentType = "ATO_LETTER",
                Content = "# Authorization Letter\n\n*No active authorization decision. Run compliance_issue_authorization first.*",
                Status = "not_available"
            });
        }

        _logger.LogInformation(
            "Authorization package bundled for system {SystemId}: {Count} documents, format: {Format}",
            systemId, package.DocumentCount, docFormat);

        return package;
    }
}
