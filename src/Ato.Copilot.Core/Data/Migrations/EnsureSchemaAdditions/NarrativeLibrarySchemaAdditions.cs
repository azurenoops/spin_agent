using Ato.Copilot.Core.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

public static class NarrativeLibrarySchemaAdditions
{
    public static Task ApplyAsync(AtoCopilotContext db, CancellationToken cancellationToken = default)
    {
        if (db.Database.IsSqlite()) return db.Database.ExecuteSqlRawAsync(SqliteScript, cancellationToken);
        if (db.Database.IsSqlServer()) return db.Database.ExecuteSqlRawAsync(SqlServerScript, cancellationToken);
        if (db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory") return Task.CompletedTask;
        throw new NotSupportedException("Narrative Library requires SQLite or SQL Server.");
    }

    private const string SqliteScript = """
        CREATE TABLE IF NOT EXISTS "NarrativeReferences" (
            "Id" TEXT NOT NULL PRIMARY KEY, "TenantId" TEXT NOT NULL, "ReferenceKey" TEXT NOT NULL,
            "Title" TEXT NOT NULL, "Scope" TEXT NOT NULL, "ScopeId" TEXT NOT NULL,
            "ImportedForSystemId" TEXT NOT NULL, "SourceName" TEXT NOT NULL, "SourceSha256" TEXT NOT NULL,
            "OriginalPassagesJson" TEXT NOT NULL, "PassagesJson" TEXT NOT NULL,
            "Version" INTEGER NOT NULL, "Revision" INTEGER NOT NULL, "IsPublished" INTEGER NOT NULL,
            "CreatedAt" TEXT NOT NULL, "CreatedBy" TEXT NOT NULL, "PublishedAt" TEXT NULL, "PublishedBy" TEXT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_NarrativeReferences_TenantId_ReferenceKey_Version"
            ON "NarrativeReferences" ("TenantId", "ReferenceKey", "Version");
        CREATE TABLE IF NOT EXISTS "NarrativeProposals" (
            "Id" TEXT NOT NULL PRIMARY KEY, "TenantId" TEXT NOT NULL, "RegisteredSystemId" TEXT NOT NULL,
            "ControlId" TEXT NOT NULL, "NarrativeType" TEXT NOT NULL, "BaseVersion" INTEGER NOT NULL,
            "BeforeContent" TEXT NOT NULL, "ProposedContent" TEXT NOT NULL, "StateHash" TEXT NOT NULL,
            "DeduplicationKey" TEXT NOT NULL, "ProvenanceJson" TEXT NOT NULL, "ConflictsJson" TEXT NOT NULL,
            "MissingEvidenceJson" TEXT NOT NULL, "Status" TEXT NOT NULL, "Revision" INTEGER NOT NULL,
            "CreatedAt" TEXT NOT NULL, "CreatedBy" TEXT NOT NULL, "ReviewedAt" TEXT NULL,
            "ReviewedBy" TEXT NULL, "ReviewNote" TEXT NULL, "AcceptedVersion" INTEGER NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_NarrativeProposals_TenantId_DeduplicationKey"
            ON "NarrativeProposals" ("TenantId", "DeduplicationKey");
        """;

    private const string SqlServerScript = """
        IF OBJECT_ID(N'[dbo].[NarrativeReferences]', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[NarrativeReferences] (
                [Id] uniqueidentifier NOT NULL PRIMARY KEY, [TenantId] uniqueidentifier NOT NULL,
                [ReferenceKey] uniqueidentifier NOT NULL, [Title] nvarchar(200) NOT NULL,
                [Scope] nvarchar(20) NOT NULL, [ScopeId] nvarchar(36) NOT NULL,
                [ImportedForSystemId] nvarchar(36) NOT NULL, [SourceName] nvarchar(200) NOT NULL,
                [SourceSha256] nvarchar(64) NOT NULL, [OriginalPassagesJson] nvarchar(max) NOT NULL,
                [PassagesJson] nvarchar(max) NOT NULL, [Version] int NOT NULL, [Revision] int NOT NULL,
                [IsPublished] bit NOT NULL, [CreatedAt] datetime2 NOT NULL, [CreatedBy] nvarchar(200) NOT NULL,
                [PublishedAt] datetime2 NULL, [PublishedBy] nvarchar(200) NULL
            );
        END;
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_NarrativeReferences_TenantId_ReferenceKey_Version'
            AND object_id = OBJECT_ID(N'[dbo].[NarrativeReferences]'))
            CREATE UNIQUE INDEX [IX_NarrativeReferences_TenantId_ReferenceKey_Version]
                ON [dbo].[NarrativeReferences] ([TenantId], [ReferenceKey], [Version]);
        IF OBJECT_ID(N'[dbo].[NarrativeProposals]', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[NarrativeProposals] (
                [Id] uniqueidentifier NOT NULL PRIMARY KEY, [TenantId] uniqueidentifier NOT NULL,
                [RegisteredSystemId] nvarchar(36) NOT NULL, [ControlId] nvarchar(20) NOT NULL,
                [NarrativeType] nvarchar(20) NOT NULL, [BaseVersion] int NOT NULL,
                [BeforeContent] nvarchar(8000) NOT NULL, [ProposedContent] nvarchar(8000) NOT NULL,
                [StateHash] nvarchar(64) NOT NULL, [DeduplicationKey] nvarchar(64) NOT NULL,
                [ProvenanceJson] nvarchar(max) NOT NULL, [ConflictsJson] nvarchar(max) NOT NULL,
                [MissingEvidenceJson] nvarchar(max) NOT NULL, [Status] nvarchar(20) NOT NULL,
                [Revision] int NOT NULL, [CreatedAt] datetime2 NOT NULL, [CreatedBy] nvarchar(200) NOT NULL,
                [ReviewedAt] datetime2 NULL, [ReviewedBy] nvarchar(200) NULL,
                [ReviewNote] nvarchar(2000) NULL, [AcceptedVersion] int NULL
            );
        END;
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_NarrativeProposals_TenantId_DeduplicationKey'
            AND object_id = OBJECT_ID(N'[dbo].[NarrativeProposals]'))
            CREATE UNIQUE INDEX [IX_NarrativeProposals_TenantId_DeduplicationKey]
                ON [dbo].[NarrativeProposals] ([TenantId], [DeduplicationKey]);
        """;
}