using System.Text.Json;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Mcp.Services;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Mcp.Endpoints;

public static partial class DashboardEndpoints
{
    private static PackageReadinessAction ReadinessView(string path) =>
        new(true, false, path, "Open", "Read-only navigation; use the authorized source workflow for changes.");

    private static async Task<IResult> ReadReadinessWorkspace(string systemId, HttpRequest request, HttpContext http,
        AtoCopilotContext db, PackageReadinessService service, ICurrentUserService actor, CancellationToken ct)
    {
        var selection = ReadinessSelection(request);
        var system = await db.RegisteredSystems.AsNoTracking().SingleOrDefaultAsync(x => x.Id == systemId && x.IsActive, ct)
            ?? throw new KeyNotFoundException();
        var source = await service.ReadSourceAsync(systemId, selection, actor.CurrentUserId, ct);
        var latest = await ReadinessRuns(db, systemId, selection).OrderByDescending(x => x.EvaluatedAt).ThenBy(x => x.Id).FirstOrDefaultAsync(ct);
        var run = latest == null ? null : ReadinessRunDto(latest, source, await ReadinessPermissions(http, systemId, ct));
        var ready = run?.Outcome == "Ready" && run.Freshness.State == "Current";
        var checks = latest == null ? [] : PackageReadinessService.Checks(latest);
        var packageQuery = db.AuthorizationPackages.AsNoTracking().Where(x => x.RegisteredSystemId == systemId && x.Purpose == selection.Purpose);
        var selectedHash = PackageReadinessService.SelectionHash(selection);
        var retainedOnly = selection.RetainedContext != null;
        if (selection.RetainedContext != null)
        {
            var scopedRuns = ReadinessRuns(db, systemId, selection).Select(x => x.Id);
            packageQuery = packageQuery.Where(x => x.ReadinessRunId != null && scopedRuns.Contains(x.ReadinessRunId));
        }
        var packageCount = await packageQuery.CountAsync(ct);
        // SQLite cannot translate DateTimeOffset ordering. The stored UTC timestamps are ordered in SQL,
        // with explicit tenant/system/selection fences and a bounded result on both providers.
        var packagePage = db.Database.IsSqlite()
            ? db.AuthorizationPackages.FromSqlInterpolated($"""
                SELECT * FROM AuthorizationPackages WHERE TenantId={system.TenantId} AND RegisteredSystemId={systemId}
                AND Purpose={(int)selection.Purpose} AND ({!retainedOnly} OR ReadinessRunId IN
                    (SELECT Id FROM PackageReadinessRuns WHERE TenantId={system.TenantId} AND RegisteredSystemId={systemId}
                     AND Purpose={(int)selection.Purpose} AND SelectionHash={selectedHash}))
                ORDER BY GeneratedAt DESC, Id LIMIT 5
                """)
            : db.AuthorizationPackages.FromSqlInterpolated($"""
                SELECT TOP(5) * FROM AuthorizationPackages WHERE TenantId={system.TenantId} AND RegisteredSystemId={systemId}
                AND Purpose={(int)selection.Purpose} AND ({!retainedOnly}=1 OR ReadinessRunId IN
                    (SELECT Id FROM PackageReadinessRuns WHERE TenantId={system.TenantId} AND RegisteredSystemId={systemId}
                     AND Purpose={(int)selection.Purpose} AND SelectionHash={selectedHash}))
                ORDER BY GeneratedAt DESC, Id
                """);
        var packages = (await packagePage.AsNoTracking().ToListAsync(ct)).OrderByDescending(x => x.GeneratedAt).ThenBy(x => x.Id).ToArray();
        var exports = packages.Take(5).Select(x => new PackageReadinessRecord("package", x.Id, x.Status.ToString(),
            x.GeneratedAt.UtcDateTime, x.Purpose, x.ReadinessSourceHash,
            x.ReadinessSourceHash == null ? "Unknown" : x.ReadinessSourceHash == source.Hash ? "CurrentSource" : "Historical",
            ReadinessView("documents?tab=exports"))).ToArray();
        var packageIds = packageQuery.Select(x => x.Id);
        var exchangeQuery = db.Set<EmassExchangeRecord>().AsNoTracking()
            .Where(x => x.RegisteredSystemId == systemId && packageIds.Contains(x.PackageId));
        var exchangeCount = await exchangeQuery.CountAsync(ct);
        var exchanges = await exchangeQuery.OrderByDescending(x => x.Version).ThenBy(x => x.Id).Take(5).ToListAsync(ct);
        var decisionsQuery = db.AuthorizationDecisions.AsNoTracking().Where(x => x.RegisteredSystemId == systemId);
        if (selection.RetainedContext != null)
            decisionsQuery = decisionsQuery.Where(x => x.Id == selection.RetainedContext.AuthorizationDecisionId);
        var decisionCount = await decisionsQuery.CountAsync(ct);
        var decisions = await decisionsQuery.OrderByDescending(x => x.DecisionDate).ThenBy(x => x.Id).Take(5).ToListAsync(ct);
        var state = source.State == "Unavailable" ? "Unavailable" : run == null ? "NotChecked"
            : run.Outcome == "Failed" ? "Failed" : run.Freshness.State == "Stale" ? "Stale" : run.Outcome;
        var runRecord = run == null ? Array.Empty<PackageReadinessRecord>() :
            [new PackageReadinessRecord("readiness-run", run.Id, run.Outcome, run.EvaluatedAt, selection.Purpose,
                run.SourceHash, run.Freshness.State == "Current" ? "CurrentSource" : "Historical", ReadinessView("documents"))];
        PackageReadinessProgress[] progress =
        [
            new("prepare", state, "Preparation reflects evaluated requirements, not document counts.", runRecord, runRecord.Length, ReadinessView("documents")),
            new("validate", state, run == null ? "No matching evaluation is recorded." : "Retained validation and current source freshness are separate facts.",
                runRecord, runRecord.Length, ReadinessView("documents")),
            new("export", packageCount == 0 ? "NotRecorded" : "Recorded", "Recorded package jobs; Failed or pending jobs are not completed exports.",
                exports, packageCount, ReadinessView("documents?tab=exports")),
            new("emass", exchangeCount == 0 ? "NotRecorded" : "Recorded",
                "Human-recorded eMASS observations only. No live submission connector is established.",
                exchanges.Take(5).Select(x => new PackageReadinessRecord("emass-exchange", x.Id, x.Outcome, x.RecordedAt.UtcDateTime,
                    selection.Purpose, x.PackageHash, "Historical", ReadinessView("emass/status"))).ToArray(),
                exchangeCount, ReadinessView("emass/status")),
            new("decision", decisionCount == 0 ? "NotRecorded" : "Recorded", "Recorded decisions are independent of validation, export and receipt observations.",
                decisions.Take(5).Select(x => new PackageReadinessRecord("authorization-decision", x.Id,
                    $"{x.DecisionType}{(x.ExpirationDate < DateTime.UtcNow ? " (expired)" : "")}{(!x.IsActive ? " (inactive)" : "")}",
                    Utc(x.DecisionDate), null, x.BaselinePackageHash, "Unknown", ReadinessView("authorize"))).ToArray(),
                decisionCount, ReadinessView("authorize"))
        ];
        return Results.Ok(new
        {
            systemId, selection.Purpose, selection.RetainedContext, selectionHash = PackageReadinessService.SelectionHash(selection),
            source, latestRun = run, permissions = new { canValidate = true, validateReason = (string?)null,
                canGenerate = ready, generateReason = ready ? null : "A current Ready evaluation for this purpose and source is required." },
            progress, documents = selection.RetainedContext == null
                ? (await ReadinessDocuments(db, systemId, checks, ct)).Select(x => x with { SourceState = run?.Freshness.State ?? "NotChecked" }).ToArray()
                : await RetainedReadinessDocuments(db, systemId, selection, source, ct),
            rmf = await ReadinessRmf(db, system, ct)
        });
    }

