using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Provenance;
using Ato.Copilot.Core.Models.Provenance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Mcp.Services;

/// <summary>
/// EF Core-backed implementation of <see cref="IModelCallLedger"/> (#941 — Epic 10).
///
/// Append-only: <see cref="RecordAsync"/> only ever inserts rows.
/// Uses <see cref="IDbContextFactory{T}"/> so singleton services (McpServer) can safely
/// call it — matches the house pattern for AtoCopilotContext in Program.cs.
///
/// Schema note: the ModelCall table is created via EnsureSchemaAdditionsAsync in dev
/// and via a dedicated migration in prod.  Legacy rows will backfill NULLs.
/// </summary>
public sealed class ModelCallLedger : IModelCallLedger
{
    private readonly IDbContextFactory<AtoCopilotContext> _contextFactory;
    private readonly ILogger<ModelCallLedger> _logger;

    public ModelCallLedger(
        IDbContextFactory<AtoCopilotContext> contextFactory,
        ILogger<ModelCallLedger> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> RecordAsync(
        ModelCall record,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        db.ModelCalls.Add(record);
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[ModelCallLedger] Recorded call {Id} conversation={ConvId} " +
            "index={Index} provider={Provider} model={Model} latency={LatencyMs}ms",
            record.Id, record.ConversationId, record.CallIndex,
            record.Provider, record.ModelId, record.LatencyMs);

        return record.Id;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ModelCall>> GetByConversationAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.ModelCalls
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CallIndex)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }
}
