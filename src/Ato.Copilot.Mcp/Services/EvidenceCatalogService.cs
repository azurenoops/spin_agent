using System.Data.Common;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Interfaces.Storage;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Mcp.Services;

public sealed record EvidenceCatalogControl(string ControlId, string? Title, string Kind);
public sealed record EvidenceCatalogRow(string Id, string RecordId, string Source, string Name,
    string SourceLabel, DateTimeOffset? RecordedAt, string? Category,
    IReadOnlyList<EvidenceCatalogControl> Controls, bool LinksKnown);
public sealed record EvidenceCatalogSource(string Source, string State, string? Message);
public sealed record EvidenceCatalogCounts(int? All, int? System, int? Provider, int? MissingLinks);
public sealed record EvidenceCatalogPermissions(bool CanUpload, string? UploadReason,
    bool CanManageEvidence, string? ManageReason, bool CanManageLinks, string? LinkReason);
public sealed record EvidenceCatalogList(string SystemId, IReadOnlyList<EvidenceCatalogRow> Items,
    int? TotalCount, int AvailableCount, int Page, int PageSize, EvidenceCatalogCounts Counts,
    IReadOnlyList<EvidenceCatalogSource> Sources, EvidenceCatalogPermissions Permissions);
public sealed record EvidenceCatalogActions(bool CanDownload, string? DownloadUrl, bool CanReplace,
    bool CanDelete, bool CanCollect, bool CanLink, string? ManageReason, string? LinkReason);
public sealed record EvidenceCatalogProvenance(string Label, string Value);
public sealed record EvidenceCatalogHistory(string Id, string Label, DateTimeOffset? At,
    string? Actor, string? FileName, string? DownloadUrl);
public sealed record EvidenceCatalogDetail(string SystemId, EvidenceCatalogRow Item, string? Description,
    string? Owner, string? RecordedBy, string? Version, string? ContentHash, string? ContentType,
    long? FileSizeBytes, string Availability, string? AvailabilityReason, string? Summary,
    string? Review, string? Currency, string? Relevance, IReadOnlyList<EvidenceCatalogProvenance> Provenance,
    IReadOnlyList<EvidenceCatalogHistory> History, EvidenceCatalogActions Permissions);
public sealed record EvidenceCatalogQuery(string View = "all", string? Search = null, string? Family = null,
    string? Category = null, string? Source = null, DateTimeOffset? DateFrom = null, DateTimeOffset? DateTo = null,
    string SortBy = "recordedAt", string SortOrder = "desc", int Page = 1, int PageSize = 50);

