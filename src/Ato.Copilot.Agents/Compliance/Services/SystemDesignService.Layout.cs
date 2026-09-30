using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.SystemDesign;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Agents.Compliance.Services;

public sealed partial class SystemDesignService
{
    private static void View(string view)
    {
        if (view is not ("Context" or "Boundary" or "Network" or "DataFlows"))
            throw new ArgumentException("Unknown design view.");
    }
    private IQueryable<SystemDesignLayoutRecord> Layouts(AtoCopilotContext db, string id, string view) =>
        db.Set<SystemDesignLayoutRecord>().Where(x => x.TenantId == TenantId && x.SystemId == id && x.View == view);
    public async Task<DesignLayout> GetLayoutAsync(string systemId, string view, CancellationToken ct = default)
    {
        View(view);
        await using var db = await factory.CreateDbContextAsync(ct);
        await AuthorizeAsync(db, systemId, ct);
        var row = await Layouts(db, systemId, view).AsNoTracking().SingleOrDefaultAsync(ct);
        var graph = await GetAsync(systemId, ct);
        var layout = row is null ? new DesignLayout { View = view } : Read<DesignLayout>(row.LayoutJson);
        var nodeIds = graph.Nodes.Select(x => x.Id).Concat(graph.Groups.Select(x => x.Id)).ToHashSet();
        var positions = layout.Positions.Where(x => nodeIds.Contains(x.Key)).ToDictionary(x => x.Key, x => x.Value);
        var index = positions.Count;
        foreach (var node in graph.Nodes.OrderBy(x => x.Id))
            if (!positions.ContainsKey(node.Id))
            {
                // New records fill a deterministic grid; retained manual positions never move.
                positions[node.Id] = new(index % 5 * 260, index / 5 * 160);
                index++;
            }
        return layout with { Positions = positions,
            CollapsedGroups = layout.CollapsedGroups.Where(x => graph.Groups.Any(g => g.Id == x)).ToArray(),
            EdgeRouting = layout.EdgeRouting.Where(x => graph.Edges.Any(e => e.Id == x.Key)).ToDictionary(x => x.Key, x => x.Value) };
    }
    public async Task<DesignLayout> SaveLayoutAsync(string systemId, SaveDesignLayoutRequest request, CancellationToken ct = default)
    {
        if (request.Layout is null) throw new ArgumentException("Layout is required.");
        View(request.Layout.View);
        await using var db = await factory.CreateDbContextAsync(ct);
        var (_, actions) = await AuthorizeAsync(db, systemId, ct);
        RequireEdit(actions);
        var layout = request.Layout;
        var graph = await GetAsync(systemId, ct);
        static bool Coordinate(double x) => double.IsFinite(x) && Math.Abs(x) <= 1_000_000;
        if (layout.Positions is null || layout.CollapsedGroups is null || layout.EdgeRouting is null || layout.Visibility is null
            || layout.Positions.Count > 1200 ||
            layout.Positions.Any(x => x.Value is null || !graph.Nodes.Any(n => n.Id == x.Key) && !graph.Groups.Any(g => g.Id == x.Key)
                || !Coordinate(x.Value.X) || !Coordinate(x.Value.Y))
            || layout.Viewport is null || !Coordinate(layout.Viewport.X) || !Coordinate(layout.Viewport.Y)
            || !double.IsFinite(layout.Viewport.Zoom) || layout.Viewport.Zoom is < 0.05 or > 10
            || layout.CollapsedGroups.Any(x => !graph.Groups.Any(g => g.Id == x))
            || layout.EdgeRouting.Count > 3000 || layout.EdgeRouting.Any(x => x.Value is null || !graph.Edges.Any(e => e.Id == x.Key) || x.Value.Length > 2000)
            || layout.Visibility.Count > 4200 || Json(layout).Length > 500_000)
            throw new ArgumentException("Layout contains invalid coordinates or record references.");
        var row = await Layouts(db, systemId, layout.View).SingleOrDefaultAsync(ct);
        Expected(row?.Version ?? 0, request.ExpectedVersion);
        if (row is null) { row = new() { TenantId = TenantId, SystemId = systemId, View = layout.View }; db.Add(row); }
        row.Version++;
        var result = layout with { Version = row.Version };
        row.LayoutJson = Json(result);
        await db.SaveChangesAsync(ct);
        return result;
    }
}
