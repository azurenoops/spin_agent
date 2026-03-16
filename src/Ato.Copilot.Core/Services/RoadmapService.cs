using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Kanban;
using Ato.Copilot.Core.Interfaces.Roadmap;
using Ato.Copilot.Core.Models.Roadmap;
using Ato.Copilot.Core.Dtos.Dashboard;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TaskStatus = Ato.Copilot.Core.Models.Kanban.TaskStatus;

namespace Ato.Copilot.Core.Services;

/// <summary>
/// Service for implementation roadmap operations — generation, retrieval,
/// progress tracking, Kanban bridge, updates, and PDF export.
/// </summary>
public class RoadmapService : IRoadmapService
{
    private readonly AtoCopilotContext _context;
    private readonly IChatClient? _chatClient;
    private readonly IKanbanService _kanbanService;
    private readonly CapabilityService _capabilityService;
    private readonly ILogger<RoadmapService> _logger;

    /// <summary>
    /// Well-known NIST SP 800-53 control dependency pairs (Research R4).
    /// Key = prerequisite control, Value = dependent controls.
    /// </summary>
    private static readonly Dictionary<string, string[]> ControlDependencies = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IA-2"] = ["AC-2"],
        ["AC-2"] = ["AU-6", "AC-3"],
        ["CM-6"] = ["CM-7"],
        ["RA-3"] = ["CA-5"],
        ["PL-2"] = ["CA-2"]
    };

    public RoadmapService(
        AtoCopilotContext context,
        IKanbanService kanbanService,
        CapabilityService capabilityService,
        ILogger<RoadmapService> logger,
        IChatClient? chatClient = null)
    {
        _context = context;
        _kanbanService = kanbanService;
        _capabilityService = capabilityService;
        _logger = logger;
        _chatClient = chatClient;
    }

    // ─── Risk Calculation (Research R3) ──────────────────────────────────────

    /// <summary>
    /// Calculates risk reduction percentage for a set of items relative to total risk points.
    /// Uses weighted severity: CAT I = 10, CAT II = 5, CAT III = 1.
    /// </summary>
    public static double CalculateRiskReduction(IEnumerable<RoadmapItem> items, double totalRiskPoints)
    {
        if (totalRiskPoints <= 0) return 0;
        var itemPoints = items.Sum(i => i.RiskPoints);
        return itemPoints / totalRiskPoints * 100;
    }

    /// <summary>
    /// Returns the risk point value for a given severity level.
    /// </summary>
    public static double GetRiskPoints(ItemSeverity severity) => severity switch
    {
        ItemSeverity.Critical => 10,
        ItemSeverity.High => 5,
        ItemSeverity.Medium => 1,
        _ => 1
    };

    // ─── GenerateRoadmapAsync (T011) ─────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ImplementationRoadmap> GenerateRoadmapAsync(
        string systemId,
        string createdBy,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating roadmap for system {SystemId}", systemId);

        // Fetch gap analysis
        var gapAnalysis = await _capabilityService.GetGapAnalysisAsync(systemId, null, cancellationToken);
        if (gapAnalysis is null)
            throw new InvalidOperationException($"Cannot generate roadmap: no baseline selected for system {systemId}. Select a baseline first.");

        var allGaps = gapAnalysis.FamilyBreakdown
            .SelectMany(f => f.UnmappedControls.Select(c => new { Control = c, Family = f }))
            .ToList();

        if (allGaps.Count == 0)
        {
            _logger.LogInformation("No gaps found for system {SystemId} — no roadmap needed", systemId);
            throw new InvalidOperationException("NO_GAPS");
        }

        // Build roadmap items from gap data
        var roadmapItems = allGaps.Select(g => new RoadmapItem
        {
            ControlId = g.Control.ControlId,
            ControlTitle = g.Control.ControlTitle,
            ControlFamily = g.Family.FamilyCode,
            GapType = GapType.Unmapped,
            Severity = DetermineSeverity(g.Control.ControlId),
            RiskPoints = GetRiskPoints(DetermineSeverity(g.Control.ControlId)),
            EstimatedEffortDays = 1,
            AssignedRole = "Engineer"
        }).ToList();

        // Query historical Kanban data for effort refinement (FR-004)
        var historicalEffort = await GetHistoricalEffortAsync(
            roadmapItems.Select(i => i.ControlId).Distinct().ToList(),
            cancellationToken);

        // Attempt AI clustering (R1) or fall back to deterministic (T013)
        List<PhaseAssignment> phaseAssignments;
        if (_chatClient is not null)
        {
            try
            {
                phaseAssignments = await ClusterWithAiAsync(roadmapItems, historicalEffort, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI clustering failed — falling back to deterministic grouping");
                phaseAssignments = ClusterDeterministic(roadmapItems);
            }
        }
        else
        {
            _logger.LogInformation("No IChatClient configured — using deterministic clustering");
            phaseAssignments = ClusterDeterministic(roadmapItems);
        }

        // Apply historical effort data to refine estimates
        foreach (var item in roadmapItems)
        {
            if (historicalEffort.TryGetValue(item.ControlId, out var medianDays) && medianDays > 0)
            {
                item.EstimatedEffortDays = medianDays;
                item.EstimationSource = "Historical";
            }
        }

        // Archive any existing Active roadmap
        var existingActive = await _context.ImplementationRoadmaps
            .FirstOrDefaultAsync(r => r.SystemId == systemId && r.Status == RoadmapStatus.Active, cancellationToken);
        if (existingActive is not null)
        {
            existingActive.Status = RoadmapStatus.Archived;
            existingActive.UpdatedAt = DateTime.UtcNow;
        }

        // Build the roadmap entity
        var totalRiskPoints = roadmapItems.Sum(i => i.RiskPoints);
        var roadmap = new ImplementationRoadmap
        {
            SystemId = systemId,
            Name = $"{gapAnalysis.BaselineLevel} Baseline Roadmap",
            Status = RoadmapStatus.Draft,
            BaselineLevel = gapAnalysis.BaselineLevel,
            TotalGaps = allGaps.Count,
            TotalEstimatedEffort = roadmapItems.Sum(i => i.EstimatedEffortDays),
            TotalRiskPoints = totalRiskPoints,
            ProjectedRiskReduction = 100,
            CreatedBy = createdBy
        };

        // Build phases from assignments
        var phases = new List<RoadmapPhase>();
        foreach (var group in phaseAssignments.GroupBy(a => a.PhaseOrder).OrderBy(g => g.Key))
        {
            var phaseItems = group.Select(a =>
            {
                var item = roadmapItems.First(i => i.ControlId == a.ControlId);
                item.DisplayOrder = group.ToList().IndexOf(a) + 1;
                return item;
            }).ToList();

            var phase = new RoadmapPhase
            {
                RoadmapId = roadmap.Id,
                Name = group.First().PhaseName,
                DisplayOrder = group.Key,
                EstimatedEffort = phaseItems.Sum(i => i.EstimatedEffortDays),
                RiskPoints = phaseItems.Sum(i => i.RiskPoints),
                RiskReductionPercent = CalculateRiskReduction(phaseItems, totalRiskPoints),
                TargetStartWeek = CalculateStartWeek(group.Key, phaseAssignments, roadmapItems),
                TargetEndWeek = CalculateEndWeek(group.Key, phaseItems),
                TotalItemCount = phaseItems.Count,
                Items = phaseItems
            };

            foreach (var item in phaseItems)
            {
                item.PhaseId = phase.Id;
                item.RoadmapId = roadmap.Id;

                // Set dependencies
                var deps = GetDependencies(item.ControlId, roadmapItems.Select(i => i.ControlId).ToList());
                if (deps.Count > 0)
                    item.DependsOn = string.Join(",", deps);
            }

            phases.Add(phase);
        }

        roadmap.Phases = phases;
        _context.ImplementationRoadmaps.Add(roadmap);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Generated roadmap {RoadmapId} with {PhaseCount} phases covering {GapCount} gaps",
            roadmap.Id, phases.Count, allGaps.Count);

        return roadmap;
    }

    // ─── GetRoadmapAsync (T012) ──────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ImplementationRoadmap?> GetRoadmapAsync(
        string systemId,
        bool includeItems = true,
        CancellationToken cancellationToken = default)
    {
        var query = _context.ImplementationRoadmaps
            .Where(r => r.SystemId == systemId && r.Status == RoadmapStatus.Active);

        if (includeItems)
        {
            query = query
                .Include(r => r.Phases.OrderBy(p => p.DisplayOrder))
                    .ThenInclude(p => p.Items.OrderBy(i => i.DisplayOrder));
        }
        else
        {
            query = query.Include(r => r.Phases.OrderBy(p => p.DisplayOrder));
        }

        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    // ─── GetRoadmapProgressAsync (T033) ──────────────────────────────────────

    /// <inheritdoc />
    public async Task<RoadmapProgressResult?> GetRoadmapProgressAsync(
        string systemId,
        CancellationToken cancellationToken = default)
    {
        var roadmap = await GetRoadmapAsync(systemId, includeItems: true, cancellationToken);
        if (roadmap is null) return null;

        var system = await _context.RegisteredSystems
            .FirstOrDefaultAsync(s => s.Id == systemId, cancellationToken);

        var allItems = roadmap.Phases.SelectMany(p => p.Items).ToList();
        var completedItems = allItems.Count(i => i.Status == ItemStatus.Complete);
        var overallCompletion = allItems.Count > 0 ? (double)completedItems / allItems.Count * 100 : 0;

        // Compute actual risk reduction via current gap analysis (FR-014)
        double actualRiskReduction = 0;
        var currentGaps = await _capabilityService.GetGapAnalysisAsync(systemId, null, cancellationToken);
        if (currentGaps is not null && roadmap.TotalGaps > 0)
        {
            var currentGapCount = currentGaps.GapCount;
            actualRiskReduction = (double)(roadmap.TotalGaps - currentGapCount) / roadmap.TotalGaps * 100;
        }

        // Build risk curve
        var riskCurve = BuildRiskCurve(roadmap);

        // Build phase progress
        var now = DateTime.UtcNow;
        var phaseProgress = roadmap.Phases.Select(p =>
        {
            var completion = p.TotalItemCount > 0 ? (double)p.CompletedItemCount / p.TotalItemCount * 100 : 0;
            var isOverdue = p.TargetCompletionDate.HasValue && now > p.TargetCompletionDate.Value && p.Status != PhaseStatus.Complete;
            var daysOverdue = isOverdue ? (int)(now - p.TargetCompletionDate!.Value).TotalDays : 0;

            return new PhaseProgress
            {
                Name = p.Name,
                DisplayOrder = p.DisplayOrder,
                CompletionPercent = Math.Round(completion, 1),
                ItemsCompleted = p.CompletedItemCount,
                ItemsTotal = p.TotalItemCount,
                Status = p.Status.ToString(),
                IsOverdue = isOverdue,
                DaysOverdue = daysOverdue,
                ProjectedRiskReductionPercent = p.RiskReductionPercent,
                ActualRiskReductionPercent = p.Status == PhaseStatus.Complete
                    ? p.RiskReductionPercent
                    : Math.Round(completion / 100 * p.RiskReductionPercent, 1)
            };
        }).ToList();

        return new RoadmapProgressResult
        {
            RoadmapId = roadmap.Id,
            SystemName = system?.Name ?? systemId,
            OverallCompletionPercent = Math.Round(overallCompletion, 1),
            ItemsCompleted = completedItems,
            ItemsTotal = allItems.Count,
            ProjectedRiskReduction = roadmap.ProjectedRiskReduction,
            ActualRiskReduction = Math.Round(actualRiskReduction, 1),
            RiskCurve = riskCurve,
            PhaseProgress = phaseProgress
        };
    }

    // ─── UpdateRoadmapAsync (T038-T044) ──────────────────────────────────────

    /// <inheritdoc />
    public async Task<ImplementationRoadmap> UpdateRoadmapAsync(
        string systemId,
        RoadmapUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        var roadmap = await _context.ImplementationRoadmaps
            .Include(r => r.Phases.OrderBy(p => p.DisplayOrder))
                .ThenInclude(p => p.Items)
            .FirstOrDefaultAsync(r => r.SystemId == systemId && r.Status == RoadmapStatus.Active, cancellationToken)
            ?? throw new InvalidOperationException($"No active roadmap found for system {systemId}");

        if (request.MoveItem is not null)
            await MoveItemBetweenPhasesAsync(roadmap, request.MoveItem, cancellationToken);

        if (request.UpdateEffort is not null)
            UpdateItemEffort(roadmap, request.UpdateEffort);

        if (request.UpdateRole is not null)
            await UpdateItemRoleAsync(roadmap, request.UpdateRole, cancellationToken);

        if (request.MergePhases is not null)
            MergePhases(roadmap, request.MergePhases);

        if (request.SplitPhase is not null)
            SplitPhase(roadmap, request.SplitPhase);

        // Remove empty phases after operations (T044)
        var emptyPhases = roadmap.Phases.Where(p => p.Items.Count == 0).ToList();
        foreach (var empty in emptyPhases)
        {
            _context.RoadmapPhases.Remove(empty);
            roadmap.Phases.Remove(empty);
            _logger.LogWarning("Removed empty phase '{PhaseName}' after restructuring", empty.Name);
        }

        // Renumber display orders
        for (var i = 0; i < roadmap.Phases.Count; i++)
            roadmap.Phases[i].DisplayOrder = i + 1;

        RecalculateRoadmapAggregates(roadmap);
        roadmap.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        return roadmap;
    }

    // ─── CreateBoardFromRoadmapAsync (T028) ──────────────────────────────────

    /// <inheritdoc />
    public async Task<BoardFromRoadmapResult> CreateBoardFromRoadmapAsync(
        string systemId,
        string createdBy,
        CancellationToken cancellationToken = default)
    {
        var roadmap = await _context.ImplementationRoadmaps
            .Include(r => r.Phases)
                .ThenInclude(p => p.Items)
            .FirstOrDefaultAsync(r => r.SystemId == systemId && r.Status == RoadmapStatus.Active, cancellationToken)
            ?? throw new InvalidOperationException($"No active roadmap found for system {systemId}");

        if (!string.IsNullOrEmpty(roadmap.LinkedBoardId))
        {
            _logger.LogWarning("Roadmap {RoadmapId} already has a linked board {BoardId}", roadmap.Id, roadmap.LinkedBoardId);
        }

        var board = await _kanbanService.CreateBoardAsync(
            $"{roadmap.Name} Remediation",
            systemId,
            createdBy,
            cancellationToken);

        var allItems = roadmap.Phases.SelectMany(p => p.Items).ToList();
        var tasksCreated = 0;

        foreach (var item in allItems)
        {
            var task = await _kanbanService.CreateTaskAsync(
                board.Id,
                $"{item.ControlId}: {item.ControlTitle}",
                item.ControlId,
                createdBy,
                description: $"Gap: {item.GapType}. Control family: {item.ControlFamily}. Estimated effort: {item.EstimatedEffortDays} days.",
                cancellationToken: cancellationToken);

            item.LinkedTaskId = task.Id;
            task.RoadmapItemId = item.Id;
            tasksCreated++;
        }

        roadmap.LinkedBoardId = board.Id;
        roadmap.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created board {BoardId} with {TaskCount} tasks from roadmap {RoadmapId}",
            board.Id, tasksCreated, roadmap.Id);

        return new BoardFromRoadmapResult
        {
            BoardId = board.Id,
            BoardName = board.Name,
            TasksCreated = tasksCreated,
            RoadmapId = roadmap.Id,
            PhasesMapped = roadmap.Phases.Count
        };
    }

    // ─── SyncRoadmapItemStatusAsync (T030) ───────────────────────────────────

    /// <inheritdoc />
    public async Task SyncRoadmapItemStatusAsync(
        string roadmapItemId,
        TaskStatus newTaskStatus,
        CancellationToken cancellationToken = default)
    {
        var item = await _context.RoadmapItems
            .Include(i => i.Phase)
            .FirstOrDefaultAsync(i => i.Id == roadmapItemId, cancellationToken);

        if (item is null)
        {
            _logger.LogWarning("RoadmapItem {ItemId} not found for sync", roadmapItemId);
            return;
        }

        var newStatus = MapTaskStatusToItemStatus(newTaskStatus);
        if (item.Status == newStatus) return;

        var oldStatus = item.Status;
        item.Status = newStatus;
        item.UpdatedAt = DateTime.UtcNow;

        // Update phase cached counts
        var phase = item.Phase!;
        if (newStatus == ItemStatus.Complete && oldStatus != ItemStatus.Complete)
            phase.CompletedItemCount++;
        else if (newStatus != ItemStatus.Complete && oldStatus == ItemStatus.Complete)
            phase.CompletedItemCount = Math.Max(0, phase.CompletedItemCount - 1);

        // Update phase status
        if (phase.CompletedItemCount >= phase.TotalItemCount && phase.TotalItemCount > 0)
            phase.Status = PhaseStatus.Complete;
        else if (phase.CompletedItemCount > 0 || newStatus == ItemStatus.InProgress)
            phase.Status = PhaseStatus.InProgress;

        phase.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Synced RoadmapItem {ItemId} status from {OldStatus} to {NewStatus}",
            roadmapItemId, oldStatus, newStatus);
    }

    // ─── ExportRoadmapPdfAsync (T045) ────────────────────────────────────────

    /// <inheritdoc />
    public async Task<byte[]> ExportRoadmapPdfAsync(
        string systemId,
        CancellationToken cancellationToken = default)
    {
        var roadmap = await GetRoadmapAsync(systemId, includeItems: true, cancellationToken)
            ?? throw new InvalidOperationException($"No active roadmap found for system {systemId}");

        QuestPDF.Settings.License = LicenseType.Community;

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(10));

                // Header
                page.Header().Column(col =>
                {
                    col.Item().Text("Implementation Roadmap").Bold().FontSize(18);
                    col.Item().Text(roadmap.Name).FontSize(14).FontColor(Colors.Blue.Medium);
                    col.Item().Text($"Status: {roadmap.Status} · Generated: {roadmap.CreatedAt:yyyy-MM-dd}").FontSize(9).FontColor(Colors.Grey.Medium);
                    col.Item().PaddingBottom(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                });

                // Content
                page.Content().Column(col =>
                {
                    // Summary metrics
                    col.Item().PaddingBottom(8).Row(row =>
                    {
                        row.RelativeItem().Text($"Total Gaps: {roadmap.TotalGaps}").Bold();
                        row.RelativeItem().Text($"Total Effort: {roadmap.TotalEstimatedEffort:F0} days").Bold();
                        row.RelativeItem().Text($"Phases: {roadmap.Phases.Count}").Bold();
                        row.RelativeItem().Text($"Risk Reduction: {roadmap.ProjectedRiskReduction:F1}%").Bold();
                    });

                    col.Item().PaddingBottom(12).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten3);

                    // Phase details
                    foreach (var phase in roadmap.Phases.OrderBy(p => p.DisplayOrder))
                    {
                        col.Item().PaddingBottom(4).Text(
                            $"Phase {phase.DisplayOrder}: {phase.Name}")
                            .Bold().FontSize(12);

                        col.Item().PaddingBottom(2).Text(
                            $"Weeks {phase.TargetStartWeek}–{phase.TargetEndWeek} · " +
                            $"{phase.TotalItemCount} items · {phase.EstimatedEffort:F0}d · " +
                            $"{phase.RiskReductionPercent:F1}% risk reduction · Status: {phase.Status}")
                            .FontSize(9).FontColor(Colors.Grey.Medium);

                        // Items table
                        if (phase.Items is { Count: > 0 })
                        {
                            col.Item().PaddingBottom(8).Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(2);  // Control ID
                                    columns.RelativeColumn(2);  // Gap Type
                                    columns.RelativeColumn(1);  // Severity
                                    columns.RelativeColumn(1);  // Effort
                                    columns.RelativeColumn(2);  // Role
                                    columns.RelativeColumn(1);  // Status
                                });

                                // Header row
                                table.Header(header =>
                                {
                                    header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Control ID").Bold().FontSize(8);
                                    header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Gap Type").Bold().FontSize(8);
                                    header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Severity").Bold().FontSize(8);
                                    header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Effort").Bold().FontSize(8);
                                    header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Role").Bold().FontSize(8);
                                    header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Status").Bold().FontSize(8);
                                });

                                foreach (var item in phase.Items)
                                {
                                    table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).Padding(3).Text(item.ControlId).FontSize(8);
                                    table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).Padding(3).Text(item.GapType.ToString()).FontSize(8);
                                    table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).Padding(3).Text(item.Severity.ToString()).FontSize(8);
                                    table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).Padding(3).Text($"{item.EstimatedEffortDays:F0}d").FontSize(8);
                                    table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).Padding(3).Text(item.AssignedRole ?? "—").FontSize(8);
                                    table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).Padding(3).Text(item.Status.ToString()).FontSize(8);
                                }
                            });
                        }
                    }
                });

                // Footer
                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("ATO Copilot · Implementation Roadmap · Page ").FontSize(8).FontColor(Colors.Grey.Medium);
                    text.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Medium);
                    text.Span(" of ").FontSize(8).FontColor(Colors.Grey.Medium);
                    text.TotalPages().FontSize(8).FontColor(Colors.Grey.Medium);
                });
            });
        });

        return document.GeneratePdf();
    }

    // ─── Private Helpers ─────────────────────────────────────────────────────

    private static ItemStatus MapTaskStatusToItemStatus(TaskStatus taskStatus) => taskStatus switch
    {
        TaskStatus.Backlog or TaskStatus.ToDo => ItemStatus.NotStarted,
        TaskStatus.InProgress or TaskStatus.InReview or TaskStatus.Blocked => ItemStatus.InProgress,
        TaskStatus.Done => ItemStatus.Complete,
        _ => ItemStatus.NotStarted
    };

    private static ItemSeverity DetermineSeverity(string controlId)
    {
        // Critical controls (CAT I) — core identity and access
        var criticalPrefixes = new[] { "IA-", "AC-2", "AC-3", "AC-6", "AU-2", "SI-2", "SI-3" };
        if (criticalPrefixes.Any(p => controlId.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            return ItemSeverity.Critical;

        // High controls (CAT II) — infrastructure and config
        var highPrefixes = new[] { "CM-", "SC-", "AU-", "CA-", "RA-" };
        if (highPrefixes.Any(p => controlId.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            return ItemSeverity.High;

        return ItemSeverity.Medium;
    }

    private async Task<Dictionary<string, double>> GetHistoricalEffortAsync(
        List<string> controlIds,
        CancellationToken cancellationToken)
    {
        if (controlIds.Count == 0) return new();

        var completedTasks = await _context.RemediationTasks
            .Where(t => t.Status == TaskStatus.Done && controlIds.Contains(t.ControlId))
            .GroupBy(t => t.ControlId)
            .Select(g => new
            {
                ControlId = g.Key,
                AvgDays = g.Average(t => (double)EF.Functions.DateDiffDay(t.CreatedAt, t.UpdatedAt))
            })
            .ToListAsync(cancellationToken);

        return completedTasks.ToDictionary(t => t.ControlId, t => Math.Max(t.AvgDays, 1.0));
    }

    private async Task<List<PhaseAssignment>> ClusterWithAiAsync(
        List<RoadmapItem> items,
        Dictionary<string, double> historicalEffort,
        CancellationToken cancellationToken)
    {
        var controlsJson = JsonSerializer.Serialize(items.Select(i => new
        {
            i.ControlId,
            i.ControlTitle,
            i.ControlFamily,
            Severity = i.Severity.ToString(),
            i.RiskPoints,
            HistoricalEffortDays = historicalEffort.GetValueOrDefault(i.ControlId, 0)
        }));

        var dependenciesJson = JsonSerializer.Serialize(ControlDependencies);

        var prompt = $$"""
            You are a compliance expert. Cluster these NIST 800-53 controls into 3-5 implementation phases.

            Rules:
            1. Prioritize critical (CAT I) controls in earlier phases
            2. Respect these dependency constraints — prerequisites must be in an equal or earlier phase:
            {{dependenciesJson}}
            3. Group related control families together when possible
            4. Balance effort across phases
            5. Each phase needs a descriptive name

            Controls to cluster:
            {{controlsJson}}

            For each control, also estimate the implementation effort in person-days based on:
            - Control complexity and enhancement count
            - If historical effort data is provided (> 0), weight it at 60%
            - Typical industry implementation timelines

            Respond with ONLY valid JSON in this exact format:
            [
              {
                "controlId": "AC-2",
                "phaseOrder": 1,
                "phaseName": "Critical Controls",
                "estimatedEffortDays": 4.0
              }
            ]
            """;

        var response = await _chatClient!.GetResponseAsync(prompt, cancellationToken: cancellationToken);

        var responseText = response.Text?.Trim() ?? "[]";
        // Strip markdown code fences if present
        if (responseText.StartsWith("```"))
        {
            var lines = responseText.Split('\n');
            responseText = string.Join('\n', lines.Skip(1).TakeWhile(l => !l.StartsWith("```")));
        }

        var assignments = JsonSerializer.Deserialize<List<PhaseAssignment>>(responseText, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];

        // Apply AI effort estimates to items
        foreach (var assignment in assignments)
        {
            var item = items.FirstOrDefault(i => i.ControlId.Equals(assignment.ControlId, StringComparison.OrdinalIgnoreCase));
            if (item is not null && assignment.EstimatedEffortDays > 0 && item.EstimationSource != "Historical")
            {
                item.EstimatedEffortDays = assignment.EstimatedEffortDays;
            }
        }

        // Validate dependencies
        ValidateDependencyOrder(assignments);

        return assignments;
    }

    private static List<PhaseAssignment> ClusterDeterministic(List<RoadmapItem> items)
    {
        var assignments = new List<PhaseAssignment>();
        var criticalItems = items.Where(i => i.Severity == ItemSeverity.Critical).ToList();
        var highItems = items.Where(i => i.Severity == ItemSeverity.High).ToList();
        var mediumItems = items.Where(i => i.Severity == ItemSeverity.Medium).ToList();

        var phaseOrder = 1;
        if (criticalItems.Count > 0)
        {
            assignments.AddRange(criticalItems.Select(i => new PhaseAssignment
            {
                ControlId = i.ControlId,
                PhaseOrder = phaseOrder,
                PhaseName = "Critical Controls",
                EstimatedEffortDays = i.EstimatedEffortDays
            }));
            phaseOrder++;
        }

        if (highItems.Count > 0)
        {
            assignments.AddRange(highItems.Select(i => new PhaseAssignment
            {
                ControlId = i.ControlId,
                PhaseOrder = phaseOrder,
                PhaseName = "High Priority Controls",
                EstimatedEffortDays = i.EstimatedEffortDays
            }));
            phaseOrder++;
        }

        if (mediumItems.Count > 0)
        {
            assignments.AddRange(mediumItems.Select(i => new PhaseAssignment
            {
                ControlId = i.ControlId,
                PhaseOrder = phaseOrder,
                PhaseName = "Remaining Controls",
                EstimatedEffortDays = i.EstimatedEffortDays
            }));
        }

        // Post-assignment dependency validation
        ValidateDependencyOrder(assignments);

        return assignments;
    }

    private static void ValidateDependencyOrder(List<PhaseAssignment> assignments)
    {
        foreach (var (prereq, dependents) in ControlDependencies)
        {
            var prereqAssignment = assignments.FirstOrDefault(a =>
                a.ControlId.Equals(prereq, StringComparison.OrdinalIgnoreCase));
            if (prereqAssignment is null) continue;

            foreach (var dep in dependents)
            {
                var depAssignment = assignments.FirstOrDefault(a =>
                    a.ControlId.Equals(dep, StringComparison.OrdinalIgnoreCase));
                if (depAssignment is null) continue;

                // If dependent is in an earlier phase than prerequisite, move it
                if (depAssignment.PhaseOrder < prereqAssignment.PhaseOrder)
                {
                    depAssignment.PhaseOrder = prereqAssignment.PhaseOrder;
                }
            }
        }
    }

    private static List<string> GetDependencies(string controlId, List<string> allControlIds)
    {
        var deps = new List<string>();
        foreach (var (prereq, _) in ControlDependencies)
        {
            if (ControlDependencies.TryGetValue(prereq, out var dependents) &&
                dependents.Any(d => d.Equals(controlId, StringComparison.OrdinalIgnoreCase)) &&
                allControlIds.Contains(prereq, StringComparer.OrdinalIgnoreCase))
            {
                deps.Add(prereq);
            }
        }
        return deps;
    }

    private static int? CalculateStartWeek(int phaseOrder, List<PhaseAssignment> allAssignments, List<RoadmapItem> items)
    {
        if (phaseOrder == 1) return 1;
        // Each prior phase gets ~2 weeks per 20 effort-days
        var priorPhases = allAssignments
            .Where(a => a.PhaseOrder < phaseOrder)
            .GroupBy(a => a.PhaseOrder)
            .Select(g => Math.Max(1, (int)Math.Ceiling(g.Sum(a => a.EstimatedEffortDays) / 10)))
            .Sum();
        return priorPhases + 1;
    }

    private static int? CalculateEndWeek(int phaseOrder, List<RoadmapItem> phaseItems)
    {
        var startWeek = phaseOrder; // simplified
        var weeks = Math.Max(1, (int)Math.Ceiling(phaseItems.Sum(i => i.EstimatedEffortDays) / 10));
        return startWeek + weeks - 1;
    }

    private List<RiskCurvePoint> BuildRiskCurve(ImplementationRoadmap roadmap)
    {
        var points = new List<RiskCurvePoint>
        {
            new() { Week = 0, RiskPoints = roadmap.TotalRiskPoints, RiskReductionPercent = 0 }
        };

        var cumulativeReduction = 0.0;
        foreach (var phase in roadmap.Phases.OrderBy(p => p.DisplayOrder))
        {
            cumulativeReduction += phase.RiskReductionPercent;
            var remainingPoints = roadmap.TotalRiskPoints * (1 - cumulativeReduction / 100);
            points.Add(new RiskCurvePoint
            {
                Week = phase.TargetEndWeek ?? phase.DisplayOrder * 2,
                RiskPoints = Math.Round(remainingPoints, 1),
                RiskReductionPercent = Math.Round(cumulativeReduction, 1)
            });
        }

        return points;
    }

    private async Task MoveItemBetweenPhasesAsync(
        ImplementationRoadmap roadmap,
        MoveItemRequest request,
        CancellationToken cancellationToken)
    {
        var item = roadmap.Phases.SelectMany(p => p.Items)
            .FirstOrDefault(i => i.ControlId.Equals(request.ControlId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Item with ControlId '{request.ControlId}' not found");

        var targetPhase = roadmap.Phases.FirstOrDefault(p => p.DisplayOrder == request.TargetPhaseOrder)
            ?? throw new InvalidOperationException($"Phase with order {request.TargetPhaseOrder} not found");

        var sourcePhase = roadmap.Phases.First(p => p.Id == item.PhaseId);
        if (sourcePhase.Id == targetPhase.Id) return;

        sourcePhase.Items.Remove(item);
        item.PhaseId = targetPhase.Id;
        targetPhase.Items.Add(item);

        RecalculatePhaseAggregates(sourcePhase, roadmap.TotalRiskPoints);
        RecalculatePhaseAggregates(targetPhase, roadmap.TotalRiskPoints);
    }

    private void UpdateItemEffort(ImplementationRoadmap roadmap, UpdateEffortRequest request)
    {
        var item = roadmap.Phases.SelectMany(p => p.Items)
            .FirstOrDefault(i => i.ControlId.Equals(request.ControlId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Item with ControlId '{request.ControlId}' not found");

        item.EstimatedEffortDays = request.EffortDays;
        item.EstimationSource = "Manual";
        item.UpdatedAt = DateTime.UtcNow;

        var phase = roadmap.Phases.First(p => p.Id == item.PhaseId);
        phase.EstimatedEffort = phase.Items.Sum(i => i.EstimatedEffortDays);
    }

    private async Task UpdateItemRoleAsync(
        ImplementationRoadmap roadmap,
        UpdateRoleRequest request,
        CancellationToken cancellationToken)
    {
        var item = roadmap.Phases.SelectMany(p => p.Items)
            .FirstOrDefault(i => i.ControlId.Equals(request.ControlId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Item with ControlId '{request.ControlId}' not found");

        item.AssignedRole = request.AssignedRole;
        item.UpdatedAt = DateTime.UtcNow;

        // Propagate to linked Kanban task (T041)
        if (!string.IsNullOrEmpty(item.LinkedTaskId))
        {
            var task = await _context.RemediationTasks
                .FirstOrDefaultAsync(t => t.Id == item.LinkedTaskId, cancellationToken);
            if (task is not null)
            {
                task.AssigneeName = request.AssignedRole;
                task.UpdatedAt = DateTime.UtcNow;
            }
        }
    }

    private static void MergePhases(ImplementationRoadmap roadmap, MergePhasesRequest request)
    {
        var source = roadmap.Phases.FirstOrDefault(p => p.DisplayOrder == request.SourcePhaseOrder)
            ?? throw new InvalidOperationException($"Source phase {request.SourcePhaseOrder} not found");
        var target = roadmap.Phases.FirstOrDefault(p => p.DisplayOrder == request.TargetPhaseOrder)
            ?? throw new InvalidOperationException($"Target phase {request.TargetPhaseOrder} not found");

        foreach (var item in source.Items.ToList())
        {
            item.PhaseId = target.Id;
            target.Items.Add(item);
        }
        source.Items.Clear();
    }

    private static void SplitPhase(ImplementationRoadmap roadmap, SplitPhaseRequest request)
    {
        var phase = roadmap.Phases.FirstOrDefault(p => p.DisplayOrder == request.PhaseOrder)
            ?? throw new InvalidOperationException($"Phase {request.PhaseOrder} not found");

        var orderedItems = phase.Items.OrderBy(i => i.DisplayOrder).ToList();
        if (request.SplitAfterItemIndex < 0 || request.SplitAfterItemIndex >= orderedItems.Count - 1)
            throw new InvalidOperationException("Invalid split index");

        var newPhase = new RoadmapPhase
        {
            RoadmapId = roadmap.Id,
            Name = $"{phase.Name} (Part 2)",
            DisplayOrder = phase.DisplayOrder + 1
        };

        var itemsToMove = orderedItems.Skip(request.SplitAfterItemIndex + 1).ToList();
        foreach (var item in itemsToMove)
        {
            phase.Items.Remove(item);
            item.PhaseId = newPhase.Id;
            newPhase.Items.Add(item);
        }

        // Insert new phase after the split phase
        var insertIndex = roadmap.Phases.IndexOf(phase) + 1;
        roadmap.Phases.Insert(insertIndex, newPhase);
    }

    private static void RecalculatePhaseAggregates(RoadmapPhase phase, double totalRoadmapRiskPoints)
    {
        phase.EstimatedEffort = phase.Items.Sum(i => i.EstimatedEffortDays);
        phase.RiskPoints = phase.Items.Sum(i => i.RiskPoints);
        phase.RiskReductionPercent = totalRoadmapRiskPoints > 0
            ? phase.RiskPoints / totalRoadmapRiskPoints * 100
            : 0;
        phase.TotalItemCount = phase.Items.Count;
        phase.CompletedItemCount = phase.Items.Count(i => i.Status == ItemStatus.Complete);
        phase.UpdatedAt = DateTime.UtcNow;
    }

    private static void RecalculateRoadmapAggregates(ImplementationRoadmap roadmap)
    {
        roadmap.TotalEstimatedEffort = roadmap.Phases.Sum(p => p.EstimatedEffort);
        roadmap.TotalRiskPoints = roadmap.Phases.Sum(p => p.RiskPoints);

        // Recalculate phase risk reduction percentages
        foreach (var phase in roadmap.Phases)
            RecalculatePhaseAggregates(phase, roadmap.TotalRiskPoints);
    }
}

/// <summary>Internal model for AI/deterministic phase assignment results.</summary>
internal class PhaseAssignment
{
    public string ControlId { get; set; } = "";
    public int PhaseOrder { get; set; }
    public string PhaseName { get; set; } = "";
    public double EstimatedEffortDays { get; set; }
}
