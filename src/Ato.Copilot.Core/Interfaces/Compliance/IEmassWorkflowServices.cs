using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Interfaces.Compliance;

public interface IEmassExportReadinessService
{
    Task<EmassExportReadinessResult> CheckReadinessAsync(
        string systemId,
        CancellationToken cancellationToken = default);
}

public interface IEmassRoundTripSyncService
{
    Task<EmassSyncResult> StartSyncAsync(
        string systemId,
        Stream excelStream,
        bool acknowledgeUnresolved = false,
        CancellationToken cancellationToken = default);

    Task<EmassConflictDto> ResolveConflictAsync(
        string systemId,
        string conflictId,
        ResolveConflictRequest request,
        string resolvedBy,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmassConflictDto>> GetConflictsAsync(
        string systemId,
        ConflictStatus? status = ConflictStatus.Unresolved,
        string? batchId = null,
        int limit = 50,
        int offset = 0,
        CancellationToken cancellationToken = default);
}

public interface IEmassWorkflowStatusService
{
    Task<EmassWorkflowStatus> GetStatusAsync(
        string systemId,
        CancellationToken cancellationToken = default);
}