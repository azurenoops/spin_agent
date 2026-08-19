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

// ─── #648 Decomposition: Inheritance domain routes ─────────────────────────────
public static partial class DashboardEndpoints
{
    private static void MapInheritanceRoutes(IEndpointRouteBuilder group, IEndpointRouteBuilder app)
    {
        group.MapGet("/systems/{systemId}/inheritance", async (
            string systemId,
            [FromQuery] string? family,
            [FromQuery] string? inheritanceType,
            [FromQuery] string? search,
            [FromQuery] string? source,
            [FromQuery] int page,
            [FromQuery] int pageSize,
            [FromQuery] string? sortBy,
            [FromQuery] string? sortDirection,
            IBaselineService baselineService,
            AtoCopilotContext context,
            ITenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 50;
            if (pageSize > 200) pageSize = 200;

            var baseline = await baselineService.GetBaselineAsync(systemId, includeDetails: true, cancellationToken: ct);
            if (baseline == null)
                return Results.NotFound(new ErrorResponse
                {
                    Error = "System or baseline not found",
                    ErrorCode = "BASELINE_NOT_FOUND",
                    Suggestion = "Ensure the system has a control baseline configured"
                });

            // Load org defaults for LEFT JOIN
            var orgDefaultLookup = await context.OrgInheritanceDefaults
                .AsNoTracking()
                .ToDictionaryAsync(d => d.ControlId, d => d, StringComparer.OrdinalIgnoreCase, ct);

            // Feature 048 (T137, FR-083): resolve current tenant display name and the
            // set of OrgInheritanceDefault.Id values currently published as global
            // baselines. Drives the per-row `Source: Global Baseline` vs
            // `Source: <Tenant.DisplayName>` label in the inheritance UI.
            var effectiveTenantId = tenantContext.EffectiveTenantId;
            var orgDisplayName = effectiveTenantId == Guid.Empty
                ? null
                : await context.Tenants
                    .AsNoTracking()
                    .Where(t => t.Id == effectiveTenantId)
                    .Select(t => t.DisplayName)
                    .FirstOrDefaultAsync(ct);
            var globalBaselineSourceIds = await context.GlobalBaselines
                .AsNoTracking()
                .Where(g => g.Kind == "OrgInheritanceDefault" && g.UnpublishedAt == null)
                .Select(g => g.SourceId)
                .ToListAsync(ct);
            var globalBaselineSourceIdSet = globalBaselineSourceIds.Count == 0
                ? null
                : new HashSet<Guid>(globalBaselineSourceIds);

            // Build designation list from all control IDs + inheritance records
            var inheritanceLookup = baseline.Inheritances.ToDictionary(i => i.ControlId, i => i, StringComparer.OrdinalIgnoreCase);
            var allDesignations = baseline.ControlIds.Select(cid =>
            {
                inheritanceLookup.TryGetValue(cid, out var inh);
                orgDefaultLookup.TryGetValue(cid, out var orgDefault);
                // OrgInheritanceDefault.Id is stored as a GUID-shaped string; GlobalBaseline.SourceId is a Guid.
                // Match them defensively via TryParse so malformed legacy ids don't throw.
                var isGlobalBaseline = orgDefault != null
                    && globalBaselineSourceIdSet != null
                    && Guid.TryParse(orgDefault.Id, out var orgDefaultGuid)
                    && globalBaselineSourceIdSet.Contains(orgDefaultGuid);
                return new
                {
                    id = inh?.Id ?? string.Empty,
                    controlId = cid,
                    family = ComplianceFrameworks.ExtractControlFamily(cid),
                    inheritanceType = inh?.InheritanceType.ToString() ?? "Undesignated",
                    provider = inh?.Provider,
                    customerResponsibility = inh?.CustomerResponsibility,
                    designationSource = inh?.DesignationSource,
                    orgDefault = orgDefault == null ? null : new
                    {
                        id = orgDefault.Id,
                        inheritanceType = orgDefault.InheritanceType.ToString(),
                        provider = orgDefault.Provider,
                        sourceCapabilities = orgDefault.SourceCapabilityNames,
                        mappingRole = orgDefault.MappingRole.ToString(),
                    },
                    setBy = inh?.SetBy,
                    setAt = inh?.SetAt,
                    // Feature 048 (T137, FR-083): provenance fields driving the
                    // `Source: <Tenant or Global Baseline>` label in the UI.
                    isGlobalBaseline,
                    orgDisplayName
                };
            }).ToList();

            // Apply filters
            var filtered = allDesignations.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(family))
                filtered = filtered.Where(d => d.family.Equals(family, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(inheritanceType))
                filtered = filtered.Where(d => d.inheritanceType.Equals(inheritanceType, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(search))
                filtered = filtered.Where(d =>
                    d.controlId.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    (d.provider != null && d.provider.Contains(search, StringComparison.OrdinalIgnoreCase)));
            if (!string.IsNullOrWhiteSpace(source))
            {
                filtered = source.ToLowerInvariant() switch
                {
                    "org" or "orgdefault" or "orgderived" => filtered.Where(d => d.designationSource == "OrgDerived"),
                    "override" or "manual" => filtered.Where(d => d.designationSource is "Manual" or "ProfileApply" or "CrmImport" or "BulkUpdate"),
                    "undesignated" => filtered.Where(d => d.designationSource == null && d.inheritanceType == "Undesignated"),
                    _ => filtered,
                };
            }

            // Sort
            var sortField = sortBy?.ToLowerInvariant() ?? "controlid";
            var desc = sortDirection?.Equals("desc", StringComparison.OrdinalIgnoreCase) == true;
            filtered = sortField switch
            {
                "family" => desc ? filtered.OrderByDescending(d => d.family) : filtered.OrderBy(d => d.family),
                "inheritancetype" => desc ? filtered.OrderByDescending(d => d.inheritanceType) : filtered.OrderBy(d => d.inheritanceType),
                "setat" => desc ? filtered.OrderByDescending(d => d.setAt) : filtered.OrderBy(d => d.setAt),
                _ => desc ? filtered.OrderByDescending(d => d.controlId) : filtered.OrderBy(d => d.controlId),
            };

            var total = filtered.Count();
            var items = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            // Summary
            var totalControls = baseline.ControlIds.Count;
            var inheritedCount = baseline.Inheritances.Count(i => i.InheritanceType == InheritanceType.Inherited);
            var sharedCount = baseline.Inheritances.Count(i => i.InheritanceType == InheritanceType.Shared);
            var customerCount = baseline.Inheritances.Count(i => i.InheritanceType == InheritanceType.Customer);
            var undesignatedCount = totalControls - inheritedCount - sharedCount - customerCount;
            var pct = totalControls > 0 ? Math.Round((double)(inheritedCount + sharedCount + customerCount) / totalControls * 100, 1) : 0;

            // Source breakdown
            var orgDefaultCount = baseline.Inheritances.Count(i => i.DesignationSource == "OrgDerived");
            var systemOverrideCount = baseline.Inheritances.Count(i => i.DesignationSource is "Manual" or "ProfileApply" or "CrmImport" or "BulkUpdate");

            return Results.Ok(new
            {
                items,
                totalItems = total,
                page,
                pageSize,
                summary = new
                {
                    totalControls,
                    inheritedCount,
                    sharedCount,
                    customerCount,
                    undesignatedCount,
                    inheritancePercentage = pct,
                    orgDefaultCount,
                    systemOverrideCount,
                    sourceBreakdown = new
                    {
                        orgDerived = orgDefaultCount,
                        manual = baseline.Inheritances.Count(i => i.DesignationSource == "Manual"),
                        profileApply = baseline.Inheritances.Count(i => i.DesignationSource == "ProfileApply"),
                        crmImport = baseline.Inheritances.Count(i => i.DesignationSource == "CrmImport"),
                        bulkUpdate = baseline.Inheritances.Count(i => i.DesignationSource == "BulkUpdate"),
                        undesignated = undesignatedCount,
                    }
                }
            });
        }).WithName("ListInheritanceDesignations");

        // ── PUT /systems/{systemId}/inheritance — set designations (single + bulk)
        // TODO(FR-026): Add role validation — restrict writes to AO and Security Engineer roles
        //   when auth context is available. Return 403 Forbidden for unauthorized users.
        group.MapPut("/systems/{systemId}/inheritance", async (
            string systemId,
            Feature043SetInheritanceRequest req,
            IBaselineService baselineService,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            if (req.Designations == null || req.Designations.Count == 0)
                return Results.BadRequest(new ErrorResponse { Error = "At least one designation is required.", ErrorCode = "INVALID_INPUT" });

            // Validate inheritance types
            foreach (var d in req.Designations)
            {
                if (!Enum.TryParse<InheritanceType>(d.InheritanceType, true, out var iType))
                    return Results.BadRequest(new ErrorResponse { Error = $"Invalid inheritance type '{d.InheritanceType}'", ErrorCode = "INVALID_INPUT" });

                if ((iType == InheritanceType.Inherited || iType == InheritanceType.Shared) && string.IsNullOrWhiteSpace(d.Provider))
                    return Results.BadRequest(new ErrorResponse { Error = $"Provider is required for '{d.InheritanceType}' on control {d.ControlId}", ErrorCode = "INVALID_INPUT" });

                if (iType == InheritanceType.Customer && string.IsNullOrWhiteSpace(d.CustomerResponsibility))
                    return Results.BadRequest(new ErrorResponse { Error = $"Customer responsibility is required for 'Customer' on control {d.ControlId}", ErrorCode = "INVALID_INPUT" });
            }

            var changeSource = InheritanceChangeSource.Manual;
            if (!string.IsNullOrWhiteSpace(req.ChangeSource))
                Enum.TryParse(req.ChangeSource, true, out changeSource);

            var mappings = req.Designations.Select(d => new InheritanceInput
            {
                ControlId = d.ControlId,
                InheritanceType = d.InheritanceType,
                Provider = d.Provider,
                CustomerResponsibility = d.CustomerResponsibility
            });

            var result = await baselineService.SetInheritanceAsync(systemId, mappings, "dashboard-user", changeSource, ct);

            var totalControls = result.Baseline.ControlIds.Count;
            var undesignated = totalControls - result.InheritedCount - result.SharedCount - result.CustomerCount;
            var pct = totalControls > 0 ? Math.Round((double)(result.InheritedCount + result.SharedCount + result.CustomerCount) / totalControls * 100, 1) : 0;

            context.DashboardActivities.Add(new DashboardActivity
            {
                RegisteredSystemId = systemId,
                EventType = "InheritanceUpdated",
                Actor = "dashboard-user",
                Summary = $"Updated {result.ControlsUpdated} control inheritance designations (source: {changeSource})",
                RelatedEntityType = "ControlBaseline",
                RelatedEntityId = result.Baseline.Id,
            });
            await context.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                controlsUpdated = result.ControlsUpdated,
                inheritedCount = result.InheritedCount,
                sharedCount = result.SharedCount,
                customerCount = result.CustomerCount,
                skippedControls = result.SkippedControls,
                narrativesAutoUpdated = result.NarrativesAutoUpdated,
                summary = new
                {
                    totalControls,
                    inheritedCount = result.InheritedCount,
                    sharedCount = result.SharedCount,
                    customerCount = result.CustomerCount,
                    undesignatedCount = undesignated,
                    inheritancePercentage = pct
                }
            });
        }).WithName("SetInheritanceDesignations");

