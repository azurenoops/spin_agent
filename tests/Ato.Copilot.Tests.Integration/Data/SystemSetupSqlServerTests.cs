using System.Data;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Data;

public sealed class SystemSetupSqlServerTests(BoundarySchemaSqlServerFixture fixture)
    : IClassFixture<BoundarySchemaSqlServerFixture>
{
    [SkippableFact]
    public async Task LegacyUpgradeAndRepeatedStartup_PreserveOpaqueDraftAndBoundSource()
    {
        // Arrange
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        await using var db = await fixture.CreateDatabaseAsync();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE dbo.RegisteredSystems (
                Id NVARCHAR(36) NOT NULL PRIMARY KEY,
                TenantId UNIQUEIDENTIFIER NOT NULL,
                Name NVARCHAR(200) NOT NULL);
            CREATE TABLE dbo.EmassImportSessions (
                Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                TenantId UNIQUEIDENTIFIER NOT NULL,
                ContentChecksumSha256 CHAR(64) NOT NULL);
            INSERT INTO dbo.RegisteredSystems(Id,TenantId,Name)
                VALUES (N'opaque-legacy-system', '11111111-1111-1111-1111-111111111111', N'Retained system');
            INSERT INTO dbo.EmassImportSessions(Id,TenantId,ContentChecksumSha256)
                VALUES ('22222222-2222-2222-2222-222222222222', '11111111-1111-1111-1111-111111111111', REPLICATE('a',64));
            """);
        await SystemSetupSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        var draftJson = """{"schemaVersion":1,"objective":"initialAto","lastScreen":"s-review"}""";
        var commandJson = """{"action":"Save","appliedRevision":3}""";
        var proposalJson = """{"state":"parsed","fields":[]}""";
        var snapshotJson = """{"previewHash":"retained-preview"}""";
        var receiptJson = """{"sourceHash":"retained-source-hash"}""";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dbo.RegisteredSystems SET
                SetupDraftJson={draftJson},
                SetupRevision=3, SetupRequestKey=N'retained-create-request',
                SetupRequestHash=REPLICATE('b',64),
                SetupActorPersonId='33333333-3333-3333-3333-333333333333',
                SetupLastCommandJson={commandJson}
            WHERE Id=N'opaque-legacy-system';
            UPDATE dbo.EmassImportSessions SET
                TargetSystemId=N'opaque-legacy-system', RequestKey=N'retained-source-request',
                RequestPayloadHash=REPLICATE('c',64),
                RequestActorPersonId='33333333-3333-3333-3333-333333333333',
                ReviewRevision=1, ReviewProposalJson={proposalJson},
                ReviewSnapshotJson={snapshotJson},
                ApplyReceiptJson={receiptJson};
            """);
        var before = await SnapshotAsync(db);

        // Act
        await SystemSetupSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await SystemSetupSchemaAdditions.ApplyAsync(db, NullLogger.Instance);

        // Assert
        (await SnapshotAsync(db)).Should().Be(before);
        before.Should().Contain("opaque-legacy-system").And.Contain("retained-create-request")
            .And.Contain("retained-source-request").And.Contain("retained-preview");
        await using var command = db.Database.GetDbConnection().CreateCommand();
        if (command.Connection!.State != ConnectionState.Open) await command.Connection.OpenAsync();
        command.CommandText = """
            SELECT COUNT(*) FROM sys.indexes WHERE is_unique=1 AND has_filter=1
              AND name IN ('UX_RegisteredSystem_SetupRequest','UX_EmassImportSession_SystemSourceRequest');
            """;
        Convert.ToInt32(await command.ExecuteScalarAsync()).Should().Be(2);
    }

    private static async Task<string> SnapshotAsync(AtoCopilotContext db)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        if (command.Connection!.State != ConnectionState.Open) await command.Connection.OpenAsync();
        command.CommandText = """
            SELECT
              (SELECT Id,TenantId,Name,SetupDraftJson,SetupRevision,SetupCompletedAt,
                      SetupRequestKey,SetupRequestHash,SetupActorPersonId,SetupLastCommandJson
               FROM dbo.RegisteredSystems ORDER BY Id FOR JSON PATH, INCLUDE_NULL_VALUES) AS Systems,
              (SELECT Id,TenantId,ContentChecksumSha256,TargetSystemId,RequestKey,RequestPayloadHash,
                      RequestActorPersonId,ReviewRevision,ReviewProposalJson,ReviewSnapshotJson,ApplyReceiptJson
               FROM dbo.EmassImportSessions ORDER BY Id FOR JSON PATH, INCLUDE_NULL_VALUES) AS Sources
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
            """;
        return Convert.ToString(await command.ExecuteScalarAsync())!;
    }
}