    private static async Task<IReadOnlyList<PackageReadinessDocument>> RetainedReadinessDocuments(AtoCopilotContext db,
        string systemId, PackageReadinessSelection selection, PackageReadinessService.SourceState source, CancellationToken ct)
    {
        var pins = selection.RetainedContext!;
        var baseline = await db.AuthorizationPackages.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == pins.BaselinePackageId && x.RegisteredSystemId == systemId, ct);
        var records = baseline == null ? Array.Empty<PackageReadinessRecord>() :
            [new PackageReadinessRecord("package", baseline.Id, baseline.Status.ToString(), baseline.GeneratedAt.UtcDateTime,
                baseline.Purpose, baseline.ContentHash, source.State == "Available" ? "CurrentSource" : "Unknown", ReadinessView("documents?tab=exports"))];
        var documents = new List<PackageReadinessDocument>
        {
            new("retained-baseline", "Selected retained baseline package", baseline == null ? "Missing" : "Present",
                baseline?.Status.ToString(), null, source.State == "Available" ? "Pinned" : "Unavailable", null,
                records.Length, records, ReadinessView("documents?tab=exports"))
        };
        if (pins.ChangePreviewId is { } id)
        {
            var preview = await db.SspExports.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.SystemId == systemId, ct);
            var changes = preview == null ? Array.Empty<PackageReadinessRecord>() :
                [new PackageReadinessRecord("ssp-preview", preview.Id.ToString(), preview.Status, preview.GeneratedAt.UtcDateTime,
                    null, preview.ContentHash, source.State == "Available" ? "CurrentSource" : "Unknown", ReadinessView("documents/preview"))];
            documents.Add(new("reviewed-change", "Selected reviewed SSP change", preview == null ? "Missing" : "Present",
                preview?.Status, null, source.State == "Available" ? "Pinned" : "Unavailable", null,
                changes.Length, changes, ReadinessView("documents/preview")));
        }
        return documents;
    }

    private static async Task<IReadOnlyList<PackageReadinessDocument>> ReadinessDocuments(AtoCopilotContext db, string systemId,
        IReadOnlyList<PackageReadinessCheck> checks, CancellationToken ct)
    {
        var documents = new List<PackageReadinessDocument>();
        void Add(string kind, string title, IEnumerable<PackageReadinessRecord> records, string path, string? review = null, int? total = null)
        {
            var list = records.ToArray();
            var recordCount = total ?? list.Length;
            var relevant = checks.Where(x => x.Category == kind).ToArray();
            var outcome = relevant.Length == 0 ? null : OrderedReadinessChecks(relevant).First().Outcome;
            documents.Add(new(kind, title, recordCount == 0 ? "Missing" : "Present",
                recordCount == 1 ? list[0].Status : recordCount > 1 ? "Multiple records" : null, review, null,
                outcome, recordCount, list.Take(5).ToArray(), ReadinessView(path)));
        }
        async Task AddPage<T>(string kind, string title, IQueryable<T> query, Func<T, PackageReadinessRecord> project,
            string path, string? review = null) where T : class =>
            Add(kind, title, (await query.AsNoTracking().Take(5).ToListAsync(ct)).Select(project), path, review, await query.CountAsync(ct));
        PackageReadinessRecord Record(string kind, string id, string status, DateTime? at, string path) =>
            new(kind, id, status, at, null, null, "Unknown", ReadinessView(path));
        await AddPage("ssp", "SSP section records", db.SspSections.Where(x => x.RegisteredSystemId == systemId).OrderBy(x => x.SectionNumber),
            x => Record("ssp-section", x.Id, x.Status.ToString(), null, "narratives"), "narratives");
        await AddPage("sap", "Assessment plans", db.SecurityAssessmentPlans.Where(x => x.RegisteredSystemId == systemId).OrderByDescending(x => x.GeneratedAt),
            x => Record("sap", x.Id, x.Status.ToString(), Utc(x.GeneratedAt), "assessments?tab=plan"), "assessments?tab=plan");
        await AddPage("sar", "Assessment reports", db.SecurityAssessmentReports.Where(x => x.RegisteredSystemId == systemId).OrderByDescending(x => x.CreatedAt),
            x => Record("sar", x.Id, x.Status.ToString(), Utc(x.CreatedAt), "assessments"), "assessments");
        await AddPage("poam", "POA&M records", db.PoamItems.Where(x => x.RegisteredSystemId == systemId).OrderBy(x => x.Id),
            x => Record("poam", x.Id, x.Status.ToString(), null, "poam"), "poam");
        await AddPage("evidence", "Evidence artifacts", db.EvidenceArtifacts.Where(x => x.RegisteredSystemId == systemId && !x.IsDeleted).OrderByDescending(x => x.UploadedAt),
            x => Record("evidence", x.Id, x.NarrativeType.ToString(), Utc(x.UploadedAt), "evidence"), "evidence");
        await AddPage("inventory", "Hardware/software inventory", db.InventoryItems.Where(x => x.RegisteredSystemId == systemId).OrderBy(x => x.Id),
            x => Record("inventory", x.Id, "Recorded", null, "security-capabilities/inventory?tab=hardware-software"), "security-capabilities/inventory?tab=hardware-software");
        var pta = await db.PrivacyThresholdAnalyses.AsNoTracking().Where(x => x.RegisteredSystemId == systemId).ToListAsync(ct);
        var pia = await db.PrivacyImpactAssessments.AsNoTracking().Where(x => x.RegisteredSystemId == systemId).ToListAsync(ct);
        Add("privacy", "Privacy records", pta.Select(x => Record("pta", x.Id, x.Determination.ToString(), null, "legal"))
            .Concat(pia.Select(x => Record("pia", x.Id, x.Status.ToString(), null, "legal"))), "legal");
        var baseline = await db.ControlBaselines.AsNoTracking().Where(x => x.RegisteredSystemId == systemId).ToListAsync(ct);
        Add("responsibility", "Baseline-backed responsibility sources", baseline.Select(x => Record("baseline", x.Id, x.BaselineLevel, null, "inheritance/subscriptions")),
            "inheritance/subscriptions", "Baseline availability is not confirmation of inherited responsibility.");
        return documents;
    }

    private static async Task<PackageReadinessRmf> ReadinessRmf(AtoCopilotContext db, RegisteredSystem system, CancellationToken ct)
    {
        var candidates = await db.AuditLogs.AsNoTracking().Where(x => x.Action == "RmfPhase.Transitioned" && x.Details.Contains(system.Id))
            .OrderByDescending(x => x.Timestamp).ToListAsync(ct);
        var transitions = new List<PackageReadinessTransition>();
        foreach (var entry in candidates.Where(x => x.AffectedResources.Contains(system.Id)))
        {
            var details = JsonSerializer.Deserialize<RmfPhaseTransitionAuditDetails>(entry.Details, PackageReadinessService.Json)
                ?? throw new InvalidDataException("Recorded RMF transition details are invalid.");
            if (details.SystemId == system.Id)
                transitions.Add(new(entry.Id, details.PreviousPhase, details.TargetPhase, Utc(entry.Timestamp), entry.UserId));
        }
        return new(system.CurrentRmfStep.ToString(), transitions.Take(5).ToArray(), transitions.Count);
    }
}
