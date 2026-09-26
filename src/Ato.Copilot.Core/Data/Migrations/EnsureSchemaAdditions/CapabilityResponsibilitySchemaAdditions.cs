using System.Data;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

/// <summary>Additive production schema for the explicit system-subscription review workflow.</summary>
public static class CapabilityResponsibilitySchemaAdditions
{
    public static async Task ApplyAsync(AtoCopilotContext db, ILogger logger, CancellationToken ct = default)
    {
        if (!db.Database.IsSqlServer() && !db.Database.IsSqlite())
            throw new NotSupportedException("Capability responsibility persistence requires SQL Server or SQLite.");
        if (db.Database.IsSqlite()) await UpgradeSqliteSourceEventsAsync(db, ct);
        await db.Database.ExecuteSqlRawAsync(db.Database.IsSqlServer() ? SqlServerScript : SqliteScript, ct);
        await BackfillRoutingAsync(db, logger, ct);
        var unattributed = await db.Set<CspResponsibilitySourceEvent>().CountAsync(e => e.Actor == null, ct);
        if (unattributed > 0)
            logger.LogWarning("{Count} legacy provider events lack actor provenance; provider re-review is required before fanout", unattributed);
        logger.LogInformation("Verified capability responsibility persistence schema");
    }

    private static async Task UpgradeSqliteSourceEventsAsync(AtoCopilotContext db, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        var alreadyOpen = connection.State == ConnectionState.Open;
        if (!alreadyOpen) await db.Database.OpenConnectionAsync(ct);
        try
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS CapabilitySubscriptions (
                  Id TEXT NOT NULL PRIMARY KEY, RegisteredSystemId TEXT NOT NULL, CspInheritedCapabilityId TEXT NOT NULL,
                  SubscribedBy TEXT NOT NULL DEFAULT 'dashboard-user', SubscribedAt TEXT NOT NULL,
                  IsActive INTEGER NOT NULL DEFAULT 1);
                CREATE INDEX IF NOT EXISTS IX_CapabilitySubscription_System_Capability
                  ON CapabilitySubscriptions(RegisteredSystemId,CspInheritedCapabilityId);
                """, ct);
            var subscriptions = await SqliteColumnsAsync(connection, "PRAGMA table_info(\"CapabilitySubscriptions\")", ct);
            if (!subscriptions.Contains("RoutingTenantId"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE CapabilitySubscriptions ADD COLUMN RoutingTenantId TEXT NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000'", ct);
            if (!subscriptions.Contains("RoutingCapabilityId"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE CapabilitySubscriptions ADD COLUMN RoutingCapabilityId TEXT NOT NULL DEFAULT ''", ct);
            var reviews = await SqliteColumnsAsync(connection, "PRAGMA table_info(\"CapabilityResponsibilityConfirmations\")", ct);
            if (reviews.Count > 0)
            {
                if (!reviews.Contains("ProviderCoverageVerified"))
                    await db.Database.ExecuteSqlRawAsync("ALTER TABLE CapabilityResponsibilityConfirmations ADD COLUMN ProviderCoverageVerified INTEGER NULL", ct);
                if (!reviews.Contains("CustomerDutiesReviewed"))
                    await db.Database.ExecuteSqlRawAsync("ALTER TABLE CapabilityResponsibilityConfirmations ADD COLUMN CustomerDutiesReviewed INTEGER NULL", ct);
                if (!reviews.Contains("ReviewNotes"))
                    await db.Database.ExecuteSqlRawAsync("ALTER TABLE CapabilityResponsibilityConfirmations ADD COLUMN ReviewNotes TEXT NULL", ct);
            }
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info(\"CspResponsibilitySourceEvents\")";
                await using var reader = await command.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) columns.Add(reader.GetString(1));
            }
            if (columns.Count == 0) return;
            if (!columns.Contains("LastSubscriptionId"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE CspResponsibilitySourceEvents ADD COLUMN LastSubscriptionId TEXT NULL", ct);
            if (!columns.Contains("FanoutCompleted"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE CspResponsibilitySourceEvents ADD COLUMN FanoutCompleted INTEGER NOT NULL DEFAULT 0", ct);
            if (!columns.Contains("NextExpansionUtcTicks"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE CspResponsibilitySourceEvents ADD COLUMN NextExpansionUtcTicks INTEGER NOT NULL DEFAULT 0", ct);
            if (!columns.Contains("ExpansionRevision"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE CspResponsibilitySourceEvents ADD COLUMN ExpansionRevision INTEGER NOT NULL DEFAULT 0", ct);
            if (!columns.Contains("Actor"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE CspResponsibilitySourceEvents ADD COLUMN Actor TEXT NULL", ct);
            if (!columns.Contains("Sequence"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE CspResponsibilitySourceEvents ADD COLUMN Sequence INTEGER NOT NULL DEFAULT 0", ct);
            await db.Database.ExecuteSqlRawAsync("""
                    WITH ordered AS (
                      SELECT Id, ROW_NUMBER() OVER (PARTITION BY CapabilityId ORDER BY CreatedAt, Id) AS ordinal
                      FROM CspResponsibilitySourceEvents)
                    UPDATE CspResponsibilitySourceEvents SET Sequence =
                      (SELECT ordinal FROM ordered WHERE ordered.Id = CspResponsibilitySourceEvents.Id)
                    WHERE Sequence = 0;
                    """, ct);
            await db.Database.ExecuteSqlRawAsync(
                "DROP INDEX IF EXISTS IX_CspResponsibilitySourceEvents_CapabilityId_SourceRevision", ct);
        }
        finally
        {
            if (!alreadyOpen) await db.Database.CloseConnectionAsync();
        }
    }

    private static async Task<HashSet<string>> SqliteColumnsAsync(System.Data.Common.DbConnection connection, string query, CancellationToken ct)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = query;
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) columns.Add(reader.GetString(1));
        return columns;
    }

    private static async Task BackfillRoutingAsync(AtoCopilotContext db, ILogger logger, CancellationToken ct)
    {
        string? cursor = null;
        while (true)
        {
            var page = await (from subscription in db.CapabilitySubscriptions.AsNoTracking()
                join owner in db.RegisteredSystems on subscription.RegisteredSystemId equals owner.Id into owners
                from system in owners.DefaultIfEmpty()
                where cursor == null || string.Compare(subscription.Id, cursor) > 0
                orderby subscription.Id
                select new { subscription.Id, subscription.CspInheritedCapabilityId, TenantId = system == null ? Guid.Empty : system.TenantId })
                .Take(100).ToListAsync(ct);
            if (page.Count == 0) break;
            foreach (var item in page)
            {
                if (!Guid.TryParse(item.CspInheritedCapabilityId, out var capabilityId))
                    throw new InvalidDataException($"Subscription {item.Id} has an invalid provider routing identity.");
                var canonical = capabilityId.ToString();
                await db.CapabilitySubscriptions.Where(s => s.Id == item.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(s => s.RoutingTenantId, item.TenantId)
                        .SetProperty(s => s.RoutingCapabilityId, canonical), ct);
            }
            cursor = page[^1].Id;
        }
        while (true)
        {
            var page = await db.Set<CapabilityResponsibilityImpact>().AsNoTracking()
                .Where(i => i.AcknowledgedAt == null && !db.Set<CapabilityResponsibilityDelivery>().Any(d => d.ImpactId == i.Id))
                .OrderBy(i => i.Id).Take(100).Select(i => new { i.Id, i.TenantId, i.RegisteredSystemId }).ToListAsync(ct);
            if (page.Count == 0) break;
            foreach (var item in page)
                CapabilityResponsibilityRouting.StageImpact(db, item.Id, item.TenantId, item.RegisteredSystemId);
            await db.SaveChangesAsync(ct);
        }
        var missing = await db.CapabilitySubscriptions.CountAsync(s => s.IsActive && s.RoutingTenantId == Guid.Empty, ct);
        if (missing > 0) logger.LogWarning("{Count} active subscriptions lack a routable customer tenant; ownership repair is required", missing);
    }

    private const string SqliteScript = """
        CREATE TABLE IF NOT EXISTS CspResponsibilitySourceEvents (
          Id TEXT NOT NULL PRIMARY KEY, CapabilityId TEXT NOT NULL, ComponentId TEXT NOT NULL,
          CspProfileId TEXT NOT NULL, SourceRevision TEXT NOT NULL, Sequence INTEGER NOT NULL,
          Actor TEXT NULL, IsAvailable INTEGER NOT NULL, CreatedAt TEXT NOT NULL,
          LastSubscriptionId TEXT NULL, FanoutCompleted INTEGER NOT NULL DEFAULT 0,
          NextExpansionUtcTicks INTEGER NOT NULL DEFAULT 0, ExpansionRevision INTEGER NOT NULL DEFAULT 0);
        CREATE UNIQUE INDEX IF NOT EXISTS IX_CspResponsibilitySourceEvents_CapabilityId_Sequence
          ON CspResponsibilitySourceEvents(CapabilityId,Sequence);
        CREATE INDEX IF NOT EXISTS IX_CspResponsibilitySourceEvents_FanoutCompleted_NextExpansionUtcTicks_Id
          ON CspResponsibilitySourceEvents(FanoutCompleted,NextExpansionUtcTicks,Id);
        CREATE INDEX IF NOT EXISTS IX_CapabilitySubscription_Routing
          ON CapabilitySubscriptions(RoutingCapabilityId,IsActive,Id);
        CREATE TABLE IF NOT EXISTS CapabilityResponsibilityDeliveries (
          Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL, RegisteredSystemId TEXT NOT NULL,
          SourceEventId TEXT NULL, ImpactId TEXT NULL, SubscriptionId TEXT NULL,
          BaselineId TEXT NULL, ControlCursor TEXT NULL, Outcome TEXT NOT NULL,
          NextAttemptUtcTicks INTEGER NOT NULL, LeaseUntilUtcTicks INTEGER NOT NULL,
          LeaseToken TEXT NULL, Attempts INTEGER NOT NULL, Revision INTEGER NOT NULL, CompletedAt TEXT NULL);
        CREATE UNIQUE INDEX IF NOT EXISTS IX_CapabilityResponsibilityDeliveries_ImpactId
          ON CapabilityResponsibilityDeliveries(ImpactId) WHERE ImpactId IS NOT NULL;
        CREATE INDEX IF NOT EXISTS IX_CapabilityResponsibilityDeliveries_CompletedAt_NextAttemptUtcTicks_LeaseUntilUtcTicks_Id
          ON CapabilityResponsibilityDeliveries(CompletedAt,NextAttemptUtcTicks,LeaseUntilUtcTicks,Id);
        CREATE INDEX IF NOT EXISTS IX_CapabilityResponsibilityDeliveries_TenantId_RegisteredSystemId_CompletedAt
          ON CapabilityResponsibilityDeliveries(TenantId,RegisteredSystemId,CompletedAt);
        CREATE TABLE IF NOT EXISTS CapabilityResponsibilityConfirmations (
          Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL, RegisteredSystemId TEXT NOT NULL,
          SubscriptionId TEXT NOT NULL, ReviewedBaselineId TEXT NOT NULL, ControlId TEXT NOT NULL,
          SourceRevision TEXT NOT NULL, SourceSnapshotJson TEXT NOT NULL, InheritanceType TEXT NOT NULL,
          Provider TEXT NULL, CustomerResponsibility TEXT NULL, ConfirmedBy TEXT NOT NULL,
          ConfirmedAt TEXT NOT NULL, IsCurrent INTEGER NOT NULL,
          ProviderCoverageVerified INTEGER NULL, CustomerDutiesReviewed INTEGER NULL, ReviewNotes TEXT NULL,
          FOREIGN KEY(RegisteredSystemId) REFERENCES RegisteredSystems(Id));
        CREATE UNIQUE INDEX IF NOT EXISTS IX_CapabilityResponsibilityConfirmations_TenantId_RegisteredSystemId_SubscriptionId_ControlId
          ON CapabilityResponsibilityConfirmations(TenantId,RegisteredSystemId,SubscriptionId,ControlId) WHERE IsCurrent = 1;
        CREATE TABLE IF NOT EXISTS CapabilityResponsibilityProjections (
          Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL, RegisteredSystemId TEXT NOT NULL,
          ControlBaselineId TEXT NOT NULL, ControlId TEXT NOT NULL, ControlInheritanceId TEXT NULL,
          AppliedHash TEXT NULL, StateHash TEXT NOT NULL, Revision INTEGER NOT NULL,
          FOREIGN KEY(RegisteredSystemId) REFERENCES RegisteredSystems(Id));
        CREATE UNIQUE INDEX IF NOT EXISTS IX_CapabilityResponsibilityProjections_TenantId_RegisteredSystemId_ControlBaselineId_ControlId
          ON CapabilityResponsibilityProjections(TenantId,RegisteredSystemId,ControlBaselineId,ControlId);
        CREATE TABLE IF NOT EXISTS CapabilityResponsibilityImpacts (
          Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL, RegisteredSystemId TEXT NOT NULL,
          ControlBaselineId TEXT NOT NULL, ControlId TEXT NOT NULL, ProjectionId TEXT NOT NULL,
          Revision INTEGER NOT NULL, StateHash TEXT NOT NULL, Reason TEXT NOT NULL,
          SourcesJson TEXT NOT NULL, Actor TEXT NOT NULL, CreatedAt TEXT NOT NULL, AcknowledgedAt TEXT NULL,
          FOREIGN KEY(RegisteredSystemId) REFERENCES RegisteredSystems(Id));
        CREATE UNIQUE INDEX IF NOT EXISTS IX_CapabilityResponsibilityImpacts_ProjectionId_Revision
          ON CapabilityResponsibilityImpacts(ProjectionId,Revision);
        CREATE INDEX IF NOT EXISTS IX_CapabilityResponsibilityImpacts_TenantId_RegisteredSystemId_AcknowledgedAt
          ON CapabilityResponsibilityImpacts(TenantId,RegisteredSystemId,AcknowledgedAt);
        """;

    private const string SqlServerScript = """
        IF OBJECT_ID(N'dbo.CapabilitySubscriptions', N'U') IS NULL
        BEGIN
          CREATE TABLE dbo.CapabilitySubscriptions (
            Id NVARCHAR(36) NOT NULL PRIMARY KEY, RegisteredSystemId NVARCHAR(36) NOT NULL,
            CspInheritedCapabilityId NVARCHAR(36) NOT NULL, SubscribedBy NVARCHAR(254) NOT NULL DEFAULT 'dashboard-user',
            SubscribedAt DATETIME2 NOT NULL, IsActive BIT NOT NULL DEFAULT 1);
          CREATE INDEX IX_CapabilitySubscription_System_Capability
            ON dbo.CapabilitySubscriptions(RegisteredSystemId,CspInheritedCapabilityId);
        END;
        IF COL_LENGTH('dbo.CapabilitySubscriptions', 'RoutingTenantId') IS NULL
          ALTER TABLE dbo.CapabilitySubscriptions ADD RoutingTenantId UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT DF_CapabilitySubscriptions_RoutingTenant DEFAULT '00000000-0000-0000-0000-000000000000';
        IF COL_LENGTH('dbo.CapabilitySubscriptions', 'RoutingCapabilityId') IS NULL
          ALTER TABLE dbo.CapabilitySubscriptions ADD RoutingCapabilityId NVARCHAR(36) NOT NULL
            CONSTRAINT DF_CapabilitySubscriptions_RoutingCapability DEFAULT '';
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CapabilitySubscription_Routing'
          AND object_id = OBJECT_ID(N'dbo.CapabilitySubscriptions'))
          EXEC(N'CREATE INDEX IX_CapabilitySubscription_Routing ON dbo.CapabilitySubscriptions(RoutingCapabilityId,IsActive,Id)');
        IF OBJECT_ID(N'dbo.CapabilityResponsibilityDeliveries', N'U') IS NULL
        BEGIN
          CREATE TABLE dbo.CapabilityResponsibilityDeliveries (
            Id NVARCHAR(64) NOT NULL PRIMARY KEY, TenantId UNIQUEIDENTIFIER NOT NULL,
            RegisteredSystemId NVARCHAR(36) NOT NULL, SourceEventId UNIQUEIDENTIFIER NULL,
            ImpactId UNIQUEIDENTIFIER NULL, SubscriptionId NVARCHAR(36) NULL,
            BaselineId NVARCHAR(36) NULL, ControlCursor NVARCHAR(20) NULL, Outcome NVARCHAR(40) NOT NULL,
            NextAttemptUtcTicks BIGINT NOT NULL, LeaseUntilUtcTicks BIGINT NOT NULL, LeaseToken UNIQUEIDENTIFIER NULL,
            Attempts INT NOT NULL, Revision BIGINT NOT NULL, CompletedAt DATETIMEOFFSET NULL);
          CREATE UNIQUE INDEX IX_CapabilityResponsibilityDeliveries_ImpactId
            ON dbo.CapabilityResponsibilityDeliveries(ImpactId) WHERE ImpactId IS NOT NULL;
          CREATE INDEX IX_CapabilityResponsibilityDeliveries_CompletedAt_NextAttemptUtcTicks_LeaseUntilUtcTicks_Id
            ON dbo.CapabilityResponsibilityDeliveries(CompletedAt,NextAttemptUtcTicks,LeaseUntilUtcTicks,Id);
          CREATE INDEX IX_CapabilityResponsibilityDeliveries_TenantId_RegisteredSystemId_CompletedAt
            ON dbo.CapabilityResponsibilityDeliveries(TenantId,RegisteredSystemId,CompletedAt);
        END;
        IF OBJECT_ID(N'dbo.CspResponsibilitySourceEvents', N'U') IS NOT NULL
        BEGIN
          IF COL_LENGTH('dbo.CspResponsibilitySourceEvents', 'LastSubscriptionId') IS NULL
            ALTER TABLE dbo.CspResponsibilitySourceEvents ADD LastSubscriptionId NVARCHAR(36) NULL;
          IF COL_LENGTH('dbo.CspResponsibilitySourceEvents', 'FanoutCompleted') IS NULL
            ALTER TABLE dbo.CspResponsibilitySourceEvents ADD FanoutCompleted BIT NOT NULL
              CONSTRAINT DF_CspSourceEvent_FanoutCompleted DEFAULT 0;
          IF COL_LENGTH('dbo.CspResponsibilitySourceEvents', 'NextExpansionUtcTicks') IS NULL
            ALTER TABLE dbo.CspResponsibilitySourceEvents ADD NextExpansionUtcTicks BIGINT NOT NULL
              CONSTRAINT DF_CspSourceEvent_NextExpansion DEFAULT 0;
          IF COL_LENGTH('dbo.CspResponsibilitySourceEvents', 'ExpansionRevision') IS NULL
            ALTER TABLE dbo.CspResponsibilitySourceEvents ADD ExpansionRevision BIGINT NOT NULL
              CONSTRAINT DF_CspSourceEvent_ExpansionRevision DEFAULT 0;
          IF COL_LENGTH('dbo.CspResponsibilitySourceEvents', 'Actor') IS NULL
            ALTER TABLE dbo.CspResponsibilitySourceEvents ADD Actor NVARCHAR(200) NULL;
          IF COL_LENGTH('dbo.CspResponsibilitySourceEvents', 'Sequence') IS NULL
          BEGIN
            ALTER TABLE dbo.CspResponsibilitySourceEvents ADD [Sequence] BIGINT NOT NULL
              CONSTRAINT DF_CspResponsibilitySourceEvents_Sequence DEFAULT 0;
          END;
          EXEC(N'WITH ordered AS (SELECT Id, ROW_NUMBER() OVER (PARTITION BY CapabilityId ORDER BY CreatedAt, Id) AS ordinal
            FROM dbo.CspResponsibilitySourceEvents)
            UPDATE source SET [Sequence] = ordered.ordinal FROM dbo.CspResponsibilitySourceEvents source
            INNER JOIN ordered ON source.Id = ordered.Id WHERE source.[Sequence] = 0');
          IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CspResponsibilitySourceEvents_CapabilityId_SourceRevision'
            AND object_id = OBJECT_ID(N'dbo.CspResponsibilitySourceEvents'))
            DROP INDEX IX_CspResponsibilitySourceEvents_CapabilityId_SourceRevision ON dbo.CspResponsibilitySourceEvents;
          IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CspResponsibilitySourceEvents_CapabilityId_Sequence'
            AND object_id = OBJECT_ID(N'dbo.CspResponsibilitySourceEvents'))
            EXEC(N'CREATE UNIQUE INDEX IX_CspResponsibilitySourceEvents_CapabilityId_Sequence
              ON dbo.CspResponsibilitySourceEvents(CapabilityId,[Sequence])');
        END;
        IF OBJECT_ID(N'dbo.CspResponsibilitySourceEvents', N'U') IS NULL
        BEGIN
          CREATE TABLE dbo.CspResponsibilitySourceEvents (
            Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, CapabilityId UNIQUEIDENTIFIER NOT NULL,
            ComponentId UNIQUEIDENTIFIER NOT NULL, CspProfileId UNIQUEIDENTIFIER NOT NULL,
            SourceRevision NVARCHAR(64) NOT NULL, [Sequence] BIGINT NOT NULL, Actor NVARCHAR(200) NULL,
            IsAvailable BIT NOT NULL, CreatedAt DATETIMEOFFSET NOT NULL,
            LastSubscriptionId NVARCHAR(36) NULL, FanoutCompleted BIT NOT NULL DEFAULT 0,
            NextExpansionUtcTicks BIGINT NOT NULL DEFAULT 0, ExpansionRevision BIGINT NOT NULL DEFAULT 0);
          CREATE UNIQUE INDEX IX_CspResponsibilitySourceEvents_CapabilityId_Sequence
            ON dbo.CspResponsibilitySourceEvents(CapabilityId,Sequence);
        END;
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CspResponsibilitySourceEvents_FanoutCompleted_NextExpansionUtcTicks_Id'
          AND object_id = OBJECT_ID(N'dbo.CspResponsibilitySourceEvents'))
          EXEC(N'CREATE INDEX IX_CspResponsibilitySourceEvents_FanoutCompleted_NextExpansionUtcTicks_Id
            ON dbo.CspResponsibilitySourceEvents(FanoutCompleted,NextExpansionUtcTicks,Id)');
        IF OBJECT_ID(N'dbo.CapabilityResponsibilityConfirmations', N'U') IS NULL
        BEGIN
          CREATE TABLE dbo.CapabilityResponsibilityConfirmations (
            Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, TenantId UNIQUEIDENTIFIER NOT NULL,
            RegisteredSystemId NVARCHAR(36) NOT NULL, SubscriptionId NVARCHAR(36) NOT NULL,
            ReviewedBaselineId NVARCHAR(36) NOT NULL, ControlId NVARCHAR(20) NOT NULL,
            SourceRevision NVARCHAR(64) NOT NULL, SourceSnapshotJson NVARCHAR(MAX) NOT NULL,
            InheritanceType NVARCHAR(20) NOT NULL, Provider NVARCHAR(200) NULL,
            CustomerResponsibility NVARCHAR(2000) NULL, ConfirmedBy NVARCHAR(200) NOT NULL,
            ConfirmedAt DATETIMEOFFSET NOT NULL, IsCurrent BIT NOT NULL,
            FOREIGN KEY(RegisteredSystemId) REFERENCES dbo.RegisteredSystems(Id));
          CREATE UNIQUE INDEX IX_CapabilityResponsibilityConfirmations_TenantId_RegisteredSystemId_SubscriptionId_ControlId
            ON dbo.CapabilityResponsibilityConfirmations(TenantId,RegisteredSystemId,SubscriptionId,ControlId) WHERE IsCurrent = 1;
        END;
        IF COL_LENGTH(N'dbo.CapabilityResponsibilityConfirmations', N'ProviderCoverageVerified') IS NULL
          ALTER TABLE dbo.CapabilityResponsibilityConfirmations ADD ProviderCoverageVerified BIT NULL;
        IF COL_LENGTH(N'dbo.CapabilityResponsibilityConfirmations', N'CustomerDutiesReviewed') IS NULL
          ALTER TABLE dbo.CapabilityResponsibilityConfirmations ADD CustomerDutiesReviewed BIT NULL;
        IF COL_LENGTH(N'dbo.CapabilityResponsibilityConfirmations', N'ReviewNotes') IS NULL
          ALTER TABLE dbo.CapabilityResponsibilityConfirmations ADD ReviewNotes NVARCHAR(2000) NULL;
        IF OBJECT_ID(N'dbo.CapabilityResponsibilityProjections', N'U') IS NULL
        BEGIN
          CREATE TABLE dbo.CapabilityResponsibilityProjections (
            Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, TenantId UNIQUEIDENTIFIER NOT NULL,
            RegisteredSystemId NVARCHAR(36) NOT NULL, ControlBaselineId NVARCHAR(36) NOT NULL,
            ControlId NVARCHAR(20) NOT NULL, ControlInheritanceId NVARCHAR(36) NULL,
            AppliedHash NVARCHAR(64) NULL, StateHash NVARCHAR(64) NOT NULL, Revision BIGINT NOT NULL,
            FOREIGN KEY(RegisteredSystemId) REFERENCES dbo.RegisteredSystems(Id));
          CREATE UNIQUE INDEX IX_CapabilityResponsibilityProjections_TenantId_RegisteredSystemId_ControlBaselineId_ControlId
            ON dbo.CapabilityResponsibilityProjections(TenantId,RegisteredSystemId,ControlBaselineId,ControlId);
        END;
        IF OBJECT_ID(N'dbo.CapabilityResponsibilityImpacts', N'U') IS NULL
        BEGIN
          CREATE TABLE dbo.CapabilityResponsibilityImpacts (
            Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, TenantId UNIQUEIDENTIFIER NOT NULL,
            RegisteredSystemId NVARCHAR(36) NOT NULL, ControlBaselineId NVARCHAR(36) NOT NULL,
            ControlId NVARCHAR(20) NOT NULL, ProjectionId UNIQUEIDENTIFIER NOT NULL,
            Revision BIGINT NOT NULL, StateHash NVARCHAR(64) NOT NULL, Reason NVARCHAR(40) NOT NULL,
            SourcesJson NVARCHAR(MAX) NOT NULL, Actor NVARCHAR(200) NOT NULL,
            CreatedAt DATETIMEOFFSET NOT NULL, AcknowledgedAt DATETIMEOFFSET NULL,
            FOREIGN KEY(RegisteredSystemId) REFERENCES dbo.RegisteredSystems(Id));
          CREATE UNIQUE INDEX IX_CapabilityResponsibilityImpacts_ProjectionId_Revision
            ON dbo.CapabilityResponsibilityImpacts(ProjectionId,Revision);
          CREATE INDEX IX_CapabilityResponsibilityImpacts_TenantId_RegisteredSystemId_AcknowledgedAt
            ON dbo.CapabilityResponsibilityImpacts(TenantId,RegisteredSystemId,AcknowledgedAt);
        END;
        """;
}
