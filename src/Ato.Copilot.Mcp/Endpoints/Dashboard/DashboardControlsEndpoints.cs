using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Agents.Document.Tools;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Kanban;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Kanban;
using Ato.Copilot.Core.Models.Poam;
using Ato.Copilot.Core.Services;
using Ato.Copilot.Mcp.Services;
using System.Text.RegularExpressions;

using KanbanTaskStatus = Ato.Copilot.Core.Models.Kanban.TaskStatus;

namespace Ato.Copilot.Mcp.Endpoints;

// ─── #648 Decomposition: Controls domain routes ─────────────────────────────
public static partial class DashboardEndpoints
{
    private static void MapControlRoutes(IEndpointRouteBuilder group, IEndpointRouteBuilder app)
    {
        group.MapGet("/controls", async (
                INistControlsService nistService,
                IReferenceDataService refData,
                string? search,
                string? family,
                string? type,
                int page = 1,
                int pageSize = 50,
                CancellationToken ct = default) =>
            {
                var allControls = await nistService.GetAllControlsAsync(ct);
                if (allControls.Count == 0)
                    return Results.Ok(new { items = Array.Empty<object>(), total = 0, page, pageSize });

                // NIST 800-53B baselines
                var nistLowSet = new HashSet<string>(refData.GetBaselineControlIds("Low"), StringComparer.OrdinalIgnoreCase);
                var nistModSet = new HashSet<string>(refData.GetBaselineControlIds("Moderate"), StringComparer.OrdinalIgnoreCase);
                var nistHighSet = new HashSet<string>(refData.GetBaselineControlIds("High"), StringComparer.OrdinalIgnoreCase);

                // FedRAMP Rev 5 baselines (distinct from NIST 800-53B)
                var fedrampLiSaasSet = new HashSet<string>(refData.GetFedRampBaselineControlIds("li-saas"), StringComparer.OrdinalIgnoreCase);
                var fedrampLowSet = new HashSet<string>(refData.GetFedRampBaselineControlIds("low"), StringComparer.OrdinalIgnoreCase);
                var fedrampModSet = new HashSet<string>(refData.GetFedRampBaselineControlIds("moderate"), StringComparer.OrdinalIgnoreCase);
                var fedrampHighSet = new HashSet<string>(refData.GetFedRampBaselineControlIds("high"), StringComparer.OrdinalIgnoreCase);

                // DoD CNSSI 1253 overlay lookup sets
                var il2Controls = new HashSet<string>(
                    refData.GetOverlayEntries("IL2").Select(o => o.ControlId),
                    StringComparer.OrdinalIgnoreCase);
                var il4Controls = new HashSet<string>(
                    refData.GetOverlayEntries("IL4").Select(o => o.ControlId),
                    StringComparer.OrdinalIgnoreCase);
                var il5Controls = new HashSet<string>(
                    refData.GetOverlayEntries("IL5").Select(o => o.ControlId),
                    StringComparer.OrdinalIgnoreCase);
                var il6Controls = new HashSet<string>(
                    refData.GetOverlayEntries("IL6").Select(o => o.ControlId),
                    StringComparer.OrdinalIgnoreCase);

                // Filter
                IEnumerable<NistControl> filtered = allControls;

                if (!string.IsNullOrWhiteSpace(search))
                {
                    var q = search.Trim();
                    filtered = filtered.Where(c =>
                        c.Id.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        c.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        c.Family.Contains(q, StringComparison.OrdinalIgnoreCase));
                }

                if (!string.IsNullOrWhiteSpace(family))
                    filtered = filtered.Where(c => c.Family.Equals(family, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(type))
                {
                    if (type.Equals("control", StringComparison.OrdinalIgnoreCase))
                        filtered = filtered.Where(c => !c.IsEnhancement);
                    else if (type.Equals("enhancement", StringComparison.OrdinalIgnoreCase))
                        filtered = filtered.Where(c => c.IsEnhancement);
                }

                var sortedList = filtered
                    .OrderBy(c => c.Family)
                    .ThenBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var total = sortedList.Count;
                var paged = sortedList
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(c =>
                    {
                        var upperId = c.Id.ToUpperInvariant();
                        return new
                        {
                            id = upperId,
                            family = c.Family.ToUpperInvariant(),
                            familyName = NistFamilyNames.GetValueOrDefault(c.Family.ToUpperInvariant(), c.Family),
                            title = c.Title,
                            type = c.IsEnhancement ? "Enhancement" : "Control",
                            baselines = new
                            {
                                nistLow = nistLowSet.Contains(upperId) || nistLowSet.Contains(c.Id),
                                nistModerate = nistModSet.Contains(upperId) || nistModSet.Contains(c.Id),
                                nistHigh = nistHighSet.Contains(upperId) || nistHighSet.Contains(c.Id),
                                fedrampLiSaas = fedrampLiSaasSet.Contains(upperId) || fedrampLiSaasSet.Contains(c.Id),
                                fedrampLow = fedrampLowSet.Contains(upperId) || fedrampLowSet.Contains(c.Id),
                                fedrampModerate = fedrampModSet.Contains(upperId) || fedrampModSet.Contains(c.Id),
                                fedrampHigh = fedrampHighSet.Contains(upperId) || fedrampHighSet.Contains(c.Id),
                                il2 = il2Controls.Contains(upperId) || il2Controls.Contains(c.Id),
                                il4 = il4Controls.Contains(upperId) || il4Controls.Contains(c.Id),
                                il5 = il5Controls.Contains(upperId) || il5Controls.Contains(c.Id),
                                il6 = il6Controls.Contains(upperId) || il6Controls.Contains(c.Id),
                            },
                        };
                    })
                    .ToList();

                return Results.Ok(new { items = paged, total, page, pageSize });
            })
            .WithName("GetControlCatalog");

        // ─── Control Detail ─────────────────────────────────────────────────
        group.MapGet("/controls/{controlId}", async (
                string controlId,
                INistControlsService nistService,
                IReferenceDataService refData,
                CancellationToken ct) =>
            {
                var allControls = await nistService.GetAllControlsAsync(ct);
                var control = allControls.FirstOrDefault(c =>
                    c.Id.Equals(controlId, StringComparison.OrdinalIgnoreCase));

                if (control == null)
                    return Results.NotFound(new { error = $"Control '{controlId}' not found." });

                var upperId = control.Id.ToUpperInvariant();

                // Gather baseline membership
                var nistLow = refData.GetBaselineControlIds("Low");
                var nistMod = refData.GetBaselineControlIds("Moderate");
                var nistHigh = refData.GetBaselineControlIds("High");
                var frLiSaas = refData.GetFedRampBaselineControlIds("li-saas");
                var frLow = refData.GetFedRampBaselineControlIds("low");
                var frMod = refData.GetFedRampBaselineControlIds("moderate");
                var frHigh = refData.GetFedRampBaselineControlIds("high");

                // Gather overlay parameters per IL
                var overlayDetails = new[] { "IL2", "IL4", "IL5", "IL6" }
                    .Select(il =>
                    {
                        var entry = refData.GetOverlayEntries(il)
                            .FirstOrDefault(o => o.ControlId.Equals(control.Id, StringComparison.OrdinalIgnoreCase)
                                              || o.ControlId.Equals(upperId, StringComparison.OrdinalIgnoreCase));
                        return new
                        {
                            level = il,
                            applicable = entry != null,
                            parameters = entry?.Parameters ?? new Dictionary<string, string>(),
                            enhancements = entry?.Enhancements ?? new List<string>(),
                            notes = entry?.Notes,
                        };
                    })
                    .ToList();

                // Gather child enhancements
                var enhancements = allControls
                    .Where(c => c.ParentControlId != null &&
                                c.ParentControlId.Equals(control.Id, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
                    .Select(c => new
                    {
                        id = c.Id.ToUpperInvariant(),
                        title = c.Title,
                    })
                    .ToList();

                bool ContainsAny(IReadOnlyList<string> list, string id1, string id2)
                    => list.Any(x => x.Equals(id1, StringComparison.OrdinalIgnoreCase)
                                  || x.Equals(id2, StringComparison.OrdinalIgnoreCase));

                return Results.Ok(new
                {
                    id = upperId,
                    family = control.Family.ToUpperInvariant(),
                    familyName = NistFamilyNames.GetValueOrDefault(control.Family.ToUpperInvariant(), control.Family),
                    title = control.Title,
                    description = control.Description,
                    type = control.IsEnhancement ? "Enhancement" : "Control",
                    parentControlId = control.ParentControlId?.ToUpperInvariant(),
                    azureImplementation = control.AzureImplementation,
                    fedRampParameters = control.FedRampParameters,
                    baselines = new
                    {
                        nistLow = ContainsAny(nistLow, upperId, control.Id),
                        nistModerate = ContainsAny(nistMod, upperId, control.Id),
                        nistHigh = ContainsAny(nistHigh, upperId, control.Id),
                        fedrampLiSaas = ContainsAny(frLiSaas, upperId, control.Id),
                        fedrampLow = ContainsAny(frLow, upperId, control.Id),
                        fedrampModerate = ContainsAny(frMod, upperId, control.Id),
                        fedrampHigh = ContainsAny(frHigh, upperId, control.Id),
                    },
                    dodOverlays = overlayDetails,
                    enhancements,
                });
            })
            .WithName("GetControlDetail");

        // ─── Multi-Framework Catalog Endpoints (Feature 044) ────────────────

        group.MapGet("/frameworks", async (
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            var frameworks = await context.ComplianceFrameworks
                .Where(f => f.IsActive)
                .OrderBy(f => f.Name)
                .Select(f => new
                {
                    f.Id,
                    f.Identifier,
                    f.Name,
                    f.Version,
                    f.Publisher,
                    f.ControlCount,
                    f.ImportedAt,
                    f.IsActive,
                    baselines = f.Baselines
                        .OrderBy(b => b.Level)
                        .Select(b => new { b.Id, b.Level, b.ControlCount, b.ImportedAt })
                        .ToList(),
                })
                .ToListAsync(ct);

            return Results.Ok(frameworks);
        })
        .WithName("ListFrameworks");

        group.MapGet("/frameworks/{frameworkId}/controls", async (
            string frameworkId,
            AtoCopilotContext context,
            string? search,
            string? family,
            int page = 1,
            int pageSize = 50,
            CancellationToken ct = default) =>
        {
            // Resolve by ID or Identifier
            var framework = await context.ComplianceFrameworks
                .FirstOrDefaultAsync(f => f.Id == frameworkId || f.Identifier == frameworkId, ct);

            if (framework is null)
                return Results.NotFound(new { error = "Framework not found" });

            IQueryable<FrameworkControl> query = context.FrameworkControls
                .Where(c => c.FrameworkId == framework.Id && !c.Withdrawn);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var q = search.Trim();
                query = query.Where(c =>
                    c.ControlId.Contains(q) ||
                    c.Title.Contains(q) ||
                    c.Family.Contains(q));
            }

            if (!string.IsNullOrWhiteSpace(family))
                query = query.Where(c => c.Family == family.ToUpperInvariant());

            var total = await query.CountAsync(ct);

            var items = await query
                .OrderBy(c => c.SortOrder)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(c => new
                {
                    c.ControlId,
                    c.Family,
                    c.Title,
                    c.IsEnhancement,
                    c.ParentControlId,
                    type = c.IsEnhancement ? "Enhancement" : "Control",
                })
                .ToListAsync(ct);

            // Look up baseline membership for returned controls
            var controlIds = items.Select(i => i.ControlId).ToList();
            var baselineIds = await context.FrameworkBaselines
                .Where(b => b.FrameworkId == framework.Id)
                .Select(b => new { b.Id, b.Level })
                .ToListAsync(ct);

            var baselineMembership = new Dictionary<string, Dictionary<string, bool>>(StringComparer.OrdinalIgnoreCase);
            foreach (var bl in baselineIds)
            {
                var memberIds = await context.BaselineControlEntries
                    .Where(e => e.BaselineId == bl.Id && controlIds.Contains(e.ControlId))
                    .Select(e => e.ControlId)
                    .ToListAsync(ct);

                var memberSet = new HashSet<string>(memberIds, StringComparer.OrdinalIgnoreCase);
                foreach (var cid in controlIds)
                {
                    if (!baselineMembership.ContainsKey(cid))
                        baselineMembership[cid] = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                    baselineMembership[cid][bl.Level] = memberSet.Contains(cid);
                }
            }

            var result = items.Select(i => new
            {
                id = i.ControlId,
                family = i.Family,
                familyName = NistFamilyNames.GetValueOrDefault(i.Family, i.Family),
                title = i.Title,
                type = i.type,
                baselines = baselineMembership.GetValueOrDefault(i.ControlId) ?? new Dictionary<string, bool>(),
            });

            return Results.Ok(new { items = result, total, page, pageSize, frameworkId = framework.Identifier });
        })
        .WithName("GetFrameworkControls");

        group.MapGet("/frameworks/{frameworkId}/controls/{controlId}", async (
            string frameworkId,
            string controlId,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            var framework = await context.ComplianceFrameworks
                .FirstOrDefaultAsync(f => f.Id == frameworkId || f.Identifier == frameworkId, ct);
            if (framework is null)
                return Results.NotFound(new { error = "Framework not found" });

            var control = await context.FrameworkControls
                .FirstOrDefaultAsync(c => c.FrameworkId == framework.Id && c.ControlId == controlId, ct);
            if (control is null)
                return Results.NotFound(new { error = "Control not found" });

            // Child enhancements
            var enhancements = await context.FrameworkControls
                .Where(c => c.FrameworkId == framework.Id && c.ParentControlId == control.ControlId && !c.Withdrawn)
                .OrderBy(c => c.SortOrder)
                .Select(c => new { c.ControlId, c.Title })
                .ToListAsync(ct);

            // Baseline membership
            var baselines = await context.FrameworkBaselines
                .Where(b => b.FrameworkId == framework.Id)
                .ToListAsync(ct);

            var baselineMap = new Dictionary<string, bool>();
            foreach (var bl in baselines)
            {
                var isMember = await context.BaselineControlEntries
                    .AnyAsync(e => e.BaselineId == bl.Id && e.ControlId == controlId, ct);
                baselineMap[bl.Level] = isMember;
            }

            return Results.Ok(new
            {
                id = control.ControlId,
                family = control.Family,
                familyName = NistFamilyNames.GetValueOrDefault(control.Family, control.Family),
                title = control.Title,
                description = ResolveControlCatalogDescription(control.Description),
                type = control.IsEnhancement ? "Enhancement" : "Control",
                parentControlId = control.ParentControlId,
                withdrawn = control.Withdrawn,
                withdrawnTo = control.WithdrawnTo,
                baselines = baselineMap,
                enhancements,
                framework = new { framework.Identifier, framework.Name, framework.Version },
            });
        })
        .WithName("GetFrameworkControlDetail");

        group.MapPost("/frameworks/import", async (
            IFrameworkImportService importService,
            CancellationToken ct) =>
        {
            var result = await importService.ImportAllAsync(ct);
            return Results.Ok(new
            {
                frameworksImported = result.FrameworksImported,
                totalControls = result.TotalControls,
                totalBaselines = result.TotalBaselines,
                errors = result.Errors,
            });
        })
        .WithName("ImportAllFrameworks");

        group.MapPost("/frameworks/{identifier}/import", async (
            string identifier,
            IFrameworkImportService importService,
            CancellationToken ct) =>
        {
            var count = await importService.ImportFrameworkAsync(identifier, ct);
            return Results.Ok(new { identifier, controlsImported = count });
        })
        .WithName("ImportSingleFramework");
    }
}
