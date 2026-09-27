using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Agents.Compliance.Services;

internal static class ApprovedNarrativeDocumentData
{
    /// <summary>Applies approved content only to detached export projections, never to tracked working drafts.</summary>
    internal static async Task<List<DocumentSourceReference>> ApplyAsync(AtoCopilotContext db,
        List<ControlImplementation> implementations, CancellationToken ct)
    {
        var ids = implementations.Select(x => x.ApprovedVersionId).OfType<string>().ToArray();
        var versions = await db.Set<NarrativeVersion>().AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var sources = new List<DocumentSourceReference>();
        foreach (var implementation in implementations.Where(x => x.ApprovedVersionId != null))
        {
            if (!versions.TryGetValue(implementation.ApprovedVersionId!, out var version)
                || version.ControlImplementationId != implementation.Id || version.Status != SspSectionStatus.Approved)
                throw new InvalidOperationException($"Approved narrative version for {implementation.ControlId} is unavailable.");
            if (version.SnapshotJson != null)
                NarrativeContentSnapshot.Restore(implementation, version.SnapshotJson);
            else
            {
                // Legacy approval retained only the combined narrative. Do not borrow newer draft halves.
                implementation.Narrative = version.Content;
                implementation.PolicyNarrative = null;
                implementation.TechnicalNarrative = null;
            }
            sources.Add(new("ApprovedNarrative", implementation.Id, version.Id,
                ApprovedProfileDocumentData.Hash(version.SnapshotJson ?? version.Content)));
        }
        return sources;
    }
}
