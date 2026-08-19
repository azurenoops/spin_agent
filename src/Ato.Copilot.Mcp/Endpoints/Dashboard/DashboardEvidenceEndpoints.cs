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

// ─── #648 Decomposition: Evidence domain routes ─────────────────────────────
public static partial class DashboardEndpoints
{
    private static void MapEvidenceRoutes(IEndpointRouteBuilder group, IEndpointRouteBuilder app)
    {
        group.MapPost("/systems/{systemId}/evidence", async (
                string systemId,
                HttpRequest request,
                IEvidenceArtifactService evidenceService,
                HttpContext httpContext,
                CancellationToken ct) =>
            {
                if (!request.HasFormContentType)
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "Request must be multipart/form-data",
                        ErrorCode = "INVALID_CONTENT_TYPE",
                    });

                var form = await request.ReadFormAsync(ct);
                var file = form.Files.GetFile("file");

                if (file is null || file.Length == 0)
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "File is required and must not be empty",
                        ErrorCode = "MISSING_FILE",
                    });

                var categoryStr = form["artifactCategory"].ToString();
                if (string.IsNullOrWhiteSpace(categoryStr))
                    categoryStr = form["category"].ToString();
                if (string.IsNullOrWhiteSpace(categoryStr) ||
                    !Enum.TryParse<ArtifactCategory>(categoryStr, ignoreCase: true, out var category))
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "Valid artifactCategory is required",
                        ErrorCode = "INVALID_CATEGORY",
                    });

                var controlImplementationId = form["controlImplementationId"].ToString();
                var securityCapabilityId = form["securityCapabilityId"].ToString();

                if (string.IsNullOrWhiteSpace(controlImplementationId))
                    controlImplementationId = null;
                if (string.IsNullOrWhiteSpace(securityCapabilityId))
                    securityCapabilityId = null;

                if (controlImplementationId is null && securityCapabilityId is null)
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "Either controlImplementationId or securityCapabilityId is required",
                        ErrorCode = "MISSING_TARGET",
                    });

                var description = form["description"].ToString();
                if (string.IsNullOrWhiteSpace(description))
                    description = null;

                var collectionMethod = CollectionMethod.Manual;
                var methodStr = form["collectionMethod"].ToString();
                if (!string.IsNullOrWhiteSpace(methodStr))
                    Enum.TryParse<CollectionMethod>(methodStr, ignoreCase: true, out collectionMethod);

                var userId = httpContext.User?.Identity?.Name ?? "dashboard-user";

                try
                {
                    using var stream = file.OpenReadStream();
                    var artifact = await evidenceService.UploadAsync(
                        systemId,
                        file.FileName,
                        file.ContentType,
                        stream,
                        category,
                        userId,
                        controlImplementationId,
                        securityCapabilityId,
                        description,
                        collectionMethod,
                        ct);

                    return Results.Ok(new
                    {
                        artifact.Id,
                        artifact.FileName,
                        artifact.ContentType,
                        artifact.FileSizeBytes,
                        ArtifactCategory = artifact.ArtifactCategory.ToString(),
                        CollectionMethod = artifact.CollectionMethod.ToString(),
                        artifact.ContentHash,
                        artifact.UploadedBy,
                        artifact.UploadedAt,
                        artifact.ControlImplementationId,
                        artifact.SecurityCapabilityId,
                        artifact.Description,
                    });
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = ex.Message,
                        ErrorCode = "VALIDATION_ERROR",
                    });
                }
                catch (KeyNotFoundException ex)
                {
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = ex.Message,
                        ErrorCode = "NOT_FOUND",
                    });
                }
            })
            .WithName("UploadEvidence")
            .DisableAntiforgery();

        // ─── Control Evidence (US1 T015) ─────────────────────────────────────

        // T015: GET /systems/{systemId}/controls/{controlId}/evidence
        group.MapGet("/systems/{systemId}/controls/{controlId}/evidence", async (
                string systemId,
                string controlId,
                IEvidenceArtifactService evidenceService,
                AtoCopilotContext db,
                CancellationToken ct) =>
            {
                // Find control implementations for this control in this system
                var controlImpls = await db.ControlImplementations
                    .Where(ci => ci.RegisteredSystemId == systemId && ci.ControlId == controlId)
                    .Select(ci => ci.Id)
                    .ToListAsync(ct);

                // Direct evidence: artifacts attached to this control's implementations
                var directArtifacts = new List<EvidenceArtifact>();
                foreach (var implId in controlImpls)
                {
                    var artifacts = await evidenceService.ListForControlAsync(implId, ct);
                    directArtifacts.AddRange(artifacts);
                }

                // Inherited evidence: from capabilities linked to this control
                var capabilityIds = await db.CapabilityControlMappings
                    .Where(ccm => ccm.ControlId == controlId &&
                                  (ccm.RegisteredSystemId == null || ccm.RegisteredSystemId == systemId))
                    .Select(ccm => ccm.SecurityCapabilityId)
                    .Distinct()
                    .ToListAsync(ct);

                var inheritedArtifacts = await db.EvidenceArtifacts
                    .Where(ea => ea.RegisteredSystemId == systemId &&
                                 ea.SecurityCapabilityId != null &&
                                 capabilityIds.Contains(ea.SecurityCapabilityId) &&
                                 !ea.IsDeleted)
                    .Include(ea => ea.SecurityCapability)
                    .ToListAsync(ct);

                // Automated evidence: from ComplianceEvidence table
                var automatedEvidence = await db.Evidence
                    .Where(ce => ce.ControlId == controlId &&
                                 db.ControlImplementations.Any(ci =>
                                     ci.ControlId == ce.ControlId && ci.RegisteredSystemId == systemId))
                    .ToListAsync(ct);

                return Results.Ok(new
                {
                    direct = directArtifacts.Select(a => new
                    {
                        a.Id,
                        Source = "Manual",
                        a.FileName,
                        a.ContentType,
                        a.FileSizeBytes,
                        ArtifactCategory = a.ArtifactCategory.ToString(),
                        CollectionMethod = a.CollectionMethod.ToString(),
                        a.ContentHash,
                        a.UploadedBy,
                        a.UploadedAt,
                        a.ControlImplementationId,
                        a.Description,
                    }),
                    inherited = inheritedArtifacts.Select(a => new
                    {
                        a.Id,
                        Source = "Manual",
                        a.FileName,
                        a.ContentType,
                        a.FileSizeBytes,
                        ArtifactCategory = a.ArtifactCategory.ToString(),
                        CollectionMethod = a.CollectionMethod.ToString(),
                        a.ContentHash,
                        a.UploadedBy,
                        a.UploadedAt,
                        a.SecurityCapabilityId,
                        InheritedFromCapability = a.SecurityCapability?.Name,
                        a.Description,
                    }),
                    automated = automatedEvidence.Select(ce => new
                    {
                        ce.Id,
                        Source = "Automated",
                        FileName = (string?)null,
                        ContentType = (string?)null,
                        FileSizeBytes = (long?)null,
                        ArtifactCategory = ce.EvidenceCategory.ToString(),
                        ce.ControlId,
                        Description = $"Automated evidence for {ce.ControlId}",
                        UploadedBy = "ATO Copilot (automated)",
                        UploadedAt = ce.CollectedAt,
                        ce.ContentHash,
                    }),
                });
            })
            .WithName("GetControlEvidence");

        // ─── Evidence Download (US1 T016) ────────────────────────────────────

        // T016: GET /systems/{systemId}/evidence/{evidenceId}/download
        group.MapGet("/systems/{systemId}/evidence/{evidenceId}/download", async (
                string systemId,
                string evidenceId,
                IEvidenceArtifactService evidenceService,
                CancellationToken ct) =>
            {
                var result = await evidenceService.DownloadAsync(evidenceId, ct);
                if (result is null)
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = "Evidence not found or no file available",
                        ErrorCode = "NOT_FOUND",
                    });

                var (stream, fileName, contentType) = result.Value;
                return Results.File(stream, contentType, fileName);
            })
            .WithName("DownloadEvidence");

        // ─── Evidence Repository (US2 T020) ─────────────────────────────────

        // T020: GET /systems/{systemId}/evidence — paginated evidence list
        group.MapGet("/systems/{systemId}/evidence", async (
                string systemId,
                int? page,
                int? pageSize,
                string? search,
                string? controlFamily,
                string? category,
                string? source,
                string? dateFrom,
                string? dateTo,
                string? sortBy,
                string? sortOrder,
                IEvidenceArtifactService evidenceService,
                AtoCopilotContext db,
                CancellationToken ct) =>
            {
                var p = Math.Max(page ?? 1, 1);
                var ps = Math.Clamp(pageSize ?? 50, 1, 100);
                var desc = string.Equals(sortOrder, "asc", StringComparison.OrdinalIgnoreCase) ? false : true;
                ArtifactCategory? catFilter = null;
                if (!string.IsNullOrWhiteSpace(category) &&
                    Enum.TryParse<ArtifactCategory>(category, ignoreCase: true, out var parsedCat))
                    catFilter = parsedCat;

                var showManual = source is null || string.Equals(source, "manual", StringComparison.OrdinalIgnoreCase);
                var showAutomated = source is null || string.Equals(source, "automated", StringComparison.OrdinalIgnoreCase);

                var items = new List<object>();
                var totalCount = 0;

                // Manual evidence
                if (showManual)
                {
                    var (manualItems, manualTotal) = await evidenceService.ListForSystemAsync(
                        systemId, p, ps, search, controlFamily, catFilter,
                        sortBy ?? "uploadedAt", desc, ct);
                    totalCount += manualTotal;

                    items.AddRange(manualItems.Select(a => new
                    {
                        a.Id,
                        Source = "Manual",
                        a.FileName,
                        a.ContentType,
                        a.FileSizeBytes,
                        ArtifactCategory = a.ArtifactCategory.ToString(),
                        ControlId = (string?)null,
                        a.ControlImplementationId,
                        a.SecurityCapabilityId,
                        a.Description,
                        a.UploadedBy,
                        a.UploadedAt,
                        a.ContentHash,
                    }));
                }

                // Automated evidence
                if (showAutomated)
                {
                    var autoQuery = db.Evidence
                        .Where(ce => db.ControlImplementations.Any(ci =>
                            ci.ControlId == ce.ControlId && ci.RegisteredSystemId == systemId));

                    if (!string.IsNullOrWhiteSpace(controlFamily))
                        autoQuery = autoQuery.Where(ce => ce.ControlId.StartsWith(controlFamily));

                    if (!string.IsNullOrWhiteSpace(search))
                        autoQuery = autoQuery.Where(ce =>
                            ce.ControlId.Contains(search) || ce.Description.Contains(search));

                    if (!string.IsNullOrWhiteSpace(dateFrom) && DateTime.TryParse(dateFrom, out var from))
                        autoQuery = autoQuery.Where(ce => ce.CollectedAt >= from);
                    if (!string.IsNullOrWhiteSpace(dateTo) && DateTime.TryParse(dateTo, out var to))
                        autoQuery = autoQuery.Where(ce => ce.CollectedAt <= to);

                    var autoTotal = await autoQuery.CountAsync(ct);
                    totalCount += autoTotal;

                    var autoItems = await autoQuery
                        .OrderByDescending(ce => ce.CollectedAt)
                        .Skip((p - 1) * ps)
                        .Take(ps)
                        .ToListAsync(ct);

                    items.AddRange(autoItems.Select(ce => new
                    {
                        ce.Id,
                        Source = "Automated",
                        FileName = (string?)null,
                        ContentType = (string?)null,
                        FileSizeBytes = (long?)null,
                        ArtifactCategory = ce.EvidenceCategory.ToString(),
                        ControlId = (string?)ce.ControlId,
                        ControlImplementationId = (string?)null,
                        SecurityCapabilityId = (string?)null,
                        Description = (string?)ce.Description,
                        UploadedBy = "ATO Copilot (automated)",
                        UploadedAt = ce.CollectedAt,
                        ContentHash = (string?)ce.ContentHash,
                    }));
                }

                return Results.Ok(new { items, totalCount, page = p, pageSize = ps });
            })
            .WithName("ListEvidence");

        // ─── Evidence Summary (US2 T021) ─────────────────────────────────────

        // T021: GET /systems/{systemId}/evidence/summary
        group.MapGet("/systems/{systemId}/evidence/summary", async (
                string systemId,
                IEvidenceArtifactService evidenceService,
                CancellationToken ct) =>
            {
                var summary = await evidenceService.GetSummaryAsync(systemId, ct);
                return Results.Ok(new
                {
                    summary.TotalCount,
                    summary.ManualCount,
                    summary.AutomatedCount,
                    summary.ControlsWithEvidence,
                    summary.TotalControls,
                    summary.CoveragePercentage,
                });
            })
            .WithName("GetEvidenceSummary");

        // ─── Evidence Detail (US2 T022) ──────────────────────────────────────

        // T022: GET /systems/{systemId}/evidence/{evidenceId}
        group.MapGet("/systems/{systemId}/evidence/{evidenceId}", async (
                string systemId,
                string evidenceId,
                IEvidenceArtifactService evidenceService,
                AtoCopilotContext db,
                CancellationToken ct) =>
            {
                // Try manual evidence first
                var artifact = await evidenceService.GetByIdAsync(evidenceId, ct);
                if (artifact is not null && artifact.RegisteredSystemId == systemId)
                {
                    var versions = await db.EvidenceVersions
                        .Where(v => v.EvidenceArtifactId == evidenceId)
                        .OrderByDescending(v => v.ReplacedAt)
                        .Select(v => new
                        {
                            v.Id,
                            v.FileName,
                            v.FileSizeBytes,
                            v.ContentHash,
                            v.ReplacedBy,
                            v.ReplacedAt,
                            v.PurgeAfter,
                            v.IsFilePurged,
                        })
                        .ToListAsync(ct);

                    // Resolve controlId from ControlImplementation
                    string? controlId = null;
                    if (artifact.ControlImplementationId is not null)
                    {
                        controlId = await db.ControlImplementations
                            .Where(ci => ci.Id == artifact.ControlImplementationId)
                            .Select(ci => ci.ControlId)
                            .FirstOrDefaultAsync(ct);
                    }

                    return Results.Ok(new
                    {
                        artifact.Id,
                        Source = "Manual",
                        artifact.FileName,
                        artifact.ContentType,
                        artifact.FileSizeBytes,
                        artifact.StoragePath,
                        ArtifactCategory = artifact.ArtifactCategory.ToString(),
                        CollectionMethod = artifact.CollectionMethod.ToString(),
                        ControlId = controlId,
                        artifact.ControlImplementationId,
                        artifact.SecurityCapabilityId,
                        CapabilityName = artifact.SecurityCapability?.Name,
                        artifact.Description,
                        artifact.UploadedBy,
                        artifact.UploadedAt,
                        artifact.ContentHash,
                        Versions = versions,
                    });
                }

                // Try automated evidence
                var compEvidence = await db.Evidence
                    .FirstOrDefaultAsync(ce => ce.Id == evidenceId, ct);

                if (compEvidence is not null)
                {
                    return Results.Ok(new
                    {
                        compEvidence.Id,
                        Source = "Automated",
                        FileName = (string?)null,
                        ContentType = (string?)null,
                        FileSizeBytes = (long?)null,
                        StoragePath = (string?)null,
                        ArtifactCategory = compEvidence.EvidenceCategory.ToString(),
                        CollectionMethod = (string?)compEvidence.CollectionMethod,
                        ControlId = (string?)compEvidence.ControlId,
                        ControlImplementationId = (string?)null,
                        SecurityCapabilityId = (string?)null,
                        CapabilityName = (string?)null,
                        Description = (string?)compEvidence.Description,
                        UploadedBy = "ATO Copilot (automated)",
                        UploadedAt = compEvidence.CollectedAt,
                        ContentHash = (string?)compEvidence.ContentHash,
                        Versions = Array.Empty<object>(),
                    });
                }

                return Results.NotFound(new ErrorResponse
                {
                    Error = "Evidence not found",
                    ErrorCode = "NOT_FOUND",
                });
            })
            .WithName("GetEvidenceDetail");

        // ─── Collect Evidence Trigger (US5 T033) ────────────────────────────

        // T033: POST /systems/{systemId}/controls/{controlId}/collect-evidence
        group.MapPost("/systems/{systemId}/controls/{controlId}/collect-evidence", async (
                string systemId,
                string controlId,
                IEvidenceStorageService evidenceStorageService,
                AtoCopilotContext db,
                CancellationToken ct) =>
            {
                // Resolve a subscription ID from the system's Azure profile
                var system = await db.RegisteredSystems
                    .Where(s => s.Id == systemId)
                    .FirstOrDefaultAsync(ct);

                var subscriptionId = system?.AzureProfile?.SubscriptionIds?.FirstOrDefault();

                if (string.IsNullOrEmpty(subscriptionId))
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "No Azure subscription found for this system. Configure an authorization boundary with a subscription ID.",
                        ErrorCode = "NO_SUBSCRIPTION",
                    });

                try
                {
                    var evidence = await evidenceStorageService.CollectEvidenceAsync(
                        controlId, subscriptionId, cancellationToken: ct);

                    return Results.Ok(new
                    {
                        EvidenceId = evidence.Id,
                        evidence.ControlId,
                        evidence.EvidenceType,
                        CollectedAt = evidence.CollectedAt,
                        evidence.ContentHash,
                    });
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return Results.StatusCode(502);
                }
            })
            .WithName("CollectEvidence");

        // ─── Delete Evidence (US6 T035) ─────────────────────────────────────

        // T035: DELETE /systems/{systemId}/evidence/{evidenceId}
        group.MapDelete("/systems/{systemId}/evidence/{evidenceId}", async (
                string systemId,
                string evidenceId,
                IEvidenceArtifactService evidenceService,
                HttpContext httpContext,
                CancellationToken ct) =>
            {
                var userId = httpContext.User?.Identity?.Name ?? "dashboard-user";
                var deleted = await evidenceService.DeleteAsync(evidenceId, userId, ct);

                return deleted
                    ? Results.NoContent()
                    : Results.NotFound(new ErrorResponse
                    {
                        Error = "Evidence not found",
                        ErrorCode = "NOT_FOUND",
                    });
            })
            .WithName("DeleteEvidence");

        // ─── Replace Evidence (US6 T036) ─────────────────────────────────────

        // T036: PUT /systems/{systemId}/evidence/{evidenceId}
        group.MapPut("/systems/{systemId}/evidence/{evidenceId}", async (
                string systemId,
                string evidenceId,
                HttpRequest request,
                IEvidenceArtifactService evidenceService,
                HttpContext httpContext,
                CancellationToken ct) =>
            {
                if (!request.HasFormContentType)
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "Request must be multipart/form-data",
                        ErrorCode = "INVALID_CONTENT_TYPE",
                    });

                var form = await request.ReadFormAsync(ct);
                var file = form.Files.GetFile("file");

                if (file is null || file.Length == 0)
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "File is required",
                        ErrorCode = "MISSING_FILE",
                    });

                var description = form["description"].ToString();
                if (string.IsNullOrWhiteSpace(description))
                    description = null;

                var userId = httpContext.User?.Identity?.Name ?? "dashboard-user";

                try
                {
                    using var stream = file.OpenReadStream();
                    var artifact = await evidenceService.ReplaceAsync(
                        evidenceId,
                        file.FileName,
                        file.ContentType,
                        stream,
                        userId,
                        description: description,
                        cancellationToken: ct);

                    return Results.Ok(new
                    {
                        artifact.Id,
                        artifact.FileName,
                        artifact.ContentType,
                        artifact.FileSizeBytes,
                        ArtifactCategory = artifact.ArtifactCategory.ToString(),
                        CollectionMethod = artifact.CollectionMethod.ToString(),
                        artifact.ContentHash,
                        artifact.UploadedBy,
                        artifact.UploadedAt,
                        artifact.ControlImplementationId,
                        artifact.SecurityCapabilityId,
                        artifact.Description,
                    });
                }
                catch (KeyNotFoundException ex)
                {
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = ex.Message,
                        ErrorCode = "NOT_FOUND",
                    });
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = ex.Message,
                        ErrorCode = "VALIDATION_ERROR",
                    });
                }
            })
            .WithName("ReplaceEvidence")
            .DisableAntiforgery();

        // ─── Version History (US6 T037) ──────────────────────────────────────

        // T037: GET /systems/{systemId}/evidence/{evidenceId}/versions
        group.MapGet("/systems/{systemId}/evidence/{evidenceId}/versions", async (
                string systemId,
                string evidenceId,
                AtoCopilotContext db,
                CancellationToken ct) =>
            {
                var versions = await db.EvidenceVersions
                    .Where(v => v.EvidenceArtifactId == evidenceId)
                    .OrderByDescending(v => v.ReplacedAt)
                    .Select(v => new
                    {
                        v.Id,
                        v.FileName,
                        v.FileSizeBytes,
                        v.ContentHash,
                        v.ReplacedBy,
                        v.ReplacedAt,
                        v.PurgeAfter,
                        v.IsFilePurged,
                    })
                    .ToListAsync(ct);
                return Results.Ok(versions);
            })
            .WithName("GetEvidenceVersions");

        // T037b: GET /systems/{systemId}/evidence/{evidenceId}/versions/{versionId}/download
        group.MapGet("/systems/{systemId}/evidence/{evidenceId}/versions/{versionId}/download", async (
                string systemId,
                string evidenceId,
                string versionId,
                AtoCopilotContext db,
                Core.Interfaces.Storage.IFileStorageProvider storageProvider,
                CancellationToken ct) =>
            {
                var version = await db.EvidenceVersions
                    .FirstOrDefaultAsync(v => v.Id == versionId && v.EvidenceArtifactId == evidenceId, ct);

                if (version is null)
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = "Version not found",
                        ErrorCode = "NOT_FOUND",
                    });

                if (version.IsFilePurged)
                    return Results.StatusCode(410); // Gone

                var exists = await storageProvider.ExistsAsync(version.StoragePath, ct);
                if (!exists)
                    return Results.StatusCode(410);

                var stream = await storageProvider.GetAsync(version.StoragePath, ct);
                return Results.File(stream, "application/octet-stream", version.FileName);
            })
            .WithName("DownloadEvidenceVersion");

        // ── GET /evidence/settings ─────────────────────────────────────────
        group.MapGet("/evidence/settings", (IConfiguration configuration) =>
            {
                return Results.Ok(new
                {
                    StorageProvider = configuration.GetValue<string>("Evidence:StorageProvider") ?? "Local",
                    RetentionDays = configuration.GetValue<int>("Evidence:RetentionDays") is > 0 and var rd ? rd : 365,
                    LocalStoragePath = configuration.GetValue<string>("Evidence:LocalStoragePath") ?? "/data/evidence",
                    PurgeIntervalHours = configuration.GetValue<int>("Evidence:PurgeIntervalHours") is > 0 and var pi ? pi : 24,
                });
            })
            .WithName("GetEvidenceSettings");

        // ─── Issue #418 — Enhanced Evidence Automation Endpoints ──────────────

        // POST /evidence/correlate — map an evidence source to a NIST control
        group.MapPost("/evidence/correlate", async (
                [FromBody] EvidenceCorrelateRequest req,
                IEvidenceCorrelationEngine correlationEngine,
                HttpContext httpContext,
                CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(req.ControlId) || string.IsNullOrWhiteSpace(req.EvidenceReferenceId))
                    return Results.BadRequest(new ErrorResponse { Error = "ControlId and EvidenceReferenceId are required", ErrorCode = "VALIDATION_ERROR" });

                var actor = httpContext.User?.Identity?.Name ?? "dashboard-user";
                var mapping = await correlationEngine.CorrelateEvidenceAsync(
                    req.ControlId,
                    req.SubscriptionId ?? string.Empty,
                    req.EvidenceReferenceId,
                    req.SourceType,
                    actor,
                    req.Note,
                    ct);

                return Results.Created($"/evidence/{req.SubscriptionId}/mappings/{req.ControlId}", new
                {
                    mapping.Id,
                    mapping.ControlId,
                    mapping.SubscriptionId,
                    mapping.EvidenceReferenceId,
                    SourceType = mapping.EvidenceSourceType.ToString(),
                    mapping.CorrelationScore,
                    mapping.MappingNote,
                    mapping.MappedBy,
                    mapping.MappedAt
                });
            })
            .WithName("CorrelateEvidence");

        // GET /evidence/{subscriptionId}/mappings/{controlId} — all evidence for a control
        group.MapGet("/evidence/{subscriptionId}/mappings/{controlId}", async (
                string subscriptionId,
                string controlId,
                IEvidenceCorrelationEngine correlationEngine,
                CancellationToken ct) =>
            {
                var mappings = await correlationEngine.GetMappingsForControlAsync(controlId, subscriptionId, ct);
                return Results.Ok(mappings.Select(m => new
                {
                    m.Id,
                    m.ControlId,
                    m.SubscriptionId,
                    m.EvidenceReferenceId,
                    SourceType = m.EvidenceSourceType.ToString(),
                    m.CorrelationScore,
                    m.MappingNote,
                    m.MappedBy,
                    m.MappedAt
                }));
            })
            .WithName("GetControlEvidenceMappings");

        // GET /evidence/{subscriptionId}/stale — stale evidence requiring re-collection
        group.MapGet("/evidence/{subscriptionId}/stale", async (
                string subscriptionId,
                IEvidenceFreshnessService freshnessService,
                CancellationToken ct) =>
            {
                var stale = await freshnessService.GetStaleEvidenceAsync(subscriptionId, ct);
                return Results.Ok(stale.Select(r => new
                {
                    r.Id,
                    r.ControlId,
                    r.SubscriptionId,
                    r.LastCollectedAt,
                    r.FreshnessWindowHours,
                    StaleAfter = r.LastCollectedAt.AddHours(r.FreshnessWindowHours),
                    IsStale = true,
                    SourceType = r.EvidenceSourceType.ToString()
                }));
            })
            .WithName("GetStaleEvidence");

        // GET /evidence/{subscriptionId}/freshness/{controlId} — freshness record
        group.MapGet("/evidence/{subscriptionId}/freshness/{controlId}", async (
                string subscriptionId,
                string controlId,
                IEvidenceFreshnessService freshnessService,
                CancellationToken ct) =>
            {
                var record = await freshnessService.GetFreshnessAsync(controlId, subscriptionId, ct);
                if (record is null)
                    return Results.NotFound(new ErrorResponse { Error = "No freshness record found", ErrorCode = "NOT_FOUND" });

                return Results.Ok(new
                {
                    record.Id,
                    record.ControlId,
                    record.SubscriptionId,
                    record.LastCollectedAt,
                    record.FreshnessWindowHours,
                    StaleAfter = record.LastCollectedAt.AddHours(record.FreshnessWindowHours),
                    record.IsStale,
                    SourceType = record.EvidenceSourceType.ToString()
                });
            })
            .WithName("GetEvidenceFreshness");

        // GET /evidence/{subscriptionId}/audit-trail — queryable audit trail
        group.MapGet("/evidence/{subscriptionId}/audit-trail", async (
                string subscriptionId,
                [FromQuery] string? controlId,
                [FromQuery] int days,
                IEvidenceAuditService auditService,
                CancellationToken ct) =>
            {
                if (days <= 0) days = 30;
                var events = await auditService.GetAuditTrailAsync(controlId, subscriptionId, days, ct);
                return Results.Ok(events.Select(e => new
                {
                    e.Id,
                    EventType = e.EventType.ToString(),
                    e.ControlId,
                    e.SubscriptionId,
                    e.ActorId,
                    e.Description,
                    e.OccurredAt,
                    e.Metadata
                }));
            })
            .WithName("GetEvidenceAuditTrail");

        // ═══════════════════════════════════════════════════════════════════════
        // POA&M Management Endpoints (Feature 039)
        // ═══════════════════════════════════════════════════════════════════════


        // ── GET /systems/{systemId}/poam — list POA&M items (paginated, filtered)
    }
}
