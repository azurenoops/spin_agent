using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.SystemDesign;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Agents.Compliance.Services;

public sealed partial class SystemDesignService
{
    private Task<SystemComponentPlacementOptions> ScopeSourceAsync(string systemId, string source, string componentId, CancellationToken ct) =>
        (workspace ?? throw new InvalidOperationException("The component source service is unavailable."))
        .GetSystemComponentPlacementsAsync(TenantId, systemId, source, componentId,
            new(true, false, false, false, false, false), ct);

    public async Task<SystemDesignGraph> SaveComponentScopeAsync(string systemId, SaveComponentScopeRequest request,
        CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var (_, actions) = await AuthorizeAsync(db, systemId, ct);
        RequireEdit(actions);
        var graph = await GetAsync(systemId, ct);
        Expected(graph.Revision, request.ExpectedRevision);
        return await SaveDesignAsync(systemId, new(graph.Revision, graph.Nodes, graph.Edges, graph.Groups,
            "Save component service-use scope draft; canonical placements and reviewed baseline retained."), request, ct);
    }

    private async Task<ComponentScopeUse> ValidateComponentScopeAsync(string systemId, SaveComponentScopeRequest request, CancellationToken ct)
    {
        if (request.Source is not ("local" or "provider") || string.IsNullOrWhiteSpace(request.ComponentId)
            || request.ComponentId.Length > 100 || string.IsNullOrWhiteSpace(request.SourceRevision)
            || request.SourceRevision.Length > 64 || request.Usage is null || request.Usage.Length > 2000
            || request.Decision is not ("Included" or "Excluded" or "NeedsConfirmation")
            || request.Decision == "Included" && string.IsNullOrWhiteSpace(request.BoundaryId)
            || request.Decision != "Included" && request.BoundaryId is not null)
            throw new ArgumentException("Choose a scope decision, a recorded area for included use and at most 2000 characters of usage.");
        var source = await ScopeSourceAsync(systemId, request.Source, request.ComponentId, ct);
        if (source.SourceRevision != request.SourceRevision)
            throw new DbUpdateConcurrencyException("Component source changed. Compare the current source before saving; your notes are retained.");
        if (request.Decision == "Included" && !source.SourceAvailable)
            throw new ArgumentException("The source is unavailable for new included use. Record exclusion or a confirmation question instead.");
        var boundary = source.Boundaries.SingleOrDefault(x => x.Id == request.BoundaryId);
        if (request.Decision == "Included" && boundary is null)
            throw new ArgumentException("Choose an area recorded in this system.");
        return new(source.Source, source.RecordId, source.ComponentName, source.SourceRevision,
            request.Decision, boundary?.Id, boundary?.Name, request.Usage.Trim())
            { WordingBasis = await WordingBasisAsync(systemId, request, ct) };
    }

    private async Task<ScopeWordingBasis?> WordingBasisAsync(string systemId, SaveComponentScopeRequest request, CancellationToken ct)
    {
        if (request.WordingDraftId is null)
        {
            if (request.WordingDraftRevision is not null) throw new ArgumentException("A wording draft identifier is required.");
            return null;
        }
        await using var db = await factory.CreateDbContextAsync(ct);
        var draft = await db.Set<ResponsibilityDraft>().AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == request.WordingDraftId && x.TenantId == TenantId && x.RegisteredSystemId == systemId, ct)
            ?? throw new KeyNotFoundException("The proposed wording draft was not found in this system.");
        if (draft.Revision != request.WordingDraftRevision || draft.GenerationState == "Failed")
            throw new DbUpdateConcurrencyException("The wording proposal changed or could not be prepared. Compare it before saving.");
        var suggestion = Read<ResponsibilityDraftSuggestion>(draft.SuggestionJson);
        var wording = suggestion.Values.GetValueOrDefault("scope")?.Origin == "AI proposed"
            ? suggestion.Values["scope"] : suggestion.Values.GetValueOrDefault("customer");
        if (wording is null || string.IsNullOrWhiteSpace(wording.Value))
            throw new ArgumentException("The referenced draft contains no proposed system-use wording.");
        return new(draft.Id, draft.Revision, draft.SourceHash, wording.Origin, wording.Value,
            request.Usage.Trim() != wording.Value.Trim(), wording.SourceIds);
    }

    private async Task<IReadOnlyList<DesignGap>> ComponentScopeGapsAsync(SystemDesignGraph graph, CancellationToken ct)
    {
        var gaps = new List<DesignGap>();
        foreach (var scope in graph.ComponentScopes)
        {
            var key = $"{scope.Source}:{scope.ComponentId}";
            var url = $"/systems/{Uri.EscapeDataString(graph.SystemId)}/security-capabilities?grouping=component";
            try
            {
                var current = await ScopeSourceAsync(graph.SystemId, scope.Source, scope.ComponentId, ct);
                if (current.SourceRevision != scope.SourceRevision
                    || scope.Decision == "Included" && !current.SourceAvailable
                    || scope.BoundaryId is not null && !current.Boundaries.Any(x => x.Id == scope.BoundaryId && x.Name == scope.BoundaryName))
                    gaps.Add(new($"ComponentScopeSource:{key}", "Error", $"The source or recorded system area for {scope.Name} changed. Compare and resave its scope draft.",
                        key, "Boundary", "Component service-use scope", "System Owner", url));
            }
            catch (KeyNotFoundException)
            {
                gaps.Add(new($"ComponentScopeSource:{key}", "Error", $"The source for {scope.Name} is unavailable. Its retained scope has not been overwritten.",
                    key, "Boundary", "Component service-use scope", "System Owner", url));
            }
            if (scope.Decision == "NeedsConfirmation")
                gaps.Add(new($"ComponentScopeUnresolved:{key}", "Error", $"Confirm how {scope.Name} is used by this system.",
                    key, "Boundary", "Component service-use scope", "System Owner", url));
        }
        return gaps;
    }
}
