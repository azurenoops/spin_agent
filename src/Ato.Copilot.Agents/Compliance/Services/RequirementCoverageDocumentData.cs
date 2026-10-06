using System.Text;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>Source-qualified document projection; never changes persisted narratives or approval state.</summary>
internal static class RequirementCoverageDocumentData
{
    internal sealed record Control(string SelectedId, CatalogRequirementControl? Source,
        ControlImplementation? Implementation, RequirementCoverageSnapshot? Snapshot,
        IReadOnlyList<RequirementResponse> Responses, IReadOnlyDictionary<string, string> Parameters, IReadOnlyList<string> Gaps);
    internal sealed record Projection(BaselineCatalogBinding? Binding, IReadOnlyList<Control> Controls,
        IReadOnlyList<string> Gaps, IReadOnlyList<DocumentSourceReference> Sources);

    internal static async Task<Projection> LoadAsync(AtoCopilotContext db, string systemId,
        ControlBaseline? baseline, IReadOnlyList<ControlImplementation> implementations, CancellationToken ct,
        bool evaluateCurrent = false)
    {
        var system = await db.RegisteredSystems.AsNoTracking().SingleAsync(x => x.Id == systemId, ct);
        var binding = baseline is null ? null : await CatalogSourceReader.ReadBindingAsync(db,
            x => x.Id == baseline.RequirementCatalogBindingId
                && x.ControlBaselineId == baseline.Id && x.TenantId == system.TenantId, ct);
        var gaps = new List<string>();
        if (baseline is null)
            gaps.Add("No control baseline found. Select a baseline and reconcile its catalog source.");
        var sources = new List<DocumentSourceReference>();
        RequirementCatalog? catalog = null;
        if (binding is null)
            gaps.Add("Catalog source needs reconciliation; no source control or statement references can be verified.");
        else if (RequirementCoverageService.Hash(binding.CatalogJson) != binding.ContentHash)
            gaps.Add("Catalog source integrity check failed.");
        else
        {
            try { catalog = RequirementCatalog.Parse(binding.CatalogJson); }
            catch (Exception ex) when (ex is JsonException or ArgumentException)
            { gaps.Add($"Catalog source is invalid: {ex.Message}"); }
            sources.Add(new("RequirementCatalog", binding.Id, binding.CatalogVersion, binding.ContentHash));
            if (!Uri.TryCreate(binding.SourceUri, UriKind.Absolute, out _))
                gaps.Add("Catalog source URI is unavailable or invalid.");
        }
        var evidence = await db.EvidenceArtifacts.AsNoTracking().Where(x => x.RegisteredSystemId == systemId
            && x.TenantId == system.TenantId && !x.IsDeleted).ToDictionaryAsync(x => x.Id, ct);
        var controls = new List<Control>();
        var selectedIds = baseline?.ControlIds ?? implementations.Select(x => x.ControlId).ToList();
        if (selectedIds.Count == 0) gaps.Add("Requirement coverage has no selected baseline controls.");
        foreach (var selected in selectedIds)
        {
            var controlGaps = new List<string>();
            var source = catalog?.Controls.SingleOrDefault(x => x.Id == selected || x.DisplayId == selected);
            var implementation = implementations.SingleOrDefault(x => x.TenantId == system.TenantId
                && (x.ControlId == selected || source != null && (x.ControlId == source.Id || x.ControlId == source.DisplayId)));
            if (source is null) controlGaps.Add("Catalog source control is unresolved.");
            if (source?.Withdrawn == true) controlGaps.Add("Catalog source control is withdrawn.");
            if (implementation is null) controlGaps.Add("Narrative response is missing.");
            var snapshot = ReadSnapshot(implementation?.ApprovedRequirementCoverageJson, controlGaps);
            if (evaluateCurrent && snapshot is not null)
            {
                var working = ReadSnapshot(implementation?.RequirementCoverageJson, controlGaps);
                if (working is null || ContentHash(working) != ContentHash(snapshot))
                    controlGaps.Add("Working requirement mappings differ from the retained review.");
            }
            if (snapshot is null)
            {
                controlGaps.Add("Requirement mappings are unreviewed; legacy text remains unstructured.");
                if (implementation?.ApprovedRequirementCoverageJson is null)
                    snapshot = ReadSnapshot(implementation?.RequirementCoverageJson, controlGaps);
            }
            var responses = ValidateSnapshot(binding, source, implementation, snapshot, evidence, controlGaps);
            if (snapshot is not null)
                sources.Add(new("RequirementCoverage", implementation!.Id, snapshot.ReviewedAt?.ToString("O") ?? "unreviewed",
                    RequirementCoverageService.Hash(JsonSerializer.Serialize(snapshot))));
            var parameters = snapshot is not null && source is not null && binding is not null
                && snapshot.BindingId == binding.Id && snapshot.CatalogHash == binding.ContentHash
                ? snapshot.Parameters.Where(x => source.Parameters.Any(p => p.Id == x.Key) && !string.IsNullOrWhiteSpace(x.Value))
                    .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal)
                : new Dictionary<string, string>();
            controls.Add(new(selected, source, implementation, snapshot, responses, parameters, controlGaps));
            gaps.AddRange(controlGaps.Select(x => $"{selected}: {x}"));
        }
        return new(catalog is null ? null : binding, controls, gaps, sources);
    }

    private static string ContentHash(RequirementCoverageSnapshot snapshot) =>
        RequirementCoverageService.Hash(JsonSerializer.Serialize(new
        {
            snapshot.BindingId, snapshot.CatalogHash, snapshot.NarrativeHash,
            Responses = snapshot.Responses?.OrderBy(x => x.StatementId, StringComparer.Ordinal).ThenBy(x => x.Kind, StringComparer.Ordinal),
            Parameters = snapshot.Parameters?.OrderBy(x => x.Key, StringComparer.Ordinal)
        }));

    private static RequirementCoverageSnapshot? ReadSnapshot(string? json, List<string> gaps)
    {
        try
        {
            var snapshot = RequirementCoverageService.Deserialize(json);
            if (snapshot is not null && (snapshot.Responses is null || snapshot.Parameters is null
                || snapshot.Responses.Any(x => x is null || x.Evidence is null || x.Evidence.Any(pin => pin is null))))
            {
                gaps.Add("Requirement mapping snapshot has invalid responses, parameters or evidence pins.");
                return null;
            }
            return snapshot;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        { gaps.Add($"Requirement mapping snapshot is invalid: {ex.Message}"); return null; }
    }

    private static IReadOnlyList<RequirementResponse> ValidateSnapshot(BaselineCatalogBinding? binding,
        CatalogRequirementControl? source, ControlImplementation? implementation, RequirementCoverageSnapshot? snapshot,
        IReadOnlyDictionary<string, EvidenceArtifact> evidence, List<string> gaps)
    {
        if (snapshot is null)
        {
            foreach (var requirement in source?.Requirements ?? [])
                gaps.Add($"Missing response for source requirement '{requirement.Id}'.");
            foreach (var parameter in source?.Parameters ?? [])
                gaps.Add($"Parameter '{parameter.Id}' has no assigned value.");
            gaps.Add("Evidence pins are missing for requirement responses.");
            return [];
        }
        if (snapshot.ReviewedAt is null || snapshot.ReviewerPersonId is null
            || snapshot.ReviewerPersonId == snapshot.AuthorPersonId || string.IsNullOrWhiteSpace(snapshot.ReviewedBy))
            gaps.Add("Requirement mappings lack an independent human review.");
        if (implementation is null || snapshot.NarrativeHash != RequirementCoverageService.NarrativeHash(implementation))
            gaps.Add("Narrative content changed after requirement mapping review.");
        if (binding is null || source is null || snapshot.BindingId != binding.Id || snapshot.CatalogHash != binding.ContentHash)
        { gaps.Add("Requirement mappings do not match the pinned catalog source."); return []; }
        if (source.Requirements.Count == 0) gaps.Add("Structured source requirements are unavailable.");
        var valid = new List<RequirementResponse>();
        foreach (var response in snapshot.Responses)
        {
            if (!source.Requirements.Any(x => x.Id == response.StatementId))
            { gaps.Add($"Unknown source statement '{response.StatementId}' for this control."); continue; }
            if (response.Kind is not ("Policy" or "Technical") || string.IsNullOrWhiteSpace(response.Response))
            { gaps.Add($"Invalid or missing response for '{response.StatementId}'."); continue; }
            if (response.Evidence is null || response.Evidence.Count == 0 || response.Evidence.Any(pin =>
                !evidence.TryGetValue(pin.ArtifactId, out var artifact) || string.IsNullOrWhiteSpace(pin.ContentHash)
                || artifact.ContentHash != pin.ContentHash))
                gaps.Add($"Evidence is missing or changed for '{response.StatementId}' ({response.Kind}).");
            valid.Add(response);
        }
        foreach (var duplicate in valid.GroupBy(x => (x.StatementId, x.Kind)).Where(x => x.Count() > 1))
            gaps.Add($"Duplicate response for '{duplicate.Key.StatementId}' ({duplicate.Key.Kind}).");
        foreach (var requirement in source.Requirements.Where(x => !valid.Any(r => r.StatementId == x.Id)))
            gaps.Add($"Missing response for source requirement '{requirement.Id}'.");
        foreach (var parameter in source.Parameters.Where(x =>
            !snapshot.Parameters.TryGetValue(x.Id, out var value) || string.IsNullOrWhiteSpace(value)))
            gaps.Add($"Parameter '{parameter.Id}' has no assigned value.");
        foreach (var parameter in snapshot.Parameters.Keys.Where(id => !source.Parameters.Any(x => x.Id == id)))
            gaps.Add($"Unknown source parameter '{parameter}'.");
        return valid;
    }

    internal static string Render(Control control)
    {
        var text = new StringBuilder();
        text.AppendLine($"Requirement coverage: {control.Source?.DisplayId ?? control.SelectedId}");
        if (control.Source is { } source)
        {
            text.AppendLine($"Source control: {source.Id}; parent: {source.ParentId ?? "(none)"}");
            foreach (var requirement in source.Requirements)
            {
                text.AppendLine($"{requirement.Label} {requirement.Id}: {requirement.Text}");
                foreach (var response in control.Responses.Where(x => x.StatementId == requirement.Id))
                {
                    text.AppendLine($"{response.Kind}: {response.Response}");
                    foreach (var pin in response.Evidence ?? [])
                        text.AppendLine($"Evidence: {pin.ArtifactId} @ {pin.ContentHash}");
                }
            }
            foreach (var parameter in source.Parameters)
                text.AppendLine($"Parameter {parameter.Id}: {control.Parameters.GetValueOrDefault(parameter.Id) ?? "[Unassigned]"}");
        }
        if (control.Snapshot is { } snapshot)
        {
            text.AppendLine($"Catalog binding: {snapshot.BindingId} @ {snapshot.CatalogHash}");
            text.AppendLine($"Authored by: {snapshot.AuthoredBy} ({snapshot.AuthorPersonId}) at {snapshot.AuthoredAt:O}");
            text.AppendLine($"Mapping review: {snapshot.ReviewedBy ?? "[Unreviewed]"} ({snapshot.ReviewerPersonId}) at {snapshot.ReviewedAt:O}");
            text.AppendLine($"Narrative revision hash: {snapshot.NarrativeHash}");
            var assistance = snapshot.FirstPasses ?? (snapshot.FirstPass is null ? [] : new[] { snapshot.FirstPass });
            foreach (var firstPass in assistance)
            {
                text.AppendLine($"AI-assisted draft basis: {firstPass.Kind}; prepared {firstPass.GeneratedAt:O}; source context {firstPass.ContextHash}. Human edits and independent review are separate; assistance is not implementation or approval evidence.");
                foreach (var reference in firstPass.Sources)
                    text.AppendLine($"First-pass source: {reference.Title} ({reference.Id}); version {reference.Version}; hash {reference.ContentHash}; {reference.ReviewState}.");
            }
        }
        foreach (var gap in control.Gaps) text.AppendLine($"Requirement coverage gap: {gap}");
        return text.ToString();
    }

    internal static string Render(Projection projection) =>
        "Documentation preparation only; not eMASS acceptance or an authorization decision.\n"
        + (projection.Binding is { } binding
            ? $"Catalog: {binding.FrameworkIdentifier} {binding.CatalogVersion}; source URI: {binding.SourceUri}; hash: {binding.ContentHash}\n" : "")
        + string.Join("\n", projection.Gaps.Select(x => $"Requirement coverage gap: {x}"))
        + "\n\n" + string.Join("\n\n", projection.Controls.Select(Render));
}
