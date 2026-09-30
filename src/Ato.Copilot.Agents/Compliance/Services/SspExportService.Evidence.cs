using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Ato.Copilot.Agents.Compliance.Services;

public partial class SspExportService
{
    private async Task<OscalExportResult> AddFinalDocumentSourcesAsync(
        string systemId, SspExport? export, OscalExportResult generated, CancellationToken ct)
    {
        if (_evidenceSharing == null && _responsibilities == null) return generated;
        if (export?.SourceTenantId is not Guid tenantId || tenantId == Guid.Empty
            || export.RequestedPersonId is not Guid personId || personId == Guid.Empty)
            throw new UnauthorizedAccessException("Final document sources require a captured mission tenant and requester.");

        // Queue consumers have no HTTP workspace. Restore only server-captured identity in a
        // fresh scope; the existing source services reauthorize the requester's current access.
        using var scope = _scopeFactory.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>() as TenantContext
            ?? throw new UnauthorizedAccessException("Final document source workspace cannot be established.");
        tenant.TenantId = tenantId;
        tenant.PersonId = personId;
        tenant.IsWorkspaceRequest = true;
        using var context = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().Push(tenant);
        var responsibilities = _responsibilities == null ? null
            : scope.ServiceProvider.GetRequiredService<ICapabilityResponsibilityService>();
        var evidence = _evidenceSharing == null ? null
            : scope.ServiceProvider.GetRequiredService<IProviderEvidenceSharingService>();
        generated = await AddResponsibilitiesAsync(systemId, generated, ct, responsibilities);
        return await AddProviderEvidenceAsync(systemId, generated, ct, evidence);
    }