/// <summary>A read projection over the existing evidence stores; never expands a provider grant.</summary>
public sealed class EvidenceCatalogService(AtoCopilotContext db, IProviderEvidenceSharingService provider,
    IFileStorageProvider storage, ILogger<EvidenceCatalogService> logger)
{
    public static IQueryable<ComplianceEvidence> ScopedAutomated(AtoCopilotContext db, string systemId) =>
        db.Evidence.Where(e => db.Assessments.Any(a => a.Id == e.AssessmentId
            && a.RegisteredSystemId == systemId && a.TenantId == e.TenantId));

    public async Task<EvidenceCatalogList> ListAsync(string systemId, EvidenceCatalogQuery query,
        EvidenceCatalogPermissions permissions, CancellationToken ct)
    {
        var rows = new List<EvidenceCatalogRow>();
        var sources = new List<EvidenceCatalogSource>();
        try
        {
            rows.AddRange(await SystemRowsAsync(systemId, ct));
            sources.Add(new("system", "available", null));
        }
        catch (Exception ex) when (IsOperational(ex))
        {
            logger.LogWarning(ex, "System evidence catalog source failed for {SystemId}", systemId);
            sources.Add(new("system", "unavailable", "System evidence is unavailable. Retry the request."));
        }
        try
        {
            rows.AddRange((await ProviderRowsAsync(systemId, ct)).Select(ProviderRow));
            sources.Add(new("provider", "available", null));
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Provider evidence catalog source denied for {SystemId}", systemId);
            sources.Add(new("provider", "denied", "Provider summaries are not accessible in this workspace."));
        }
        catch (Exception ex) when (IsOperational(ex))
        {
            logger.LogWarning(ex, "Provider evidence catalog source failed for {SystemId}", systemId);
            sources.Add(new("provider", "unavailable", "Provider summaries are unavailable. Retry the request."));
        }

        var filtered = rows.DistinctBy(r => r.Id).Where(r =>
            (string.IsNullOrWhiteSpace(query.Search) || r.Name.Contains(query.Search.Trim(), StringComparison.OrdinalIgnoreCase)
                || r.Controls.Any(c => c.ControlId.Contains(query.Search.Trim(), StringComparison.OrdinalIgnoreCase)
                    || c.Title?.Contains(query.Search.Trim(), StringComparison.OrdinalIgnoreCase) == true))
            && (string.IsNullOrWhiteSpace(query.Family) || r.Controls.Any(c =>
                c.ControlId.Split('-')[0].Equals(query.Family.Trim(), StringComparison.OrdinalIgnoreCase)))
            && (string.IsNullOrWhiteSpace(query.Category) || string.Equals(r.Category, query.Category, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(query.Source) || r.Source.Equals(query.Source, StringComparison.OrdinalIgnoreCase))
            && (!query.DateFrom.HasValue || r.RecordedAt >= query.DateFrom)
            && (!query.DateTo.HasValue || r.RecordedAt <= query.DateTo)).ToList();
        var systemKnown = sources[0].State == "available"
            || string.Equals(query.Source, "Provider", StringComparison.OrdinalIgnoreCase);
        var providerKnown = sources[1].State == "available"
            || (!string.IsNullOrWhiteSpace(query.Source) && !query.Source.Equals("Provider", StringComparison.OrdinalIgnoreCase))
            || !string.IsNullOrWhiteSpace(query.Family) || !string.IsNullOrWhiteSpace(query.Category);
        var counts = new EvidenceCatalogCounts(
            systemKnown && providerKnown ? filtered.Count : null,
            systemKnown ? filtered.Count(r => r.Source != "Provider") : null,
            providerKnown ? filtered.Count(r => r.Source == "Provider") : null,
            systemKnown ? filtered.Count(r => r.Source != "Provider" && r.LinksKnown && r.Controls.Count == 0) : null);
        var selected = filtered.Where(r => query.View switch
        {
            "system" => r.Source != "Provider",
            "provider" => r.Source == "Provider",
            _ => true
        }).ToList();
        var descending = query.SortOrder != "asc";
        IOrderedEnumerable<EvidenceCatalogRow> sorted = query.SortBy.ToLowerInvariant() switch
        {
            "name" or "filename" => descending ? selected.OrderByDescending(r => r.Name, StringComparer.OrdinalIgnoreCase)
                : selected.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
            "category" => descending ? selected.OrderByDescending(r => r.Category, StringComparer.OrdinalIgnoreCase)
                : selected.OrderBy(r => r.Category, StringComparer.OrdinalIgnoreCase),
            "source" => descending ? selected.OrderByDescending(r => r.Source) : selected.OrderBy(r => r.Source),
            _ => descending ? selected.OrderByDescending(r => r.RecordedAt) : selected.OrderBy(r => r.RecordedAt)
        };
        return new(systemId, sorted.ThenBy(r => r.Id, StringComparer.Ordinal)
            .Skip((int)Math.Min((long)(query.Page - 1) * query.PageSize, int.MaxValue)).Take(query.PageSize).ToArray(),
            query.View switch { "system" => counts.System, "provider" => counts.Provider, _ => counts.All },
            selected.Count, query.Page, query.PageSize, counts, sources, permissions);
    }

    private async Task<List<EvidenceCatalogRow>> SystemRowsAsync(string systemId, CancellationToken ct)
    {
        var implementations = await db.ControlImplementations.AsNoTracking()
            .Where(c => c.RegisteredSystemId == systemId).ToListAsync(ct);
        var implementationIds = implementations.Select(c => c.Id).ToArray();
        var mappings = await db.CapabilityControlMappings.AsNoTracking()
            .Where(m => m.RegisteredSystemId == systemId || m.RegisteredSystemId == null).ToListAsync(ct);
        var links = await db.ControlValidationLinks.AsNoTracking()
            .Where(l => implementationIds.Contains(l.ControlImplementationId)
                && l.LinkType == ControlValidationLinkType.EvidenceArtifact).ToListAsync(ct);
        var titles = await db.NistControls.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Title, ct);
        EvidenceCatalogControl Control(string id, string kind) =>
            new(id, titles.GetValueOrDefault(id), kind);
        var artifacts = await db.EvidenceArtifacts.AsNoTracking()
            .Where(a => a.RegisteredSystemId == systemId && !a.IsDeleted).ToListAsync(ct);
        var rows = artifacts.Select(a =>
        {
            var controls = new List<EvidenceCatalogControl>();
            var direct = implementations.Find(c => c.Id == a.ControlImplementationId);
            if (direct is not null) controls.Add(Control(direct.ControlId, "direct"));
            if (a.SecurityCapabilityId is not null)
                controls.AddRange(mappings.Where(m => m.SecurityCapabilityId == a.SecurityCapabilityId
                    && implementations.Any(c => c.ControlId == m.ControlId)).Select(m => Control(m.ControlId, "capability")));
            controls.AddRange(links.Where(l => l.LinkTarget == a.Id || l.LinkTarget == $"evidence://{a.Id}"
                    || l.LinkTarget == $"artifact:{a.Id}")
                .Select(l => Control(implementations.Single(c => c.Id == l.ControlImplementationId).ControlId, "validation")));
            return new EvidenceCatalogRow($"artifact:{a.Id}", a.Id, "Manual", a.FileName, "System upload",
                RecordedDate(a.UploadedAt), a.ArtifactCategory.ToString(),
                controls.DistinctBy(c => c.ControlId, StringComparer.OrdinalIgnoreCase).OrderBy(c => c.ControlId).ToArray(), true);
        }).ToList();
        var automated = await ScopedAutomated(db, systemId).AsNoTracking().ToListAsync(ct);
        rows.AddRange(automated.Select(e => new EvidenceCatalogRow($"automated:{e.Id}", e.Id, "Automated",
            NullIfEmpty(e.Description) ?? NullIfEmpty(e.EvidenceType) ?? "Automated evidence", "Automated collection",
            RecordedDate(e.CollectedAt), e.EvidenceCategory.ToString(), [Control(e.ControlId, "automated")], true)));
        return rows;
    }

    private async Task<List<ProviderEvidenceShareResponse>> ProviderRowsAsync(string systemId, CancellationToken ct)
    {
        var result = new List<ProviderEvidenceShareResponse>();
        for (var page = 1; ; page++)
        {
            var batch = await provider.ListMissionAsync(systemId, page, 100, ct);
            result.AddRange(batch.Items.Where(r => r.SystemId == systemId && r.RevokedAt is null));
            if (batch.Items.Count == 0 || page * 100 >= batch.Total) break;
        }
        return result;
    }

    private static EvidenceCatalogRow ProviderRow(ProviderEvidenceShareResponse r) =>
        new($"provider:{r.ShareId:D}", r.ShareId.ToString("D"), "Provider", r.Summary,
            "Approved provider summary", r.ApprovedAt == default ? null : r.ApprovedAt, null, [], false);

    public async Task<EvidenceCatalogDetail?> DetailAsync(string systemId, string id,
        EvidenceCatalogPermissions permissions, CancellationToken ct)
    {
        if (id.StartsWith("provider:", StringComparison.Ordinal)
            && Guid.TryParse(id["provider:".Length..], out var shareId))
        {
            var share = (await ProviderRowsAsync(systemId, ct)).SingleOrDefault(r => r.ShareId == shareId);
            if (share is null) return null;
            // Recheck grant and retained-summary integrity, not private source attachment state.
            await provider.SummaryContentAsync(systemId, shareId, ct);
            return new(systemId, ProviderRow(share), null, null, share.ApprovedBy, share.Version.ToString(),
                share.ContentHash, "application/json", null, "SummaryOnly", "Only the approved summary is shared.",
                share.Summary, null, null, null,
                [new("Permission", share.Permission), new("Approved by", share.ApprovedBy),
                    new("Assignment", share.AssignmentId.ToString())],
                [new(share.ShareId.ToString(), "Summary approved", share.ApprovedAt == default ? null : share.ApprovedAt, share.ApprovedBy, null, null)],
                new(true, $"/api/dashboard/systems/{Uri.EscapeDataString(systemId)}/provider-evidence/{shareId:D}/content",
                    false, false, false, false, "Provider summaries are managed by their provider.",
                    "Provider summaries cannot be linked as system artifacts."));
        }
        var row = (await SystemRowsAsync(systemId, ct)).SingleOrDefault(r => r.Id == id);
        if (row is null) return null;
        if (row.Source == "Automated")
        {
            var e = await ScopedAutomated(db, systemId).AsNoTracking().SingleAsync(e => e.Id == row.RecordId, ct);
            var system = await db.RegisteredSystems.AsNoTracking().SingleAsync(s => s.Id == systemId, ct);
            var hasSubscription = system.AzureProfile?.SubscriptionIds?.Any(s => !string.IsNullOrWhiteSpace(s)) == true;
            var hasControl = await db.ControlImplementations.AnyAsync(c =>
                c.RegisteredSystemId == systemId && c.ControlId == e.ControlId, ct);
            var collectReason = permissions.ManageReason
                ?? (!hasSubscription ? "Configure a subscription for this system before collecting evidence."
                    : !hasControl ? "The control is no longer implemented in this system." : null);
            return new(systemId, row, e.Description, null, NullIfEmpty(e.CollectedBy), null,
                NullIfEmpty(e.ContentHash), null, null, "Unknown", "No downloadable file is recorded.",
                null, null, null, null,
                NullIfEmpty(e.CollectionMethod) is { } method ? [new("Collection method", method)] : [],
                [new(e.Id, "Evidence collected", RecordedDate(e.CollectedAt), NullIfEmpty(e.CollectedBy), null, null)],
                new(false, null, false, false, collectReason is null, false,
                    collectReason ?? "Automated records cannot be replaced or deleted here.",
                    "Only uploaded system artifacts support validation links."));
        }
        var artifact = await db.EvidenceArtifacts.AsNoTracking().Include(a => a.Versions)
            .SingleAsync(a => a.Id == row.RecordId && a.RegisteredSystemId == systemId && !a.IsDeleted, ct);
        var available = false;
        var availability = "Unavailable";
        var reason = "The stored file is no longer available.";
        try
        {
            available = await storage.ExistsAsync(artifact.StoragePath, ct);
            if (available) { availability = "FileAvailable"; reason = null; }
        }
        catch (Exception ex) when (IsOperational(ex) || ex is UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Evidence file availability check failed for {EvidenceId}", artifact.Id);
            reason = "File availability could not be checked. Retry the request.";
        }
        var root = $"/api/dashboard/systems/{Uri.EscapeDataString(systemId)}/evidence/{Uri.EscapeDataString(artifact.Id)}";
        return new(systemId, row, artifact.Description, null, NullIfEmpty(artifact.UploadedBy), null,
            NullIfEmpty(artifact.ContentHash), NullIfEmpty(artifact.ContentType), artifact.FileSizeBytes, availability, reason,
            null, null, null, null, [new("Collection method", artifact.CollectionMethod.ToString())],
            artifact.Versions.OrderByDescending(v => v.ReplacedAt).Select(v =>
                new EvidenceCatalogHistory(v.Id, "Previous file replaced", RecordedDate(v.ReplacedAt), NullIfEmpty(v.ReplacedBy),
                    v.FileName, v.IsFilePurged ? null : $"{root}/versions/{Uri.EscapeDataString(v.Id)}/download"))
                .Append(new(artifact.Id, "Uploaded", RecordedDate(artifact.UploadedAt), NullIfEmpty(artifact.UploadedBy), null, null)).ToArray(),
            new(available, available ? $"{root}/download" : null, permissions.CanManageEvidence,
                permissions.CanManageEvidence, false, permissions.CanManageLinks, permissions.ManageReason, permissions.LinkReason));
    }

    public static bool IsOperational(Exception ex) =>
        ex is IOException or HttpRequestException or TimeoutException or DbException or Azure.RequestFailedException;
    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static DateTimeOffset? RecordedDate(DateTime value) =>
        value == default ? null : new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
