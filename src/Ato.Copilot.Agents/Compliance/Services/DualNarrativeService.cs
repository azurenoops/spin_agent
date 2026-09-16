using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>Implements policy and technical narrative authoring and evidence grouping.</summary>
public sealed class DualNarrativeService : IDualNarrativeService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DualNarrativeService> _logger;

    public DualNarrativeService(
        IServiceScopeFactory scopeFactory,
        ILogger<DualNarrativeService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DualNarrativeResponse> GetAsync(
        string systemId,
        string controlId,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var implementation = await FindImplementationAsync(db, systemId, controlId, cancellationToken);
        return await BuildResponseAsync(db, implementation, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<DualNarrativeResponse> UpdateAsync(
        string systemId,
        string controlId,
        string? policyNarrative,
        bool updatePolicy,
        string? technicalNarrative,
        bool updateTechnical,
        string role,
        string authoredBy,
        CancellationToken cancellationToken = default)
    {
        if (!updatePolicy && !updateTechnical)
        {
            throw new ArgumentException("At least one narrative field must be provided.");
        }
        if (policyNarrative?.Length > 8000 || technicalNarrative?.Length > 8000)
        {
            throw new ArgumentException("Narrative fields cannot exceed 8000 characters.");
        }
        if (updatePolicy && !CanWritePolicy(role))
        {
            throw new UnauthorizedAccessException($"Role '{role}' is not permitted to write the policy narrative.");
        }
        if (updateTechnical && !CanWriteTechnical(role))
        {
            throw new UnauthorizedAccessException($"Role '{role}' is not permitted to write the technical narrative.");
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var implementation = await FindImplementationAsync(db, systemId, controlId, cancellationToken);

        if (updatePolicy)
        {
            implementation.PolicyNarrative = policyNarrative;
        }
        if (updateTechnical)
        {
            implementation.TechnicalNarrative = technicalNarrative;
            implementation.MigratedFromLegacy = false;
        }

        implementation.AuthoredBy = authoredBy;
        implementation.ModifiedAt = DateTime.UtcNow;
        implementation.ApprovalStatus = SspSectionStatus.Draft;
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Updated dual narrative for {ControlId} in {SystemId}; policy={PolicyUpdated}, technical={TechnicalUpdated}",
            controlId,
            systemId,
            updatePolicy,
            updateTechnical);

        return await BuildResponseAsync(db, implementation, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<EvidenceArtifactSummary> ClassifyEvidenceAsync(
        string artifactId,
        EvidenceNarrativeType narrativeType,
        string? rationale,
        string classifiedBy,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var artifact = await db.EvidenceArtifacts
            .FirstOrDefaultAsync(item => item.Id == artifactId && !item.IsDeleted, cancellationToken)
            ?? throw new InvalidOperationException($"EVIDENCE_NOT_FOUND: Evidence artifact '{artifactId}' not found.");

        artifact.NarrativeType = narrativeType;
        artifact.AutoTagRationale = rationale;
        artifact.ManuallyTaggedBy = narrativeType == EvidenceNarrativeType.Unclassified
            ? null
            : classifiedBy;
        await db.SaveChangesAsync(cancellationToken);
        return ToSummary(artifact);
    }

    private static async Task<ControlImplementation> FindImplementationAsync(
        AtoCopilotContext db,
        string systemId,
        string controlId,
        CancellationToken cancellationToken) =>
        await db.ControlImplementations.FirstOrDefaultAsync(
            item => item.RegisteredSystemId == systemId && item.ControlId == controlId,
            cancellationToken)
        ?? throw new InvalidOperationException(
            $"NARRATIVE_NOT_FOUND: No control implementation found for system '{systemId}' control '{controlId}'.");

    private static async Task<DualNarrativeResponse> BuildResponseAsync(
        AtoCopilotContext db,
        ControlImplementation implementation,
        CancellationToken cancellationToken)
    {
        var evidence = await db.EvidenceArtifacts
            .AsNoTracking()
            .Where(item => item.ControlImplementationId == implementation.Id && !item.IsDeleted)
            .OrderByDescending(item => item.UploadedAt)
            .ToListAsync(cancellationToken);
        var summaries = evidence.Select(ToSummary).ToList();

        return new DualNarrativeResponse(
            implementation.RegisteredSystemId,
            implementation.ControlId,
            implementation.PolicyNarrative,
            implementation.TechnicalNarrative,
            implementation.Narrative,
            implementation.MigratedFromLegacy,
            summaries.Where(item => item.NarrativeType is EvidenceNarrativeType.Policy or EvidenceNarrativeType.Combined).ToList(),
            summaries.Where(item => item.NarrativeType is EvidenceNarrativeType.Technical or EvidenceNarrativeType.Combined).ToList(),
            summaries.Where(item => item.NarrativeType == EvidenceNarrativeType.Unclassified).ToList(),
            false,
            false,
            null,
            null);
    }

    private static EvidenceArtifactSummary ToSummary(EvidenceArtifact artifact) => new(
        artifact.Id,
        artifact.FileName,
        artifact.ContentType,
        artifact.FileSizeBytes,
        artifact.NarrativeType,
        artifact.AutoTagRationale,
        artifact.ManuallyTaggedBy,
        artifact.UploadedAt);

    private static bool CanWritePolicy(string role) =>
        HasRole(role, "Administrator", "SecurityLead", "Analyst");

    private static bool CanWriteTechnical(string role) =>
        HasRole(role, "Administrator", "SecurityLead", "Analyst", "PlatformEngineer", "Sca");

    private static bool HasRole(string role, params string[] allowedRoles) =>
        allowedRoles.Any(allowed =>
            role.Equals(allowed, StringComparison.OrdinalIgnoreCase) ||
            role.Equals($"Compliance.{allowed}", StringComparison.OrdinalIgnoreCase));
}