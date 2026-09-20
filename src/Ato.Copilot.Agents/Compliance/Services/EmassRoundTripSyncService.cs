using System.Diagnostics;
using System.Globalization;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Services;

public sealed class EmassRoundTripSyncService(
    IDbContextFactory<AtoCopilotContext> contextFactory,
    ILogger<EmassRoundTripSyncService>? logger = null) : IEmassRoundTripSyncService
{
    public async Task<EmassSyncResult> StartSyncAsync(
        string systemId,
        Stream excelStream,
        bool acknowledgeUnresolved = false,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        ArgumentNullException.ThrowIfNull(excelStream);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var system = await context.RegisteredSystems
            .FirstOrDefaultAsync(candidate => candidate.Id == systemId, cancellationToken)
            ?? throw new InvalidOperationException($"RegisteredSystem '{systemId}' not found.");

        var unresolvedCount = await context.EmassConflicts.CountAsync(
            conflict => conflict.RegisteredSystemId == systemId &&
                conflict.ConflictStatus == ConflictStatus.Unresolved,
            cancellationToken);
        if (unresolvedCount > 0 && !acknowledgeUnresolved)
            throw new UnresolvedEmassConflictsException(unresolvedCount);

        using var workbook = new XLWorkbook(excelStream);
        var batchId = Guid.NewGuid().ToString();
        var detectedAt = DateTimeOffset.UtcNow;
        var conflicts = new Dictionary<string, EmassConflict>(StringComparer.OrdinalIgnoreCase);
        var identicalFields = 0;
        var controlsSheet = workbook.Worksheets.FirstOrDefault(sheet =>
            sheet.Name.Equals("Controls", StringComparison.OrdinalIgnoreCase));
        var poamSheet = workbook.Worksheets.FirstOrDefault(sheet =>
            sheet.Name.Equals("POAM", StringComparison.OrdinalIgnoreCase));
        if (controlsSheet is null && poamSheet is null)
            throw new InvalidDataException("The workbook must contain a Controls or POAM worksheet.");

        if (controlsSheet is not null)
            identicalFields += await CompareControlsAsync(
                context, controlsSheet, system, batchId, detectedAt, conflicts, cancellationToken);
        if (poamSheet is not null)
            identicalFields += await ComparePoamAsync(
                context, poamSheet, system, batchId, detectedAt, conflicts, cancellationToken);

        context.EmassConflicts.AddRange(conflicts.Values);
        await context.SaveChangesAsync(cancellationToken);

        logger?.LogInformation(
            "Completed eMASS round-trip sync for {SystemId}; batch {BatchId} created {ConflictCount} conflicts in {ElapsedMilliseconds} ms",
            systemId,
            batchId,
            conflicts.Count,
            stopwatch.ElapsedMilliseconds);

        return new EmassSyncResult(
            batchId,
            systemId,
            conflicts.Count,
            identicalFields,
            acknowledgeUnresolved ? unresolvedCount : 0,
            detectedAt);
    }

    public async Task<EmassConflictDto> ResolveConflictAsync(
        string systemId,
        string conflictId,
        ResolveConflictRequest request,
        string resolvedBy,
        CancellationToken cancellationToken = default)
    {
        if (request.Resolution == ConflictStatus.Unresolved)
            throw new ArgumentException("A conflict resolution must be KeepSpin, AcceptEmass, or Deferred.");

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var conflict = await context.EmassConflicts.FirstOrDefaultAsync(
            candidate => candidate.Id == conflictId && candidate.RegisteredSystemId == systemId,
            cancellationToken)
            ?? throw new InvalidOperationException($"eMASS conflict '{conflictId}' not found.");
        if (conflict.ConflictStatus is ConflictStatus.KeepSpin or ConflictStatus.AcceptEmass)
            throw new EmassConflictAlreadyResolvedException(conflictId);

        if (request.Resolution == ConflictStatus.AcceptEmass)
            await ApplyEmassValueAsync(context, conflict, cancellationToken);

        conflict.ConflictStatus = request.Resolution;
        conflict.ResolvedAt = DateTimeOffset.UtcNow;
        conflict.ResolvedBy = resolvedBy;
        conflict.Notes = request.Notes;
        context.AuditLogs.Add(new AuditLogEntry
        {
            TenantId = conflict.TenantId,
            UserId = resolvedBy,
            UserRole = "ISSO/ISSM",
            Action = "EmassConflict.Resolve",
            AffectedResources = [systemId, conflictId],
            Details = $"Resolution={request.Resolution}; Field={conflict.FieldName}",
        });
        await context.SaveChangesAsync(cancellationToken);
        return ToDto(conflict);
    }

    public async Task<IReadOnlyList<EmassConflictDto>> GetConflictsAsync(
        string systemId,
        ConflictStatus? status = ConflictStatus.Unresolved,
        string? batchId = null,
        int limit = 50,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.EmassConflicts
            .AsNoTracking()
            .Where(conflict => conflict.RegisteredSystemId == systemId);
        if (status.HasValue)
            query = query.Where(conflict => conflict.ConflictStatus == status.Value);
        if (!string.IsNullOrWhiteSpace(batchId))
            query = query.Where(conflict => conflict.SyncBatchId == batchId);

        return await query
            .OrderByDescending(conflict => conflict.DetectedAt)
            .Skip(Math.Max(0, offset))
            .Take(Math.Clamp(limit, 1, 200))
            .Select(conflict => ToDto(conflict))
            .ToListAsync(cancellationToken);
    }

    private static Dictionary<string, int> ReadColumns(IXLWorksheet worksheet) =>
        worksheet.Row(1).CellsUsed().ToDictionary(
            cell => cell.GetString().Trim(),
            cell => cell.Address.ColumnNumber,
            StringComparer.OrdinalIgnoreCase);

    private static async Task<int> CompareControlsAsync(
        AtoCopilotContext context,
        IXLWorksheet worksheet,
        RegisteredSystem system,
        string batchId,
        DateTimeOffset detectedAt,
        IDictionary<string, EmassConflict> conflicts,
        CancellationToken cancellationToken)
    {
        var columns = ReadColumns(worksheet);
        RequireColumns(columns, "System Name", "System Acronym", "DITPR ID", "eMASS ID",
            "Control Identifier", "Implementation Status", "Implementation Narrative");
        var implementations = await context.ControlImplementations
            .Where(implementation => implementation.RegisteredSystemId == system.Id)
            .ToDictionaryAsync(
                implementation => implementation.ControlId,
                StringComparer.OrdinalIgnoreCase,
                cancellationToken);
        var identicalFields = 0;
        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;

        for (var row = 2; row <= lastRow; row++)
        {
            ValidateEmassId(system, GetValue(worksheet, row, columns, "eMASS ID"));
            identicalFields++;
            Compare(conflicts, system, batchId, detectedAt, "SystemInfo", null,
                "SystemInfo.SystemName", system.Name,
                GetValue(worksheet, row, columns, "System Name"), ref identicalFields);
            Compare(conflicts, system, batchId, detectedAt, "SystemInfo", null,
                "SystemInfo.Acronym", system.Acronym,
                GetValue(worksheet, row, columns, "System Acronym"), ref identicalFields);
            Compare(conflicts, system, batchId, detectedAt, "SystemInfo", null,
                "SystemInfo.DitprId", system.DitprId,
                GetValue(worksheet, row, columns, "DITPR ID"), ref identicalFields);

            var controlId = GetValue(worksheet, row, columns, "Control Identifier");
            if (string.IsNullOrWhiteSpace(controlId))
                continue;
            if (!implementations.TryGetValue(controlId, out var implementation))
            {
                Compare(conflicts, system, batchId, detectedAt, "ControlImplementation",
                    null, "ControlImplementation.Deletion", null, controlId, ref identicalFields);
                continue;
            }

            Compare(conflicts, system, batchId, detectedAt, "ControlImplementation",
                implementation.Id, "ControlImplementation.ImplementationStatus",
                FormatStatus(implementation.ImplementationStatus),
                GetValue(worksheet, row, columns, "Implementation Status"), ref identicalFields);
            Compare(conflicts, system, batchId, detectedAt, "ControlImplementation",
                implementation.Id, "ControlImplementation.Narrative", implementation.Narrative,
                GetValue(worksheet, row, columns, "Implementation Narrative"), ref identicalFields);
        }

        return identicalFields;
    }

    private static async Task<int> ComparePoamAsync(
        AtoCopilotContext context,
        IXLWorksheet worksheet,
        RegisteredSystem system,
        string batchId,
        DateTimeOffset detectedAt,
        IDictionary<string, EmassConflict> conflicts,
        CancellationToken cancellationToken)
    {
        var columns = ReadColumns(worksheet);
        RequireColumns(columns, "System Name", "eMASS ID", "POA&M ID", "Weakness",
            "Weakness Source", "Point of Contact", "POC Email", "Security Control Number",
            "Scheduled Completion Date", "Resources Required", "Cost Estimate", "Status",
            "Completion Date", "Comments");
        var poamItems = await context.PoamItems
            .Where(item => item.RegisteredSystemId == system.Id)
            .ToDictionaryAsync(item => item.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var identicalFields = 0;
        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;

        for (var row = 2; row <= lastRow; row++)
        {
            ValidateEmassId(system, GetValue(worksheet, row, columns, "eMASS ID"));
            identicalFields++;
            Compare(conflicts, system, batchId, detectedAt, "SystemInfo", null,
                "SystemInfo.SystemName", system.Name,
                GetValue(worksheet, row, columns, "System Name"), ref identicalFields);

            var poamId = GetValue(worksheet, row, columns, "POA&M ID");
            if (string.IsNullOrWhiteSpace(poamId))
                continue;
            if (!poamItems.TryGetValue(poamId, out var item))
            {
                Compare(conflicts, system, batchId, detectedAt, "PoamItem",
                    null, "PoamItem.Deletion", null, poamId, ref identicalFields);
                continue;
            }

            ComparePoamField("Weakness", item.Weakness, "Weakness");
            ComparePoamField("WeaknessSource", item.WeaknessSource, "Weakness Source");
            ComparePoamField("PointOfContact", item.PointOfContact, "Point of Contact");
            ComparePoamField("PocEmail", item.PocEmail, "POC Email");
            ComparePoamField("SecurityControlNumber", item.SecurityControlNumber, "Security Control Number");
            ComparePoamField("ScheduledCompletionDate", item.ScheduledCompletionDate.ToString("MM/dd/yyyy"), "Scheduled Completion Date");
            ComparePoamField("ResourcesRequired", item.ResourcesRequired, "Resources Required");
            ComparePoamField("CostEstimate", item.CostEstimate?.ToString("F2", CultureInfo.InvariantCulture), "Cost Estimate");
            ComparePoamField("Status", item.Status.ToString(), "Status");
            ComparePoamField("ActualCompletionDate", item.ActualCompletionDate?.ToString("MM/dd/yyyy"), "Completion Date");
            ComparePoamField("Comments", item.Comments, "Comments");

            void ComparePoamField(string property, string? spinValue, string column) =>
                Compare(conflicts, system, batchId, detectedAt, "PoamItem", item.Id,
                    $"PoamItem.{property}", spinValue,
                    GetValue(worksheet, row, columns, column), ref identicalFields);
        }

        return identicalFields;
    }

    private static void ValidateEmassId(RegisteredSystem system, string? importedEmassId)
    {
        if (!Equivalent(system.EmassId, importedEmassId))
            throw new EmassSystemIdMismatchException(system.EmassId, importedEmassId);
    }

    private static void RequireColumns(Dictionary<string, int> columns, params string[] required)
    {
        var missing = required.Where(column => !columns.ContainsKey(column)).ToArray();
        if (missing.Length > 0)
            throw new InvalidDataException($"Missing required eMASS columns: {string.Join(", ", missing)}.");
    }

    private static string? GetValue(
        IXLWorksheet worksheet,
        int row,
        IReadOnlyDictionary<string, int> columns,
        string column)
    {
        var value = worksheet.Cell(row, columns[column]).GetString().Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static void Compare(
        IDictionary<string, EmassConflict> conflicts,
        RegisteredSystem system,
        string batchId,
        DateTimeOffset detectedAt,
        string entityType,
        string? entityId,
        string fieldName,
        string? spinValue,
        string? emassValue,
        ref int identicalFields)
    {
        if (Equivalent(spinValue, emassValue))
        {
            identicalFields++;
            return;
        }

        var key = $"{entityType}:{entityId}:{fieldName}";
        conflicts.TryAdd(key, new EmassConflict
        {
            TenantId = system.TenantId,
            RegisteredSystemId = system.Id,
            SyncBatchId = batchId,
            EntityType = entityType,
            EntityId = entityId,
            FieldName = fieldName,
            SpinValue = spinValue,
            EmassValue = emassValue,
            DetectedAt = detectedAt,
        });
    }

    private static bool Equivalent(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase) ||
        (string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right));

    private static string FormatStatus(ImplementationStatus status) => status switch
    {
        ImplementationStatus.PartiallyImplemented => "Partially Implemented",
        ImplementationStatus.NotApplicable => "Not Applicable",
        _ => status.ToString(),
    };

    private static async Task ApplyEmassValueAsync(
        AtoCopilotContext context,
        EmassConflict conflict,
        CancellationToken cancellationToken)
    {
        if (conflict.EntityType == "SystemInfo")
        {
            var system = await context.RegisteredSystems.FirstAsync(
                candidate => candidate.Id == conflict.RegisteredSystemId, cancellationToken);
            switch (conflict.FieldName)
            {
                case "SystemInfo.SystemName": system.Name = conflict.EmassValue ?? string.Empty; break;
                case "SystemInfo.Acronym": system.Acronym = conflict.EmassValue; break;
                case "SystemInfo.DitprId": system.DitprId = conflict.EmassValue; break;
                default: throw new InvalidOperationException($"Unsupported conflict field '{conflict.FieldName}'.");
            }
            system.ModifiedAt = DateTime.UtcNow;
            return;
        }

        if (conflict.EntityType == "ControlImplementation")
        {
            var implementation = await context.ControlImplementations.FirstAsync(
                candidate => candidate.Id == conflict.EntityId &&
                    candidate.RegisteredSystemId == conflict.RegisteredSystemId,
                cancellationToken);
            switch (conflict.FieldName)
            {
                case "ControlImplementation.ImplementationStatus":
                    implementation.ImplementationStatus = ParseStatus(conflict.EmassValue);
                    break;
                case "ControlImplementation.Narrative":
                    implementation.SetCombinedNarrative(conflict.EmassValue);
                    break;
                default: throw new InvalidOperationException($"Unsupported conflict field '{conflict.FieldName}'.");
            }
            implementation.ModifiedAt = DateTime.UtcNow;
            return;
        }

        if (conflict.EntityType == "PoamItem")
        {
            var item = await context.PoamItems.FirstAsync(
                candidate => candidate.Id == conflict.EntityId &&
                    candidate.RegisteredSystemId == conflict.RegisteredSystemId,
                cancellationToken);
            switch (conflict.FieldName)
            {
                case "PoamItem.Weakness": item.Weakness = conflict.EmassValue ?? string.Empty; break;
                case "PoamItem.WeaknessSource": item.WeaknessSource = conflict.EmassValue ?? string.Empty; break;
                case "PoamItem.PointOfContact": item.PointOfContact = conflict.EmassValue ?? string.Empty; break;
                case "PoamItem.PocEmail": item.PocEmail = conflict.EmassValue; break;
                case "PoamItem.SecurityControlNumber": item.SecurityControlNumber = conflict.EmassValue ?? string.Empty; break;
                case "PoamItem.ScheduledCompletionDate": item.ScheduledCompletionDate = ParseDate(conflict.EmassValue); break;
                case "PoamItem.ResourcesRequired": item.ResourcesRequired = conflict.EmassValue; break;
                case "PoamItem.CostEstimate": item.CostEstimate = ParseDecimal(conflict.EmassValue); break;
                case "PoamItem.Status": item.Status = ParsePoamStatus(conflict.EmassValue); break;
                case "PoamItem.ActualCompletionDate": item.ActualCompletionDate = ParseNullableDate(conflict.EmassValue); break;
                case "PoamItem.Comments": item.Comments = conflict.EmassValue; break;
                default: throw new InvalidOperationException($"Unsupported conflict field '{conflict.FieldName}'.");
            }
            item.ModifiedAt = DateTime.UtcNow;
            return;
        }

        throw new InvalidOperationException($"Unsupported conflict entity '{conflict.EntityType}'.");
    }

    private static ImplementationStatus ParseStatus(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "implemented" => ImplementationStatus.Implemented,
            "partially implemented" => ImplementationStatus.PartiallyImplemented,
            "not applicable" => ImplementationStatus.NotApplicable,
            "planned" or "not implemented" => ImplementationStatus.Planned,
            _ => throw new InvalidDataException($"Unsupported implementation status '{value}'."),
        };

    private static DateTime ParseDate(string? value) => ParseNullableDate(value)
        ?? throw new InvalidDataException($"A date value is required, but received '{value}'.");

    private static DateTime? ParseNullableDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date))
            return date;
        throw new InvalidDataException($"Unsupported date value '{value}'.");
    }

    private static decimal? ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            return amount;
        throw new InvalidDataException($"Unsupported cost value '{value}'.");
    }

    private static PoamStatus ParsePoamStatus(string? value) =>
        Enum.TryParse<PoamStatus>(value?.Replace(" ", string.Empty), true, out var status)
            ? status
            : throw new InvalidDataException($"Unsupported POA&M status '{value}'.");

    private static EmassConflictDto ToDto(EmassConflict conflict) => new(
        conflict.Id,
        conflict.EntityType,
        conflict.EntityId,
        conflict.FieldName,
        conflict.SpinValue,
        conflict.EmassValue,
        conflict.ConflictStatus,
        conflict.DetectedAt,
        conflict.ResolvedAt,
        conflict.ResolvedBy);
}

public sealed class UnresolvedEmassConflictsException(int count)
    : InvalidOperationException($"{count} unresolved eMASS conflicts require acknowledgment.")
{
    public int Count { get; } = count;
}

public sealed class EmassSystemIdMismatchException(string? expected, string? actual)
    : InvalidOperationException($"The uploaded eMASS ID '{actual}' does not match '{expected}'.");

public sealed class EmassConflictAlreadyResolvedException(string conflictId)
    : InvalidOperationException($"eMASS conflict '{conflictId}' is already resolved.");