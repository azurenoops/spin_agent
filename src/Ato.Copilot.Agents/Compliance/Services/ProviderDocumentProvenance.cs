using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Workspaces;
using Ato.Copilot.Core.Services.ProviderAuthorizations;
using Microsoft.EntityFrameworkCore;
using static Ato.Copilot.Core.Services.ProviderAuthorizations.ProviderAuthorizationStore;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>Resolves metadata only through the mission's retained adoption pin; never reads private attachments.</summary>
internal static class ProviderDocumentProvenance
{
    internal sealed record DocumentProjection(string Content, IReadOnlyList<string> SourceGaps, IReadOnlyList<string> AccessGaps);
    internal sealed record Source(CapabilityAdoptionSnapshot Adoption, ProviderAuthorizationRevision Revision,
        CreateProviderDecisionRequest Decision, ProviderCapabilityRelease Release, ProviderCatalogContextSnapshot Context);
    private sealed record ReleasePin(string ReleaseSnapshotHash, long ReleaseRevision);
    private sealed record AdoptionMaterial(AdoptProviderCapabilityRequest Request, ReleasePin Selected);

    internal static async Task<DocumentProjection> ProjectDocumentAsync(AtoCopilotContext db, RegisteredSystem system, CancellationToken ct)
    {
        var sourceGaps = new List<string>();
        var sources = await ResolveAsync(db, system, sourceGaps, ct);
        var text = new StringBuilder();
        if (sources.Count == 0)
        {
            text.AppendLine("No verified adopted provider authorization source is available for this system.");
            foreach (var gap in sourceGaps) text.AppendLine(gap);
            return new(text.ToString(), sourceGaps, []);
        }
        var baseline = await db.ControlBaselines.AsNoTracking().SingleOrDefaultAsync(b =>
            b.RegisteredSystemId == system.Id && b.TenantId == system.TenantId, ct);
        var subscriptions = sources.Select(s => s.Adoption.SubscriptionId).Distinct().ToArray();
        var confirmations = baseline == null ? [] : await db.Set<CapabilityResponsibilityConfirmation>().AsNoTracking()
            .Where(c => c.TenantId == system.TenantId && c.RegisteredSystemId == system.Id && c.IsCurrent
                && c.ReviewedBaselineId == baseline.Id && subscriptions.Contains(c.SubscriptionId))
            .OrderBy(c => c.ControlId).ToListAsync(ct);
        var inScope = baseline?.ControlIds.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        foreach (var source in sources)
        {
            text.AppendLine($"### Reviewed provider source: {source.Decision.Reference}");
            text.AppendLine($"Record kind: {source.Decision.RecordKind}");
            if (!string.IsNullOrWhiteSpace(source.Decision.UpstreamProvider))
                text.AppendLine($"Upstream provider: {source.Decision.UpstreamProvider}");
            text.AppendLine($"Issuer: {source.Decision.IssuingAuthority}; issuer kind: {source.Decision.IssuingAuthorityType}");
            text.AppendLine($"Decision as stated: {source.Decision.DecisionAsStated}; issued: {source.Decision.IssuedOn}");
            if (source.Decision.EffectiveOn != null) text.AppendLine($"Effective: {source.Decision.EffectiveOn}");
            if (source.Decision.ExpiresOn != null) text.AppendLine($"Expires: {source.Decision.ExpiresOn}");
            text.AppendLine($"Expiry basis: {source.Decision.ExpiryBasis}");
            text.AppendLine($"Scope: {source.Decision.ScopeStatement}");
            text.AppendLine($"Conditions: {string.Join("; ", source.Decision.Conditions)}");
            text.AppendLine($"Selected release: {source.Release.Id}; revision: {source.Release.Revision}; SHA-256: {source.Release.SnapshotHash}");
            text.AppendLine($"Adoption: {source.Adoption.Id}; decision revision: {source.Revision.Id}; source SHA-256: {source.Revision.SnapshotHash}");
            text.AppendLine($"Publication context: {source.Context.Id}; SHA-256: {source.Context.SnapshotHash}");
            var matching = confirmations.Where(c => c.SubscriptionId == source.Adoption.SubscriptionId
                && string.Equals(c.SourceRevision, source.Release.SnapshotHash, StringComparison.OrdinalIgnoreCase)
                && inScope.Contains(c.ControlId)).ToArray();
            if (matching.Length == 0)
                text.AppendLine("Responsibility review gap: no current baseline-bound confirmation matches this selected release.");
            foreach (var confirmation in matching)
            {
                text.AppendLine($"Recorded responsibility confirmation — {confirmation.ControlId}: {confirmation.InheritanceType}");
                text.AppendLine($"Provider: {confirmation.Provider}; customer responsibility: {confirmation.CustomerResponsibility}");
                text.AppendLine($"Confirmed by: {confirmation.ConfirmedBy}; at: {confirmation.ConfirmedAt:O}; baseline: {confirmation.ReviewedBaselineId}");
                text.AppendLine($"Confirmation: {confirmation.Id}; reviewed source: {confirmation.SourceRevision}");
            }
            text.AppendLine();
        }
        const string evidenceGap = "Provider evidence access gap: this builder does not evaluate current summary-sharing grants. " +
            "No private source attachments are included. Use a permission-checked retained JSON preview to include approved summaries.";
        text.AppendLine(evidenceGap);
        foreach (var gap in sourceGaps) text.AppendLine(gap);
        return new(text.ToString(), sourceGaps, [evidenceGap]);
    }

