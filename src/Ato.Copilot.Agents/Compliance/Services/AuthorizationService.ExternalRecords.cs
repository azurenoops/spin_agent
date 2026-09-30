using System.Data;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services.Roles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ato.Copilot.Agents.Compliance.Services;

public partial class AuthorizationService
{
    public async Task<AuthorizationDecision> RecordExternalAuthorizationAsync(
        string systemId, ExternalAuthorizationRecordInput input, string recordedBy, string recordedByName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordedBy);
        if (string.IsNullOrWhiteSpace(input.DecisionType))
            throw new InvalidOperationException("The recorded decision type is required.");
        var normalized = input.DecisionType.Equals("ATOwC", StringComparison.OrdinalIgnoreCase) ? "AtoWithConditions" : input.DecisionType;
        if (!Enum.TryParse<AuthorizationDecisionType>(normalized, true, out var decisionType)
            || !Enum.IsDefined(decisionType)
            || !Enum.TryParse<ComplianceRiskLevel>(input.ResidualRiskLevel, true, out var risk)
            || !Enum.IsDefined(risk))
            throw new InvalidOperationException("A valid recorded decision type and residual risk are required.");
        if (input.DecisionDate == default || input.DecisionDate > DateTime.UtcNow
            || decisionType != AuthorizationDecisionType.Dato && input.ExpirationDate is null
            || input.ExpirationDate <= input.DecisionDate
            || string.IsNullOrWhiteSpace(input.IssuingAuthority) || input.IssuingAuthority.Length > 500
            || input.TermsAndConditions?.Length > 8000)
            throw new InvalidOperationException("Review the issuing authority, decision date, expiration and conditions.");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        await SystemWorkspaceAccessPolicy.RequireAsync(db, systemId, permission => permission.CanDecideAuthorization, cancellationToken);
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
        var system = await db.RegisteredSystems.SingleOrDefaultAsync(x => x.Id == systemId && x.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("The system is not accessible.");
        var source = await db.EvidenceArtifacts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == input.SourceEvidenceId
            && x.RegisteredSystemId == systemId && x.TenantId == system.TenantId && !x.IsDeleted, cancellationToken)
            ?? throw new InvalidOperationException("The retained decision source is not accessible in this system.");
        var package = await db.AuthorizationPackages.AsNoTracking().SingleOrDefaultAsync(x => x.Id == input.BaselinePackageId
            && x.RegisteredSystemId == systemId && x.TenantId == system.TenantId && x.Status == PackageStatus.Completed, cancellationToken)
            ?? throw new InvalidOperationException("Select a completed package belonging to this system.");
        if (string.IsNullOrWhiteSpace(package.FilePath) || package.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("The completed package is unavailable or expired. Select an available retained baseline.");
        if (string.IsNullOrWhiteSpace(source.ContentHash) || string.IsNullOrWhiteSpace(package.ContentHash)
            || !string.Equals(source.ContentHash, input.ExpectedSourceHash, StringComparison.Ordinal)
            || !string.Equals(package.ContentHash, input.ExpectedPackageHash, StringComparison.Ordinal))
            throw new InvalidOperationException("The source or retained package hash changed. Refresh and review again.");
        var prior = await db.AuthorizationDecisions.Where(x => x.RegisteredSystemId == systemId
            && x.TenantId == system.TenantId && x.IsActive).SingleOrDefaultAsync(cancellationToken);
        if (prior?.Id != input.ExpectedActiveDecisionId)
            throw new InvalidOperationException("The current decision changed. Refresh and review again.");
        if (input.MakeCurrent && prior is not null && input.DecisionDate < prior.DecisionDate)
            throw new InvalidOperationException("An older decision can be retained as history, not replace a newer current decision.");

        var decision = new AuthorizationDecision
        {
            TenantId = system.TenantId, RegisteredSystemId = systemId, DecisionType = decisionType,
            DecisionDate = input.DecisionDate, ExpirationDate = input.ExpirationDate, ResidualRiskLevel = risk,
            TermsAndConditions = input.TermsAndConditions, SourceEvidenceId = source.Id, SourceEvidenceHash = source.ContentHash,
            ExternalIssuingAuthority = input.IssuingAuthority.Trim(), BaselinePackageId = package.Id, BaselinePackageHash = package.ContentHash,
            RecordedBy = recordedBy, RecordedAt = DateTime.UtcNow, IssuedBy = recordedBy, IssuedByName = recordedByName,
            IsActive = input.MakeCurrent,
        };
        if (input.MakeCurrent && prior is not null) { prior.IsActive = false; prior.SupersededById = decision.Id; }
        db.AuthorizationDecisions.Add(decision);
        db.DashboardActivities.Add(new DashboardActivity
        {
            TenantId = system.TenantId, RegisteredSystemId = systemId, EventType = "ExternalAuthorizationRecorded",
            Actor = recordedBy, Summary = $"Recorded external {decisionType} decision against retained package {package.Id}.",
            RelatedEntityType = nameof(AuthorizationDecision), RelatedEntityId = decision.Id,
        });
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return decision;
    }
}
