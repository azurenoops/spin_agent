using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Implements system interconnection registry, ISA/MOU agreement tracking, and agreement validation.
/// NIST SP 800-47 / DoD Instruction 8510.01 interconnection documentation requirements.
/// </summary>
public class InterconnectionService : IInterconnectionService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InterconnectionService> _logger;

    public InterconnectionService(
        IServiceScopeFactory scopeFactory,
        ILogger<InterconnectionService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<InterconnectionResult> AddInterconnectionAsync(
        string systemId,
        string targetSystemName,
        InterconnectionType interconnectionType,
        DataFlowDirection dataFlowDirection,
        string dataClassification,
        string createdBy,
        string? targetSystemOwner = null,
        string? targetSystemAcronym = null,
        string? dataDescription = null,
        List<string>? protocolsUsed = null,
        List<string>? portsUsed = null,
        List<string>? securityMeasures = null,
        string? authenticationMethod = null,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var system = await context.RegisteredSystems
            .FirstOrDefaultAsync(s => s.Id == systemId, cancellationToken)
            ?? throw new InvalidOperationException($"System '{systemId}' not found.");

        var interconnection = new SystemInterconnection
        {
            RegisteredSystemId = systemId,
            TargetSystemName = targetSystemName,
            InterconnectionType = interconnectionType,
            DataFlowDirection = dataFlowDirection,
            DataClassification = dataClassification,
            CreatedBy = createdBy,
            TargetSystemOwner = targetSystemOwner,
            TargetSystemAcronym = targetSystemAcronym,
            DataDescription = dataDescription,
            ProtocolsUsed = protocolsUsed ?? [],
            PortsUsed = portsUsed ?? [],
            SecurityMeasures = securityMeasures ?? [],
            AuthenticationMethod = authenticationMethod,
            Status = InterconnectionStatus.Proposed
        };

        context.SystemInterconnections.Add(interconnection);

        // Clear HasNoExternalInterconnections if previously set
        if (system.HasNoExternalInterconnections)
        {
            system.HasNoExternalInterconnections = false;
            _logger.LogInformation(
                "Cleared HasNoExternalInterconnections for system {SystemId} due to new interconnection {InterconnectionId}",
                systemId, interconnection.Id);
        }

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Registered interconnection {InterconnectionId} from system {SystemId} to {TargetSystem}",
            interconnection.Id, systemId, targetSystemName);

        return new InterconnectionResult(
            interconnection.Id,
            interconnection.TargetSystemName,
            interconnection.Status,
            HasAgreement: false);
    }

    /// <inheritdoc />
    public async Task<List<InterconnectionResult>> ListInterconnectionsAsync(
        string systemId,
        InterconnectionStatus? statusFilter = null,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var systemExists = await context.RegisteredSystems
            .AnyAsync(s => s.Id == systemId, cancellationToken);
        if (!systemExists)
            throw new InvalidOperationException($"System '{systemId}' not found.");

        var query = context.SystemInterconnections
            .Include(i => i.Agreements)
            .Where(i => i.RegisteredSystemId == systemId);

        if (statusFilter.HasValue)
            query = query.Where(i => i.Status == statusFilter.Value);

        var interconnections = await query
            .OrderBy(i => i.CreatedAt)
            .ToListAsync(cancellationToken);

        _logger.LogInformation(
            "Listed {Count} interconnection(s) for system '{SystemId}' (filter={StatusFilter})",
            interconnections.Count, systemId, statusFilter);

        return interconnections.Select(i => new InterconnectionResult(
            i.Id,
            i.TargetSystemName,
            i.Status,
            HasAgreement: i.Agreements.Any())).ToList();
    }

    /// <inheritdoc />
    public async Task<InterconnectionResult> UpdateInterconnectionAsync(
        string interconnectionId,
        string? targetSystemName = null,
        InterconnectionType? interconnectionType = null,
        DataFlowDirection? dataFlowDirection = null,
        string? dataClassification = null,
        string? dataDescription = null,
        List<string>? protocolsUsed = null,
        List<string>? portsUsed = null,
        List<string>? securityMeasures = null,
        string? authenticationMethod = null,
        InterconnectionStatus? status = null,
        string? statusReason = null,
        CancellationToken cancellationToken = default,
        string? targetSystemOwner = null,
        string? targetSystemAcronym = null)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var interconnection = await context.SystemInterconnections
            .Include(i => i.Agreements)
            .FirstOrDefaultAsync(i => i.Id == interconnectionId, cancellationToken)
            ?? throw new InvalidOperationException($"Interconnection '{interconnectionId}' not found.");

        // Require reason for Suspended/Terminated
        if (status is InterconnectionStatus.Suspended or InterconnectionStatus.Terminated
            && string.IsNullOrWhiteSpace(statusReason))
        {
            throw new InvalidOperationException(
                "Status reason is required when suspending or terminating an interconnection.");
        }

        // Apply updates
        if (targetSystemName is not null) interconnection.TargetSystemName = targetSystemName;
        if (targetSystemOwner is not null) interconnection.TargetSystemOwner = targetSystemOwner;
        if (targetSystemAcronym is not null) interconnection.TargetSystemAcronym = targetSystemAcronym;
        if (interconnectionType.HasValue) interconnection.InterconnectionType = interconnectionType.Value;
        if (dataFlowDirection.HasValue) interconnection.DataFlowDirection = dataFlowDirection.Value;
        if (dataClassification is not null) interconnection.DataClassification = dataClassification;
        if (dataDescription is not null) interconnection.DataDescription = dataDescription;
        if (protocolsUsed is not null) interconnection.ProtocolsUsed = protocolsUsed;
        if (portsUsed is not null) interconnection.PortsUsed = portsUsed;
        if (securityMeasures is not null) interconnection.SecurityMeasures = securityMeasures;
        if (authenticationMethod is not null) interconnection.AuthenticationMethod = authenticationMethod;
        if (status.HasValue)
        {
            interconnection.Status = status.Value;
            interconnection.StatusReason = statusReason;
        }

        interconnection.ModifiedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Updated interconnection {InterconnectionId}: status={Status}",
            interconnectionId, interconnection.Status);

        return new InterconnectionResult(
            interconnection.Id,
            interconnection.TargetSystemName,
            interconnection.Status,
            HasAgreement: interconnection.Agreements.Any());
    }

    /// <inheritdoc />
    public async Task<IsaGenerationResult> GenerateIsaAsync(
        string interconnectionId,
        string createdBy,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var interconnection = await context.SystemInterconnections
            .FirstOrDefaultAsync(i => i.Id == interconnectionId, cancellationToken)
            ?? throw new InvalidOperationException($"Interconnection '{interconnectionId}' not found.");

        if (interconnection.Status == InterconnectionStatus.Terminated)
            throw new InvalidOperationException("Cannot generate ISA for a terminated interconnection.");

        var system = await context.RegisteredSystems
            .Include(s => s.RmfRoleAssignments)
            .FirstOrDefaultAsync(s => s.Id == interconnection.RegisteredSystemId, cancellationToken);

        var systemName = system?.Name ?? "Unknown System";
        var title = $"Interconnection Security Agreement — {systemName} ↔ {interconnection.TargetSystemName}";

        // Build 7-section NIST SP 800-47 template
        var narrative = BuildIsaNarrative(system, interconnection);

        var agreement = new InterconnectionAgreement
        {
            SystemInterconnectionId = interconnectionId,
            AgreementType = AgreementType.Isa,
            Title = title,
            Status = AgreementStatus.Draft,
            NarrativeDocument = narrative,
            CreatedBy = createdBy
        };

        context.InterconnectionAgreements.Add(agreement);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Generated ISA {AgreementId} for interconnection {InterconnectionId}",
            agreement.Id, interconnectionId);

        return new IsaGenerationResult(
            agreement.Id, title, AgreementType.Isa, narrative);
    }

    /// <inheritdoc />
    public async Task<InterconnectionAgreement> RegisterAgreementAsync(
        string interconnectionId,
        AgreementType agreementType,
        string title,
        string createdBy,
        string? documentReference = null,
        AgreementStatus status = AgreementStatus.Draft,
        DateTime? effectiveDate = null,
        DateTime? expirationDate = null,
        string? signedByLocal = null,
        string? signedByRemote = null,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var interconnection = await context.SystemInterconnections
            .FirstOrDefaultAsync(i => i.Id == interconnectionId, cancellationToken)
            ?? throw new InvalidOperationException($"Interconnection '{interconnectionId}' not found.");

        if (interconnection.Status == InterconnectionStatus.Terminated)
            throw new InvalidOperationException("Cannot register agreement for a terminated interconnection.");

        var agreement = new InterconnectionAgreement
        {
            SystemInterconnectionId = interconnectionId,
            AgreementType = agreementType,
            Title = title,
            DocumentReference = documentReference,
            Status = status,
            EffectiveDate = effectiveDate,
            ExpirationDate = expirationDate,
            SignedByLocal = signedByLocal,
            SignedByRemote = signedByRemote,
            CreatedBy = createdBy
        };

        context.InterconnectionAgreements.Add(agreement);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Registered agreement {AgreementId} for interconnection {InterconnectionId}",
            agreement.Id, interconnectionId);

        return agreement;
    }

    /// <inheritdoc />
    public async Task<InterconnectionAgreement> UpdateAgreementAsync(
        string agreementId,
        AgreementStatus? status = null,
        DateTime? effectiveDate = null,
        DateTime? expirationDate = null,
        string? signedByLocal = null,
        DateTime? signedByLocalDate = null,
        string? signedByRemote = null,
        DateTime? signedByRemoteDate = null,
        string? reviewNotes = null,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var agreement = await context.InterconnectionAgreements
            .FirstOrDefaultAsync(a => a.Id == agreementId, cancellationToken)
            ?? throw new InvalidOperationException($"Agreement '{agreementId}' not found.");

        // Terminated agreements can only have review_notes updated
        if (agreement.Status == AgreementStatus.Terminated)
        {
            if (status.HasValue || effectiveDate.HasValue || expirationDate.HasValue
                || signedByLocal is not null || signedByLocalDate.HasValue
                || signedByRemote is not null || signedByRemoteDate.HasValue)
            {
                throw new InvalidOperationException(
                    "Cannot update a terminated agreement (except review_notes).");
            }

            if (reviewNotes is not null)
            {
                agreement.ReviewNotes = reviewNotes;
                agreement.ModifiedAt = DateTime.UtcNow;
                await context.SaveChangesAsync(cancellationToken);
            }
            return agreement;
        }

        if (status.HasValue) agreement.Status = status.Value;
        if (effectiveDate.HasValue) agreement.EffectiveDate = effectiveDate.Value;
        if (expirationDate.HasValue) agreement.ExpirationDate = expirationDate.Value;
        if (signedByLocal is not null) agreement.SignedByLocal = signedByLocal;
        if (signedByLocalDate.HasValue) agreement.SignedByLocalDate = signedByLocalDate.Value;
        if (signedByRemote is not null) agreement.SignedByRemote = signedByRemote;
        if (signedByRemoteDate.HasValue) agreement.SignedByRemoteDate = signedByRemoteDate.Value;
        if (reviewNotes is not null) agreement.ReviewNotes = reviewNotes;

        agreement.ModifiedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Agreement '{AgreementId}' updated, status={Status}",
            agreementId, agreement.Status);

        return agreement;
    }
    /// <inheritdoc />
    public async Task CertifyNoInterconnectionsAsync(
        string systemId,
        bool certify,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var system = await context.RegisteredSystems
            .FirstOrDefaultAsync(s => s.Id == systemId, cancellationToken)
            ?? throw new InvalidOperationException($"System '{systemId}' not found.");

        if (certify)
        {
            var activeCount = await context.SystemInterconnections
                .CountAsync(i => i.RegisteredSystemId == systemId
                    && i.Status == InterconnectionStatus.Active, cancellationToken);

            if (activeCount > 0)
                throw new InvalidOperationException(
                    $"System has {activeCount} active interconnection(s). Suspend or terminate them before certifying no interconnections.");
        }

        system.HasNoExternalInterconnections = certify;
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "System {SystemId} HasNoExternalInterconnections set to {Certify}",
            systemId, certify);
    }

    /// <inheritdoc />
    public async Task<AgreementValidationResult> ValidateAgreementsAsync(
        string systemId,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var system = await context.RegisteredSystems
            .FirstOrDefaultAsync(s => s.Id == systemId, cancellationToken)
            ?? throw new InvalidOperationException($"System '{systemId}' not found.");

        // HasNoExternalInterconnections bypass
        if (system.HasNoExternalInterconnections)
        {
            return new AgreementValidationResult(
                TotalInterconnections: 0,
                CompliantCount: 0,
                ExpiringWithin90DaysCount: 0,
                MissingAgreementCount: 0,
                ExpiredAgreementCount: 0,
                IsFullyCompliant: true,
                Items: []);
        }

        var activeInterconnections = await context.SystemInterconnections
            .Include(i => i.Agreements)
            .Where(i => i.RegisteredSystemId == systemId
                && i.Status == InterconnectionStatus.Active)
            .ToListAsync(cancellationToken);

        var items = new List<AgreementValidationItem>();
        var compliant = 0;
        var expiringSoon = 0;
        var missing = 0;
        var expired = 0;
        var now = DateTime.UtcNow;

        foreach (var ic in activeInterconnections)
        {
            var signedAgreements = ic.Agreements
                .Where(a => a.Status == AgreementStatus.Signed)
                .ToList();

            if (signedAgreements.Count == 0)
            {
                missing++;
                items.Add(new AgreementValidationItem(
                    ic.Id, ic.TargetSystemName, "Missing", null, null,
                    "No signed agreement found"));
                continue;
            }

            // Check if any signed agreement is current (not expired)
            var validAgreement = signedAgreements
                .FirstOrDefault(a => !a.ExpirationDate.HasValue || a.ExpirationDate > now);

            if (validAgreement is null)
            {
                // All signed agreements are expired
                expired++;
                var mostRecent = signedAgreements.OrderByDescending(a => a.ExpirationDate).First();
                items.Add(new AgreementValidationItem(
                    ic.Id, ic.TargetSystemName, "Expired",
                    mostRecent.Title, mostRecent.ExpirationDate,
                    "Agreement has expired"));
                continue;
            }

            // Check if expiring within 90 days
            if (validAgreement.ExpirationDate.HasValue
                && validAgreement.ExpirationDate.Value <= now.AddDays(90))
            {
                expiringSoon++;
                compliant++;
                var daysLeft = (int)(validAgreement.ExpirationDate.Value - now).TotalDays;
                items.Add(new AgreementValidationItem(
                    ic.Id, ic.TargetSystemName, "ExpiringSoon",
                    validAgreement.Title, validAgreement.ExpirationDate,
                    $"Agreement expires in {daysLeft} days"));
            }
            else
            {
                compliant++;
                items.Add(new AgreementValidationItem(
                    ic.Id, ic.TargetSystemName, "Compliant",
                    validAgreement.Title, validAgreement.ExpirationDate,
                    null));
            }
        }

        var result = new AgreementValidationResult(
            TotalInterconnections: activeInterconnections.Count,
            CompliantCount: compliant,
            ExpiringWithin90DaysCount: expiringSoon,
            MissingAgreementCount: missing,
            ExpiredAgreementCount: expired,
            IsFullyCompliant: missing == 0 && expired == 0,
            Items: items);

        _logger.LogInformation(
            "Agreement validation for system '{SystemId}': {Total} interconnections, compliant={Compliant}, missing={Missing}, expired={Expired}",
            systemId, result.TotalInterconnections, compliant, missing, expired);

        return result;
    }

    // ─── Private helpers ─────────────────────────────────────────────────────

    private static string BuildIsaNarrative(
        RegisteredSystem? system,
        SystemInterconnection interconnection)
    {
        var systemName = system?.Name ?? "Unknown System";
        var roles = system?.RmfRoleAssignments ?? [];
        var issm = roles.FirstOrDefault(r => r.RmfRole == RmfRole.Issm);
        var isso = roles.FirstOrDefault(r => r.RmfRole == RmfRole.Isso);

        return $"""
            # Interconnection Security Agreement

            ## 1. Introduction

            This Interconnection Security Agreement (ISA) establishes the security requirements for the connection between {systemName} and {interconnection.TargetSystemName} operated by {interconnection.TargetSystemOwner ?? "[Organization]"}.

            ## 2. System Description

            ### 2.1 System A: {systemName}
            - **System Type**: {system?.SystemType.ToString() ?? "N/A"}
            - **Hosting**: {system?.HostingEnvironment ?? "N/A"}

            ### 2.2 System B: {interconnection.TargetSystemName}
            - **Owner**: {interconnection.TargetSystemOwner ?? "[To be determined]"}

            ## 3. Interconnection Details

            - **Connection Type**: {interconnection.InterconnectionType}
            - **Data Flow**: {interconnection.DataFlowDirection}
            - **Data Classification**: {interconnection.DataClassification}
            - **Protocols**: {(interconnection.ProtocolsUsed.Count > 0 ? string.Join(", ", interconnection.ProtocolsUsed) : "N/A")}
            - **Ports**: {(interconnection.PortsUsed.Count > 0 ? string.Join(", ", interconnection.PortsUsed) : "N/A")}

            ## 4. Security Controls

            {(interconnection.SecurityMeasures.Count > 0 ? string.Join("\n", interconnection.SecurityMeasures.Select(m => $"- {m}")) : "- [To be determined]")}

            ## 5. Roles and Responsibilities

            ### System A Personnel
            - **ISSM**: {issm?.UserDisplayName ?? issm?.UserId ?? "[To be assigned]"}
            - **ISSO**: {isso?.UserDisplayName ?? isso?.UserId ?? "[To be assigned]"}

            ### System B Personnel
            - **Point of Contact**: {interconnection.TargetSystemOwner ?? "[To be determined]"}

            ## 6. Agreement Terms

            - **Effective Date**: [To be determined]
            - **Duration**: 1 year with annual renewal
            - **Termination**: Either party may terminate with 30 days written notice

            ## 7. Signatures

            | Role | Name | Signature | Date |
            |------|------|-----------|------|
            | System A AO | | | |
            | System B AO | | | |
            | System A ISSM | {issm?.UserDisplayName ?? ""} | | |
            """;
    }
}