    internal static async Task<List<Source>> ResolveAsync(AtoCopilotContext db, RegisteredSystem system,
        List<string> gaps, CancellationToken ct)
    {
        var subscriptions = await db.CapabilitySubscriptions.AsNoTracking()
            .Where(s => s.IsActive && s.RegisteredSystemId == system.Id && s.RoutingTenantId == system.TenantId)
            .OrderBy(s => s.Id).ToListAsync(ct);
        var ownedHistory = db.Set<CapabilityAdoptionSnapshot>().AsNoTracking()
            .Where(x => x.SystemId == system.Id && x.TenantId == system.TenantId);
        var historicalSubscriptionIds = await ownedHistory.Select(x => x.SubscriptionId).Distinct().ToListAsync(ct);
        var selectedIds = subscriptions.Where(s => s.CurrentAdoptionSnapshotId.HasValue)
            .Select(s => s.CurrentAdoptionSnapshotId!.Value).ToArray();
        var adoptions = await ownedHistory.Where(x => selectedIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var result = new List<Source>();
        foreach (var subscription in subscriptions)
        {
            if (subscription.CurrentAdoptionSnapshotId is not { } selectedId)
            {
                if (historicalSubscriptionIds.Contains(subscription.Id))
                    gaps.Add($"Provider authorization: subscription '{subscription.Id}' has no current mission adoption selected. Explicitly adopt a reviewed release; history alone cannot select one.");
                continue;
            }
            if (!adoptions.TryGetValue(selectedId, out var adoption) || adoption.SubscriptionId != subscription.Id)
            {
                gaps.Add($"Provider authorization: subscription '{subscription.Id}' has an unavailable or mismatched current adoption.");
                continue;
            }
            if (await db.Set<SystemProviderScopeSelection>().AsNoTracking().AnyAsync(x =>
                x.TenantId == system.TenantId && x.SystemId == system.Id
                && x.AssignmentId == adoption.AssignmentId && x.State == "Removed", ct))
            {
                gaps.Add($"Provider authorization: capability subscription '{subscription.Id}' retains adoption '{adoption.Id}' " +
                    "for a removed provider scope. Retire or reconcile that capability through its existing workflow; retained history is not a current document source.");
                continue;
            }
            try
            {
                result.AddRange(await ResolveAdoptionAsync(db, adoption, ct));
            }
            catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException)
            {
                gaps.Add($"Provider authorization: adoption '{adoption.Id}' cannot support export. {ex.Message}");
            }
        }
        return result;
    }

    private static async Task<List<Source>> ResolveAdoptionAsync(AtoCopilotContext db,
        CapabilityAdoptionSnapshot adoption, CancellationToken ct)
    {
        Require(Hash(adoption.SnapshotJson) == adoption.SnapshotHash, "The adoption digest does not match.");
        var retained = Read<AdoptionMaterial>(adoption.SnapshotJson);
        var pin = retained.Request;
        Require(pin != null && pin.ReleaseId == adoption.ReleaseId && pin.CapabilityId == adoption.CapabilityId
            && pin.AssignmentId == adoption.AssignmentId && pin.ExpectedAssignmentRevision == adoption.AssignmentRevision,
            "The retained adoption request does not match its release, capability or assignment.");
        var subscription = await db.CapabilitySubscriptions.AsNoTracking().SingleAsync(x => x.Id == adoption.SubscriptionId, ct);
        Require(Guid.TryParse(subscription.CspInheritedCapabilityId, out var subscribedCapability) && subscribedCapability == adoption.CapabilityId,
            "The subscription does not match the adopted capability.");
        var release = await db.ProviderCapabilityReleases.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == adoption.ReleaseId && x.CapabilityId == adoption.CapabilityId, ct);
        Require(release != null && retained.Selected != null
            && release.Revision == retained.Selected.ReleaseRevision
            && release.SnapshotHash == retained.Selected.ReleaseSnapshotHash,
            "The exact adopted release is unavailable or changed.");

