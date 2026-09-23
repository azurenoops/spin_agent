using Ato.Copilot.Core.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

public static class NarrativeLibrarySchemaAdditions
{
    public static async Task ApplyAsync(AtoCopilotContext db, CancellationToken cancellationToken = default)
    {
        if (db.Database.IsSqlite())
        {
            await db.Database.ExecuteSqlRawAsync(SqliteScript, cancellationToken);
            await AllowOrganizationOriginsSqliteAsync(db, cancellationToken);
            var columns = await db.Database.SqlQueryRaw<string>(
                """SELECT name AS Value FROM pragma_table_info('NarrativeProposals')""").ToListAsync(cancellationToken);
            foreach (var (name, sql) in SqliteImpactColumns)
                if (!columns.Contains(name)) await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
            var receiptColumns = await db.Database.SqlQueryRaw<string>(
                """SELECT name AS Value FROM pragma_table_info('NarrativeImpactReceipts')""").ToListAsync(cancellationToken);
            foreach (var (name, sql) in SqliteReceiptColumns)
                if (!receiptColumns.Contains(name)) await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }
        else if (db.Database.IsSqlServer())
        {
            await db.Database.ExecuteSqlRawAsync(SqlServerScript, cancellationToken);
            await db.Database.ExecuteSqlRawAsync(SqlServerImpactColumns, cancellationToken);
        }
        else if (db.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory")
            throw new NotSupportedException("Narrative Library requires SQLite or SQL Server.");
    }

    private static async Task AllowOrganizationOriginsSqliteAsync(AtoCopilotContext db, CancellationToken ct)
    {
        var required = await db.Database.SqlQueryRaw<int>(
            """SELECT "notnull" AS Value FROM pragma_table_info('NarrativeReferences') WHERE name = 'ImportedForSystemId'""").SingleAsync(ct);
        if (required == 0) return;
        var unexpected = await db.Database.SqlQueryRaw<string>("""
            SELECT name AS Value FROM pragma_table_info('NarrativeReferences')
            WHERE name NOT IN ('Id','TenantId','ReferenceKey','Title','Scope','ScopeId','ImportedForSystemId',
              'SourceName','SourceSha256','OriginalPassagesJson','PassagesJson','Version','Revision','IsPublished',
              'CreatedAt','CreatedBy','PublishedAt','PublishedBy')
            UNION ALL
            SELECT name AS Value FROM sqlite_master WHERE tbl_name = 'NarrativeReferences'
              AND (type = 'trigger' OR (type = 'index' AND sql IS NOT NULL
                AND name NOT IN ('IX_NarrativeReferences_TenantId_ReferenceKey_Version',
                  'IX_NarrativeReferences_TenantId_Scope_ScopeId_Title_Version')));
            """).ToListAsync(ct);
        if (unexpected.Count > 0)
            throw new InvalidOperationException("Narrative reference origin upgrade requires review of unmanaged columns, indexes or triggers.");
        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.Database.ExecuteSqlRawAsync(SqliteOrganizationOrigins, ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
    }

    private const string SqliteOrganizationOrigins = """
        CREATE TABLE "NarrativeReferences_StandaloneLibraryUpgrade" (
            "Id" TEXT NOT NULL PRIMARY KEY, "TenantId" TEXT NOT NULL, "ReferenceKey" TEXT NOT NULL,
            "Title" TEXT NOT NULL, "Scope" TEXT NOT NULL, "ScopeId" TEXT NOT NULL,
            "ImportedForSystemId" TEXT NULL, "SourceName" TEXT NOT NULL, "SourceSha256" TEXT NOT NULL,
            "OriginalPassagesJson" TEXT NOT NULL, "PassagesJson" TEXT NOT NULL,
            "Version" INTEGER NOT NULL, "Revision" INTEGER NOT NULL, "IsPublished" INTEGER NOT NULL,
            "CreatedAt" TEXT NOT NULL, "CreatedBy" TEXT NOT NULL, "PublishedAt" TEXT NULL, "PublishedBy" TEXT NULL
        );
        INSERT INTO "NarrativeReferences_StandaloneLibraryUpgrade"
            ("Id","TenantId","ReferenceKey","Title","Scope","ScopeId","ImportedForSystemId","SourceName","SourceSha256",
             "OriginalPassagesJson","PassagesJson","Version","Revision","IsPublished","CreatedAt","CreatedBy","PublishedAt","PublishedBy")
            SELECT "Id","TenantId","ReferenceKey","Title","Scope","ScopeId","ImportedForSystemId","SourceName","SourceSha256",
             "OriginalPassagesJson","PassagesJson","Version","Revision","IsPublished","CreatedAt","CreatedBy","PublishedAt","PublishedBy"
            FROM "NarrativeReferences";
        DROP TABLE "NarrativeReferences";
        ALTER TABLE "NarrativeReferences_StandaloneLibraryUpgrade" RENAME TO "NarrativeReferences";
        CREATE UNIQUE INDEX "IX_NarrativeReferences_TenantId_ReferenceKey_Version"
            ON "NarrativeReferences" ("TenantId","ReferenceKey","Version");
        CREATE UNIQUE INDEX "IX_NarrativeReferences_TenantId_Scope_ScopeId_Title_Version"
            ON "NarrativeReferences" ("TenantId","Scope","ScopeId","Title","Version");
        """;

    private static readonly (string Name, string Sql)[] SqliteImpactColumns =
    [
        ("ChangeSourceKind", """ALTER TABLE "NarrativeProposals" ADD COLUMN "ChangeSourceKind" TEXT NULL"""),
        ("ChangeSourceId", """ALTER TABLE "NarrativeProposals" ADD COLUMN "ChangeSourceId" TEXT NULL"""),
        ("GenerationErrorCode", """ALTER TABLE "NarrativeProposals" ADD COLUMN "GenerationErrorCode" TEXT NULL""")
    ];

    private static readonly (string Name, string Sql)[] SqliteReceiptColumns =
    [
        ("SourceContextJson", """ALTER TABLE "NarrativeImpactReceipts" ADD COLUMN "SourceContextJson" TEXT NULL"""),
        ("SourceKind", """ALTER TABLE "NarrativeImpactReceipts" ADD COLUMN "SourceKind" TEXT NULL"""),
        ("SourceId", """ALTER TABLE "NarrativeImpactReceipts" ADD COLUMN "SourceId" TEXT NULL"""),
        ("SourceActor", """ALTER TABLE "NarrativeImpactReceipts" ADD COLUMN "SourceActor" TEXT NULL""")
    ];

    private const string SqlServerImpactColumns = """
        IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.NarrativeReferences')
            AND name = N'ImportedForSystemId' AND is_nullable = 0)
            ALTER TABLE [dbo].[NarrativeReferences] ALTER COLUMN [ImportedForSystemId] nvarchar(36) NULL;
        IF COL_LENGTH(N'dbo.NarrativeProposals', N'ChangeSourceKind') IS NULL
            ALTER TABLE [dbo].[NarrativeProposals] ADD [ChangeSourceKind] nvarchar(32) NULL;
        IF COL_LENGTH(N'dbo.NarrativeProposals', N'ChangeSourceId') IS NULL
            ALTER TABLE [dbo].[NarrativeProposals] ADD [ChangeSourceId] nvarchar(200) NULL;
        IF COL_LENGTH(N'dbo.NarrativeProposals', N'GenerationErrorCode') IS NULL
            ALTER TABLE [dbo].[NarrativeProposals] ADD [GenerationErrorCode] nvarchar(64) NULL;
        IF COL_LENGTH(N'dbo.NarrativeImpactReceipts', N'SourceContextJson') IS NULL
            ALTER TABLE [dbo].[NarrativeImpactReceipts] ADD [SourceContextJson] nvarchar(max) NULL;
        IF COL_LENGTH(N'dbo.NarrativeImpactReceipts', N'SourceKind') IS NULL
            ALTER TABLE [dbo].[NarrativeImpactReceipts] ADD [SourceKind] nvarchar(32) NULL;
        IF COL_LENGTH(N'dbo.NarrativeImpactReceipts', N'SourceId') IS NULL
            ALTER TABLE [dbo].[NarrativeImpactReceipts] ADD [SourceId] nvarchar(200) NULL;
        IF COL_LENGTH(N'dbo.NarrativeImpactReceipts', N'SourceActor') IS NULL
            ALTER TABLE [dbo].[NarrativeImpactReceipts] ADD [SourceActor] nvarchar(200) NULL;
        """;

    private const string SqliteScript = """
        CREATE TABLE IF NOT EXISTS "NarrativeReferences" (
            "Id" TEXT NOT NULL PRIMARY KEY, "TenantId" TEXT NOT NULL, "ReferenceKey" TEXT NOT NULL,
            "Title" TEXT NOT NULL, "Scope" TEXT NOT NULL, "ScopeId" TEXT NOT NULL,
            "ImportedForSystemId" TEXT NULL, "SourceName" TEXT NOT NULL, "SourceSha256" TEXT NOT NULL,
            "OriginalPassagesJson" TEXT NOT NULL, "PassagesJson" TEXT NOT NULL,
            "Version" INTEGER NOT NULL, "Revision" INTEGER NOT NULL, "IsPublished" INTEGER NOT NULL,
            "CreatedAt" TEXT NOT NULL, "CreatedBy" TEXT NOT NULL, "PublishedAt" TEXT NULL, "PublishedBy" TEXT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_NarrativeReferences_TenantId_ReferenceKey_Version"
            ON "NarrativeReferences" ("TenantId", "ReferenceKey", "Version");
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_NarrativeReferences_TenantId_Scope_ScopeId_Title_Version"
            ON "NarrativeReferences" ("TenantId", "Scope", "ScopeId", "Title", "Version");
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
        CREATE TABLE IF NOT EXISTS "NarrativeImpactReceipts" (
            "Id" TEXT NOT NULL PRIMARY KEY, "TenantId" TEXT NOT NULL, "ImpactId" TEXT NOT NULL,
            "RegisteredSystemId" TEXT NOT NULL, "ControlId" TEXT NOT NULL, "NarrativeType" TEXT NOT NULL,
            "NarrativeProposalId" TEXT NOT NULL, "RecordedAt" TEXT NOT NULL, "SourceContextJson" TEXT NULL,
            FOREIGN KEY ("NarrativeProposalId") REFERENCES "NarrativeProposals" ("Id") ON DELETE RESTRICT
        );
        CREATE INDEX IF NOT EXISTS "IX_NarrativeImpactReceipts_NarrativeProposalId"
            ON "NarrativeImpactReceipts" ("NarrativeProposalId");
        CREATE TABLE IF NOT EXISTS "ProviderNarrativeReferences" (
            "Id" TEXT NOT NULL PRIMARY KEY, "CspProfileId" TEXT NOT NULL, "ReferenceKey" TEXT NOT NULL,
            "Title" TEXT NOT NULL, "Scope" TEXT NOT NULL, "ScopeId" TEXT NOT NULL,
            "SourceName" TEXT NOT NULL, "SourceSha256" TEXT NOT NULL,
            "OriginalPassagesJson" TEXT NOT NULL, "PassagesJson" TEXT NOT NULL,
            "Version" INTEGER NOT NULL, "Revision" INTEGER NOT NULL, "IsPublished" INTEGER NOT NULL,
            "CreatedAt" TEXT NOT NULL, "CreatedBy" TEXT NOT NULL, "PublishedAt" TEXT NULL, "PublishedBy" TEXT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_ProviderNarrativeReferences_CspProfileId_ReferenceKey_Version"
            ON "ProviderNarrativeReferences" ("CspProfileId","ReferenceKey","Version");
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_ProviderNarrativeReferences_CspProfileId_Scope_ScopeId_Title_Version"
            ON "ProviderNarrativeReferences" ("CspProfileId","Scope","ScopeId","Title","Version");
        CREATE TABLE IF NOT EXISTS "NarrativeReferencePublications" (
            "Id" TEXT NOT NULL PRIMARY KEY, "TenantId" TEXT NOT NULL, "ReferenceId" TEXT NOT NULL,
            "SourceRevision" TEXT NOT NULL, "PayloadJson" TEXT NOT NULL, "RecordedBy" TEXT NOT NULL,
            "RecordedAt" TEXT NOT NULL, "Status" TEXT NOT NULL, "Revision" INTEGER NOT NULL,
            "DeliveredAt" TEXT NULL, "DeliveryErrorCode" TEXT NULL,
            FOREIGN KEY ("ReferenceId") REFERENCES "NarrativeReferences" ("Id") ON DELETE RESTRICT
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_NarrativeReferencePublications_ReferenceId"
            ON "NarrativeReferencePublications" ("ReferenceId");
        CREATE TABLE IF NOT EXISTS "ProviderNarrativeReferencePublications" (
            "Id" TEXT NOT NULL PRIMARY KEY, "CspProfileId" TEXT NOT NULL, "ReferenceId" TEXT NOT NULL,
            "SourceRevision" TEXT NOT NULL, "PayloadJson" TEXT NOT NULL, "RecordedBy" TEXT NOT NULL,
            "RecordedAt" TEXT NOT NULL, "Status" TEXT NOT NULL, "Revision" INTEGER NOT NULL,
            "DeliveredAt" TEXT NULL, "DeliveryErrorCode" TEXT NULL,
            FOREIGN KEY ("ReferenceId") REFERENCES "ProviderNarrativeReferences" ("Id") ON DELETE RESTRICT
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_ProviderNarrativeReferencePublications_ReferenceId"
            ON "ProviderNarrativeReferencePublications" ("ReferenceId");
        """;

    private const string SqlServerScript = """
        IF OBJECT_ID(N'[dbo].[NarrativeReferences]', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[NarrativeReferences] (
                [Id] uniqueidentifier NOT NULL PRIMARY KEY, [TenantId] uniqueidentifier NOT NULL,
                [ReferenceKey] uniqueidentifier NOT NULL, [Title] nvarchar(200) NOT NULL,
                [Scope] nvarchar(20) NOT NULL, [ScopeId] nvarchar(36) NOT NULL,
                [ImportedForSystemId] nvarchar(36) NULL, [SourceName] nvarchar(200) NOT NULL,
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
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_NarrativeReferences_TenantId_Scope_ScopeId_Title_Version'
            AND object_id = OBJECT_ID(N'[dbo].[NarrativeReferences]'))
            CREATE UNIQUE INDEX [IX_NarrativeReferences_TenantId_Scope_ScopeId_Title_Version]
                ON [dbo].[NarrativeReferences] ([TenantId], [Scope], [ScopeId], [Title], [Version]);
        IF OBJECT_ID(N'[dbo].[NarrativeProposals]', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[NarrativeProposals] (
                [Id] uniqueidentifier NOT NULL PRIMARY KEY, [TenantId] uniqueidentifier NOT NULL,
                [RegisteredSystemId] nvarchar(36) NOT NULL, [ControlId] nvarchar(20) NOT NULL,
                [NarrativeType] nvarchar(20) NOT NULL, [BaseVersion] int NOT NULL,
                [BeforeContent] nvarchar(max) NOT NULL, [ProposedContent] nvarchar(max) NOT NULL,
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
        IF OBJECT_ID(N'[dbo].[NarrativeImpactReceipts]', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[NarrativeImpactReceipts] (
                [Id] nvarchar(64) NOT NULL PRIMARY KEY, [TenantId] uniqueidentifier NOT NULL,
                [ImpactId] nvarchar(128) NOT NULL, [RegisteredSystemId] nvarchar(36) NOT NULL,
                [ControlId] nvarchar(20) NOT NULL, [NarrativeType] nvarchar(20) NOT NULL,
                [NarrativeProposalId] uniqueidentifier NOT NULL, [RecordedAt] datetime2 NOT NULL, [SourceContextJson] nvarchar(max) NULL,
                CONSTRAINT [FK_NarrativeImpactReceipts_NarrativeProposals_NarrativeProposalId]
                    FOREIGN KEY ([NarrativeProposalId]) REFERENCES [dbo].[NarrativeProposals] ([Id])
            );
        END;
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_NarrativeImpactReceipts_NarrativeProposalId'
            AND object_id = OBJECT_ID(N'[dbo].[NarrativeImpactReceipts]'))
            CREATE INDEX [IX_NarrativeImpactReceipts_NarrativeProposalId]
                ON [dbo].[NarrativeImpactReceipts] ([NarrativeProposalId]);
        IF OBJECT_ID(N'[dbo].[ProviderNarrativeReferences]', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[ProviderNarrativeReferences] (
                [Id] uniqueidentifier NOT NULL PRIMARY KEY, [CspProfileId] uniqueidentifier NOT NULL,
                [ReferenceKey] uniqueidentifier NOT NULL, [Title] nvarchar(200) NOT NULL,
                [Scope] nvarchar(20) NOT NULL, [ScopeId] uniqueidentifier NOT NULL,
                [SourceName] nvarchar(200) NOT NULL, [SourceSha256] nvarchar(64) NOT NULL,
                [OriginalPassagesJson] nvarchar(max) NOT NULL, [PassagesJson] nvarchar(max) NOT NULL,
                [Version] int NOT NULL, [Revision] int NOT NULL, [IsPublished] bit NOT NULL,
                [CreatedAt] datetime2 NOT NULL, [CreatedBy] nvarchar(200) NOT NULL,
                [PublishedAt] datetime2 NULL, [PublishedBy] nvarchar(200) NULL
            );
        END;
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProviderNarrativeReferences_CspProfileId_ReferenceKey_Version'
            AND object_id = OBJECT_ID(N'[dbo].[ProviderNarrativeReferences]'))
            CREATE UNIQUE INDEX [IX_ProviderNarrativeReferences_CspProfileId_ReferenceKey_Version]
                ON [dbo].[ProviderNarrativeReferences] ([CspProfileId],[ReferenceKey],[Version]);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProviderNarrativeReferences_CspProfileId_Scope_ScopeId_Title_Version'
            AND object_id = OBJECT_ID(N'[dbo].[ProviderNarrativeReferences]'))
            CREATE UNIQUE INDEX [IX_ProviderNarrativeReferences_CspProfileId_Scope_ScopeId_Title_Version]
                ON [dbo].[ProviderNarrativeReferences] ([CspProfileId],[Scope],[ScopeId],[Title],[Version]);
        IF OBJECT_ID(N'[dbo].[NarrativeReferencePublications]', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[NarrativeReferencePublications] (
                [Id] uniqueidentifier NOT NULL PRIMARY KEY, [TenantId] uniqueidentifier NOT NULL,
                [ReferenceId] uniqueidentifier NOT NULL, [SourceRevision] nvarchar(64) NOT NULL,
                [PayloadJson] nvarchar(max) NOT NULL, [RecordedBy] nvarchar(200) NOT NULL, [RecordedAt] datetime2 NOT NULL,
                [Status] nvarchar(20) NOT NULL, [Revision] int NOT NULL, [DeliveredAt] datetime2 NULL,
                [DeliveryErrorCode] nvarchar(64) NULL,
                CONSTRAINT [FK_NarrativeReferencePublications_NarrativeReferences_ReferenceId]
                    FOREIGN KEY ([ReferenceId]) REFERENCES [dbo].[NarrativeReferences] ([Id])
            );
        END;
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_NarrativeReferencePublications_ReferenceId'
            AND object_id = OBJECT_ID(N'[dbo].[NarrativeReferencePublications]'))
            CREATE UNIQUE INDEX [IX_NarrativeReferencePublications_ReferenceId]
                ON [dbo].[NarrativeReferencePublications] ([ReferenceId]);
        IF OBJECT_ID(N'[dbo].[ProviderNarrativeReferencePublications]', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[ProviderNarrativeReferencePublications] (
                [Id] uniqueidentifier NOT NULL PRIMARY KEY, [CspProfileId] uniqueidentifier NOT NULL,
                [ReferenceId] uniqueidentifier NOT NULL, [SourceRevision] nvarchar(64) NOT NULL,
                [PayloadJson] nvarchar(max) NOT NULL, [RecordedBy] nvarchar(200) NOT NULL, [RecordedAt] datetime2 NOT NULL,
                [Status] nvarchar(20) NOT NULL, [Revision] int NOT NULL, [DeliveredAt] datetime2 NULL,
                [DeliveryErrorCode] nvarchar(64) NULL,
                CONSTRAINT [FK_ProviderNarrativeReferencePublications_ProviderNarrativeReferences_ReferenceId]
                    FOREIGN KEY ([ReferenceId]) REFERENCES [dbo].[ProviderNarrativeReferences] ([Id])
            );
        END;
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProviderNarrativeReferencePublications_ReferenceId'
            AND object_id = OBJECT_ID(N'[dbo].[ProviderNarrativeReferencePublications]'))
            CREATE UNIQUE INDEX [IX_ProviderNarrativeReferencePublications_ReferenceId]
                ON [dbo].[ProviderNarrativeReferencePublications] ([ReferenceId]);
        """;
}