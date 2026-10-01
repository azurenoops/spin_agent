using System.Data;
using System.Data.Common;
using System.Linq.Expressions;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Ato.Copilot.Core.Data;

/// <summary>Reads large catalog JSON without SqlClient's buffered async LOB materialization.</summary>
public static class CatalogSourceReader
{
    /// <summary>Resolve scoped metadata first, then stream the immutable binding source.</summary>
    public static async Task<BaselineCatalogBinding?> ReadBindingAsync(AtoCopilotContext db,
        Expression<Func<BaselineCatalogBinding, bool>> predicate, CancellationToken ct = default)
    {
        var query = db.BaselineCatalogBindings.AsNoTracking().Where(predicate);
        if (!db.Database.IsSqlServer()) return await query.SingleOrDefaultAsync(ct);
        var binding = await query.Select(x => new BaselineCatalogBinding
        {
            Id = x.Id, TenantId = x.TenantId, ControlBaselineId = x.ControlBaselineId,
            FrameworkId = x.FrameworkId, FrameworkIdentifier = x.FrameworkIdentifier,
            CatalogVersion = x.CatalogVersion, SourceUri = x.SourceUri, Publisher = x.Publisher,
            ContentHash = x.ContentHash, Rationale = x.Rationale, BoundBy = x.BoundBy, BoundAt = x.BoundAt
        }).SingleOrDefaultAsync(ct);
        if (binding is null) return null;
        binding.CatalogJson = await ReadTextAsync(db, """
            SELECT [CatalogJson] FROM [BaselineCatalogBindings]
            WHERE [Id]=@id AND [TenantId]=@tenant AND [ControlBaselineId]=@baseline AND [ContentHash]=@hash
            """, command =>
        {
            Parameter(command, "@id", DbType.String, binding.Id);
            Parameter(command, "@tenant", DbType.Guid, binding.TenantId);
            Parameter(command, "@baseline", DbType.String, binding.ControlBaselineId);
            Parameter(command, "@hash", DbType.String, binding.ContentHash);
        }, ct) ?? throw new InvalidDataException("The retained catalog source is missing.");
        return binding;
    }

    /// <summary>Resolve reference metadata first and detect concurrent source replacement.</summary>
    public static async Task<ComplianceFramework?> ReadFrameworkAsync(AtoCopilotContext db,
        Expression<Func<ComplianceFramework, bool>> predicate, CancellationToken ct = default)
    {
        var query = db.ComplianceFrameworks.AsNoTracking().Where(predicate);
        if (!db.Database.IsSqlServer()) return await query.SingleOrDefaultAsync(ct);
        var framework = await query.Select(x => new ComplianceFramework
        {
            Id = x.Id, Identifier = x.Identifier, Name = x.Name, Version = x.Version, Publisher = x.Publisher,
            CatalogUrl = x.CatalogUrl, OscalModelType = x.OscalModelType, ImportedAt = x.ImportedAt,
            ControlCount = x.ControlCount, IsActive = x.IsActive,
            RequirementCatalogVersion = x.RequirementCatalogVersion,
            RequirementCatalogSourceUri = x.RequirementCatalogSourceUri,
            RequirementCatalogCapturedAt = x.RequirementCatalogCapturedAt
        }).SingleOrDefaultAsync(ct);
        if (framework is null) return null;
        framework.RequirementCatalogJson = await ReadTextAsync(db, """
            SELECT [RequirementCatalogJson] FROM [ComplianceFrameworks]
            WHERE [Id]=@id AND ([RequirementCatalogCapturedAt]=@captured
                OR ([RequirementCatalogCapturedAt] IS NULL AND @captured IS NULL))
            """, command =>
        {
            Parameter(command, "@id", DbType.String, framework.Id);
            Parameter(command, "@captured", DbType.DateTime2, framework.RequirementCatalogCapturedAt);
        }, ct);
        return framework;
    }

    private static async Task<string?> ReadTextAsync(AtoCopilotContext db, string sql,
        Action<DbCommand> parameters, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = db.Database.GetCommandTimeout() ?? 30;
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            parameters(command);
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess | CommandBehavior.SingleRow, ct);
            if (!await reader.ReadAsync(ct))
                throw new DbUpdateConcurrencyException("Catalog source changed or is no longer accessible. Reload before continuing.");
            if (await reader.IsDBNullAsync(0, ct)) return null;
            using var text = reader.GetTextReader(0);
            return await text.ReadToEndAsync(ct);
        }
        finally
        {
            if (openedHere) await db.Database.CloseConnectionAsync();
        }
    }

    private static void Parameter(DbCommand command, string name, DbType type, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
