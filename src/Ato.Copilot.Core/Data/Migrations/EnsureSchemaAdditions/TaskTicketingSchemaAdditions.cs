using Ato.Copilot.Core.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

public static class TaskTicketingSchemaAdditions
{
    public static async Task ApplyAsync(AtoCopilotContext db, ILogger logger, CancellationToken cancellationToken = default)
    {
        var sqlServer = db.Database.IsSqlServer();
        if (!sqlServer && !db.Database.IsSqlite())
        {
            if (db.Database.ProviderName?.Contains("InMemory", StringComparison.Ordinal) == true) return;
            throw new InvalidOperationException("Task ticketing schema requires SQL Server or SQLite.");
        }
        await db.Database.ExecuteSqlRawAsync(sqlServer ? SqlServer : Sqlite, cancellationToken);
        logger.LogInformation("Verified task-owned ticketing schema");
    }

    private const string SqlServer = """
        IF OBJECT_ID(N'dbo.TaskTicketLinks', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.TaskTicketLinks (
                Id nvarchar(36) NOT NULL PRIMARY KEY, TenantId uniqueidentifier NOT NULL,
                RegisteredSystemId nvarchar(36) NOT NULL, TaskId nvarchar(36) NOT NULL,
                TicketingIntegrationId nvarchar(36) NOT NULL, Provider nvarchar(30) NOT NULL,
                BaseUrl nvarchar(500) NOT NULL, ProjectKey nvarchar(200) NOT NULL,
                CorrelationKey nvarchar(100) NOT NULL, ExternalRef nvarchar(200) NULL,
                ExternalStatus nvarchar(100) NULL, ExternalAssignee nvarchar(200) NULL,
                State nvarchar(30) NOT NULL, LastError nvarchar(1000) NULL,
                CreateAttempted bit NOT NULL, LastSuccessfulSyncAt datetime2 NULL,
                CreatedAt datetime2 NOT NULL, RowVersion uniqueidentifier NOT NULL
            );
        END;
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TaskTicketLinks_TenantId_RegisteredSystemId_TaskId' AND object_id = OBJECT_ID(N'dbo.TaskTicketLinks'))
            CREATE UNIQUE INDEX IX_TaskTicketLinks_TenantId_RegisteredSystemId_TaskId ON dbo.TaskTicketLinks(TenantId, RegisteredSystemId, TaskId);
        IF OBJECT_ID(N'dbo.TaskTicketAudits', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.TaskTicketAudits (
                Id nvarchar(36) NOT NULL PRIMARY KEY, TenantId uniqueidentifier NOT NULL,
                TaskTicketLinkId nvarchar(36) NOT NULL, ActorId nvarchar(36) NOT NULL,
                Action nvarchar(30) NOT NULL, ExternalRef nvarchar(200) NULL, CreatedAt datetime2 NOT NULL
            );
        END;
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TaskTicketAudits_TenantId_TaskTicketLinkId' AND object_id = OBJECT_ID(N'dbo.TaskTicketAudits'))
            CREATE INDEX IX_TaskTicketAudits_TenantId_TaskTicketLinkId ON dbo.TaskTicketAudits(TenantId, TaskTicketLinkId);
        """;

    private const string Sqlite = """
        CREATE TABLE IF NOT EXISTS "TaskTicketLinks" (
            "Id" TEXT NOT NULL PRIMARY KEY, "TenantId" TEXT NOT NULL,
            "RegisteredSystemId" TEXT NOT NULL, "TaskId" TEXT NOT NULL,
            "TicketingIntegrationId" TEXT NOT NULL, "Provider" TEXT NOT NULL,
            "BaseUrl" TEXT NOT NULL, "ProjectKey" TEXT NOT NULL,
            "CorrelationKey" TEXT NOT NULL, "ExternalRef" TEXT NULL,
            "ExternalStatus" TEXT NULL, "ExternalAssignee" TEXT NULL,
            "State" TEXT NOT NULL, "LastError" TEXT NULL,
            "CreateAttempted" INTEGER NOT NULL, "LastSuccessfulSyncAt" TEXT NULL,
            "CreatedAt" TEXT NOT NULL, "RowVersion" TEXT NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_TaskTicketLinks_TenantId_RegisteredSystemId_TaskId"
            ON "TaskTicketLinks" ("TenantId", "RegisteredSystemId", "TaskId");
        CREATE TABLE IF NOT EXISTS "TaskTicketAudits" (
            "Id" TEXT NOT NULL PRIMARY KEY, "TenantId" TEXT NOT NULL,
            "TaskTicketLinkId" TEXT NOT NULL, "ActorId" TEXT NOT NULL,
            "Action" TEXT NOT NULL, "ExternalRef" TEXT NULL, "CreatedAt" TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS "IX_TaskTicketAudits_TenantId_TaskTicketLinkId"
            ON "TaskTicketAudits" ("TenantId", "TaskTicketLinkId");
        """;
}
