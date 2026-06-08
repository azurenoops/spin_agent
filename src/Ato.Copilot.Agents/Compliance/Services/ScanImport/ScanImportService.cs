// ═══════════════════════════════════════════════════════════════════════════
// Feature 017 — SCAP/STIG Viewer Import: Core Import Service
// Implements IScanImportService — CKL/XCCDF import, CKL export, import mgmt.
// ═══════════════════════════════════════════════════════════════════════════

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Services.ScanImport;

/// <summary>
/// Core import orchestration service for SCAP/STIG scan results.
/// Parses CKL/XCCDF XML, resolves STIG→CCI→NIST, creates findings and
/// effectiveness records, handles conflict resolution and dry-run mode.
/// </summary>
public class ScanImportService : IScanImportService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IStigKnowledgeService _stigService;
    private readonly IBaselineService _baselineService;
    private readonly IRmfLifecycleService _rmfService;
    private readonly IAssessmentArtifactService _artifactService;
    private readonly ICklParser _cklParser;
    private readonly IXccdfParser _xccdfParser;
    private readonly ICklGenerator _cklGenerator;
    private readonly ISystemSubscriptionResolver _subscriptionResolver;
    private readonly PrismaCsvParser _prismaCsvParser;
    private readonly PrismaApiJsonParser _prismaApiJsonParser;
    private readonly INessusParser _nessusParser;
    private readonly INessusControlMapper _nessusControlMapper;
    private readonly ILogger<ScanImportService> _logger;

    public ScanImportService(
        IServiceScopeFactory scopeFactory,
        IStigKnowledgeService stigService,
        IBaselineService baselineService,
        IRmfLifecycleService rmfService,
        IAssessmentArtifactService artifactService,
        ICklParser cklParser,
        IXccdfParser xccdfParser,
        ICklGenerator cklGenerator,
        ISystemSubscriptionResolver subscriptionResolver,
        PrismaCsvParser prismaCsvParser,
        PrismaApiJsonParser prismaApiJsonParser,
        INessusParser nessusParser,
        INessusControlMapper nessusControlMapper,
        ILogger<ScanImportService> logger)
    {
        _scopeFactory = scopeFactory;
        _stigService = stigService;
        _baselineService = baselineService;
        _rmfService = rmfService;
        _artifactService = artifactService;
        _cklParser = cklParser;
        _xccdfParser = xccdfParser;
        _cklGenerator = cklGenerator;
        _subscriptionResolver = subscriptionResolver;
        _prismaCsvParser = prismaCsvParser;
        _prismaApiJsonParser = prismaApiJsonParser;
        _nessusParser = nessusParser;
        _nessusControlMapper = nessusControlMapper;
        _logger = logger;
    }

    // ─── ImportCklAsync (T014, T015, T019, T020, T021, T025–T027) ────────

    /// <inheritdoc />
    public async Task<ImportResult> ImportCklAsync(
        string systemId,
        string? assessmentId,
        byte[] fileContent,
        string fileName,
        ImportConflictResolution resolution,
        bool dryRun,
        string importedBy,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var warnings = new List<string>();
        var unmatchedRules = new List<UnmatchedRuleInfo>();
        var fileHash = ComputeSha256(fileContent);

        _logger.LogInformation(
            "CKL import started: file={FileName}, hash={FileHash}, system={SystemId}, type=CKL, resolution={Resolution}, dryRun={DryRun}",
            fileName, fileHash, systemId, resolution, dryRun);

        // ── Step 1: Parse CKL ────────────────────────────────────────────
        ParsedCklFile parsedCkl;
        try
        {
            parsedCkl = _cklParser.Parse(fileContent, fileName);
        }
        catch (CklParseException ex)
        {
            _logger.LogWarning(ex, "CKL parse failed for file {FileName}", fileName);
            return CreateFailedResult(
                $"CKL parse error: {ex.Message}",
                fileName);
        }

        // ── Step 2: Validate system ──────────────────────────────────────
        var system = await _rmfService.GetSystemAsync(systemId, ct);
        if (system is null)
        {
            return CreateFailedResult(
                $"System '{systemId}' not found.",
                fileName);
        }

        // ── Step 3: Check RMF step (warn if < Assess) ───────────────────
        if (system.CurrentRmfStep < RmfPhase.Assess)
        {
            warnings.Add(
                $"System is in RMF step '{system.CurrentRmfStep}' (expected Assess or later). " +
                "Import will proceed, but findings may not be visible in assessment workflows.");
        }

        // ── Step 4: Get control baseline ─────────────────────────────────
        var baseline = await _baselineService.GetBaselineAsync(systemId, cancellationToken: ct);
        var baselineControlIds = baseline?.ControlIds ?? new List<string>();
        var baselineSet = new HashSet<string>(baselineControlIds, StringComparer.OrdinalIgnoreCase);

        if (baseline is null)
        {
            warnings.Add("No control baseline found for system. " +
                          "All NIST controls will be treated as out-of-baseline (no ControlEffectiveness records).");
        }

        // ── Step 5: Resolve/create assessment context ────────────────────
        using var scope = _scopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        string resolvedAssessmentId;
        if (!string.IsNullOrEmpty(assessmentId))
        {
            var existing = await ctx.Assessments.FindAsync(new object[] { assessmentId }, ct);
            if (existing is null)
                return CreateFailedResult($"Assessment '{assessmentId}' not found.", fileName);
            resolvedAssessmentId = assessmentId;
        }
        else
        {
            resolvedAssessmentId = await GetOrCreateAssessmentAsync(ctx, systemId, importedBy, ct);
        }

        // ── Step 6: Duplicate detection ───────────────────────────────
        var duplicateImport = await ctx.ScanImportRecords
            .Where(r => r.FileHash == fileHash && r.RegisteredSystemId == systemId && !r.IsDryRun)
            .OrderByDescending(r => r.ImportedAt)
            .FirstOrDefaultAsync(ct);

        if (duplicateImport is not null)
        {
            warnings.Add(
                $"File previously imported on {duplicateImport.ImportedAt:yyyy-MM-dd HH:mm} UTC " +
                $"(import ID: {duplicateImport.Id}).");
        }

        // ── Step 7: Create ScanImportRecord ──────────────────────────────
        var importRecord = new ScanImportRecord
        {
            RegisteredSystemId = systemId,
            AssessmentId = resolvedAssessmentId,
            ImportType = ScanImportType.Ckl,
            FileName = fileName,
            FileHash = fileHash,
            FileSizeBytes = fileContent.Length,
            BenchmarkId = parsedCkl.StigInfo.StigId,
            BenchmarkVersion = parsedCkl.StigInfo.Version,
            BenchmarkTitle = parsedCkl.StigInfo.Title,
            TargetHostName = parsedCkl.Asset.HostName,
            TargetIpAddress = parsedCkl.Asset.HostIp,
            ScanTimestamp = null, // CKL has no scan timestamp
            ConflictResolution = resolution,
            IsDryRun = dryRun,
            ImportedBy = importedBy
        };

        // ── Step 8: Process each CKL entry ───────────────────────────────
        int openCount = 0, passCount = 0, naCount = 0, notReviewedCount = 0;
        int findingsCreated = 0, findingsUpdated = 0, skippedCount = 0, unmatchedCount = 0;
        var affectedNistControls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var importFindings = new List<ScanImportFinding>();
        var newFindings = new List<ComplianceFinding>();
        var updatedFindings = new List<ComplianceFinding>();

        // Preload existing findings for conflict detection
        var existingFindings = await ctx.Findings
            .Where(f => f.AssessmentId == resolvedAssessmentId &&
                        f.StigFinding &&
                        f.StigId != null)
            .ToListAsync(ct);

        var existingByStigId = existingFindings
            .Where(f => f.StigId is not null)
            .GroupBy(f => f.StigId!)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var entry in parsedCkl.Entries)
        {
            var importFinding = new ScanImportFinding
            {
                ScanImportRecordId = importRecord.Id,
                VulnId = entry.VulnId,
                RuleId = entry.RuleId,
                StigVersion = entry.StigVersion,
                RawStatus = entry.Status,
                RawSeverity = entry.Severity,
                FindingDetails = entry.FindingDetails,
                Comments = entry.Comments,
                SeverityOverride = entry.SeverityOverride,
                SeverityJustification = entry.SeverityJustification,
                ResolvedCciRefs = entry.CciRefs
            };

            // Count by status
            switch (entry.Status)
            {
                case "Open":
                    openCount++;
                    break;
                case "NotAFinding":
                    passCount++;
                    break;
                case "Not_Applicable":
                    naCount++;
                    break;
                case "Not_Reviewed":
                    notReviewedCount++;
                    break;
            }

            // ── Step 8a: Resolve STIG control (T014) ─────────────────────
            var stigControl = await ResolveStigControlAsync(entry, ct);

            if (stigControl is null)
            {
                importFinding.ImportAction = ImportFindingAction.Unmatched;
                unmatchedCount++;
                unmatchedRules.Add(new UnmatchedRuleInfo(
                    entry.VulnId,
                    entry.RuleId,
                    entry.RuleTitle,
                    entry.Severity));
                importFindings.Add(importFinding);
                continue;
            }

            importFinding.ResolvedStigControlId = stigControl.StigId;

            // ── Step 8b: Resolve NIST controls (T015) ────────────────────
            var nistControls = stigControl.NistControls ?? new List<string>();
            importFinding.ResolvedNistControlIds = nistControls;

            // Track out-of-baseline controls
            var outOfBaseline = nistControls
                .Where(c => !baselineSet.Contains(c))
                .ToList();
            if (outOfBaseline.Count > 0 && baseline is not null)
            {
                warnings.Add(
                    $"VULN {entry.VulnId}: NIST controls [{string.Join(", ", outOfBaseline)}] " +
                    "resolved but not in system baseline. Finding created, no effectiveness.");
            }

            // ── Step 8c: Map severity ────────────────────────────────────
            var (findingSeverity, catSeverity) = MapSeverity(
                entry.Severity, entry.SeverityOverride);
            importFinding.MappedSeverity = catSeverity;

            // ── Step 8d: Handle Not_Applicable — no finding ──────────────
            if (entry.Status == "Not_Applicable")
            {
                importFinding.ImportAction = ImportFindingAction.NotApplicable;
                importFindings.Add(importFinding);
                continue;
            }

            // ── Step 8e: Map finding status ──────────────────────────────
            var (findingStatus, findingDetails) = MapCklStatus(entry);

            // ── Step 8f: Conflict detection (T025) ───────────────────────
            var primaryControl = nistControls.FirstOrDefault() ?? string.Empty;
            var controlFamily = ExtractControlFamily(primaryControl);
            existingByStigId.TryGetValue(entry.VulnId, out var existingFinding);

            if (existingFinding is not null)
            {
                // ── Step 8g: Apply conflict resolution (T026) ────────────
                var conflictAction = ApplyConflictResolution(
                    existingFinding, entry, findingStatus, findingSeverity,
                    catSeverity, findingDetails, resolution, importRecord.Id);

                if (conflictAction == ImportFindingAction.Skipped)
                {
                    importFinding.ImportAction = ImportFindingAction.Skipped;
                    importFinding.ComplianceFindingId = existingFinding.Id;
                    skippedCount++;
                    importFindings.Add(importFinding);
                    continue;
                }

                // Overwrite or Merge updated the existing finding in-place
                importFinding.ImportAction = ImportFindingAction.Updated;
                importFinding.ComplianceFindingId = existingFinding.Id;
                findingsUpdated++;
                if (!updatedFindings.Contains(existingFinding))
                    updatedFindings.Add(existingFinding);
            }
            else
            {
                // ── Step 8h: Create new ComplianceFinding (T019) ─────────
                var newFinding = new ComplianceFinding
                {
                    ControlId = primaryControl,
                    ControlFamily = controlFamily,
                    Title = entry.RuleTitle ?? $"STIG Finding {entry.VulnId}",
                    Description = findingDetails,
                    Severity = findingSeverity,
                    Status = findingStatus,
                    ResourceId = system.Name,
                    ResourceType = "RegisteredSystem",
                    RemediationGuidance = entry.Comments ?? string.Empty,
                    Source = "CKL Import",
                    ScanSource = ScanSourceType.Combined,
                    StigFinding = true,
                    StigId = entry.VulnId,
                    CatSeverity = catSeverity,
                    AssessmentId = resolvedAssessmentId,
                    ImportRecordId = importRecord.Id
                };

                importFinding.ImportAction = entry.Status == "Not_Reviewed"
                    ? ImportFindingAction.NotReviewed
                    : ImportFindingAction.Created;
                importFinding.ComplianceFindingId = newFinding.Id;

                newFindings.Add(newFinding);
                findingsCreated++;
            }

            // Track affected NIST controls (in-baseline only) for effectiveness
            foreach (var nist in nistControls.Where(c => baselineSet.Contains(c)))
            {
                affectedNistControls.Add(nist);
            }

            importFindings.Add(importFinding);
        }

        // Log unmatched rules
        if (unmatchedRules.Count > 0)
        {
            _logger.LogWarning(
                "CKL import {FileName}: {Count} unmatched rules: {VulnIds}",
                fileName, unmatchedRules.Count,
                string.Join(", ", unmatchedRules.Select(u => u.VulnId)));
            warnings.Add(
                $"{unmatchedRules.Count} STIG rule(s) not found in curated library: " +
                string.Join(", ", unmatchedRules.Select(u => u.VulnId)));
        }

        // ── Step 9: Create evidence (T021) ───────────────────────────────
        var evidenceContent = JsonSerializer.Serialize(new
        {
            ImportType = "CKL",
            FileName = fileName,
            BenchmarkId = parsedCkl.StigInfo.StigId,
            TotalEntries = parsedCkl.Entries.Count,
            OpenCount = openCount,
            PassCount = passCount,
            NotApplicableCount = naCount,
            NotReviewedCount = notReviewedCount,
            UnmatchedCount = unmatchedCount
        });

        var evidence = new ComplianceEvidence
        {
            ControlId = affectedNistControls.FirstOrDefault() ?? string.Empty,
            SubscriptionId = string.Empty,
            EvidenceType = "StigChecklist",
            Description = $"CKL Import: {fileName} ({parsedCkl.StigInfo.Title ?? parsedCkl.StigInfo.StigId})",
            Content = evidenceContent,
            CollectedAt = DateTime.UtcNow,
            CollectedBy = importedBy,
            AssessmentId = resolvedAssessmentId,
            EvidenceCategory = EvidenceCategory.Configuration,
            ContentHash = fileHash,
            CollectionMethod = "Manual"
        };

        // ── Step 10: Upsert effectiveness (T020) ─────────────────────────
        int effectivenessCreated = 0, effectivenessUpdated = 0;
        if (!dryRun && affectedNistControls.Count > 0)
        {
            // Build aggregate status map: for each NIST control, check all current findings
            // across ALL imports (re-evaluate aggregate state)
            var allStigFindings = await ctx.Findings
                .Where(f => f.AssessmentId == resolvedAssessmentId && f.StigFinding)
                .ToListAsync(ct);

            // Include newly created findings (not yet in DB)
            var allFindings = allStigFindings.Concat(newFindings).ToList();

            // Build control → findings lookup (primary control only)
            var controlFindingMap = allFindings
                .Where(f => !string.IsNullOrEmpty(f.ControlId))
                .GroupBy(f => f.ControlId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            foreach (var controlId in affectedNistControls)
            {
                controlFindingMap.TryGetValue(controlId, out var controlFindings);
                var findingsForControl = controlFindings ?? new List<ComplianceFinding>();

                // Determine aggregate effectiveness
                var anyOpen = findingsForControl.Any(f =>
                    f.Status == FindingStatus.Open || f.Status == FindingStatus.InProgress);
                var determination = anyOpen
                    ? EffectivenessDetermination.OtherThanSatisfied
                    : EffectivenessDetermination.Satisfied;

                // Find highest severity among open findings for CAT
                CatSeverity? controlCatSeverity = null;
                if (anyOpen)
                {
                    var openFindings = findingsForControl
                        .Where(f => f.Status == FindingStatus.Open || f.Status == FindingStatus.InProgress)
                        .Where(f => f.CatSeverity.HasValue)
                        .Select(f => f.CatSeverity!.Value)
                        .ToList();
                    if (openFindings.Count > 0)
                        controlCatSeverity = openFindings.Min(); // CatI < CatII < CatIII (lowest ordinal = highest severity)
                }

                // Check existing effectiveness record
                var existingEffectiveness = await ctx.ControlEffectivenessRecords
                    .Where(e => e.AssessmentId == resolvedAssessmentId &&
                                e.RegisteredSystemId == systemId &&
                                e.ControlId == controlId)
                    .FirstOrDefaultAsync(ct);

                if (existingEffectiveness is not null)
                {
                    existingEffectiveness.Determination = determination;
                    existingEffectiveness.AssessmentMethod = "Test";
                    existingEffectiveness.AssessorId = importedBy;
                    existingEffectiveness.AssessedAt = DateTime.UtcNow;
                    existingEffectiveness.CatSeverity = controlCatSeverity;
                    if (!existingEffectiveness.EvidenceIds.Contains(evidence.Id))
                        existingEffectiveness.EvidenceIds.Add(evidence.Id);
                    existingEffectiveness.Notes = $"Re-evaluated via CKL import '{fileName}'";
                    effectivenessUpdated++;
                }
                else
                {
                    var effectiveness = new ControlEffectiveness
                    {
                        AssessmentId = resolvedAssessmentId,
                        RegisteredSystemId = systemId,
                        ControlId = controlId,
                        Determination = determination,
                        AssessmentMethod = "Test",
                        EvidenceIds = new List<string> { evidence.Id },
                        AssessorId = importedBy,
                        CatSeverity = controlCatSeverity,
                        Notes = $"Auto-determined via CKL import '{fileName}'"
                    };
                    ctx.ControlEffectivenessRecords.Add(effectiveness);
                    effectivenessCreated++;
                }
            }
        }

        // Out-of-baseline summary
        var outOfBaselineControls = importFindings
            .SelectMany(f => f.ResolvedNistControlIds)
            .Where(c => !baselineSet.Contains(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (outOfBaselineControls.Count > 0)
        {
            _logger.LogInformation(
                "CKL import {FileName}: {Count} NIST controls outside baseline: {Controls}",
                fileName, outOfBaselineControls.Count, string.Join(", ", outOfBaselineControls));
        }

        // ── Step 11: Update import record counts ─────────────────────────
        importRecord.TotalEntries = parsedCkl.Entries.Count;
        importRecord.OpenCount = openCount;
        importRecord.PassCount = passCount;
        importRecord.NotApplicableCount = naCount;
        importRecord.NotReviewedCount = notReviewedCount;
        importRecord.SkippedCount = skippedCount;
        importRecord.UnmatchedCount = unmatchedCount;
        importRecord.FindingsCreated = findingsCreated;
        importRecord.FindingsUpdated = findingsUpdated;
        importRecord.EffectivenessRecordsCreated = effectivenessCreated;
        importRecord.EffectivenessRecordsUpdated = effectivenessUpdated;
        importRecord.NistControlsAffected = affectedNistControls.Count;
        importRecord.Warnings = warnings;
        importRecord.ImportStatus = warnings.Count > 0 || unmatchedCount > 0
            ? ScanImportStatus.CompletedWithWarnings
            : ScanImportStatus.Completed;

        // ── Step 12: Persist (unless dry-run) (T027) ─────────────────────
        if (!dryRun)
        {
            ctx.ScanImportRecords.Add(importRecord);
            ctx.Evidence.Add(evidence);

            // Batch-insert in 100-record chunks to avoid SQL Server parameter limits
            // and keep memory pressure proportional (Task #175 / Epic #131).
            const int BatchSize = 100;
            foreach (var batch in importFindings.Chunk(BatchSize))
            {
                ctx.ScanImportFindings.AddRange(batch);
                await ctx.SaveChangesAsync(ct);
                ctx.ChangeTracker.Clear();
            }
            if (newFindings.Count > 0)
            {
                foreach (var batch in newFindings.Chunk(BatchSize))
                {
                    ctx.Findings.AddRange(batch);
                    await ctx.SaveChangesAsync(ct);
                    ctx.ChangeTracker.Clear();
                }
            }
            else
            {
                // Updated findings tracked by EF change tracker — flush
                await ctx.SaveChangesAsync(ct);
            }
        }

        sw.Stop();
        _logger.LogInformation(
            "CKL import completed: file={FileName}, duration={DurationMs}ms, findings={FindingsCreated}/{FindingsUpdated}, " +
            "effectiveness={EffCreated}/{EffUpdated}, warnings={WarningCount}",
            fileName, sw.ElapsedMilliseconds, findingsCreated, findingsUpdated,
            effectivenessCreated, effectivenessUpdated, warnings.Count);

        return new ImportResult(
            ImportRecordId: importRecord.Id,
            Status: importRecord.ImportStatus,
            BenchmarkId: parsedCkl.StigInfo.StigId ?? string.Empty,
            BenchmarkTitle: parsedCkl.StigInfo.Title,
            TotalEntries: importRecord.TotalEntries,
            OpenCount: openCount,
            PassCount: passCount,
            NotApplicableCount: naCount,
            NotReviewedCount: notReviewedCount,
            ErrorCount: 0,
            SkippedCount: skippedCount,
            UnmatchedCount: unmatchedCount,
            FindingsCreated: findingsCreated,
            FindingsUpdated: findingsUpdated,
            EffectivenessRecordsCreated: effectivenessCreated,
            EffectivenessRecordsUpdated: effectivenessUpdated,
            NistControlsAffected: affectedNistControls.Count,
            Warnings: warnings,
            UnmatchedRules: unmatchedRules,
            ErrorMessage: null);
    }

    // ─── ImportXccdfAsync (Phase 7 — T038) ───────────────────────────────

    /// <inheritdoc />
    public async Task<ImportResult> ImportXccdfAsync(
        string systemId,
        string? assessmentId,
        byte[] fileContent,
        string fileName,
        ImportConflictResolution resolution,
        bool dryRun,
        string importedBy,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var warnings = new List<string>();
        var unmatchedRules = new List<UnmatchedRuleInfo>();
        var fileHash = ComputeSha256(fileContent);

        _logger.LogInformation(
            "XCCDF import started: file={FileName}, hash={FileHash}, system={SystemId}, type=XCCDF, resolution={Resolution}, dryRun={DryRun}",
            fileName, fileHash, systemId, resolution, dryRun);

        // ── Step 1: Parse XCCDF ──────────────────────────────────────────
        ParsedXccdfFile parsedXccdf;
        try
        {
            parsedXccdf = _xccdfParser.Parse(fileContent, fileName);
        }
        catch (XccdfParseException ex)
        {
            _logger.LogWarning(ex, "XCCDF parse failed for file {FileName}", fileName);
            return CreateFailedResult($"XCCDF parse error: {ex.Message}", fileName);
        }

        // ── Step 2: Validate system ──────────────────────────────────────
        var system = await _rmfService.GetSystemAsync(systemId, ct);
        if (system is null)
            return CreateFailedResult($"System '{systemId}' not found.", fileName);

        // ── Step 3: Check RMF step ───────────────────────────────────────
        if (system.CurrentRmfStep < RmfPhase.Assess)
        {
            warnings.Add(
                $"System is in RMF step '{system.CurrentRmfStep}' (expected Assess or later). " +
                "Import will proceed, but findings may not be visible in assessment workflows.");
        }

        // ── Step 4: Get control baseline ─────────────────────────────────
        var baseline = await _baselineService.GetBaselineAsync(systemId, cancellationToken: ct);
        var baselineControlIds = baseline?.ControlIds ?? new List<string>();
        var baselineSet = new HashSet<string>(baselineControlIds, StringComparer.OrdinalIgnoreCase);

        if (baseline is null)
        {
            warnings.Add("No control baseline found for system. " +
                          "All NIST controls will be treated as out-of-baseline (no ControlEffectiveness records).");
        }

        // ── Step 5: Resolve/create assessment context ────────────────────
        using var scope = _scopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        string resolvedAssessmentId;
        if (!string.IsNullOrEmpty(assessmentId))
        {
            var existing = await ctx.Assessments.FindAsync(new object[] { assessmentId }, ct);
            if (existing is null)
                return CreateFailedResult($"Assessment '{assessmentId}' not found.", fileName);
            resolvedAssessmentId = assessmentId;
        }
        else
        {
            resolvedAssessmentId = await GetOrCreateAssessmentAsync(ctx, systemId, importedBy, ct);
        }

        // ── Step 6: Extract benchmark ID ──────────────────────────────
        // Extract benchmark ID from href (e.g., "xccdf_mil.disa.stig_benchmark_Windows_Server_2022_STIG")
        var benchmarkId = ExtractBenchmarkId(parsedXccdf.BenchmarkHref);
        var benchmarkTitle = parsedXccdf.Title;

        // ── Step 7: Create ScanImportRecord ──────────────────────────────
        var importRecord = new ScanImportRecord
        {
            RegisteredSystemId = systemId,
            AssessmentId = resolvedAssessmentId,
            ImportType = ScanImportType.Xccdf,
            FileName = fileName,
            FileHash = fileHash,
            FileSizeBytes = fileContent.Length,
            BenchmarkId = benchmarkId,
            BenchmarkTitle = benchmarkTitle,
            TargetHostName = parsedXccdf.Target,
            TargetIpAddress = parsedXccdf.TargetAddress,
            ScanTimestamp = parsedXccdf.StartTime,
            XccdfScore = parsedXccdf.Score,
            ConflictResolution = resolution,
            IsDryRun = dryRun,
            ImportedBy = importedBy
        };

        // ── Step 8: Process each XCCDF rule-result ───────────────────────
        int openCount = 0, passCount = 0, naCount = 0, errorCount = 0;
        int findingsCreated = 0, findingsUpdated = 0, skippedCount = 0, unmatchedCount = 0;
        var affectedNistControls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var importFindings = new List<ScanImportFinding>();
        var newFindings = new List<ComplianceFinding>();
        var updatedFindings = new List<ComplianceFinding>();

        // Preload existing findings for conflict detection
        var existingFindings = await ctx.Findings
            .Where(f => f.AssessmentId == resolvedAssessmentId && f.StigFinding && f.StigId != null)
            .ToListAsync(ct);

        var existingByStigId = existingFindings
            .Where(f => f.StigId is not null)
            .GroupBy(f => f.StigId!)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var ruleResult in parsedXccdf.Results)
        {
            var importFinding = new ScanImportFinding
            {
                ScanImportRecordId = importRecord.Id,
                VulnId = ruleResult.ExtractedRuleId,
                RuleId = ruleResult.ExtractedRuleId,
                RawStatus = ruleResult.Result,
                RawSeverity = ruleResult.Severity
            };

            // Map XCCDF result → status/counts
            var (xccdfStatus, xccdfDetails) = MapXccdfResult(ruleResult);

            switch (ruleResult.Result)
            {
                case "fail":
                    openCount++;
                    break;
                case "pass":
                    passCount++;
                    break;
                case "notapplicable":
                    naCount++;
                    break;
                case "error":
                case "unknown":
                case "notchecked":
                    errorCount++;
                    break;
            }

            // ── Step 8a: Resolve STIG control by rule ID ─────────────────
            var stigControl = await ResolveStigControlByRuleIdAsync(ruleResult.ExtractedRuleId, ct);

            if (stigControl is null)
            {
                importFinding.ImportAction = ImportFindingAction.Unmatched;
                unmatchedCount++;
                unmatchedRules.Add(new UnmatchedRuleInfo(
                    ruleResult.RuleIdRef,
                    ruleResult.ExtractedRuleId,
                    null,
                    ruleResult.Severity));
                importFindings.Add(importFinding);
                continue;
            }

            importFinding.ResolvedStigControlId = stigControl.StigId;

            // ── Step 8b: Resolve NIST controls ───────────────────────────
            var nistControls = stigControl.NistControls ?? new List<string>();
            importFinding.ResolvedNistControlIds = nistControls;

            // ── Step 8c: Map severity ────────────────────────────────────
            var (findingSeverity, catSeverity) = MapSeverity(ruleResult.Severity, null);
            importFinding.MappedSeverity = catSeverity;

            // ── Step 8d: Handle notapplicable ────────────────────────────
            if (ruleResult.Result == "notapplicable")
            {
                importFinding.ImportAction = ImportFindingAction.NotApplicable;
                importFindings.Add(importFinding);
                continue;
            }

            // ── Step 8e: Handle error/unknown/notchecked → flag for review ──
            if (ruleResult.Result is "error" or "unknown" or "notchecked")
            {
                importFinding.ImportAction = ImportFindingAction.Error;
                importFinding.FindingDetails = xccdfDetails;
                importFindings.Add(importFinding);
                warnings.Add($"Rule {ruleResult.ExtractedRuleId}: XCCDF result '{ruleResult.Result}' flagged for manual review.");
                continue;
            }

            // ── Step 8f: Conflict detection ──────────────────────────────
            var primaryControl = nistControls.FirstOrDefault() ?? string.Empty;
            var controlFamily = ExtractControlFamily(primaryControl);
            var vulnId = stigControl.VulnId ?? ruleResult.ExtractedRuleId;
            existingByStigId.TryGetValue(vulnId, out var existingFinding);

            // Build a pseudo CKL entry for conflict resolution reuse
            var pseudoEntry = new ParsedCklEntry(
                VulnId: vulnId,
                RuleId: ruleResult.ExtractedRuleId,
                StigVersion: null,
                RuleTitle: null,
                Severity: ruleResult.Severity,
                Status: ruleResult.Result == "fail" ? "Open" : "NotAFinding",
                FindingDetails: xccdfDetails,
                Comments: ruleResult.Message,
                SeverityOverride: null,
                SeverityJustification: null,
                CciRefs: new List<string>(),
                GroupTitle: null);

            if (existingFinding is not null)
            {
                var conflictAction = ApplyConflictResolution(
                    existingFinding, pseudoEntry, xccdfStatus, findingSeverity,
                    catSeverity, xccdfDetails, resolution, importRecord.Id);

                if (conflictAction == ImportFindingAction.Skipped)
                {
                    importFinding.ImportAction = ImportFindingAction.Skipped;
                    importFinding.ComplianceFindingId = existingFinding.Id;
                    skippedCount++;
                    importFindings.Add(importFinding);
                    continue;
                }

                importFinding.ImportAction = ImportFindingAction.Updated;
                importFinding.ComplianceFindingId = existingFinding.Id;
                findingsUpdated++;
                if (!updatedFindings.Contains(existingFinding))
                    updatedFindings.Add(existingFinding);
            }
            else
            {
                // ── Step 8g: Create new ComplianceFinding ────────────────
                var newFinding = new ComplianceFinding
                {
                    ControlId = primaryControl,
                    ControlFamily = controlFamily,
                    Title = $"STIG Finding {vulnId}",
                    Description = xccdfDetails,
                    Severity = findingSeverity,
                    Status = xccdfStatus,
                    ResourceId = system.Name,
                    ResourceType = "RegisteredSystem",
                    RemediationGuidance = ruleResult.Message ?? string.Empty,
                    Source = "XCCDF Import",
                    ScanSource = ScanSourceType.Combined,
                    StigFinding = true,
                    StigId = vulnId,
                    CatSeverity = catSeverity,
                    AssessmentId = resolvedAssessmentId,
                    ImportRecordId = importRecord.Id
                };

                importFinding.ImportAction = ImportFindingAction.Created;
                importFinding.ComplianceFindingId = newFinding.Id;
                newFindings.Add(newFinding);
                findingsCreated++;
            }

            // Track affected NIST controls
            foreach (var nist in nistControls.Where(c => baselineSet.Contains(c)))
                affectedNistControls.Add(nist);

            importFindings.Add(importFinding);
        }

        // Log unmatched rules
        if (unmatchedRules.Count > 0)
        {
            _logger.LogWarning(
                "XCCDF import {FileName}: {Count} unmatched rules: {RuleIds}",
                fileName, unmatchedRules.Count,
                string.Join(", ", unmatchedRules.Select(u => u.RuleId ?? u.VulnId)));
            warnings.Add(
                $"{unmatchedRules.Count} XCCDF rule(s) not found in curated library.");
        }

        // ── Step 9: Create evidence ──────────────────────────────────────
        var evidenceContent = JsonSerializer.Serialize(new
        {
            ImportType = "XCCDF",
            FileName = fileName,
            BenchmarkId = benchmarkId,
            TotalEntries = parsedXccdf.Results.Count,
            Score = parsedXccdf.Score,
            MaxScore = parsedXccdf.MaxScore,
            OpenCount = openCount,
            PassCount = passCount,
            NotApplicableCount = naCount,
            ErrorCount = errorCount,
            UnmatchedCount = unmatchedCount
        });

        var evidence = new ComplianceEvidence
        {
            ControlId = affectedNistControls.FirstOrDefault() ?? string.Empty,
            SubscriptionId = string.Empty,
            EvidenceType = "XccdfScanResult",
            Description = $"XCCDF Import: {fileName} ({benchmarkTitle ?? benchmarkId})",
            Content = evidenceContent,
            CollectedAt = DateTime.UtcNow,
            CollectedBy = importedBy,
            AssessmentId = resolvedAssessmentId,
            EvidenceCategory = EvidenceCategory.Configuration,
            ContentHash = fileHash,
            CollectionMethod = "Automated" // XCCDF = machine-verified
        };

        // ── Step 10: Upsert effectiveness ────────────────────────────────
        int effectivenessCreated = 0, effectivenessUpdated = 0;
        if (!dryRun && affectedNistControls.Count > 0)
        {
            var allStigFindings = await ctx.Findings
                .Where(f => f.AssessmentId == resolvedAssessmentId && f.StigFinding)
                .ToListAsync(ct);

            var allFindings = allStigFindings.Concat(newFindings).ToList();

            var controlFindingMap = allFindings
                .Where(f => !string.IsNullOrEmpty(f.ControlId))
                .GroupBy(f => f.ControlId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            foreach (var controlId in affectedNistControls)
            {
                controlFindingMap.TryGetValue(controlId, out var controlFindings);
                var findingsForControl = controlFindings ?? new List<ComplianceFinding>();

                var anyOpen = findingsForControl.Any(f =>
                    f.Status == FindingStatus.Open || f.Status == FindingStatus.InProgress);
                var determination = anyOpen
                    ? EffectivenessDetermination.OtherThanSatisfied
                    : EffectivenessDetermination.Satisfied;

                CatSeverity? controlCatSeverity = null;
                if (anyOpen)
                {
                    var openCats = findingsForControl
                        .Where(f => f.Status == FindingStatus.Open || f.Status == FindingStatus.InProgress)
                        .Where(f => f.CatSeverity.HasValue)
                        .Select(f => f.CatSeverity!.Value)
                        .ToList();
                    if (openCats.Count > 0)
                        controlCatSeverity = openCats.Min();
                }

                var existingEffectiveness = await ctx.ControlEffectivenessRecords
                    .Where(e => e.AssessmentId == resolvedAssessmentId &&
                                e.RegisteredSystemId == systemId &&
                                e.ControlId == controlId)
                    .FirstOrDefaultAsync(ct);

                if (existingEffectiveness is not null)
                {
                    existingEffectiveness.Determination = determination;
                    existingEffectiveness.AssessmentMethod = "Test";
                    existingEffectiveness.AssessorId = importedBy;
                    existingEffectiveness.AssessedAt = DateTime.UtcNow;
                    existingEffectiveness.CatSeverity = controlCatSeverity;
                    if (!existingEffectiveness.EvidenceIds.Contains(evidence.Id))
                        existingEffectiveness.EvidenceIds.Add(evidence.Id);
                    existingEffectiveness.Notes = $"Re-evaluated via XCCDF import '{fileName}'";
                    effectivenessUpdated++;
                }
                else
                {
                    var effectiveness = new ControlEffectiveness
                    {
                        AssessmentId = resolvedAssessmentId,
                        RegisteredSystemId = systemId,
                        ControlId = controlId,
                        Determination = determination,
                        AssessmentMethod = "Test",
                        EvidenceIds = new List<string> { evidence.Id },
                        AssessorId = importedBy,
                        CatSeverity = controlCatSeverity,
                        Notes = $"Auto-determined via XCCDF import '{fileName}'"
                    };
                    ctx.ControlEffectivenessRecords.Add(effectiveness);
                    effectivenessCreated++;
                }
            }
        }

        // ── Step 11: Update import record ────────────────────────────────
        importRecord.TotalEntries = parsedXccdf.Results.Count;
        importRecord.OpenCount = openCount;
        importRecord.PassCount = passCount;
        importRecord.NotApplicableCount = naCount;
        importRecord.ErrorCount = errorCount;
        importRecord.SkippedCount = skippedCount;
        importRecord.UnmatchedCount = unmatchedCount;
        importRecord.FindingsCreated = findingsCreated;
        importRecord.FindingsUpdated = findingsUpdated;
        importRecord.EffectivenessRecordsCreated = effectivenessCreated;
        importRecord.EffectivenessRecordsUpdated = effectivenessUpdated;
        importRecord.NistControlsAffected = affectedNistControls.Count;
        importRecord.Warnings = warnings;
        importRecord.ImportStatus = warnings.Count > 0 || unmatchedCount > 0
            ? ScanImportStatus.CompletedWithWarnings
            : ScanImportStatus.Completed;

        // ── Step 12: Persist (unless dry-run) ────────────────────────────
        if (!dryRun)
        {
            ctx.ScanImportRecords.Add(importRecord);
            ctx.Evidence.Add(evidence);

            // Batch-insert in 100-record chunks (Task #175 / Epic #131).
            const int BatchSize = 100;
            foreach (var batch in importFindings.Chunk(BatchSize))
            {
                ctx.ScanImportFindings.AddRange(batch);
                await ctx.SaveChangesAsync(ct);
                ctx.ChangeTracker.Clear();
            }
            if (newFindings.Count > 0)
            {
                foreach (var batch in newFindings.Chunk(BatchSize))
                {
                    ctx.Findings.AddRange(batch);
                    await ctx.SaveChangesAsync(ct);
                    ctx.ChangeTracker.Clear();
                }
            }
            else
            {
                await ctx.SaveChangesAsync(ct);
            }
        }

        sw.Stop();
        _logger.LogInformation(
            "XCCDF import completed: file={FileName}, duration={DurationMs}ms, findings={FindingsCreated}/{FindingsUpdated}, " +
            "effectiveness={EffCreated}/{EffUpdated}, score={Score}, warnings={WarningCount}",
            fileName, sw.ElapsedMilliseconds, findingsCreated, findingsUpdated,
            effectivenessCreated, effectivenessUpdated, parsedXccdf.Score, warnings.Count);

        return new ImportResult(
            ImportRecordId: importRecord.Id,
            Status: importRecord.ImportStatus,
            BenchmarkId: benchmarkId ?? string.Empty,
            BenchmarkTitle: benchmarkTitle,
            TotalEntries: importRecord.TotalEntries,
            OpenCount: openCount,
            PassCount: passCount,
            NotApplicableCount: naCount,
            NotReviewedCount: 0, // XCCDF has no "not reviewed" state
            ErrorCount: errorCount,
            SkippedCount: skippedCount,
            UnmatchedCount: unmatchedCount,
            FindingsCreated: findingsCreated,
            FindingsUpdated: findingsUpdated,
            EffectivenessRecordsCreated: effectivenessCreated,
            EffectivenessRecordsUpdated: effectivenessUpdated,
            NistControlsAffected: affectedNistControls.Count,
            Warnings: warnings,
            UnmatchedRules: unmatchedRules,
            ErrorMessage: null);
    }

    // ─── ExportCklAsync (Phase 8 — T044) ─────────────────────────────────

    /// <inheritdoc />
    public async Task<string> ExportCklAsync(
        string systemId,
        string benchmarkId,
        string? assessmentId,
        CancellationToken ct = default)
    {
        // ── Step 1: Validate system ──────────────────────────────────────
        var system = await _rmfService.GetSystemAsync(systemId, ct);
        if (system is null)
            throw new InvalidOperationException($"System '{systemId}' not found.");

        // ── Step 2: Get all STIG controls for benchmark ──────────────────
        var stigControls = await _stigService.GetStigControlsByBenchmarkAsync(benchmarkId, ct);
        if (stigControls.Count == 0)
            throw new InvalidOperationException(
                $"No STIG controls found for benchmark '{benchmarkId}'. " +
                "Verify the benchmark ID matches the curated STIG library.");

        // ── Step 3: Query findings for the system/assessment ─────────────
        using var scope = _scopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        IQueryable<ComplianceFinding> findingsQuery = ctx.Findings
            .Where(f => f.StigFinding && f.StigId != null);

        if (!string.IsNullOrEmpty(assessmentId))
        {
            findingsQuery = findingsQuery.Where(f => f.AssessmentId == assessmentId);
        }
        else
        {
            // Find the latest assessment for the system
            var latestAssessment = await ctx.Assessments
                .Where(a => a.RegisteredSystemId == systemId)
                .OrderByDescending(a => a.AssessedAt)
                .FirstOrDefaultAsync(ct);

            if (latestAssessment is not null)
            {
                findingsQuery = findingsQuery.Where(f => f.AssessmentId == latestAssessment.Id);
            }
        }

        var findingsList = await findingsQuery.ToListAsync(ct);

        // Build lookup dictionary by StigId (VulnId)
        var findingsDict = findingsList
            .Where(f => f.StigId is not null)
            .GroupBy(f => f.StigId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // ── Step 4: Determine benchmark metadata ─────────────────────────
        var firstControl = stigControls.First();
        var benchmarkTitle = firstControl.StigFamily is not null
            ? $"{firstControl.StigFamily} Security Technical Implementation Guide"
            : benchmarkId;
        var benchmarkVersion = "1"; // Default; StigControl doesn't carry benchmark version

        // ── Step 5: Generate CKL XML ─────────────────────────────────────
        var cklXml = _cklGenerator.Generate(
            system, stigControls, findingsDict,
            benchmarkId, benchmarkVersion, benchmarkTitle);

        // ── Step 6: Return base64 ────────────────────────────────────────
        var xmlBytes = System.Text.Encoding.UTF8.GetBytes(cklXml);
        var base64 = Convert.ToBase64String(xmlBytes);

        _logger.LogInformation(
            "CKL export completed: system={SystemId}, benchmark={BenchmarkId}, " +
            "vulnCount={VulnCount}, findingsPresent={FindingsPresent}",
            systemId, benchmarkId, stigControls.Count, findingsDict.Count);

        return base64;
    }

    // ─── ListImportsAsync (Phase 9 — T048) ──────────────────────────────

    /// <inheritdoc />
    public async Task<(List<ScanImportRecord> Records, int TotalCount)> ListImportsAsync(
        string systemId,
        int page,
        int pageSize,
        string? benchmarkId,
        string? importType,
        bool includeDryRuns,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var query = ctx.ScanImportRecords
            .Where(r => r.RegisteredSystemId == systemId);

        if (!includeDryRuns)
            query = query.Where(r => !r.IsDryRun);

        if (!string.IsNullOrEmpty(benchmarkId))
            query = query.Where(r => r.BenchmarkId == benchmarkId);

        if (!string.IsNullOrEmpty(importType) &&
            Enum.TryParse<ScanImportType>(importType, true, out var typeFilter))
            query = query.Where(r => r.ImportType == typeFilter);

        if (fromDate.HasValue)
            query = query.Where(r => r.ImportedAt >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(r => r.ImportedAt <= toDate.Value);

        var totalCount = await query.CountAsync(ct);

        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(1, page);

        var records = await query
            .OrderByDescending(r => r.ImportedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (records, totalCount);
    }

    // ─── GetImportSummaryAsync (Phase 9 — T049) ─────────────────────────

    /// <inheritdoc />
    public async Task<(ScanImportRecord Record, List<ScanImportFinding> Findings)?> GetImportSummaryAsync(
        string importId,
        CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var record = await ctx.ScanImportRecords.FindAsync(new object[] { importId }, ct);
        if (record is null)
            return null;

        var findings = await ctx.ScanImportFindings
            .Where(f => f.ScanImportRecordId == importId)
            .ToListAsync(ct);

        return (record, findings);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Private helpers
    // ═══════════════════════════════════════════════════════════════════════

    // ─── STIG Resolution (T014) ──────────────────────────────────────────

    /// <summary>
    /// Resolve a parsed CKL entry to a StigControl record.
    /// Tries: VulnId → RuleId → StigVersion (fallback chain).
    /// </summary>
    private async Task<StigControl?> ResolveStigControlAsync(
        ParsedCklEntry entry,
        CancellationToken ct)
    {
        // Primary: VulnId (e.g., "V-254239")
        var control = await _stigService.GetStigControlAsync(entry.VulnId, ct);
        if (control is not null)
            return control;

        // Fallback: RuleId (e.g., "SV-254239r849090_rule")
        if (!string.IsNullOrEmpty(entry.RuleId))
        {
            control = await _stigService.GetStigControlByRuleIdAsync(entry.RuleId, ct);
            if (control is not null)
                return control;
        }

        // Fallback: StigVersion as StigId (e.g., "WN22-AU-000010")
        if (!string.IsNullOrEmpty(entry.StigVersion))
        {
            control = await _stigService.GetStigControlAsync(entry.StigVersion, ct);
            if (control is not null)
                return control;
        }

        return null;
    }

    // ─── Severity Mapping ────────────────────────────────────────────────

    /// <summary>
    /// Map CKL severity string → FindingSeverity + CatSeverity.
    /// Applies severity override if present.
    /// </summary>
    private static (FindingSeverity Severity, CatSeverity? Cat) MapSeverity(
        string rawSeverity,
        string? severityOverride)
    {
        var effective = !string.IsNullOrWhiteSpace(severityOverride)
            ? severityOverride
            : rawSeverity;

        return effective.ToLowerInvariant() switch
        {
            "high" => (FindingSeverity.High, CatSeverity.CatI),
            "medium" => (FindingSeverity.Medium, CatSeverity.CatII),
            "low" => (FindingSeverity.Low, CatSeverity.CatIII),
            _ => (FindingSeverity.Medium, CatSeverity.CatII) // default to medium
        };
    }

    // ─── CKL Status Mapping (T019) ──────────────────────────────────────

    /// <summary>
    /// Map CKL STATUS to FindingStatus and finding details text.
    /// </summary>
    private static (FindingStatus Status, string Details) MapCklStatus(ParsedCklEntry entry)
    {
        return entry.Status switch
        {
            "Open" => (FindingStatus.Open,
                entry.FindingDetails ?? $"Open finding from STIG rule {entry.VulnId}"),

            "NotAFinding" => (FindingStatus.Remediated,
                entry.FindingDetails ?? $"Verified compliant for STIG rule {entry.VulnId}"),

            "Not_Reviewed" => (FindingStatus.Open,
                !string.IsNullOrEmpty(entry.FindingDetails)
                    ? $"Not yet reviewed. {entry.FindingDetails}"
                    : $"Not yet reviewed — STIG rule {entry.VulnId} has not been evaluated."),

            _ => (FindingStatus.Open,
                entry.FindingDetails ?? $"Unknown status '{entry.Status}' for STIG rule {entry.VulnId}")
        };
    }

    // ─── XCCDF Result Mapping (T038) ────────────────────────────────────

    /// <summary>
    /// Map XCCDF result string → FindingStatus and finding details text.
    /// </summary>
    private static (FindingStatus Status, string Details) MapXccdfResult(ParsedXccdfResult ruleResult)
    {
        return ruleResult.Result switch
        {
            "fail" => (FindingStatus.Open,
                ruleResult.Message ?? $"XCCDF scan failure for rule {ruleResult.ExtractedRuleId}"),

            "pass" => (FindingStatus.Remediated,
                ruleResult.Message ?? $"XCCDF scan passed for rule {ruleResult.ExtractedRuleId}"),

            "error" => (FindingStatus.Open,
                $"XCCDF scan error for rule {ruleResult.ExtractedRuleId}: {ruleResult.Message ?? "check failed with error"}"),

            "notchecked" => (FindingStatus.Open,
                $"XCCDF rule {ruleResult.ExtractedRuleId} was not checked."),

            "unknown" => (FindingStatus.Open,
                $"XCCDF result unknown for rule {ruleResult.ExtractedRuleId}."),

            _ => (FindingStatus.Open,
                ruleResult.Message ?? $"Unrecognized XCCDF result '{ruleResult.Result}' for rule {ruleResult.ExtractedRuleId}")
        };
    }

    /// <summary>
    /// Extract benchmark ID from XCCDF benchmark href.
    /// E.g., "xccdf_mil.disa.stig_benchmark_Windows_Server_2022_STIG" → "Windows_Server_2022_STIG"
    /// </summary>
    private static string? ExtractBenchmarkId(string? href)
    {
        if (string.IsNullOrEmpty(href)) return null;

        const string prefix = "xccdf_mil.disa.stig_benchmark_";
        if (href.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return href[prefix.Length..];

        return href;
    }

    /// <summary>
    /// Resolve a STIG control using the XCCDF extracted rule ID.
    /// Tries: RuleId → VulnId (derived from rule ID) fallback chain.
    /// </summary>
    private async Task<StigControl?> ResolveStigControlByRuleIdAsync(
        string extractedRuleId,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(extractedRuleId)) return null;

        // Primary: RuleId lookup
        var control = await _stigService.GetStigControlByRuleIdAsync(extractedRuleId, ct);
        if (control is not null) return control;

        // Fallback: try as VulnId (extract V-XXXXXX from SV-XXXXXX)
        if (extractedRuleId.StartsWith("SV-", StringComparison.OrdinalIgnoreCase))
        {
            var vulnPart = extractedRuleId[1..]; // Remove "S" → "V-XXXXXX..."
            var dashIdx = vulnPart.IndexOf('r');
            var vulnId = dashIdx > 0 ? vulnPart[..dashIdx] : vulnPart;
            control = await _stigService.GetStigControlAsync(vulnId, ct);
            if (control is not null) return control;
        }

        // Direct lookup as-is
        return await _stigService.GetStigControlAsync(extractedRuleId, ct);
    }

    // ─── Conflict Resolution (T025–T026) ─────────────────────────────────

    /// <summary>
    /// Apply conflict resolution strategy to an existing finding.
    /// Returns the action taken: Skipped or Updated.
    /// For Overwrite/Merge, modifies the existing finding in-place.
    /// </summary>
    private static ImportFindingAction ApplyConflictResolution(
        ComplianceFinding existing,
        ParsedCklEntry entry,
        FindingStatus importedStatus,
        FindingSeverity importedSeverity,
        CatSeverity? importedCat,
        string importedDetails,
        ImportConflictResolution resolution,
        string importRecordId)
    {
        switch (resolution)
        {
            case ImportConflictResolution.Skip:
                return ImportFindingAction.Skipped;

            case ImportConflictResolution.Overwrite:
                existing.Status = importedStatus;
                existing.Severity = importedSeverity;
                existing.CatSeverity = importedCat;
                existing.Description = importedDetails;
                existing.RemediationGuidance = entry.Comments ?? existing.RemediationGuidance;
                existing.ImportRecordId = importRecordId;
                existing.Title = entry.RuleTitle ?? existing.Title;
                return ImportFindingAction.Updated;

            case ImportConflictResolution.Merge:
                // Keep more-recent status (Open takes precedence for safety)
                if (importedStatus == FindingStatus.Open && existing.Status != FindingStatus.Open)
                    existing.Status = importedStatus;
                else if (importedStatus == FindingStatus.Remediated && existing.Status == FindingStatus.Open)
                {
                    // If imported says remediated but existing is open, keep open (more conservative)
                    // unless existing was already remediated
                }
                else if (existing.Status == FindingStatus.Remediated && importedStatus == FindingStatus.Remediated)
                {
                    // Both say remediated — keep as-is
                }

                // Use imported severity only if higher (CatI > CatII > CatIII)
                if (importedCat.HasValue && existing.CatSeverity.HasValue)
                {
                    if (importedCat.Value < existing.CatSeverity.Value) // lower enum = higher severity
                    {
                        existing.Severity = importedSeverity;
                        existing.CatSeverity = importedCat;
                    }
                }
                else if (importedCat.HasValue && !existing.CatSeverity.HasValue)
                {
                    existing.Severity = importedSeverity;
                    existing.CatSeverity = importedCat;
                }

                // Append finding details if different
                if (!string.IsNullOrEmpty(importedDetails) &&
                    !existing.Description.Contains(importedDetails, StringComparison.OrdinalIgnoreCase))
                {
                    existing.Description = string.IsNullOrEmpty(existing.Description)
                        ? importedDetails
                        : $"{existing.Description}\n\n[Merged from import]: {importedDetails}";
                }

                existing.ImportRecordId = importRecordId;
                return ImportFindingAction.Updated;

            default:
                return ImportFindingAction.Skipped;
        }
    }

    // ─── Assessment Context ──────────────────────────────────────────────

    /// <summary>
    /// Get an existing active assessment for the system, or create one.
    /// </summary>
    private static async Task<string> GetOrCreateAssessmentAsync(
        AtoCopilotContext ctx,
        string systemId,
        string importedBy,
        CancellationToken ct)
    {
        // Try to find an active (non-completed, non-cancelled) assessment for this system
        var existing = await ctx.Assessments
            .Where(a => a.RegisteredSystemId == systemId &&
                        a.Status != AssessmentStatus.Completed &&
                        a.Status != AssessmentStatus.Cancelled &&
                        a.Status != AssessmentStatus.Failed)
            .OrderByDescending(a => a.AssessedAt)
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
            return existing.Id;

        // Create a new assessment for STIG/SCAP import
        var assessment = new ComplianceAssessment
        {
            RegisteredSystemId = systemId,
            Framework = "NIST80053",
            ScanType = "combined",
            Status = AssessmentStatus.InProgress,
            InitiatedBy = importedBy,
            ProgressMessage = "Created for STIG/SCAP import"
        };

        ctx.Assessments.Add(assessment);
        await ctx.SaveChangesAsync(ct);

        return assessment.Id;
    }

    // ─── Utilities ───────────────────────────────────────────────────────

    /// <summary>Compute SHA-256 hash of raw bytes, returned as lowercase hex.</summary>
    internal static string ComputeSha256(byte[] data)
    {
        var hashBytes = SHA256.HashData(data);
        return Convert.ToHexStringLower(hashBytes);
    }

    /// <summary>Extract control family prefix (e.g., "AC" from "AC-2").</summary>
    private static string ExtractControlFamily(string controlId)
    {
        if (string.IsNullOrEmpty(controlId))
            return string.Empty;
        var dashIndex = controlId.IndexOf('-');
        return dashIndex > 0 ? controlId[..dashIndex] : controlId;
    }

    /// <summary>Create a failed ImportResult with error message.</summary>
    private static ImportResult CreateFailedResult(string error, string fileName)
    {
        return new ImportResult(
            ImportRecordId: string.Empty,
            Status: ScanImportStatus.Failed,
            BenchmarkId: string.Empty,
            BenchmarkTitle: null,
            TotalEntries: 0,
            OpenCount: 0,
            PassCount: 0,
            NotApplicableCount: 0,
            NotReviewedCount: 0,
            ErrorCount: 0,
            SkippedCount: 0,
            UnmatchedCount: 0,
            FindingsCreated: 0,
            FindingsUpdated: 0,
            EffectivenessRecordsCreated: 0,
            EffectivenessRecordsUpdated: 0,
            NistControlsAffected: 0,
            Warnings: new List<string>(),
            UnmatchedRules: new List<UnmatchedRuleInfo>(),
            ErrorMessage: error);
    }

    // ─── Feature 019: Prisma Cloud Import ───────────────────────────────

    /// <inheritdoc />
    public async Task<PrismaImportResult> ImportPrismaCsvAsync(
        string? systemId,
        string? assessmentId,
        byte[] fileContent,
        string fileName,
        ImportConflictResolution resolution,
        bool dryRun,
        string importedBy,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        // ── Step 1: File size check (25 MB) ──────────────────────────────
        if (fileContent.Length > 25 * 1024 * 1024)
        {
            sw.Stop();
            return new PrismaImportResult(
                new List<PrismaSystemImportResult>(),
                new List<UnresolvedSubscriptionInfo>(),
                null, 0, 0, sw.ElapsedMilliseconds,
                ErrorMessage: "File exceeds maximum size of 25 MB.");
        }

        // ── Step 2: Parse CSV ────────────────────────────────────────────
        ParsedPrismaFile parsed;
        try
        {
            parsed = _prismaCsvParser.Parse(fileContent, fileName);
        }
        catch (PrismaParseException ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "Prisma CSV parse failed for file {FileName}", fileName);
            return new PrismaImportResult(
                new List<PrismaSystemImportResult>(),
                new List<UnresolvedSubscriptionInfo>(),
                null, 0, 0, sw.ElapsedMilliseconds,
                ErrorMessage: $"CSV parse error: {ex.Message}");
        }

        var fileHash = $"sha256:{ComputeSha256(fileContent)}";
        var imports = new List<PrismaSystemImportResult>();
        var unresolvedSubscriptions = new List<UnresolvedSubscriptionInfo>();
        SkippedNonAzureInfo? skippedNonAzure = null;
        int totalProcessed = 0;
        int totalSkipped = 0;

        _logger.LogInformation(
            "Prisma CSV import started: file={FileName}, hash={FileHash}, system={SystemId}, " +
            "alerts={AlertCount}, resolution={Resolution}, dryRun={DryRun}",
            fileName, fileHash, systemId ?? "(auto)", parsed.TotalAlerts, resolution, dryRun);

        if (systemId is not null)
        {
            // ── Explicit system — validate and import all alerts ─────────
            var system = await _rmfService.GetSystemAsync(systemId, ct);
            if (system is null)
            {
                sw.Stop();
                return new PrismaImportResult(
                    new List<PrismaSystemImportResult>(),
                    new List<UnresolvedSubscriptionInfo>(),
                    null, 0, 0, sw.ElapsedMilliseconds,
                    ErrorMessage: $"System '{systemId}' not found.");
            }

            var result = await ImportPrismaAlertsForSystemAsync(
                systemId, system.Name, assessmentId, parsed.Alerts,
                fileContent, fileName, fileHash, resolution, dryRun, importedBy,
                ScanImportType.PrismaCsv, ct);
            imports.Add(result);
            totalProcessed = result.TotalAlerts;
        }
        else
        {
            // ── Auto-resolve mode — split by cloud type, then by account ─
            var azureAlerts = parsed.Alerts
                .Where(a => string.Equals(a.CloudType, "azure", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var nonAzureAlerts = parsed.Alerts
                .Where(a => !string.Equals(a.CloudType, "azure", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (nonAzureAlerts.Count > 0)
            {
                var cloudTypes = nonAzureAlerts
                    .Select(a => a.CloudType)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                skippedNonAzure = new SkippedNonAzureInfo(
                    nonAzureAlerts.Count,
                    cloudTypes,
                    $"Skipped {nonAzureAlerts.Count} non-Azure alert(s) from cloud type(s): " +
                    $"{string.Join(", ", cloudTypes)}. Provide an explicit system_id to import these.");
                totalSkipped = nonAzureAlerts.Count;
            }

            // Group Azure alerts by AccountId and resolve each
            var groupedByAccount = azureAlerts
                .GroupBy(a => a.AccountId, StringComparer.OrdinalIgnoreCase);

            foreach (var group in groupedByAccount)
            {
                var accountId = group.Key;
                var accountAlerts = group.ToList();

                var resolvedSystemId = await _subscriptionResolver.ResolveAsync(accountId, ct);
                if (resolvedSystemId is null)
                {
                    _logger.LogWarning(
                        "Prisma CSV subscription unresolved: subscriptionId={SubscriptionId}, accountName={AccountName}, alertCount={AlertCount}",
                        accountId, accountAlerts[0].AccountName, accountAlerts.Count);
                    unresolvedSubscriptions.Add(new UnresolvedSubscriptionInfo(
                        accountId,
                        accountAlerts[0].AccountName,
                        accountAlerts.Count,
                        $"Azure subscription '{accountId}' is not registered. " +
                        $"Register it with a system to import these {accountAlerts.Count} alert(s)."));
                    totalSkipped += accountAlerts.Count;
                    continue;
                }

                _logger.LogInformation(
                    "Prisma CSV subscription resolved: subscriptionId={SubscriptionId}, resolvedSystemId={ResolvedSystemId}",
                    accountId, resolvedSystemId);

                var system = await _rmfService.GetSystemAsync(resolvedSystemId, ct);
                if (system is null)
                {
                    unresolvedSubscriptions.Add(new UnresolvedSubscriptionInfo(
                        accountId,
                        accountAlerts[0].AccountName,
                        accountAlerts.Count,
                        $"Resolved system '{resolvedSystemId}' for subscription '{accountId}' not found."));
                    totalSkipped += accountAlerts.Count;
                    continue;
                }

                var result = await ImportPrismaAlertsForSystemAsync(
                    resolvedSystemId, system.Name, assessmentId, accountAlerts,
                    fileContent, fileName, fileHash, resolution, dryRun, importedBy,
                    ScanImportType.PrismaCsv, ct);
                imports.Add(result);
                totalProcessed += result.TotalAlerts;
            }
        }

        sw.Stop();
        _logger.LogInformation(
            "Prisma CSV import completed: file={FileName}, duration={DurationMs}ms, " +
            "systems={SystemCount}, totalProcessed={TotalProcessed}, skipped={TotalSkipped}, " +
            "findingsCreated={FindingsCreated}, nistControlsAffected={NistControlsAffected}, " +
            "effectivenessUpserted={EffectivenessUpserted}",
            fileName, sw.ElapsedMilliseconds, imports.Count, totalProcessed, totalSkipped,
            imports.Sum(i => i.FindingsCreated), imports.Sum(i => i.NistControlsAffected),
            imports.Sum(i => i.EffectivenessRecordsCreated + i.EffectivenessRecordsUpdated));

        return new PrismaImportResult(
            imports,
            unresolvedSubscriptions,
            skippedNonAzure,
            totalProcessed,
            totalSkipped,
            sw.ElapsedMilliseconds);
    }

    /// <summary>
    /// Import a batch of Prisma alerts for a single resolved system.
    /// Creates findings, evidence, effectiveness records, and handles conflict resolution.
    /// </summary>
    private async Task<PrismaSystemImportResult> ImportPrismaAlertsForSystemAsync(
        string systemId,
        string systemName,
        string? assessmentId,
        List<ParsedPrismaAlert> alerts,
        byte[] fileContent,
        string fileName,
        string fileHash,
        ImportConflictResolution resolution,
        bool dryRun,
        string importedBy,
        ScanImportType importType,
        CancellationToken ct)
    {
        var warnings = new List<string>();

        using var scope = _scopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        // ── Resolve assessment ───────────────────────────────────────────
        string resolvedAssessmentId;
        if (!string.IsNullOrEmpty(assessmentId))
        {
            var existing = await ctx.Assessments.FindAsync(new object[] { assessmentId }, ct);
            if (existing is null)
            {
                return new PrismaSystemImportResult(
                    string.Empty, systemId, systemName, ScanImportStatus.Failed,
                    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                    false, fileHash, dryRun,
                    new List<string> { $"Assessment '{assessmentId}' not found." });
            }
            resolvedAssessmentId = assessmentId;
        }
        else
        {
            resolvedAssessmentId = await GetOrCreateAssessmentAsync(ctx, systemId, importedBy, ct);
        }

        // ── Load baseline ────────────────────────────────────────────────
        var baseline = await _baselineService.GetBaselineAsync(systemId, cancellationToken: ct);
        var baselineSet = new HashSet<string>(
            baseline?.ControlIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

        // ── Create import record ─────────────────────────────────────────
        var importRecord = new ScanImportRecord
        {
            RegisteredSystemId = systemId,
            AssessmentId = resolvedAssessmentId,
            ImportType = importType,
            FileName = fileName,
            FileHash = fileHash,
            FileSizeBytes = fileContent.Length,
            ConflictResolution = resolution,
            IsDryRun = dryRun,
            ImportedBy = importedBy
        };

        // ── Counters ─────────────────────────────────────────────────────
        int openCount = 0, resolvedCount = 0, dismissedCount = 0, snoozedCount = 0;
        int findingsCreated = 0, findingsUpdated = 0, skippedCount = 0;
        int remediableCount = 0, cliScriptsExtracted = 0, alertsWithHistory = 0;
        var allPolicyLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var affectedNistControls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var importFindings = new List<ScanImportFinding>();
        var newFindings = new List<ComplianceFinding>();
        var unmappedPolicyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ── Preload existing import findings for conflict detection ──────
        var existingPrismaFindings = await ctx.ScanImportFindings
            .Where(f => f.PrismaAlertId != null)
            .ToListAsync(ct);

        var existingByAlertId = existingPrismaFindings
            .Where(f => f.PrismaAlertId is not null)
            .GroupBy(f => f.PrismaAlertId!)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(f => f.Id).First(),
                StringComparer.OrdinalIgnoreCase);

        // ── Process each alert ───────────────────────────────────────────
        foreach (var alert in alerts)
        {
            // Count by status
            switch (alert.Status.ToLowerInvariant())
            {
                case "open": openCount++; break;
                case "resolved": resolvedCount++; break;
                case "dismissed": dismissedCount++; break;
                case "snoozed": snoozedCount++; break;
            }

            // Check for unmapped policies (no NIST controls)
            if (alert.NistControlIds.Count == 0)
            {
                unmappedPolicyNames.Add(alert.PolicyName);
            }

            // Track API JSON enhanced metrics
            if (alert.Remediable) remediableCount++;
            if (!string.IsNullOrWhiteSpace(alert.RemediationScript)) cliScriptsExtracted++;
            if (alert.AlertHistory is { Count: > 0 }) alertsWithHistory++;
            if (alert.PolicyLabels is not null)
            {
                foreach (var label in alert.PolicyLabels)
                    allPolicyLabels.Add(label);
            }

            // Map status and severity
            var findingStatus = MapPrismaStatus(alert.Status);
            var findingSeverity = PrismaSeverityMapper.MapToFindingSeverity(alert.Severity);
            var catSeverity = PrismaSeverityMapper.MapToCatSeverity(alert.Severity);
            var description = BuildPrismaDescription(alert);

            // Primary NIST control
            var primaryControl = alert.NistControlIds.FirstOrDefault() ?? string.Empty;
            var controlFamily = ExtractControlFamily(primaryControl);

            // Create ScanImportFinding
            var importFinding = new ScanImportFinding
            {
                ScanImportRecordId = importRecord.Id,
                VulnId = alert.AlertId,
                RawStatus = alert.Status,
                RawSeverity = alert.Severity,
                MappedSeverity = catSeverity,
                ResolvedNistControlIds = alert.NistControlIds,
                PrismaAlertId = alert.AlertId,
                PrismaPolicyName = alert.PolicyName,
                CloudResourceId = alert.ResourceId,
                CloudResourceType = alert.ResourceType,
                CloudRegion = alert.Region,
                CloudAccountId = alert.AccountId
            };

            // ── Conflict detection ───────────────────────────────────────
            if (existingByAlertId.TryGetValue(alert.AlertId, out var existingImport))
            {
                if (resolution == ImportConflictResolution.Skip)
                {
                    importFinding.ImportAction = ImportFindingAction.Skipped;
                    importFinding.ComplianceFindingId = existingImport.ComplianceFindingId;
                    skippedCount++;
                    importFindings.Add(importFinding);
                    continue;
                }

                if (resolution == ImportConflictResolution.Overwrite
                    && existingImport.ComplianceFindingId is not null)
                {
                    var existingFinding = await ctx.Findings
                        .FindAsync(new object[] { existingImport.ComplianceFindingId }, ct);

                    if (existingFinding is not null)
                    {
                        existingFinding.Status = findingStatus;
                        existingFinding.Severity = findingSeverity;
                        existingFinding.CatSeverity = catSeverity;
                        existingFinding.Title = alert.PolicyName;
                        existingFinding.Description = description;
                        existingFinding.ImportRecordId = importRecord.Id;

                        importFinding.ImportAction = ImportFindingAction.Updated;
                        importFinding.ComplianceFindingId = existingFinding.Id;
                        findingsUpdated++;
                        importFindings.Add(importFinding);

                        foreach (var nist in alert.NistControlIds.Where(c => baselineSet.Contains(c)))
                            affectedNistControls.Add(nist);
                        continue;
                    }
                }
            }

            // ── Create new ComplianceFinding ─────────────────────────────
            var newFinding = new ComplianceFinding
            {
                ControlId = primaryControl,
                ControlFamily = controlFamily,
                Title = alert.PolicyName,
                Description = description,
                Severity = findingSeverity,
                Status = findingStatus,
                ResourceId = alert.ResourceId,
                ResourceType = alert.ResourceType,
                Source = "Prisma Cloud",
                ScanSource = ScanSourceType.Cloud,
                StigFinding = false,
                CatSeverity = catSeverity,
                AssessmentId = resolvedAssessmentId,
                ImportRecordId = importRecord.Id,
                // API JSON enhanced fields (populated when available)
                RemediationGuidance = alert.Recommendation ?? string.Empty,
                RemediationScript = alert.RemediationScript,
                AutoRemediable = alert.Remediable
            };

            importFinding.ImportAction = ImportFindingAction.Created;
            importFinding.ComplianceFindingId = newFinding.Id;
            newFindings.Add(newFinding);
            findingsCreated++;

            // Track affected NIST controls (in-baseline only) for effectiveness
            foreach (var nist in alert.NistControlIds.Where(c => baselineSet.Contains(c)))
                affectedNistControls.Add(nist);

            importFindings.Add(importFinding);
        }

        // ── Unmapped policy warnings ─────────────────────────────────────
        if (unmappedPolicyNames.Count > 0)
        {
            foreach (var policyName in unmappedPolicyNames)
            {
                warnings.Add($"Policy '{policyName}' has no NIST 800-53 mapping.");
            }
        }

        // ── Create evidence ──────────────────────────────────────────────
        var evidenceContent = JsonSerializer.Serialize(new
        {
            ImportType = importType.ToString(),
            FileName = fileName,
            TotalAlerts = alerts.Count,
            OpenCount = openCount,
            ResolvedCount = resolvedCount,
            DismissedCount = dismissedCount,
            SnoozedCount = snoozedCount
        });

        var evidence = new ComplianceEvidence
        {
            ControlId = affectedNistControls.FirstOrDefault() ?? string.Empty,
            SubscriptionId = string.Empty,
            EvidenceType = "CloudScanResult",
            Description = $"Prisma Cloud {importType} Import: {fileName}",
            Content = evidenceContent,
            CollectedAt = DateTime.UtcNow,
            CollectedBy = importedBy,
            AssessmentId = resolvedAssessmentId,
            EvidenceCategory = EvidenceCategory.Configuration,
            ContentHash = fileHash,
            CollectionMethod = "Automated"
        };

        // ── Upsert effectiveness (non-dry-run only) ─────────────────────
        int effectivenessCreated = 0, effectivenessUpdated = 0;
        if (!dryRun && affectedNistControls.Count > 0)
        {
            var allExistingFindings = await ctx.Findings
                .Where(f => f.AssessmentId == resolvedAssessmentId)
                .ToListAsync(ct);

            var allFindings = allExistingFindings.Concat(newFindings).ToList();
            var controlFindingMap = allFindings
                .Where(f => !string.IsNullOrEmpty(f.ControlId))
                .GroupBy(f => f.ControlId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            foreach (var controlId in affectedNistControls)
            {
                controlFindingMap.TryGetValue(controlId, out var controlFindings);
                var findingsForControl = controlFindings ?? new List<ComplianceFinding>();

                var anyOpen = findingsForControl.Any(f =>
                    f.Status == FindingStatus.Open || f.Status == FindingStatus.InProgress);
                var determination = anyOpen
                    ? EffectivenessDetermination.OtherThanSatisfied
                    : EffectivenessDetermination.Satisfied;

                CatSeverity? controlCatSeverity = null;
                if (anyOpen)
                {
                    var openSeverities = findingsForControl
                        .Where(f => f.Status == FindingStatus.Open || f.Status == FindingStatus.InProgress)
                        .Where(f => f.CatSeverity.HasValue)
                        .Select(f => f.CatSeverity!.Value)
                        .ToList();
                    if (openSeverities.Count > 0)
                        controlCatSeverity = openSeverities.Min();
                }

                var existingEffectiveness = await ctx.ControlEffectivenessRecords
                    .Where(e => e.AssessmentId == resolvedAssessmentId &&
                                e.RegisteredSystemId == systemId &&
                                e.ControlId == controlId)
                    .FirstOrDefaultAsync(ct);

                if (existingEffectiveness is not null)
                {
                    existingEffectiveness.Determination = determination;
                    existingEffectiveness.AssessmentMethod = "Test";
                    existingEffectiveness.AssessorId = importedBy;
                    existingEffectiveness.AssessedAt = DateTime.UtcNow;
                    existingEffectiveness.CatSeverity = controlCatSeverity;
                    if (!existingEffectiveness.EvidenceIds.Contains(evidence.Id))
                        existingEffectiveness.EvidenceIds.Add(evidence.Id);
                    existingEffectiveness.Notes = $"Re-evaluated via Prisma Cloud import '{fileName}'";
                    effectivenessUpdated++;
                }
                else
                {
                    var effectiveness = new ControlEffectiveness
                    {
                        AssessmentId = resolvedAssessmentId,
                        RegisteredSystemId = systemId,
                        ControlId = controlId,
                        Determination = determination,
                        AssessmentMethod = "Test",
                        EvidenceIds = new List<string> { evidence.Id },
                        AssessorId = importedBy,
                        CatSeverity = controlCatSeverity,
                        Notes = $"Auto-determined via Prisma Cloud import '{fileName}'"
                    };
                    ctx.ControlEffectivenessRecords.Add(effectiveness);
                    effectivenessCreated++;
                }
            }
        }

        // ── Update import record counts ──────────────────────────────────
        importRecord.TotalEntries = alerts.Count;
        importRecord.OpenCount = openCount;
        importRecord.FindingsCreated = findingsCreated;
        importRecord.FindingsUpdated = findingsUpdated;
        importRecord.SkippedCount = skippedCount;
        importRecord.EffectivenessRecordsCreated = effectivenessCreated;
        importRecord.EffectivenessRecordsUpdated = effectivenessUpdated;
        importRecord.NistControlsAffected = affectedNistControls.Count;
        importRecord.Warnings = warnings;
        importRecord.ImportStatus = warnings.Count > 0
            ? ScanImportStatus.CompletedWithWarnings
            : ScanImportStatus.Completed;

        // ── Persist (unless dry-run) ─────────────────────────────────────
        if (!dryRun)
        {
            ctx.ScanImportRecords.Add(importRecord);
            ctx.ScanImportFindings.AddRange(importFindings);
            ctx.Findings.AddRange(newFindings);
            ctx.Evidence.Add(evidence);
            await ctx.SaveChangesAsync(ct);
        }

        _logger.LogInformation(
            "Prisma import for system {SystemId}: findings={Created}/{Updated}, " +
            "effectiveness={EffCreated}/{EffUpdated}, skipped={Skipped}, unmapped={Unmapped}",
            systemId, findingsCreated, findingsUpdated,
            effectivenessCreated, effectivenessUpdated, skippedCount, unmappedPolicyNames.Count);

        return new PrismaSystemImportResult(
            importRecord.Id,
            systemId,
            systemName,
            importRecord.ImportStatus,
            alerts.Count,
            openCount,
            resolvedCount,
            dismissedCount,
            snoozedCount,
            findingsCreated,
            findingsUpdated,
            skippedCount,
            unmappedPolicyNames.Count,
            effectivenessCreated,
            effectivenessUpdated,
            affectedNistControls.Count,
            !dryRun,    // evidence persisted only when not dry-run
            fileHash,
            dryRun,
            warnings,
            RemediableCount: remediableCount,
            CliScriptsExtracted: cliScriptsExtracted,
            PolicyLabelsFound: allPolicyLabels.Count > 0 ? allPolicyLabels.ToList() : null,
            AlertsWithHistory: alertsWithHistory);
    }

    /// <summary>Map Prisma alert status to <see cref="FindingStatus"/>.</summary>
    private static FindingStatus MapPrismaStatus(string status) =>
        status.ToLowerInvariant() switch
        {
            "open"      => FindingStatus.Open,
            "resolved"  => FindingStatus.Remediated,
            "dismissed" => FindingStatus.Accepted,
            "snoozed"   => FindingStatus.Open,
            _           => FindingStatus.Open
        };

    /// <summary>Build description string for a Prisma Cloud finding.</summary>
    private static string BuildPrismaDescription(ParsedPrismaAlert alert)
    {
        var parts = new List<string>
        {
            $"Prisma Cloud Alert: {alert.PolicyName}",
            $"Cloud: {alert.CloudType} | Region: {alert.Region} | Resource: {alert.ResourceName}"
        };

        if (alert.Status.Equals("snoozed", StringComparison.OrdinalIgnoreCase))
            parts.Add("Note: Alert is currently snoozed in Prisma Cloud.");

        if (!string.IsNullOrEmpty(alert.Description))
            parts.Add(alert.Description);

        return string.Join("\n", parts);
    }

    /// <inheritdoc />
    public async Task<PrismaImportResult> ImportPrismaApiAsync(
        string? systemId,
        string? assessmentId,
        byte[] fileContent,
        string fileName,
        ImportConflictResolution resolution,
        bool dryRun,
        string importedBy,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        // ── Step 1: File size check (25 MB) ──────────────────────────────
        if (fileContent.Length > 25 * 1024 * 1024)
        {
            sw.Stop();
            return new PrismaImportResult(
                new List<PrismaSystemImportResult>(),
                new List<UnresolvedSubscriptionInfo>(),
                null, 0, 0, sw.ElapsedMilliseconds,
                ErrorMessage: "File exceeds maximum size of 25 MB.");
        }

        // ── Step 2: Parse API JSON ───────────────────────────────────────
        ParsedPrismaFile parsed;
        try
        {
            parsed = _prismaApiJsonParser.Parse(fileContent, fileName);
        }
        catch (PrismaParseException ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "Prisma API JSON parse failed for file {FileName}", fileName);
            return new PrismaImportResult(
                new List<PrismaSystemImportResult>(),
                new List<UnresolvedSubscriptionInfo>(),
                null, 0, 0, sw.ElapsedMilliseconds,
                ErrorMessage: $"JSON parse error: {ex.Message}");
        }

        var fileHash = $"sha256:{ComputeSha256(fileContent)}";
        var imports = new List<PrismaSystemImportResult>();
        var unresolvedSubscriptions = new List<UnresolvedSubscriptionInfo>();
        SkippedNonAzureInfo? skippedNonAzure = null;
        int totalProcessed = 0;
        int totalSkipped = 0;

        _logger.LogInformation(
            "Prisma API JSON import started: file={FileName}, hash={FileHash}, system={SystemId}, " +
            "alerts={AlertCount}, resolution={Resolution}, dryRun={DryRun}",
            fileName, fileHash, systemId ?? "(auto)", parsed.TotalAlerts, resolution, dryRun);

        if (systemId is not null)
        {
            // ── Explicit system — validate and import all alerts ─────────
            var system = await _rmfService.GetSystemAsync(systemId, ct);
            if (system is null)
            {
                sw.Stop();
                return new PrismaImportResult(
                    new List<PrismaSystemImportResult>(),
                    new List<UnresolvedSubscriptionInfo>(),
                    null, 0, 0, sw.ElapsedMilliseconds,
                    ErrorMessage: $"System '{systemId}' not found.");
            }

            var result = await ImportPrismaAlertsForSystemAsync(
                systemId, system.Name, assessmentId, parsed.Alerts,
                fileContent, fileName, fileHash, resolution, dryRun, importedBy,
                ScanImportType.PrismaApi, ct);
            imports.Add(result);
            totalProcessed = result.TotalAlerts;
        }
        else
        {
            // ── Auto-resolve mode — split by cloud type, then by account ─
            var azureAlerts = parsed.Alerts
                .Where(a => string.Equals(a.CloudType, "azure", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var nonAzureAlerts = parsed.Alerts
                .Where(a => !string.Equals(a.CloudType, "azure", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (nonAzureAlerts.Count > 0)
            {
                var cloudTypes = nonAzureAlerts
                    .Select(a => a.CloudType)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                skippedNonAzure = new SkippedNonAzureInfo(
                    nonAzureAlerts.Count,
                    cloudTypes,
                    $"Skipped {nonAzureAlerts.Count} non-Azure alert(s) from cloud type(s): " +
                    $"{string.Join(", ", cloudTypes)}. Provide an explicit system_id to import these.");
                totalSkipped = nonAzureAlerts.Count;
            }

            // Group Azure alerts by AccountId and resolve each
            var groupedByAccount = azureAlerts
                .GroupBy(a => a.AccountId, StringComparer.OrdinalIgnoreCase);

            foreach (var group in groupedByAccount)
            {
                var accountId = group.Key;
                var accountAlerts = group.ToList();

                var resolvedSystemId = await _subscriptionResolver.ResolveAsync(accountId, ct);
                if (resolvedSystemId is null)
                {
                    _logger.LogWarning(
                        "Prisma API JSON subscription unresolved: subscriptionId={SubscriptionId}, accountName={AccountName}, alertCount={AlertCount}",
                        accountId, accountAlerts[0].AccountName, accountAlerts.Count);
                    unresolvedSubscriptions.Add(new UnresolvedSubscriptionInfo(
                        accountId,
                        accountAlerts[0].AccountName,
                        accountAlerts.Count,
                        $"Azure subscription '{accountId}' is not registered. " +
                        $"Register it with a system to import these {accountAlerts.Count} alert(s)."));
                    totalSkipped += accountAlerts.Count;
                    continue;
                }

                _logger.LogInformation(
                    "Prisma API JSON subscription resolved: subscriptionId={SubscriptionId}, resolvedSystemId={ResolvedSystemId}",
                    accountId, resolvedSystemId);

                var system = await _rmfService.GetSystemAsync(resolvedSystemId, ct);
                if (system is null)
                {
                    unresolvedSubscriptions.Add(new UnresolvedSubscriptionInfo(
                        accountId,
                        accountAlerts[0].AccountName,
                        accountAlerts.Count,
                        $"Resolved system '{resolvedSystemId}' for subscription '{accountId}' not found."));
                    totalSkipped += accountAlerts.Count;
                    continue;
                }

                var result = await ImportPrismaAlertsForSystemAsync(
                    resolvedSystemId, system.Name, assessmentId, accountAlerts,
                    fileContent, fileName, fileHash, resolution, dryRun, importedBy,
                    ScanImportType.PrismaApi, ct);
                imports.Add(result);
                totalProcessed += result.TotalAlerts;
            }
        }

        sw.Stop();
        _logger.LogInformation(
            "Prisma API JSON import completed: file={FileName}, duration={DurationMs}ms, " +
            "systems={SystemCount}, totalProcessed={TotalProcessed}, skipped={TotalSkipped}, " +
            "findingsCreated={FindingsCreated}, nistControlsAffected={NistControlsAffected}, " +
            "effectivenessUpserted={EffectivenessUpserted}",
            fileName, sw.ElapsedMilliseconds, imports.Count, totalProcessed, totalSkipped,
            imports.Sum(i => i.FindingsCreated), imports.Sum(i => i.NistControlsAffected),
            imports.Sum(i => i.EffectivenessRecordsCreated + i.EffectivenessRecordsUpdated));

        return new PrismaImportResult(
            imports,
            unresolvedSubscriptions,
            skippedNonAzure,
            totalProcessed,
            totalSkipped,
            sw.ElapsedMilliseconds);
    }

    /// <inheritdoc />
    public async Task<PrismaPolicyListResult> ListPrismaPoliciesAsync(
        string systemId,
        CancellationToken ct = default)
    {
        var system = await _rmfService.GetSystemAsync(systemId, ct)
            ?? throw new InvalidOperationException($"System '{systemId}' not found.");

        using var scope = _scopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        // Get all Prisma import records for this system
        var prismaImportIds = await ctx.ScanImportRecords
            .Where(r => r.RegisteredSystemId == systemId &&
                        (r.ImportType == ScanImportType.PrismaCsv || r.ImportType == ScanImportType.PrismaApi))
            .Select(r => r.Id)
            .ToListAsync(ct);

        if (prismaImportIds.Count == 0)
        {
            return new PrismaPolicyListResult(systemId, 0, new List<PrismaPolicyEntry>());
        }

        // Get all ScanImportFindings with Prisma policy data
        var findings = await ctx.ScanImportFindings
            .Where(f => prismaImportIds.Contains(f.ScanImportRecordId) &&
                        f.PrismaPolicyName != null)
            .ToListAsync(ct);

        // Also get related ComplianceFindings for status info
        var findingIds = findings
            .Where(f => f.ComplianceFindingId != null)
            .Select(f => f.ComplianceFindingId!)
            .Distinct()
            .ToList();

        var complianceFindings = await ctx.Findings
            .Where(f => findingIds.Contains(f.Id))
            .ToListAsync(ct);

        var cfLookup = complianceFindings.ToDictionary(f => f.Id);

        // Get import records for lastSeen info
        var importRecords = await ctx.ScanImportRecords
            .Where(r => prismaImportIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, ct);

        // Group by policy name
        var policyGroups = findings.GroupBy(f => f.PrismaPolicyName!);

        var policies = new List<PrismaPolicyEntry>();
        foreach (var group in policyGroups)
        {
            var policyFindings = group.ToList();

            // Collect NIST controls from associated ComplianceFindings
            var nistControls = new HashSet<string>();
            int openCount = 0, resolvedCount = 0, dismissedCount = 0;
            var resourceTypes = new HashSet<string>();
            string lastImportId = string.Empty;
            DateTime lastSeenAt = DateTime.MinValue;

            foreach (var sif in policyFindings)
            {
                // NIST controls from the ScanImportFinding
                foreach (var nist in sif.ResolvedNistControlIds)
                    nistControls.Add(nist);

                // Status counts from associated ComplianceFinding
                if (sif.ComplianceFindingId != null && cfLookup.TryGetValue(sif.ComplianceFindingId, out var cf))
                {
                    switch (cf.Status)
                    {
                        case FindingStatus.Open: openCount++; break;
                        case FindingStatus.Remediated: resolvedCount++; break;
                        case FindingStatus.Accepted: dismissedCount++; break;
                    }
                }

                // Resource types
                if (!string.IsNullOrEmpty(sif.CloudResourceType))
                    resourceTypes.Add(sif.CloudResourceType);

                // Last seen tracking
                if (importRecords.TryGetValue(sif.ScanImportRecordId, out var imp) && imp.ImportedAt > lastSeenAt)
                {
                    lastSeenAt = imp.ImportedAt;
                    lastImportId = imp.Id;
                }
            }

            // Get severity and type from the first finding's raw data
            var firstFinding = policyFindings.First();
            policies.Add(new PrismaPolicyEntry(
                PolicyName: group.Key,
                PolicyType: "config",
                Severity: firstFinding.RawSeverity,
                NistControlIds: nistControls.OrderBy(c => c).ToList(),
                OpenCount: openCount,
                ResolvedCount: resolvedCount,
                DismissedCount: dismissedCount,
                AffectedResourceTypes: resourceTypes.OrderBy(r => r).ToList(),
                LastSeenImportId: lastImportId,
                LastSeenAt: lastSeenAt));
        }

        return new PrismaPolicyListResult(
            SystemId: systemId,
            TotalPolicies: policies.Count,
            Policies: policies.OrderBy(p => p.PolicyName).ToList());
    }

    /// <inheritdoc />
    public async Task<PrismaTrendResult> GetPrismaTrendAsync(
        string systemId,
        List<string>? importIds,
        string? groupBy,
        CancellationToken ct = default)
    {
        var system = await _rmfService.GetSystemAsync(systemId, ct)
            ?? throw new InvalidOperationException($"System '{systemId}' not found.");

        using var scope = _scopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        // Get Prisma import records for this system
        IQueryable<ScanImportRecord> importQuery = ctx.ScanImportRecords
            .Where(r => r.RegisteredSystemId == systemId &&
                        (r.ImportType == ScanImportType.PrismaCsv || r.ImportType == ScanImportType.PrismaApi));

        List<ScanImportRecord> selectedImports;

        if (importIds != null && importIds.Count > 0)
        {
            selectedImports = await importQuery
                .Where(r => importIds.Contains(r.Id))
                .OrderBy(r => r.ImportedAt)
                .ToListAsync(ct);
        }
        else
        {
            selectedImports = await importQuery
                .OrderByDescending(r => r.ImportedAt)
                .Take(2)
                .OrderBy(r => r.ImportedAt) // re-order oldest first
                .ToListAsync(ct);
        }

        if (selectedImports.Count == 0)
        {
            throw new InvalidOperationException($"No Prisma imports found for system '{systemId}'.");
        }

        // Build import snapshots
        var trendImports = selectedImports.Select(imp => new PrismaTrendImport(
            ImportId: imp.Id,
            ImportedAt: imp.ImportedAt,
            FileName: imp.FileName,
            TotalAlerts: imp.TotalEntries,
            OpenCount: imp.OpenCount,
            ResolvedCount: imp.PassCount, // PassCount reused for resolved in Prisma context
            DismissedCount: imp.NotApplicableCount // NotApplicableCount reused for dismissed
        )).ToList();

        int newFindings, resolvedFindings, persistentFindings;
        decimal remediationRate;

        if (selectedImports.Count == 1)
        {
            // Snapshot mode — all findings are "new"
            var importFindings = await ctx.ScanImportFindings
                .Where(f => f.ScanImportRecordId == selectedImports[0].Id && f.PrismaAlertId != null)
                .ToListAsync(ct);

            newFindings = importFindings.Count;
            resolvedFindings = 0;
            persistentFindings = 0;
            remediationRate = 0m;
        }
        else
        {
            var olderImport = selectedImports[0];
            var newerImport = selectedImports[^1];

            var olderAlertIds = (await ctx.ScanImportFindings
                .Where(f => f.ScanImportRecordId == olderImport.Id && f.PrismaAlertId != null)
                .Select(f => f.PrismaAlertId!)
                .ToListAsync(ct))
                .ToHashSet();

            var newerAlertIds = (await ctx.ScanImportFindings
                .Where(f => f.ScanImportRecordId == newerImport.Id && f.PrismaAlertId != null)
                .Select(f => f.PrismaAlertId!)
                .ToListAsync(ct))
                .ToHashSet();

            newFindings = newerAlertIds.Count(id => !olderAlertIds.Contains(id));
            resolvedFindings = olderAlertIds.Count(id => !newerAlertIds.Contains(id));
            persistentFindings = newerAlertIds.Count(id => olderAlertIds.Contains(id));

            var denominator = resolvedFindings + persistentFindings;
            remediationRate = denominator > 0
                ? Math.Round((decimal)resolvedFindings / denominator * 100, 2)
                : 0m;
        }

        // Build optional breakdowns
        Dictionary<string, int>? resourceTypeBreakdown = null;
        Dictionary<string, int>? nistControlBreakdown = null;

        if (groupBy != null)
        {
            var latestImport = selectedImports[^1];
            var latestFindings = await ctx.ScanImportFindings
                .Where(f => f.ScanImportRecordId == latestImport.Id && f.PrismaAlertId != null)
                .ToListAsync(ct);

            if (groupBy.Equals("resource_type", StringComparison.OrdinalIgnoreCase))
            {
                resourceTypeBreakdown = latestFindings
                    .Where(f => !string.IsNullOrEmpty(f.CloudResourceType))
                    .GroupBy(f => f.CloudResourceType!)
                    .ToDictionary(g => g.Key, g => g.Count());
            }
            else if (groupBy.Equals("nist_control", StringComparison.OrdinalIgnoreCase))
            {
                nistControlBreakdown = new Dictionary<string, int>();
                foreach (var f in latestFindings)
                {
                    foreach (var ctrl in f.ResolvedNistControlIds)
                    {
                        if (!nistControlBreakdown.ContainsKey(ctrl))
                            nistControlBreakdown[ctrl] = 0;
                        nistControlBreakdown[ctrl]++;
                    }
                }
            }
        }

        return new PrismaTrendResult(
            SystemId: systemId,
            Imports: trendImports,
            NewFindings: newFindings,
            ResolvedFindings: resolvedFindings,
            PersistentFindings: persistentFindings,
            RemediationRate: remediationRate,
            ResourceTypeBreakdown: resourceTypeBreakdown,
            NistControlBreakdown: nistControlBreakdown);
    }

    // ─── Feature 026: ImportNessusAsync (T011) ─────────────────────────

    /// <inheritdoc />
    public async Task<NessusImportResult> ImportNessusAsync(
        string systemId,
        string? assessmentId,
        byte[] fileContent,
        string fileName,
        ImportConflictResolution resolution,
        bool dryRun,
        string importedBy,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var warnings = new List<string>();
        var fileHash = ComputeSha256(fileContent);

        _logger.LogInformation(
            "Nessus import started: file={FileName}, hash={FileHash}, system={SystemId}, resolution={Resolution}, dryRun={DryRun}",
            fileName, fileHash, systemId, resolution, dryRun);

        // ── Step 1: Parse .nessus file ───────────────────────────────────
        ParsedNessusFile parsed;
        try
        {
            parsed = _nessusParser.Parse(fileContent);
        }
        catch (NessusParseException ex)
        {
            _logger.LogWarning(ex, "Nessus parse failed for file {FileName}", fileName);
            return CreateFailedNessusResult($"Nessus parse error: {ex.Message}", dryRun);
        }

        // ── Step 2: Validate system ──────────────────────────────────────
        var system = await _rmfService.GetSystemAsync(systemId, ct);
        if (system is null)
            return CreateFailedNessusResult($"System '{systemId}' not found.", dryRun);

        // ── Step 3: Check RMF step ───────────────────────────────────────
        if (system.CurrentRmfStep < RmfPhase.Assess)
        {
            warnings.Add(
                $"System is in RMF step '{system.CurrentRmfStep}' (expected Assess or later). " +
                "Import will proceed, but findings may not be visible in assessment workflows.");
        }

        // ── Step 4: Get control baseline ─────────────────────────────────
        var baseline = await _baselineService.GetBaselineAsync(systemId, cancellationToken: ct);
        var baselineControlIds = baseline?.ControlIds ?? new List<string>();
        var baselineSet = new HashSet<string>(baselineControlIds, StringComparer.OrdinalIgnoreCase);

        if (baseline is null)
        {
            warnings.Add("No control baseline found for system. " +
                          "All NIST controls will be treated as out-of-baseline (no ControlEffectiveness records).");
        }

        // ── Step 5: Resolve/create assessment context ────────────────────
        using var scope = _scopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        string resolvedAssessmentId;
        if (!string.IsNullOrEmpty(assessmentId))
        {
            var existing = await ctx.Assessments.FindAsync(new object[] { assessmentId }, ct);
            if (existing is null)
                return CreateFailedNessusResult($"Assessment '{assessmentId}' not found.", dryRun);
            resolvedAssessmentId = assessmentId;
        }
        else if (dryRun)
        {
            // Dry-run: resolve existing assessment without creating one (T023)
            var existingAssessment = await ctx.Assessments
                .Where(a => a.RegisteredSystemId == systemId &&
                            a.Status != AssessmentStatus.Completed &&
                            a.Status != AssessmentStatus.Cancelled &&
                            a.Status != AssessmentStatus.Failed)
                .OrderByDescending(a => a.AssessedAt)
                .FirstOrDefaultAsync(ct);
            resolvedAssessmentId = existingAssessment?.Id ?? $"dry-run-{Guid.NewGuid():N}";
        }
        else
        {
            resolvedAssessmentId = await GetOrCreateAssessmentAsync(ctx, systemId, importedBy, ct);
        }

        _logger.LogDebug(
            "Nessus step 5 complete: assessment={AssessmentId}, dryRun={DryRun}",
            resolvedAssessmentId, dryRun);

        // ── Step 6: Duplicate detection ──────────────────────────────────
        var duplicateImport = await ctx.ScanImportRecords
            .Where(r => r.FileHash == fileHash && r.RegisteredSystemId == systemId && !r.IsDryRun)
            .OrderByDescending(r => r.ImportedAt)
            .FirstOrDefaultAsync(ct);

        if (duplicateImport is not null)
        {
            warnings.Add(
                $"File previously imported on {duplicateImport.ImportedAt:yyyy-MM-dd HH:mm} UTC " +
                $"(import ID: {duplicateImport.Id}).");
        }

        // ── Step 7: Create ScanImportRecord ──────────────────────────────
        var credentialedScan = parsed.Hosts.Any(h => h.CredentialedScan);
        var earliestScan = parsed.Hosts
            .Where(h => h.ScanStart.HasValue)
            .Select(h => h.ScanStart!.Value)
            .DefaultIfEmpty()
            .Min();

        var importRecord = new ScanImportRecord
        {
            RegisteredSystemId = systemId,
            AssessmentId = resolvedAssessmentId,
            ImportType = ScanImportType.NessusXml,
            FileName = fileName,
            FileHash = fileHash,
            FileSizeBytes = fileContent.Length,
            BenchmarkId = "ACAS",
            BenchmarkTitle = parsed.ReportName,
            TargetHostName = parsed.Hosts.FirstOrDefault()?.Hostname,
            TargetIpAddress = parsed.Hosts.FirstOrDefault()?.HostIp,
            ScanTimestamp = earliestScan == default ? null : earliestScan,
            ConflictResolution = resolution,
            IsDryRun = dryRun,
            ImportedBy = importedBy,
            NessusCredentialedScan = credentialedScan,
            NessusHostCount = parsed.Hosts.Count
        };

        // ── Step 8: Process findings per host × plugin ───────────────────
        int criticalCount = 0, highCount = 0, mediumCount = 0, lowCount = 0;
        int findingsCreated = 0, findingsUpdated = 0, skippedCount = 0;
        var importFindings = new List<ScanImportFinding>();
        var newFindings = new List<ComplianceFinding>();
        var updatedFindings = new List<ComplianceFinding>();
        var affectedNistControls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var heuristicFamilies = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); // T024

        // Preload existing findings for conflict detection
        var existingFindings = await ctx.Findings
            .Where(f => f.AssessmentId == resolvedAssessmentId)
            .ToListAsync(ct);

        // Build dedup key → existing finding lookup
        // Dedup key: NessusPluginId + NessusHostname + NessusPort
        var existingByDedup = existingFindings
            .Where(f => f.Source == "Nessus Import" && f.StigId is not null)
            .GroupBy(f => f.StigId!)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var host in parsed.Hosts)
        {
            foreach (var plugin in host.PluginResults)
            {
                // Count by severity
                switch (plugin.Severity)
                {
                    case 4: criticalCount++; break;
                    case 3: highCount++; break;
                    case 2: mediumCount++; break;
                    case 1: lowCount++; break;
                }

                // Map Nessus severity → FindingSeverity + CatSeverity
                var (findingSeverity, catSeverity) = MapNessusSeverity(plugin.Severity);

                // Build dedup key
                var dedupKey = $"nessus:{plugin.PluginId}:{host.Hostname ?? host.Name}:{plugin.Port}";

                // ── Control mapping (T020) ───────────────────────────────
                var mapping = await _nessusControlMapper.MapAsync(plugin, ct);
                var primaryControl = mapping.NistControlIds.FirstOrDefault() ?? "RA-5";
                var controlFamily = ExtractControlFamily(primaryControl);

                // Track heuristic fallbacks for summary warning (T024)
                if (mapping.MappingSource == NessusControlMappingSource.PluginFamilyHeuristic)
                {
                    heuristicFamilies.TryGetValue(plugin.PluginFamily, out var count);
                    heuristicFamilies[plugin.PluginFamily] = count + 1;
                }

                var importFinding = new ScanImportFinding
                {
                    ScanImportRecordId = importRecord.Id,
                    VulnId = $"NESSUS-{plugin.PluginId}",
                    RuleId = plugin.PluginId.ToString(),
                    RawStatus = "Open",
                    RawSeverity = plugin.RiskFactor,
                    MappedSeverity = catSeverity,
                    FindingDetails = plugin.Synopsis,
                    Comments = plugin.PluginOutput,
                    NessusPluginId = plugin.PluginId.ToString(),
                    NessusPluginName = plugin.PluginName,
                    NessusPluginFamily = plugin.PluginFamily,
                    NessusHostname = host.Hostname ?? host.Name,
                    NessusHostIp = host.HostIp,
                    NessusPort = plugin.Port,
                    NessusProtocol = plugin.Protocol,
                    NessusServiceName = plugin.ServiceName,
                    NessusCvssV3BaseScore = plugin.CvssV3BaseScore,
                    NessusCvssV3Vector = plugin.CvssV3Vector,
                    NessusCvssV2BaseScore = plugin.CvssV2BaseScore,
                    NessusVprScore = plugin.VprScore,
                    NessusCves = plugin.Cves,
                    NessusExploitAvailable = plugin.ExploitAvailable,
                    NessusControlMappingSource = mapping.MappingSource.ToString(),
                    ResolvedNistControlIds = mapping.NistControlIds,
                    ResolvedCciRefs = mapping.CciRefs
                };

                // ── Conflict resolution ──────────────────────────────────
                existingByDedup.TryGetValue(dedupKey, out var existingFinding);

                if (existingFinding is not null)
                {
                    switch (resolution)
                    {
                        case ImportConflictResolution.Skip:
                            importFinding.ImportAction = ImportFindingAction.Skipped;
                            importFinding.ComplianceFindingId = existingFinding.Id;
                            skippedCount++;
                            importFindings.Add(importFinding);
                            continue;

                        case ImportConflictResolution.Overwrite:
                            existingFinding.Description = plugin.Description ?? plugin.Synopsis ?? plugin.PluginName;
                            existingFinding.Severity = findingSeverity;
                            existingFinding.CatSeverity = catSeverity;
                            existingFinding.RemediationGuidance = plugin.Solution ?? string.Empty;
                            importFinding.ImportAction = ImportFindingAction.Updated;
                            importFinding.ComplianceFindingId = existingFinding.Id;
                            findingsUpdated++;
                            if (!updatedFindings.Contains(existingFinding))
                                updatedFindings.Add(existingFinding);
                            importFindings.Add(importFinding);
                            continue;

                        case ImportConflictResolution.Merge:
                            // Keep existing status, append new details
                            if (!string.IsNullOrEmpty(plugin.PluginOutput))
                                existingFinding.Description += $"\n[Re-scan] {plugin.PluginOutput}";
                            importFinding.ImportAction = ImportFindingAction.Updated;
                            importFinding.ComplianceFindingId = existingFinding.Id;
                            findingsUpdated++;
                            if (!updatedFindings.Contains(existingFinding))
                                updatedFindings.Add(existingFinding);
                            importFindings.Add(importFinding);
                            continue;
                    }
                }

                // ── Create new ComplianceFinding ─────────────────────────
                var newFinding = new ComplianceFinding
                {
                    ControlId = primaryControl,
                    ControlFamily = controlFamily,
                    Title = plugin.PluginName,
                    Description = plugin.Description ?? plugin.Synopsis ?? plugin.PluginName,
                    Severity = findingSeverity,
                    Status = FindingStatus.Open,
                    ResourceId = host.Hostname ?? host.Name,
                    ResourceType = "Host",
                    RemediationGuidance = plugin.Solution ?? string.Empty,
                    Source = "Nessus Import",
                    ScanSource = ScanSourceType.Combined,
                    StigFinding = false,
                    StigId = dedupKey,
                    CatSeverity = catSeverity,
                    AssessmentId = resolvedAssessmentId,
                    ImportRecordId = importRecord.Id
                };

                importFinding.ImportAction = ImportFindingAction.Created;
                importFinding.ComplianceFindingId = newFinding.Id;
                newFindings.Add(newFinding);
                findingsCreated++;

                // Track affected NIST controls (in-baseline only) for effectiveness
                foreach (var nist in mapping.NistControlIds.Where(c => baselineSet.Contains(c)))
                    affectedNistControls.Add(nist);

                importFindings.Add(importFinding);
            }
        }

        // ── Step 8b: Warn about heuristic-mapped plugin families (T024) ──
        if (heuristicFamilies.Count > 0)
        {
            var familySummary = string.Join(", ",
                heuristicFamilies.OrderByDescending(kv => kv.Value)
                    .Select(kv => $"{kv.Key} ({kv.Value})"));
            warnings.Add(
                $"{heuristicFamilies.Values.Sum()} plugins mapped via heuristic (no STIG-ID xref): {familySummary}");
        }

        _logger.LogDebug(
            "Nessus step 8 complete: created={Created}, updated={Updated}, skipped={Skipped}, " +
            "critical={Critical}, high={High}, medium={Medium}, low={Low}, heuristicFamilies={HeuristicCount}",
            findingsCreated, findingsUpdated, skippedCount,
            criticalCount, highCount, mediumCount, lowCount, heuristicFamilies.Count);

        // ── Step 9: Create evidence ──────────────────────────────────────
        var evidenceContent = JsonSerializer.Serialize(new
        {
            ImportType = "Nessus",
            FileName = fileName,
            ReportName = parsed.ReportName,
            HostCount = parsed.Hosts.Count,
            TotalPlugins = parsed.TotalPluginResults,
            CriticalCount = criticalCount,
            HighCount = highCount,
            MediumCount = mediumCount,
            LowCount = lowCount,
            InformationalCount = parsed.InformationalCount
        });

        var evidence = new ComplianceEvidence
        {
            ControlId = "RA-5",
            SubscriptionId = string.Empty,
            EvidenceType = "VulnerabilityScan",
            Description = $"Nessus Import: {fileName} ({parsed.ReportName})",
            Content = evidenceContent,
            CollectedAt = DateTime.UtcNow,
            CollectedBy = importedBy,
            AssessmentId = resolvedAssessmentId,
            EvidenceCategory = EvidenceCategory.Configuration,
            ContentHash = fileHash,
            CollectionMethod = "Automated"
        };

        // ── Step 9b: Upsert ControlEffectiveness (T020) ──────────────────
        int effectivenessCreated = 0, effectivenessUpdated = 0;
        if (!dryRun && affectedNistControls.Count > 0)
        {
            // Build control → findings lookup for aggregate effectiveness
            var allNessusFindings = await ctx.Findings
                .Where(f => f.AssessmentId == resolvedAssessmentId && f.Source == "Nessus Import")
                .ToListAsync(ct);

            var allFindings = allNessusFindings.Concat(newFindings).ToList();

            var controlFindingMap = allFindings
                .Where(f => !string.IsNullOrEmpty(f.ControlId))
                .GroupBy(f => f.ControlId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            foreach (var controlId in affectedNistControls)
            {
                controlFindingMap.TryGetValue(controlId, out var controlFindings);
                var findingsForControl = controlFindings ?? new List<ComplianceFinding>();

                var anyOpen = findingsForControl.Any(f =>
                    f.Status == FindingStatus.Open || f.Status == FindingStatus.InProgress);
                var determination = anyOpen
                    ? EffectivenessDetermination.OtherThanSatisfied
                    : EffectivenessDetermination.Satisfied;

                CatSeverity? controlCatSeverity = null;
                if (anyOpen)
                {
                    var openSeverities = findingsForControl
                        .Where(f => f.Status == FindingStatus.Open || f.Status == FindingStatus.InProgress)
                        .Where(f => f.CatSeverity.HasValue)
                        .Select(f => f.CatSeverity!.Value)
                        .ToList();
                    if (openSeverities.Count > 0)
                        controlCatSeverity = openSeverities.Min();
                }

                var existingEffectiveness = await ctx.ControlEffectivenessRecords
                    .Where(e => e.AssessmentId == resolvedAssessmentId &&
                                e.RegisteredSystemId == systemId &&
                                e.ControlId == controlId)
                    .FirstOrDefaultAsync(ct);

                if (existingEffectiveness is not null)
                {
                    existingEffectiveness.Determination = determination;
                    existingEffectiveness.AssessmentMethod = "Test";
                    existingEffectiveness.AssessorId = importedBy;
                    existingEffectiveness.AssessedAt = DateTime.UtcNow;
                    existingEffectiveness.CatSeverity = controlCatSeverity;
                    if (!existingEffectiveness.EvidenceIds.Contains(evidence.Id))
                        existingEffectiveness.EvidenceIds.Add(evidence.Id);
                    existingEffectiveness.Notes = $"Re-evaluated via Nessus import '{fileName}'";
                    effectivenessUpdated++;
                }
                else
                {
                    var effectiveness = new ControlEffectiveness
                    {
                        AssessmentId = resolvedAssessmentId,
                        RegisteredSystemId = systemId,
                        ControlId = controlId,
                        Determination = determination,
                        AssessmentMethod = "Test",
                        EvidenceIds = new List<string> { evidence.Id },
                        AssessorId = importedBy,
                        CatSeverity = controlCatSeverity,
                        Notes = $"Auto-determined via Nessus import '{fileName}'"
                    };
                    ctx.ControlEffectivenessRecords.Add(effectiveness);
                    effectivenessCreated++;
                }
            }
        }

        // ── Step 9c: POA&M weakness generation (T032) ────────────────────
        int poamCreated = 0;
        if (!dryRun)
        {
            // Load existing ACAS POA&M items for dedup
            var existingPoams = await ctx.PoamItems
                .Where(p => p.RegisteredSystemId == systemId && p.WeaknessSource == "ACAS")
                .ToListAsync(ct);
            var existingPoamByFindingId = existingPoams
                .Where(p => p.FindingId is not null)
                .ToDictionary(p => p.FindingId!, StringComparer.OrdinalIgnoreCase);

            // Scheduled completion: 30 days for CatI, 90 days for CatII, 180 days for CatIII
            static DateTime ComputeScheduledCompletion(CatSeverity? cat) => cat switch
            {
                CatSeverity.CatI => DateTime.UtcNow.AddDays(30),
                CatSeverity.CatII => DateTime.UtcNow.AddDays(90),
                _ => DateTime.UtcNow.AddDays(180)
            };

            foreach (var finding in newFindings.Where(f =>
                f.Severity <= FindingSeverity.Medium && f.CatSeverity.HasValue))
            {
                if (existingPoamByFindingId.ContainsKey(finding.Id))
                    continue;

                var poam = new PoamItem
                {
                    RegisteredSystemId = systemId,
                    FindingId = finding.Id,
                    Weakness = finding.Title ?? finding.Description,
                    WeaknessSource = "ACAS",
                    SecurityControlNumber = finding.ControlId ?? "RA-5",
                    CatSeverity = finding.CatSeverity!.Value,
                    PointOfContact = importedBy,
                    ScheduledCompletionDate = ComputeScheduledCompletion(finding.CatSeverity),
                    Status = PoamStatus.Ongoing,
                    Comments = $"Auto-created via Nessus import '{fileName}'"
                };

                ctx.PoamItems.Add(poam);
                poamCreated++;
            }

            // T033: Flag existing POA&M entries for closure when finding resolves
            var openPoams = existingPoams
                .Where(p => p.Status == PoamStatus.Ongoing && p.FindingId is not null)
                .ToList();

            // All current open Nessus finding dedup keys
            var currentDedupKeys = newFindings
                .Concat(updatedFindings)
                .Where(f => f.Source == "Nessus Import" && f.StigId is not null)
                .Select(f => f.StigId!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var openPoam in openPoams)
            {
                // Find the linked finding to check if it's still present
                var linkedFinding = existingFindings.FirstOrDefault(f => f.Id == openPoam.FindingId);
                if (linkedFinding is not null &&
                    linkedFinding.StigId is not null &&
                    !currentDedupKeys.Contains(linkedFinding.StigId))
                {
                    openPoam.Comments = (openPoam.Comments ?? string.Empty) +
                        $"\n[{DateTime.UtcNow:yyyy-MM-dd}] Finding no longer present in scan '{fileName}' — review for closure.";
                    openPoam.ModifiedAt = DateTime.UtcNow;
                }
            }
        }
        else
        {
            // Dry-run: count how many POA&M entries would be created
            poamCreated = newFindings.Count(f =>
                f.Severity <= FindingSeverity.Medium && f.CatSeverity.HasValue);
        }

        // ── Step 10: Update import record counts ─────────────────────────
        importRecord.TotalEntries = parsed.TotalPluginResults;
        importRecord.NessusCriticalCount = criticalCount;
        importRecord.NessusHighCount = highCount;
        importRecord.NessusMediumCount = mediumCount;
        importRecord.NessusLowCount = lowCount;
        importRecord.NessusInformationalCount = parsed.InformationalCount;
        importRecord.FindingsCreated = findingsCreated;
        importRecord.FindingsUpdated = findingsUpdated;
        importRecord.SkippedCount = skippedCount;
        importRecord.OpenCount = criticalCount + highCount + mediumCount + lowCount;
        importRecord.Warnings = warnings;
        importRecord.ImportStatus = warnings.Count > 0
            ? ScanImportStatus.CompletedWithWarnings
            : ScanImportStatus.Completed;

        // ── Step 11: Persist (unless dry-run) ────────────────────────────
        if (!dryRun)
        {
            ctx.ScanImportRecords.Add(importRecord);
            ctx.ScanImportFindings.AddRange(importFindings);
            ctx.Findings.AddRange(newFindings);
            ctx.Evidence.Add(evidence);
            await ctx.SaveChangesAsync(ct);
        }

        sw.Stop();
        _logger.LogInformation(
            "Nessus import completed: file={FileName}, duration={DurationMs}ms, hosts={HostCount}, " +
            "findings={FindingsCreated}/{FindingsUpdated}, critical={Critical}, high={High}, medium={Medium}, low={Low}",
            fileName, sw.ElapsedMilliseconds, parsed.Hosts.Count,
            findingsCreated, findingsUpdated, criticalCount, highCount, mediumCount, lowCount);

        return new NessusImportResult(
            ImportRecordId: dryRun ? string.Empty : importRecord.Id,
            Status: importRecord.ImportStatus,
            ReportName: parsed.ReportName,
            TotalPluginResults: parsed.TotalPluginResults,
            InformationalCount: parsed.InformationalCount,
            CriticalCount: criticalCount,
            HighCount: highCount,
            MediumCount: mediumCount,
            LowCount: lowCount,
            HostCount: parsed.Hosts.Count,
            FindingsCreated: findingsCreated,
            FindingsUpdated: findingsUpdated,
            SkippedCount: skippedCount,
            PoamWeaknessesCreated: poamCreated,
            EffectivenessRecordsCreated: effectivenessCreated,
            EffectivenessRecordsUpdated: effectivenessUpdated,
            NistControlsAffected: affectedNistControls.Count,
            CredentialedScan: credentialedScan,
            IsDryRun: dryRun,
            Warnings: warnings,
            ErrorMessage: null);
    }

    /// <summary>Map Nessus integer severity → FindingSeverity + CatSeverity.</summary>
    internal static (FindingSeverity Severity, CatSeverity? Cat) MapNessusSeverity(int severity)
    {
        return severity switch
        {
            4 => (FindingSeverity.Critical, CatSeverity.CatI),
            3 => (FindingSeverity.High, CatSeverity.CatI),
            2 => (FindingSeverity.Medium, CatSeverity.CatII),
            1 => (FindingSeverity.Low, CatSeverity.CatIII),
            _ => (FindingSeverity.Informational, null)
        };
    }

    /// <summary>Create a failed NessusImportResult with error message.</summary>
    private static NessusImportResult CreateFailedNessusResult(string error, bool isDryRun)
    {
        return new NessusImportResult(
            ImportRecordId: string.Empty,
            Status: ScanImportStatus.Failed,
            ReportName: string.Empty,
            TotalPluginResults: 0,
            InformationalCount: 0,
            CriticalCount: 0,
            HighCount: 0,
            MediumCount: 0,
            LowCount: 0,
            HostCount: 0,
            FindingsCreated: 0,
            FindingsUpdated: 0,
            SkippedCount: 0,
            PoamWeaknessesCreated: 0,
            EffectivenessRecordsCreated: 0,
            EffectivenessRecordsUpdated: 0,
            NistControlsAffected: 0,
            CredentialedScan: false,
            IsDryRun: isDryRun,
            Warnings: new List<string>(),
            ErrorMessage: error);
    }
}