        // ── POST /systems/{systemId}/inheritance/revert-to-org-defaults — revert selected controls
        group.MapPost("/systems/{systemId}/inheritance/revert-to-org-defaults", async (
            string systemId,
            Feature044RevertRequest req,
            IOrgInheritanceService orgService,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            if (req.ControlIds == null || req.ControlIds.Count == 0)
                return Results.BadRequest(new ErrorResponse { Error = "At least one control ID is required.", ErrorCode = "INVALID_INPUT" });

            var revertedBy = req.RevertedBy ?? "dashboard-user";
            var result = await orgService.RevertToOrgDefaultsAsync(systemId, req.ControlIds, revertedBy, ct);

            context.DashboardActivities.Add(new DashboardActivity
            {
                RegisteredSystemId = systemId,
                EventType = "InheritanceReverted",
                Actor = revertedBy,
                Summary = $"Reverted {result.RevertedCount} controls to org defaults, {result.Skipped.Count} skipped",
                RelatedEntityType = "ControlInheritance",
            });
            await context.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                revertedCount = result.RevertedCount,
                skipped = result.Skipped.Select(s => new { s.ControlId, s.Reason }),
            });
        }).WithName("RevertToOrgDefaults");

        // ── GET /systems/{systemId}/inheritance/{controlId}/audit — per-control audit trail
        group.MapGet("/systems/{systemId}/inheritance/{controlId}/audit", async (
            string systemId,
            string controlId,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            // Verify system + baseline exists
            var baseline = await context.ControlBaselines
                .FirstOrDefaultAsync(b => b.RegisteredSystemId == systemId, ct);
            if (baseline == null)
                return Results.NotFound(new ErrorResponse
                {
                    Error = "System or baseline not found",
                    ErrorCode = "BASELINE_NOT_FOUND"
                });

            var entries = await context.InheritanceAuditEntries
                .Where(e => e.ControlBaselineId == baseline.Id && e.ControlId == controlId)
                .OrderBy(e => e.Timestamp)
                .Select(e => new
                {
                    id = e.Id,
                    actor = e.Actor,
                    previousInheritanceType = e.PreviousInheritanceType,
                    newInheritanceType = e.NewInheritanceType,
                    previousProvider = e.PreviousProvider,
                    newProvider = e.NewProvider,
                    previousCustomerResponsibility = e.PreviousCustomerResponsibility,
                    newCustomerResponsibility = e.NewCustomerResponsibility,
                    changeSource = e.ChangeSource.ToString(),
                    changeSourceLabel = e.ChangeSource == InheritanceChangeSource.OrgDerived ? "Org Default"
                        : e.ChangeSource == InheritanceChangeSource.OrgPropagation ? "Org Propagation"
                        : e.ChangeSource == InheritanceChangeSource.ProfileApply ? "CSP Profile"
                        : e.ChangeSource == InheritanceChangeSource.CrmImport ? "CRM Import"
                        : e.ChangeSource == InheritanceChangeSource.BulkUpdate ? "Bulk Update"
                        : "Manual",
                    timestamp = e.Timestamp
                })
                .ToListAsync(ct);

            return Results.Ok(new
            {
                controlId,
                entries
            });
        }).WithName("GetInheritanceAudit");

        // ── GET /systems/{systemId}/inheritance/crm — generate CRM
        group.MapGet("/systems/{systemId}/inheritance/crm", async (
            string systemId,
            IBaselineService baselineService,
            CancellationToken ct) =>
        {
            try
            {
                var crm = await baselineService.GenerateCrmAsync(systemId, ct);
                return Results.Ok(crm);
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(new ErrorResponse
                {
                    Error = ex.Message,
                    ErrorCode = "BASELINE_NOT_FOUND",
                    Suggestion = "Ensure the system has a control baseline configured"
                });
            }
        }).WithName("GetCrm");

        // ── GET /systems/{systemId}/inheritance/crm/export — export CRM as CSV or Excel
        group.MapGet("/systems/{systemId}/inheritance/crm/export", async (
            string systemId,
            [FromQuery] string format,
            [FromQuery] string? layout,
            IBaselineService baselineService,
            Ato.Copilot.Mcp.Services.CrmExportService crmExportService,
            CancellationToken ct) =>
        {
            var fmt = format?.ToLowerInvariant();
            if (fmt != "csv" && fmt != "excel")
                return Results.BadRequest(new ErrorResponse { Error = $"Unsupported export format: {format}", ErrorCode = "EXPORT_FORMAT_INVALID" });

            var exportLayout = layout ?? "custom";

            CrmResult crm;
            try
            {
                crm = await baselineService.GenerateCrmAsync(systemId, ct);
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(new ErrorResponse { Error = ex.Message, ErrorCode = "BASELINE_NOT_FOUND" });
            }

            var date = DateTime.UtcNow.ToString("yyyy-MM-dd");
            if (fmt == "csv")
            {
                var bytes = crmExportService.GenerateCsv(crm, exportLayout);
                return Results.File(bytes, "text/csv", $"crm-{systemId}-{date}.csv");
            }
            else
            {
                var bytes = crmExportService.GenerateExcel(crm, exportLayout);
                return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"crm-{systemId}-{date}.xlsx");
            }
        }).WithName("ExportCrm");

        // ── GET /systems/{systemId}/inheritance/csp-profiles — list available profiles
        group.MapGet("/systems/{systemId}/inheritance/csp-profiles", (
            string systemId,
            Ato.Copilot.Mcp.Services.CspProfileService cspProfileService) =>
        {
            var profiles = cspProfileService.GetProfiles();
            return Results.Ok(new
            {
                profiles = profiles.Select(p => new
                {
                    profileId = p.ProfileId,
                    name = p.Name,
                    provider = p.Provider,
                    baselineLevel = p.BaselineLevel,
                    description = p.Description,
                    controlCount = p.Controls.Count,
                    version = p.Version
                })
            });
        }).WithName("ListCspProfiles");

        // ── POST /systems/{systemId}/inheritance/apply-profile — T001 #141 ─────
        group.MapPost("/systems/{systemId}/inheritance/apply-profile", async (
            string systemId,
            Feature043ApplyProfileRequest req,
            IBaselineService baselineService,
            Ato.Copilot.Mcp.Services.CspProfileService cspProfileService,
            CancellationToken ct) =>
        {
            var baseline = await baselineService.GetBaselineAsync(systemId, includeDetails: true, cancellationToken: ct);
            if (baseline is null)
                return Results.NotFound(new ErrorResponse { Error = "Baseline not found for system.", ErrorCode = "BASELINE_NOT_FOUND" });

            var profile = cspProfileService.GetProfile(req.ProfileId);
            if (profile is null)
                return Results.NotFound(new ErrorResponse { Error = $"CSP profile not found.", ErrorCode = "PROFILE_NOT_FOUND" });

            var existingDesignations = baseline.Inheritances
                .ToDictionary(i => i.ControlId, i => i.InheritanceType.ToString(), StringComparer.OrdinalIgnoreCase);

            var matchResult = cspProfileService.MatchProfile(
                profile,
                baseline.ControlIds,
                existingDesignations,
                req.ConflictResolution ?? "skip");

            if (req.Preview)
                return Results.Ok(matchResult);

            var mappings = matchResult.MappingsToApply.Select(m => new InheritanceInput
            {
                ControlId = m.ControlId,
                InheritanceType = m.InheritanceType,
                Provider = m.Provider,
                CustomerResponsibility = m.CustomerResponsibility,
            });

            var result = await baselineService.SetInheritanceAsync(
                systemId, mappings, "dashboard-user", InheritanceChangeSource.ProfileApply, ct);

            return Results.Ok(new
            {
                applied = matchResult.MappingsToApply.Count,
                skipped = matchResult.WillSkipExisting,
                unmatched = matchResult.UnmatchedControls,
                profile = new { profile.Name, profile.Provider },
                baseline = new { result.InheritedCount, result.SharedCount, result.CustomerCount }
            });
        }).WithName("ApplyInheritanceProfile");

        // ── POST /systems/{systemId}/inheritance/import/preview — T002 #142 ────
        group.MapPost("/systems/{systemId}/inheritance/import/preview", async (
            string systemId,
            HttpRequest httpRequest,
            IBaselineService baselineService,
            Ato.Copilot.Mcp.Services.CrmExportService crmExportService,
            CancellationToken ct) =>
        {
            var baseline = await baselineService.GetBaselineAsync(systemId, cancellationToken: ct);
            if (baseline is null)
                return Results.NotFound(new ErrorResponse { Error = "Baseline not found for system.", ErrorCode = "BASELINE_NOT_FOUND" });

            var form = await httpRequest.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
                return Results.BadRequest(new ErrorResponse { Error = "No file uploaded.", ErrorCode = "FILE_REQUIRED" });

            Ato.Copilot.Mcp.Services.CrmExportService.ImportParseResult parsed;
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            using var stream = file.OpenReadStream();
            if (ext is ".xlsx" or ".xls")
                parsed = crmExportService.ParseExcel(stream);
            else
                parsed = crmExportService.ParseCsv(stream);

            var token = Guid.NewGuid().ToString("N");
            ImportPreviewCache[token] = parsed;

            return Results.Ok(new
            {
                previewToken = token,
                fileName = file.FileName,
                detectedColumns = parsed.Columns,
                rowCount = parsed.Rows.Count,
                sampleRows = parsed.SampleRows
            });
        })
        .DisableAntiforgery()
        .WithName("InheritanceImportPreview");

        // ── POST /systems/{systemId}/inheritance/import/apply — T003 #142 ─────
        group.MapPost("/systems/{systemId}/inheritance/import/apply", async (
            string systemId,
            Feature043ImportApplyRequest req,
            IBaselineService baselineService,
            Ato.Copilot.Mcp.Services.CapabilityImportService importService,
            CancellationToken ct) =>
        {
            var baseline = await baselineService.GetBaselineAsync(systemId, cancellationToken: ct);
            if (baseline is null)
                return Results.NotFound(new ErrorResponse { Error = "Baseline not found for system.", ErrorCode = "BASELINE_NOT_FOUND" });

            if (!ImportPreviewCache.TryGetValue(req.PreviewToken, out var parsed))
                return Results.BadRequest(new ErrorResponse { Error = "Preview token not found or expired.", ErrorCode = "INVALID_PREVIEW_TOKEN" });

            ImportPreviewCache.Remove(req.PreviewToken);

            var m = req.ColumnMapping;
            var rows = parsed.Rows.Select(row => new CrmImportRow
            {
                ControlId = row.TryGetValue(m.ControlId, out var cid) ? cid : "",
                InheritanceType = row.TryGetValue(m.InheritanceType, out var it) ? it : "",
                Provider = row.TryGetValue(m.Provider, out var pv) ? pv : null,
                CustomerResponsibility = row.TryGetValue(m.CustomerResponsibility, out var cr) ? cr : null,
            }).Where(r => !string.IsNullOrWhiteSpace(r.ControlId)).ToList();

            var result = await importService.ImportCrmAsync(
                "inheritance-import.csv", rows, req.ConflictResolution ?? "overwrite", ct);

            return Results.Ok(result);
        }).WithName("InheritanceImportApply");

        // Feature 045: Old CSP/CRM import endpoints removed — replaced by
        // POST /capabilities/import/csp-profile and POST /capabilities/import/crm

        // ─── Feature 044: Org-Level Inheritance Endpoints ──────────────────────

        // ── GET /inheritance/org-defaults — list org-level inheritance defaults with filters & pagination
        group.MapGet("/inheritance/org-defaults", async (
            [FromQuery] string? family,
            [FromQuery] string? inheritanceType,
            [FromQuery] string? search,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            IOrgInheritanceService orgService = default!,
            CancellationToken ct = default) =>
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 50;
            if (pageSize > 200) pageSize = 200;

            var result = await orgService.GetOrgDefaultsAsync(family, inheritanceType, search, page, pageSize, ct);
            return Results.Ok(result);
        }).WithName("GetOrgInheritanceDefaults");

        // ── POST /inheritance/org-defaults/derive — trigger org-level inheritance derivation
        group.MapPost("/inheritance/org-defaults/derive", async (
            IOrgInheritanceService orgService,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            var result = await orgService.DeriveOrgDefaultsAsync("dashboard-user", ct);

            return Results.Ok(result);
        }).WithName("DeriveOrgInheritanceDefaults");

        // ─── Control Catalog (top-level, not system-scoped) ────────────────────
    }
}
