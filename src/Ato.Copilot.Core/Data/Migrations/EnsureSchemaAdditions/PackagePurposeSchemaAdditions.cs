using Ato.Copilot.Core.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

/// <summary>Retains legacy package semantics without rewriting historical artifacts or inventing a purpose.</summary>
public static class PackagePurposeSchemaAdditions
{
    public static async Task ApplyAsync(AtoCopilotContext db, ILogger logger, CancellationToken ct = default)
    {
        if (db.Database.IsSqlServer())
        {
            await db.Database.ExecuteSqlRawAsync("""
                IF COL_LENGTH('dbo.AuthorizationPackages', 'Purpose') IS NULL
                    ALTER TABLE dbo.AuthorizationPackages ADD Purpose int NOT NULL
                    CONSTRAINT DF_AuthorizationPackages_Purpose DEFAULT 0;
                """, ct);
        }
        else if (db.Database.IsSqlite())
        {
            var exists = await db.Database.SqlQueryRaw<int>(
                """SELECT COUNT(*) AS Value FROM pragma_table_info('AuthorizationPackages') WHERE name = 'Purpose'""")
                .SingleAsync(ct);
            if (exists == 0)
                await db.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE AuthorizationPackages ADD COLUMN Purpose INTEGER NOT NULL DEFAULT 0", ct);
        }
        else
        {
            throw new NotSupportedException("Package purpose persistence requires SQLite or SQL Server.");
        }
        logger.LogInformation("Verified additive authorization package purpose schema for {Provider}", db.Database.ProviderName);
    }
}
