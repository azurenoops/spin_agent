using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ato.Copilot.Agents.Compliance.Services;

internal static class RetainedPackageContext
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    internal static string Serialize(RetainedPackageManifest manifest) => JsonSerializer.Serialize(manifest, JsonOptions);
    internal static RetainedPackageManifest Read(string json) =>
        JsonSerializer.Deserialize<RetainedPackageManifest>(json, JsonOptions) ?? throw new InvalidOperationException("Retained package context is invalid.");

    internal static async Task<RetainedPackageManifest> ResolveAsync(AtoCopilotContext db, IServiceProvider services,
        string systemId, PackagePurpose purpose, RetainedPackageSelection? selection, string actor, CancellationToken ct)
    {
        if (purpose is not (PackagePurpose.AuthorizedBaselineArchive or PackagePurpose.ChangeSubmission))
            throw new InvalidOperationException("Select AuthorizedBaselineArchive or ChangeSubmission for retained-context generation.");
        if (selection == null) throw new InvalidOperationException("A retained baseline package and recorded decision selection are required.");
        var system = await db.RegisteredSystems.AsNoTracking().SingleOrDefaultAsync(s => s.Id == systemId && s.IsActive, ct)
            ?? throw new InvalidOperationException("System not found in this workspace.");
        var baseline = await BaselineAsync(db, systemId, system.TenantId, selection.BaselinePackageId, selection.BaselineContentHash, ct);
        await RequireFileHashAsync(baseline.FilePath!, selection.BaselineContentHash, ct);
        var decision = await db.AuthorizationDecisions.AsNoTracking().SingleOrDefaultAsync(d =>
            d.Id == selection.AuthorizationDecisionId && d.RegisteredSystemId == systemId && d.TenantId == system.TenantId, ct)
            ?? throw new InvalidOperationException("The selected recorded authorization decision is unavailable for this system.");
        if (decision.DecisionType is not (AuthorizationDecisionType.Ato or AuthorizationDecisionType.AtoWithConditions or AuthorizationDecisionType.Iatt)
            || string.IsNullOrWhiteSpace(decision.IssuedBy) || decision.DecisionDate == default)
            throw new InvalidOperationException("Select a recorded positive authorization decision with an identified issuing actor.");
        var hasBaselineId = !string.IsNullOrWhiteSpace(decision.BaselinePackageId);
        var hasBaselineHash = !string.IsNullOrWhiteSpace(decision.BaselinePackageHash);
        var recordedLink = hasBaselineId && hasBaselineHash;
        if ((hasBaselineId || hasBaselineHash) &&
            (!recordedLink || !string.Equals(decision.BaselinePackageId, baseline.Id, StringComparison.Ordinal)
                || !string.Equals(decision.BaselinePackageHash, baseline.ContentHash, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The recorded decision baseline linkage is incomplete or identifies a different package version/hash.");
        var snapshot = AuthorizationPackageContextOptions.DecisionSnapshot(decision);
        var decisionHash = ApprovedProfileDocumentData.Hash(snapshot.GetRawText());
        if (selection.ExpectedDecisionSnapshotHash != null &&
            !decisionHash.Equals(selection.ExpectedDecisionSnapshotHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The recorded decision changed since selection. Review its current snapshot before generating.");
        var evidence = baseline.RetainedContextJson == null ? [] : Read(baseline.RetainedContextJson).Evidence.ToList();
        DocumentSourceManifest? changeSources = null;
        string? baselineSspHash = null;
        if (purpose == PackagePurpose.ChangeSubmission)
        {
            var change = await ChangeAsync(db, services, systemId, system.TenantId, selection, ct);
            changeSources = change.Manifest;
            evidence.AddRange(change.Manifest.Evidence);
            using var zip = ZipFile.OpenRead(baseline.FilePath!);
            var priorSsp = zip.GetEntry("oscal-ssp.json")
                ?? throw new InvalidOperationException("For an SSP change bundle, select the original completed package containing oscal-ssp.json.");
            await using var stream = priorSsp.Open();
            baselineSspHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
            if (baselineSspHash.Equals(selection.ChangeContentHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The selected SSP snapshot is identical to the retained predecessor; no SSP change is identified.");
        }
        else if (selection.ChangePreviewId.HasValue || selection.ChangeContentHash != null)
            throw new InvalidOperationException("An authorized-baseline archive does not accept a change preview.");
        var bundleScope = purpose == PackagePurpose.AuthorizedBaselineArchive
                ? "Unmodified retained package bytes plus recorded decision snapshot; no mutable artifacts regenerated."
                : "Retained predecessor plus selected SSP change with verified profile/narrative approvals; other current artifacts are not regenerated.";
        if (!recordedLink)
            bundleScope += " The selected decision has no recorded baseline linkage; selecting this pair does not establish authorization coverage.";
        var manifest = new RetainedPackageManifest(purpose, bundleScope,
            system.TenantId, systemId, actor, db.WorkspacePersonId, DateTimeOffset.UtcNow,
            baseline.Id, selection.BaselineContentHash.ToLowerInvariant(), baseline.Purpose, baseline.ValidationPassed,
            decision.Id, snapshot, decisionHash,
            selection.ChangePreviewId, selection.ChangeContentHash?.ToLowerInvariant(), baselineSspHash,
            changeSources, evidence.DistinctBy(e => (e.ShareId, e.ContentHash)).ToArray())
        {
            DecisionBaselineLinkage = recordedLink ? "RecordedExactMatch" : "UnverifiedLegacyPairing"
        };
        await VerifyEvidenceAsync(services, manifest, db.WorkspacePersonId, ct);
        if (selection.ExpectedSourceContextHash != null && !AuthorizationPackageContextOptions.SourceContextHash(manifest)
            .Equals(selection.ExpectedSourceContextHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The retained source context changed since validation. Revalidate the selected records.");
        return manifest;
    }

    internal static async Task<AuthorizationPackage> BaselineAsync(AtoCopilotContext db, string systemId,
        Guid tenantId, string baselineId, string expectedHash, CancellationToken ct)
    {
        RequireHash(expectedHash);
        var baseline = await db.AuthorizationPackages.AsNoTracking().SingleOrDefaultAsync(p =>
            p.Id == baselineId && p.RegisteredSystemId == systemId && p.TenantId == tenantId && p.Status == PackageStatus.Completed, ct)
            ?? throw new InvalidOperationException("The selected completed baseline package is unavailable for this system.");
        if (!string.Equals(baseline.ContentHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The selected baseline content hash does not match its retained record.");
        if (baseline.ValidationPassed == false || baseline.ValidationErrorCount > 0)
            throw new InvalidOperationException("The selected baseline has a recorded validation failure.");
        if (baseline.ExpiresAt <= DateTimeOffset.UtcNow || baseline.FilePath == null || !File.Exists(baseline.FilePath))
            throw new InvalidOperationException("The retained baseline bytes are expired or unavailable.");
        if (baseline.RetainedContextJson != null && ApprovedProfileDocumentData.Hash(baseline.RetainedContextJson) != baseline.RetainedContextHash)
            throw new InvalidOperationException("The baseline's retained context integrity check failed.");
        return baseline;
    }

    private static async Task<(SspExport Preview, DocumentSourceManifest Manifest)> ChangeAsync(AtoCopilotContext db,
        IServiceProvider services, string systemId, Guid tenantId, RetainedPackageSelection selection, CancellationToken ct)
    {
        if (!selection.ChangePreviewId.HasValue || selection.ChangeContentHash == null)
            throw new InvalidOperationException("ChangeSubmission requires an explicitly selected reviewed SSP preview and its content hash.");
        RequireHash(selection.ChangeContentHash);
        var preview = await db.SspExports.AsNoTracking().SingleOrDefaultAsync(p =>
            p.Id == selection.ChangePreviewId && p.SystemId == systemId && p.Status == "Preview" && p.Format == "json", ct)
            ?? throw new InvalidOperationException("The selected SSP change preview is unavailable for this system.");
        if (preview.ExpiresAt <= DateTimeOffset.UtcNow || preview.FilePath == null ||
            !string.Equals(preview.ContentHash, selection.ChangeContentHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The selected change preview is expired or its content hash changed.");
        var gaps = JsonSerializer.Deserialize<DocumentSourceGapDto[]>(preview.SourceGapsJson ?? "[]") ?? [];
        if (gaps.Any(g => g.Code != "OSCAL_SOURCE_WARNING"))
            throw new InvalidOperationException("The SSP change preview contains unresolved source/approval gaps.");
        var manifest = JsonSerializer.Deserialize<DocumentSourceManifest>(preview.SourceManifestJson ?? "null")
            ?? throw new InvalidOperationException("The SSP change preview has no retained source/version manifest.");
        await RequireReviewedSourcesAsync(db, systemId, tenantId, manifest, ct);
        var path = Path.Combine(services.GetRequiredService<IOptions<ExportSettings>>().Value.ExportsPath, preview.FilePath);
        await RequireFileHashAsync(path, selection.ChangeContentHash, ct);
        var bytes = await File.ReadAllBytesAsync(path, ct);
        using var json = JsonDocument.Parse(bytes);
        var ssp = json.RootElement.GetProperty("system-security-plan");
        var requirements = ssp.GetProperty("control-implementation").GetProperty("implemented-requirements");
        var controls = requirements.GetArrayLength();
        if (controls == 0 || controls != manifest.Narratives.Count)
            throw new InvalidOperationException("Every changed SSP control narrative must have an explicitly approved version pin.");
        var implementationIds = manifest.Narratives.Select(n => n.RecordId).Distinct().ToArray();
        var pinnedControls = await db.ControlImplementations.AsNoTracking().Where(i =>
            i.RegisteredSystemId == systemId && i.TenantId == tenantId && implementationIds.Contains(i.Id))
            .Select(i => i.ControlId).ToListAsync(ct);
        if (implementationIds.Length != controls || !pinnedControls.ToHashSet(StringComparer.OrdinalIgnoreCase)
            .SetEquals(requirements.EnumerateArray().Select(r => r.GetProperty("control-id").GetString()!)))
            throw new InvalidOperationException("Reviewed narrative pins do not cover the exact changed SSP controls.");
        var embedded = ssp.GetProperty("metadata").GetProperty("props").EnumerateArray()
            .SingleOrDefault(p => p.GetProperty("name").GetString() == "source-version-manifest");
        if (embedded.ValueKind == JsonValueKind.Undefined || !System.Text.Json.Nodes.JsonNode.DeepEquals(
            System.Text.Json.Nodes.JsonNode.Parse(embedded.GetProperty("value").GetString()!),
            System.Text.Json.Nodes.JsonNode.Parse(preview.SourceManifestJson!)))
            throw new InvalidOperationException("The selected change source manifest is not bound to its retained SSP bytes.");
        var validation = await services.GetRequiredService<IOscalSchemaValidationService>()
            .ValidateAsync(System.Text.Encoding.UTF8.GetString(bytes), "ssp", ct);
        if (!validation.IsValid) throw new InvalidOperationException("The retained SSP change does not pass the unchanged OSCAL schema.");
        return (preview, manifest);
    }

    private static async Task RequireReviewedSourcesAsync(AtoCopilotContext db, string systemId, Guid tenantId,
        DocumentSourceManifest manifest, CancellationToken ct)
    {
        var reviewedTypes = new HashSet<ProfileSectionType>();
        foreach (var pin in manifest.Profiles)
        {
            var section = await db.SystemProfileSections.AsNoTracking().SingleOrDefaultAsync(p =>
                p.Id == pin.RecordId && p.RegisteredSystemId == systemId && p.TenantId == tenantId, ct);
            var approval = await db.ProfileAuditEntries.AsNoTracking().SingleOrDefaultAsync(a =>
                a.Id == pin.VersionId && a.SystemProfileSectionId == pin.RecordId && a.TenantId == tenantId && a.Action == "Approved", ct);
            if (section == null || approval?.SnapshotJson == null || approval.SnapshotHash != pin.ContentHash
                || ApprovedProfileDocumentData.Hash(approval.SnapshotJson) != pin.ContentHash)
                throw new InvalidOperationException("A changed profile section lacks its exact retained approval.");
            reviewedTypes.Add(section.SectionType);
        }
        if (Enum.GetValues<ProfileSectionType>().Where(t => t != ProfileSectionType.LeveragedAuthorizations)
            .Any(t => !reviewedTypes.Contains(t)))
            throw new InvalidOperationException("The SSP change requires retained approvals for all five mandatory profile sections.");
        foreach (var pin in manifest.Narratives)
        {
            var version = await db.Set<NarrativeVersion>().AsNoTracking().SingleOrDefaultAsync(v =>
                v.Id == pin.VersionId && v.ControlImplementationId == pin.RecordId && v.TenantId == tenantId
                && v.ControlImplementation.RegisteredSystemId == systemId && v.Status == SspSectionStatus.Approved, ct);
            if (version == null || ApprovedProfileDocumentData.Hash(version.SnapshotJson ?? version.Content) != pin.ContentHash)
                throw new InvalidOperationException("A changed control narrative lacks its exact retained approval.");
        }
    }

    internal static async Task VerifyEvidenceAsync(IServiceProvider services, RetainedPackageManifest manifest, Guid? personId, CancellationToken ct)
    {
        foreach (var evidence in manifest.Evidence)
        {
            if (personId is not Guid person) throw new UnauthorizedAccessException("A currently authorized mission requester is required for retained shared evidence.");
            await services.GetRequiredService<IProviderEvidenceSharingService>()
                .VerifyForExportAsync(manifest.TenantId, person, manifest.SystemId, evidence.ShareId, evidence.ContentHash, ct);
        }
    }

    internal static async Task RequireFileHashAsync(string path, string expectedHash, CancellationToken ct)
    {
        RequireHash(expectedHash);
        if (!File.Exists(path)) throw new InvalidOperationException("Retained artifact bytes are unavailable.");
        await using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
        if (!hash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Retained artifact bytes do not match the selected content hash.");
    }

    private static void RequireHash(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash) || hash.Length != 64 || !hash.All(Uri.IsHexDigit))
            throw new InvalidOperationException("Supply the exact SHA-256 content hash (64 hexadecimal characters).");
    }
}
