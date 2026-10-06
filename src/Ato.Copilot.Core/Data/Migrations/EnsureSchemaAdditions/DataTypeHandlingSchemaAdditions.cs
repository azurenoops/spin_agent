using Ato.Copilot.Core.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

public static class DataTypeHandlingSchemaAdditions
{
    public static async Task ApplyAsync(AtoCopilotContext db, CancellationToken ct = default)
    {
        var columns = new (string Name, int Length)[]
        {
            ("CuiCategory", 500), ("ConfidentialityImpact", 40), ("IntegrityImpact", 40), ("AvailabilityImpact", 40),
            ("PrivacyApplicability", 40), ("RetentionRule", 2000), ("DisposalMethod", 2000),
            ("CategorizationRationale", 2000), ("CategorizationReference", 2000)
        };
        foreach (var column in columns)
        {
            if (db.Database.IsSqlServer())
                await db.Database.ExecuteSqlRawAsync(
                    $"IF COL_LENGTH('dbo.DataTypeEntries', '{column.Name}') IS NULL ALTER TABLE dbo.[DataTypeEntries] ADD [{column.Name}] nvarchar({column.Length}) NULL", ct);
            else if (db.Database.IsSqlite())
            {
                var count = await db.Database.SqlQueryRaw<int>(
                    $"SELECT COUNT(*) AS Value FROM pragma_table_info('DataTypeEntries') WHERE name = '{column.Name}'").SingleAsync(ct);
                if (count == 0) await db.Database.ExecuteSqlRawAsync($"ALTER TABLE [DataTypeEntries] ADD [{column.Name}] TEXT NULL", ct);
            }
            else throw new NotSupportedException("Information handling schema requires SQLite or SQL Server.");
        }
    }
}
