using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Services;

/// <summary>Stages provider revisions alongside source mutations, without opening any customer scope.</summary>
public static class CspResponsibilitySourceTracker
{
    public static string Snapshot(CspInheritedCapability? capability) => capability is null ? "null" :
        JsonSerializer.Serialize(new
        {
            capability.Id, capability.Name, capability.Description, capability.Status,
            Controls = capability.MappedNistControlIds.Select(c => c.ToUpperInvariant()).Distinct().Order(StringComparer.Ordinal),
            Component = new { capability.CspInheritedComponentId, capability.CspInheritedComponent.CspProfileId,
                capability.CspInheritedComponent.Name, capability.CspInheritedComponent.Description,
                capability.CspInheritedComponent.Status, capability.CspInheritedComponent.SourceArtifactReference }
        });

    /// <summary>Display captured content without exposing storage paths, signed URLs or artifact credentials.</summary>
    public static string RedactSnapshot(string snapshotJson)
    {
        if (JsonNode.Parse(snapshotJson) is not JsonObject snapshot || snapshot["Component"] is not JsonObject component)
            throw new InvalidDataException("The stored provider responsibility snapshot has an invalid structure.");
        if (component["SourceArtifactReference"] is not null)
            component["SourceArtifactReference"] = "[redacted]";
        return snapshot.ToJsonString();
    }

    public static string Revision(string snapshot) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot)));

    public static async Task StageAsync(AtoCopilotContext db, string actor, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if (actor.Length > 200) throw new ArgumentException("Provider event actor exceeds 200 characters.", nameof(actor));
        db.ChangeTracker.DetectChanges();
        var componentIds = db.ChangeTracker.Entries<CspInheritedComponent>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(e => e.Entity.Id).ToArray();
        var capabilities = await db.CspInheritedCapabilities.Where(c => componentIds.Contains(c.CspInheritedComponentId)).ToListAsync(ct);
        capabilities.AddRange(db.ChangeTracker.Entries<CspInheritedCapability>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).Select(e => e.Entity));
        foreach (var capability in capabilities.DistinctBy(c => c.Id))
        {
            var component = db.CspInheritedComponents.Local.FirstOrDefault(c => c.Id == capability.CspInheritedComponentId)
                ?? await db.CspInheritedComponents.SingleAsync(c => c.Id == capability.CspInheritedComponentId, ct);
            capability.CspInheritedComponent = component;
            var deleted = db.Entry(capability).State == EntityState.Deleted;
            var revision = Revision(Snapshot(deleted ? null : capability));
            var latest = await db.Set<CspResponsibilitySourceEvent>().Where(e => e.CapabilityId == capability.Id)
                .OrderByDescending(e => e.Sequence).FirstOrDefaultAsync(ct);
            if (latest?.SourceRevision == revision && !string.IsNullOrWhiteSpace(latest.Actor))
                continue;
            db.Set<CspResponsibilitySourceEvent>().Add(new()
            {
                CapabilityId = capability.Id, ComponentId = component.Id, CspProfileId = component.CspProfileId,
                SourceRevision = revision, Sequence = (latest?.Sequence ?? 0) + 1, Actor = actor,
                IsAvailable = !deleted && capability.Status == CspInheritedCapabilityStatus.Mapped
                    && component.Status == CspInheritedComponentStatus.Published
            });
        }
    }
}
