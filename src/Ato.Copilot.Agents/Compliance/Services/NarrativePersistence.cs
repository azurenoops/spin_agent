using Ato.Copilot.Core.Data.Context;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Agents.Compliance.Services;

internal static class NarrativePersistence
{
    public static async Task SaveAsync(AtoCopilotContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 or 1555 } ||
            exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new InvalidOperationException(
                "CONCURRENCY_CONFLICT: A matching reference revision or proposal was created concurrently. Reload before retrying.", exception);
        }
    }
}