        // Provider-owned metadata is accessible only via the tenant/system-scoped adoption above
        // and the exact provider/offering/context IDs. No current/latest-provider lookup is permitted.
        var context = await db.Set<ProviderCatalogContextSnapshot>().IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == adoption.ContextSnapshotId && x.ProviderId == adoption.ProviderId
                && x.OfferingId == adoption.OfferingId && x.ReleaseId == adoption.ReleaseId
                && x.CapabilityId == adoption.CapabilityId, ct);
        Require(context != null && Hash(context.SnapshotJson) == context.SnapshotHash
            && context.SnapshotHash == pin!.ContextSnapshotHash, "The pinned publication context is unavailable or changed.");
        var material = Read<ProviderPublicationContextMaterial>(context!.SnapshotJson);
        Require(material.OfferingId == adoption.OfferingId && material.AuthorizationRevisions is { Count: > 0 },
            "The pinned publication context has no recorded authorization source.");
        var result = new List<Source>();
        foreach (var reference in material.AuthorizationRevisions)
        {
            var revision = await db.Set<ProviderAuthorizationRevision>().IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == reference.RevisionId && x.RecordId == reference.RecordId
                    && x.ProviderId == adoption.ProviderId && x.OfferingId == adoption.OfferingId, ct);
            Require(revision != null && revision.SnapshotHash == reference.SnapshotHash
                && revision.BoundaryRevisionId == material.BoundaryRevisionId, "The pinned decision revision is unavailable or changed.");
            var source = Read<CreateProviderDecisionRequest>(revision!.SnapshotJson);
            Require(!string.IsNullOrWhiteSpace(source.Reference) && !string.IsNullOrWhiteSpace(source.IssuingAuthority)
                && source.IssuingAuthorityType is "person" or "organization"
                && DateOnly.TryParseExact(source.IssuedOn, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
                "Record the source title, issuing authority, authority type and issue date; export cannot invent them.");
            var events = await db.Set<ProviderAuthorizationLifecycleEvent>().IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.ProviderId == adoption.ProviderId && x.OfferingId == adoption.OfferingId
                    && x.RecordId == revision.RecordId).ToListAsync(ct);
            Require(source.RecordKind is "ProviderDecision" or "InheritedMicrosoftReference" or "InheritedProviderReference", "The recorded source kind is unsupported.");
            var blocker = ProviderDecisionEligibility.Evaluate(revision, events, source.RecordKind);
            Require(blocker == null, blocker?.Message ?? "");
            result.Add(new(adoption, revision, source, release!, context));
        }
        return result;
    }

    internal static void AppendOscal(List<Source> sources, Dictionary<string, object> metadata,
        Dictionary<string, object> implementation)
    {
        if (sources.Count == 0) return;
        var parties = metadata.TryGetValue("parties", out var recordedParties) && recordedParties is List<Dictionary<string, object>> existing
            ? existing : new List<Dictionary<string, object>>();
        var authorizations = new List<Dictionary<string, object>>();
        foreach (var source in sources)
        {
            // Document-local identity for the recorded issuer, not an invented external registry identity.
            var issuerId = StableId($"issuer:{source.Revision.Id}");
            if (!parties.Any(x => (string)x["uuid"] == issuerId))
                parties.Add(new() { ["uuid"] = issuerId, ["type"] = source.Decision.IssuingAuthorityType!,
                    ["name"] = source.Decision.IssuingAuthority! });
            var props = new List<Dictionary<string, string>>
            {
                Prop("record-kind", source.Decision.RecordKind),
                Prop("decision-as-stated", source.Decision.DecisionAsStated!),
                Prop("decision-revision-id", source.Revision.Id.ToString()),
                Prop("decision-snapshot-hash", source.Revision.SnapshotHash),
                Prop("adoption-id", source.Adoption.Id.ToString()),
                Prop("release-id", source.Adoption.ReleaseId.ToString()),
                Prop("release-revision", source.Release.Revision.ToString(CultureInfo.InvariantCulture)),
                Prop("release-snapshot-hash", source.Release.SnapshotHash),
                Prop("context-snapshot-id", source.Adoption.ContextSnapshotId.ToString()),
                Prop("offering-id", source.Adoption.OfferingId.ToString())
            };
            if (source.Decision.EffectiveOn != null) props.Add(Prop("effective-on", source.Decision.EffectiveOn));
            if (!string.IsNullOrWhiteSpace(source.Decision.UpstreamProvider)) props.Add(Prop("upstream-provider", source.Decision.UpstreamProvider));
            if (source.Decision.ExpiresOn != null) props.Add(Prop("expires-on", source.Decision.ExpiresOn));
            authorizations.Add(new()
            {
                ["uuid"] = StableId($"authorization:{source.Adoption.Id}:{source.Revision.Id}"),
                ["title"] = source.Decision.Reference,
                ["party-uuid"] = issuerId,
                ["date-authorized"] = source.Decision.IssuedOn!,
                ["props"] = props,
                ["remarks"] = string.Join("\n", new[] { source.Decision.ScopeStatement }.Concat(source.Decision.Conditions))
            });
        }
        metadata["parties"] = parties;
        implementation["leveraged-authorizations"] = authorizations;
    }

    private static Dictionary<string, string> Prop(string name, string value) =>
        new() { ["name"] = name, ["ns"] = "https://ato-copilot.io/ns/provider-provenance", ["value"] = value };

    private static string StableId(string value)
    {
        // RFC 4122 UUIDv5 in the URL namespace: SHA-1 is an identity algorithm here,
        // not an integrity check. Retained content is independently checked with SHA-256.
        var ns = new Guid("6ba7b811-9dad-11d1-80b4-00c04fd430c8").ToByteArray(bigEndian: true);
        var bytes = SHA1.HashData(ns.Concat(Encoding.UTF8.GetBytes($"urn:ato-copilot:{value}")).ToArray())[..16];
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes, bigEndian: true).ToString();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
