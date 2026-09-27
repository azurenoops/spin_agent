using System.Text.Json;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Dtos.Dashboard;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Agents.Compliance.Services;

public sealed record BaselinePackageOption(string Id, string Purpose, DateTimeOffset GeneratedAt, string ContentHash);
public sealed record RecordedDecisionOption(string Id, string DecisionType, DateTime DecisionDate, string Issuer, string SnapshotHash);
public sealed record RetainedPreviewOption(Guid Id, DateTimeOffset GeneratedAt, string ContentHash);
public sealed record RetainedPackageOptions(IReadOnlyList<BaselinePackageOption> Baselines,
    IReadOnlyList<RecordedDecisionOption> Decisions, IReadOnlyList<RetainedPreviewOption> Previews);

/// <summary>Read-only selection metadata over existing retained records; generation revalidates the chosen pins.</summary>
public static class AuthorizationPackageContextOptions
{
    /// <summary>Stable source fingerprint; capture time is envelope metadata, not a source revision.</summary>
    public static string SourceContextHash(RetainedPackageManifest context) =>
        ApprovedProfileDocumentData.Hash(RetainedPackageContext.Serialize(context with { CapturedAt = DateTimeOffset.MinValue }));

    public static string SourceContextHash(string retainedContextJson) => SourceContextHash(RetainedPackageContext.Read(retainedContextJson));

    public static JsonElement DecisionSnapshot(AuthorizationDecision decision) =>
        JsonSerializer.SerializeToElement(new
        {
            decision.Id, decision.RegisteredSystemId, decision.TenantId,
            DecisionType = decision.DecisionType.ToString(), decision.DecisionDate, decision.ExpirationDate,
            decision.TermsAndConditions, decision.IssuedBy, decision.IssuedByName, decision.IsActive,
            ResidualRiskLevel = decision.ResidualRiskLevel.ToString(), decision.ResidualRiskJustification,
            ComplianceScoreAtDecision = decision.RecordedAt.HasValue ? (double?)null : decision.ComplianceScoreAtDecision,
            FindingsAtDecision = decision.RecordedAt.HasValue ? null : decision.FindingsAtDecision,
            decision.ExternalIssuingAuthority, decision.SourceEvidenceId, decision.SourceEvidenceHash,
            decision.BaselinePackageId, decision.BaselinePackageHash, decision.RecordedBy, decision.RecordedAt
        }, RetainedPackageContext.JsonOptions);

    public static async Task<RetainedPackageOptions> ReadAsync(AtoCopilotContext db, ExportSettings settings,
        string systemId, CancellationToken ct)
    {
        var system = await db.RegisteredSystems.AsNoTracking().SingleOrDefaultAsync(s => s.Id == systemId && s.IsActive, ct)
            ?? throw new KeyNotFoundException("System not found in this workspace.");
        var now = DateTimeOffset.UtcNow;
        var packages = await db.AuthorizationPackages.AsNoTracking().Where(p =>
            p.RegisteredSystemId == systemId && p.TenantId == system.TenantId
            && p.Status == PackageStatus.Completed && p.ContentHash != null && p.FilePath != null
            && p.ValidationPassed != false && p.ValidationErrorCount == 0).ToListAsync(ct);
        var decisions = await db.AuthorizationDecisions.AsNoTracking().Where(d =>
            d.RegisteredSystemId == systemId && d.TenantId == system.TenantId).ToListAsync(ct);
        var previews = await db.SspExports.AsNoTracking().Where(p => p.SystemId == systemId
            && p.SourceTenantId == system.TenantId && p.Status == "Preview" && p.Format == "json"
            && p.FilePath != null && p.ContentHash != null && p.SourceManifestJson != null).ToListAsync(ct);
        return new(
            packages.Where(p => p.ExpiresAt > now && File.Exists(p.FilePath))
                .OrderByDescending(p => p.GeneratedAt).ThenBy(p => p.Id)
                .Select(p => new BaselinePackageOption(p.Id, p.Purpose.ToString(), p.GeneratedAt, p.ContentHash!)).ToArray(),
            decisions.Where(d => d.DecisionType is AuthorizationDecisionType.Ato or AuthorizationDecisionType.AtoWithConditions or AuthorizationDecisionType.Iatt
                && !string.IsNullOrWhiteSpace(d.IssuedBy) && d.DecisionDate != default)
                .OrderByDescending(d => d.DecisionDate).ThenBy(d => d.Id)
                .Select(d => new RecordedDecisionOption(d.Id, d.DecisionType.ToString(), d.DecisionDate,
                    d.ExternalIssuingAuthority ?? (string.IsNullOrWhiteSpace(d.IssuedByName) ? d.IssuedBy : d.IssuedByName),
                    ApprovedProfileDocumentData.Hash(DecisionSnapshot(d).GetRawText()))).ToArray(),
            previews.Where(p => p.ExpiresAt > now && File.Exists(Path.Combine(settings.ExportsPath, p.FilePath!)))
                .OrderByDescending(p => p.GeneratedAt).ThenBy(p => p.Id)
                .Select(p => new RetainedPreviewOption(p.Id, p.GeneratedAt, p.ContentHash!)).ToArray());
    }
}