    private async Task<OscalExportResult> AddResponsibilitiesAsync(string systemId, OscalExportResult generated,
        CancellationToken ct, ICapabilityResponsibilityService? responsibilities = null)
    {
        responsibilities ??= _responsibilities;
        if (responsibilities == null) return generated;
        var reviewed = await responsibilities.PreviewAsync(systemId, ct);
        var pins = reviewed.Items.Select(item => new DocumentResponsibilityReference(
            reviewed.BaselineId, item.SubscriptionId, item.CapabilityId, item.ControlId, item.State, item.SourceRevision,
            item.ReviewRevision, item.ReviewedSourceRevision, item.Allocation?.InheritanceType, item.Allocation?.Provider,
            item.Allocation?.CustomerResponsibility, item.ConfirmedBy, item.ConfirmedAt)).ToArray();
        var manifest = (generated.SourceManifest ?? new DocumentSourceManifest("GeneratedOscalContent", [], [])) with
        {
            Responsibilities = pins
        };
        var root = JsonNode.Parse(generated.OscalJson)!.AsObject();
        var implementation = root["system-security-plan"]!["system-implementation"]!.AsObject();
        if (pins.Length > 0)
        {
            var props = implementation["props"] as JsonArray ?? new JsonArray();
            if (implementation["props"] == null) implementation["props"] = props;
            foreach (var pin in pins)
                props.Add(new JsonObject
                {
                    ["name"] = "capability-responsibility",
                    ["ns"] = "https://ato-copilot.io/ns/document",
                    ["value"] = JsonSerializer.Serialize(pin)
                });
        }
        EmbedManifest(root["system-security-plan"]!.AsObject(), manifest);
        return generated with
        {
            OscalJson = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), SourceManifest = manifest
        };
    }

    private async Task<OscalExportResult> AddProviderEvidenceAsync(string systemId, OscalExportResult generated,
        CancellationToken ct, IProviderEvidenceSharingService? evidence = null)
    {
        evidence ??= _evidenceSharing;
        if (evidence == null) return generated;
        var root = JsonNode.Parse(generated.OscalJson)!.AsObject();
        var ssp = root["system-security-plan"]!.AsObject();
        var backMatter = ssp["back-matter"] as JsonObject ?? new JsonObject();
        var resources = backMatter["resources"] as JsonArray ?? new JsonArray();
        if (backMatter["resources"] == null) backMatter["resources"] = resources;
        if (ssp["back-matter"] == null) ssp["back-matter"] = backMatter;
        var pins = new List<DocumentEvidenceReference>();
        var gaps = new List<string>();
        try
        {
            for (var page = 1; ; page++)
            {
                var shares = await evidence.ListMissionAsync(systemId, page, 100, ct);
                foreach (var share in shares.Items)
                {
                    var content = await evidence.SummaryContentAsync(systemId, share.ShareId, ct);
                    if (!Convert.ToHexString(SHA256.HashData(content)).Equals(share.ContentHash, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Approved evidence summary changed between listing and capture.");
                    pins.Add(new(share.ShareId, share.EvidenceId, share.Version, share.PreviousVersionId, share.ContentHash,
                        share.SourceSha256, share.EvidenceRevision, share.AssignmentId, share.AssignmentRevision,
                        share.ApprovedBy, share.ApprovedAt));
                    resources.Add(new JsonObject
                    {
                        ["uuid"] = share.ShareId.ToString(),
                        ["title"] = $"Approved evidence summary version {share.Version}",
                        ["description"] = share.Summary,
                        ["base64"] = new JsonObject
                        {
                            ["filename"] = $"approved-summary-{share.ShareId}.json",
                            ["media-type"] = "application/json",
                            ["value"] = Convert.ToBase64String(content)
                        }
                    });
                }
                if (shares.Items.Count < 100 || page * 100 >= shares.Total) break;
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or KeyNotFoundException or IOException)
        {
            gaps.Add("Approved provider evidence summaries could not be verified. Recheck the mission sharing grant and create a fresh preview.");
        }
        var manifest = (generated.SourceManifest ?? new DocumentSourceManifest("GeneratedOscalContent", [], [])) with
        {
            Evidence = pins,
            EvidenceStatus = gaps.Count == 0 ? "EvaluatedApprovedSummariesOnly" : "Unavailable"
        };
        if (resources.Count == 0) ssp.Remove("back-matter");
        EmbedManifest(ssp, manifest);
        return generated with
        {
            OscalJson = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            SourceManifest = manifest, EvidenceSourceGaps = gaps,
            Statistics = generated.Statistics with { BackMatterResourceCount = generated.Statistics.BackMatterResourceCount + pins.Count }
        };
    }

    private static void EmbedManifest(JsonObject ssp, DocumentSourceManifest manifest)
    {
        var metadata = ssp["metadata"]!.AsObject();
        var properties = metadata["props"] as JsonArray ?? new JsonArray();
        if (metadata["props"] == null) metadata["props"] = properties;
        var existing = properties.OfType<JsonObject>().SingleOrDefault(p => p["name"]?.GetValue<string>() == "source-version-manifest");
        if (existing != null) existing["value"] = JsonSerializer.Serialize(manifest);
        else properties.Add(new JsonObject
        {
            ["name"] = "source-version-manifest", ["ns"] = "https://ato-copilot.io/ns/document",
            ["value"] = JsonSerializer.Serialize(manifest)
        });
    }

    private async Task ValidateCurrentEvidenceAsync(string? manifestJson, string systemId, CancellationToken ct)
    {
        var pins = manifestJson == null ? [] : JsonSerializer.Deserialize<DocumentSourceManifest>(manifestJson)?.Evidence ?? [];
        foreach (var pin in pins)
        {
            if (_evidenceSharing == null) throw new UnauthorizedAccessException("Approved evidence summary access cannot be verified.");
            var content = await _evidenceSharing.SummaryContentAsync(systemId, pin.ShareId, ct);
            if (!Convert.ToHexString(SHA256.HashData(content)).Equals(pin.ContentHash, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Retained summary pin does not match the current approved sharing grant.");
        }
    }

    private async Task ValidateWorkerEvidenceAsync(SspExport export, CancellationToken ct)
    {
        var pins = export.SourceManifestJson == null ? [] :
            JsonSerializer.Deserialize<DocumentSourceManifest>(export.SourceManifestJson)?.Evidence ?? [];
        foreach (var pin in pins)
        {
            if (_evidenceSharing == null || export.SourceTenantId is not Guid tenantId || export.RequestedPersonId is not Guid personId)
                throw new UnauthorizedAccessException("Retained evidence export has no verifiable requesting mission identity.");
            await _evidenceSharing.VerifyForExportAsync(tenantId, personId, export.SystemId, pin.ShareId, pin.ContentHash, ct);
        }
    }
}
