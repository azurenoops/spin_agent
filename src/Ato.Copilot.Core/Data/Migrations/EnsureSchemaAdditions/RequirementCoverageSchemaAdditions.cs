using Ato.Copilot.Core.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

/// <summary>Additive, rerunnable schema for source bindings and pending enhancement additions.</summary>
public static class RequirementCoverageSchemaAdditions
{
    public static async Task ApplyAsync(AtoCopilotContext db, CancellationToken ct = default)
    {
        if (db.Database.IsSqlite())
        {
            await db.Database.ExecuteSqlRawAsync(SqliteTables, ct);
            foreach (var (table, column, definition) in Columns)
            {
                var query = $"SELECT name AS Value FROM pragma_table_info('{table}')";
                var columns = await db.Database.SqlQueryRaw<string>(query).ToListAsync(ct);
                if (!columns.Contains(column))
                {
                    var sql = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {definition}";
                    await db.Database.ExecuteSqlRawAsync(sql, ct);
                }
            }
        }
        else if (db.Database.IsSqlServer())
        {
            await db.Database.ExecuteSqlRawAsync(SqlServerColumns, ct);
            await db.Database.ExecuteSqlRawAsync(SqlServerTables, ct);
            await db.Database.ExecuteSqlRawAsync("""
                IF COL_LENGTH('RequirementEnhancementProposals','ExistingNarrativeVersion') IS NULL
                    ALTER TABLE RequirementEnhancementProposals ADD ExistingNarrativeVersion int NULL;
                IF COL_LENGTH('RequirementEnhancementProposals','ExistingNarrativeHash') IS NULL
                    ALTER TABLE RequirementEnhancementProposals ADD ExistingNarrativeHash nvarchar(64) NULL;
                """, ct);
        }
        else if (db.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory")
            throw new NotSupportedException("Requirement coverage requires SQLite or SQL Server.");
    }

    private static readonly (string Table, string Column, string Definition)[] Columns =
    [
        ("ComplianceFrameworks", "RequirementCatalogJson", "TEXT NULL"),
        ("ComplianceFrameworks", "RequirementCatalogVersion", "TEXT NULL"),
        ("ComplianceFrameworks", "RequirementCatalogSourceUri", "TEXT NULL"),
        ("ComplianceFrameworks", "RequirementCatalogCapturedAt", "TEXT NULL"),
        ("ControlBaselines", "CoverageRevision", "INTEGER NOT NULL DEFAULT 0"),
        ("ControlBaselines", "SourceFrameworkIdentifier", "TEXT NULL"),
        ("ControlBaselines", "CatalogResolutionMessage", "TEXT NULL"),
        ("ControlBaselines", "RequirementCatalogBindingId", "TEXT NULL"),
        ("ControlImplementations", "RequirementCoverageJson", "TEXT NULL"),
        ("ControlImplementations", "ApprovedRequirementCoverageJson", "TEXT NULL"),
        ("RequirementEnhancementProposals", "ExistingNarrativeVersion", "INTEGER NULL"),
        ("RequirementEnhancementProposals", "ExistingNarrativeHash", "TEXT NULL")
    ];

    private const string SqlServerColumns = """
        IF COL_LENGTH('ComplianceFrameworks','RequirementCatalogJson') IS NULL
            ALTER TABLE ComplianceFrameworks ADD RequirementCatalogJson nvarchar(max) NULL;
        IF COL_LENGTH('ComplianceFrameworks','RequirementCatalogVersion') IS NULL
            ALTER TABLE ComplianceFrameworks ADD RequirementCatalogVersion nvarchar(100) NULL;
        IF COL_LENGTH('ComplianceFrameworks','RequirementCatalogSourceUri') IS NULL
            ALTER TABLE ComplianceFrameworks ADD RequirementCatalogSourceUri nvarchar(500) NULL;
        IF COL_LENGTH('ComplianceFrameworks','RequirementCatalogCapturedAt') IS NULL
            ALTER TABLE ComplianceFrameworks ADD RequirementCatalogCapturedAt datetime2 NULL;
        IF COL_LENGTH('ControlBaselines','CoverageRevision') IS NULL
            ALTER TABLE ControlBaselines ADD CoverageRevision int NOT NULL CONSTRAINT DF_ControlBaselines_CoverageRevision DEFAULT 0;
        IF COL_LENGTH('ControlBaselines','SourceFrameworkIdentifier') IS NULL
            ALTER TABLE ControlBaselines ADD SourceFrameworkIdentifier nvarchar(100) NULL;
        IF COL_LENGTH('ControlBaselines','CatalogResolutionMessage') IS NULL
            ALTER TABLE ControlBaselines ADD CatalogResolutionMessage nvarchar(1000) NULL;
        IF COL_LENGTH('ControlBaselines','RequirementCatalogBindingId') IS NULL
            ALTER TABLE ControlBaselines ADD RequirementCatalogBindingId nvarchar(36) NULL;
        IF COL_LENGTH('ControlImplementations','RequirementCoverageJson') IS NULL
            ALTER TABLE ControlImplementations ADD RequirementCoverageJson nvarchar(max) NULL;
        IF COL_LENGTH('ControlImplementations','ApprovedRequirementCoverageJson') IS NULL
            ALTER TABLE ControlImplementations ADD ApprovedRequirementCoverageJson nvarchar(max) NULL;
        """;

    private const string SqliteTables = """
        CREATE TABLE IF NOT EXISTS BaselineCatalogBindings (
            Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL, ControlBaselineId TEXT NOT NULL,
            FrameworkId TEXT NOT NULL, FrameworkIdentifier TEXT NOT NULL, CatalogVersion TEXT NOT NULL,
            SourceUri TEXT NOT NULL, Publisher TEXT NOT NULL, ContentHash TEXT NOT NULL,
            CatalogJson TEXT NOT NULL, Rationale TEXT NOT NULL, BoundBy TEXT NOT NULL, BoundAt TEXT NOT NULL,
            FOREIGN KEY(ControlBaselineId) REFERENCES ControlBaselines(Id) ON DELETE RESTRICT);
        CREATE INDEX IF NOT EXISTS IX_BaselineCatalogBindings_ControlBaselineId ON BaselineCatalogBindings(ControlBaselineId);
        CREATE TABLE IF NOT EXISTS RequirementEnhancementProposals (
            Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL, ControlBaselineId TEXT NOT NULL,
            CatalogBindingId TEXT NOT NULL, ActiveControlKey TEXT NULL, BaselineHash TEXT NOT NULL,
            BaseRevision INTEGER NOT NULL, ControlId TEXT NOT NULL, ParentControlId TEXT NOT NULL,
            Rationale TEXT NOT NULL, PolicyDraft TEXT NULL, TechnicalDraft TEXT NULL,
            Status TEXT NOT NULL, Revision INTEGER NOT NULL, AuthorPersonId TEXT NOT NULL,
            CreatedBy TEXT NOT NULL, CreatedAt TEXT NOT NULL, ReviewerPersonId TEXT NULL,
            ReviewedBy TEXT NULL, ReviewedAt TEXT NULL, ReviewNote TEXT NULL,
            FOREIGN KEY(ControlBaselineId) REFERENCES ControlBaselines(Id) ON DELETE RESTRICT);
        CREATE UNIQUE INDEX IF NOT EXISTS IX_RequirementEnhancementProposals_TenantId_ActiveControlKey
            ON RequirementEnhancementProposals(TenantId,ActiveControlKey) WHERE ActiveControlKey IS NOT NULL;
        CREATE INDEX IF NOT EXISTS IX_RequirementEnhancementProposals_ControlBaselineId
            ON RequirementEnhancementProposals(ControlBaselineId);
        """;

    private const string SqlServerTables = """
        IF OBJECT_ID('BaselineCatalogBindings','U') IS NULL
        BEGIN
            CREATE TABLE BaselineCatalogBindings (
                Id nvarchar(36) NOT NULL PRIMARY KEY, TenantId uniqueidentifier NOT NULL,
                ControlBaselineId nvarchar(36) NOT NULL, FrameworkId nvarchar(36) NOT NULL,
                FrameworkIdentifier nvarchar(100) NOT NULL, CatalogVersion nvarchar(100) NOT NULL,
                SourceUri nvarchar(500) NOT NULL, Publisher nvarchar(100) NOT NULL,
                ContentHash nvarchar(64) NOT NULL, CatalogJson nvarchar(max) NOT NULL,
                Rationale nvarchar(2000) NOT NULL, BoundBy nvarchar(200) NOT NULL, BoundAt datetime2 NOT NULL,
                CONSTRAINT FK_BaselineCatalogBindings_ControlBaselines FOREIGN KEY(ControlBaselineId)
                    REFERENCES ControlBaselines(Id));
            CREATE INDEX IX_BaselineCatalogBindings_ControlBaselineId ON BaselineCatalogBindings(ControlBaselineId);
        END;
        IF OBJECT_ID('RequirementEnhancementProposals','U') IS NULL
        BEGIN
            CREATE TABLE RequirementEnhancementProposals (
                Id nvarchar(36) NOT NULL PRIMARY KEY, TenantId uniqueidentifier NOT NULL,
                ControlBaselineId nvarchar(36) NOT NULL, CatalogBindingId nvarchar(36) NOT NULL,
                ActiveControlKey nvarchar(100) NULL, BaselineHash nvarchar(64) NOT NULL,
                BaseRevision int NOT NULL, ControlId nvarchar(50) NOT NULL, ParentControlId nvarchar(50) NOT NULL,
                Rationale nvarchar(2000) NOT NULL, PolicyDraft nvarchar(max) NULL, TechnicalDraft nvarchar(max) NULL,
                Status nvarchar(32) NOT NULL, Revision int NOT NULL, AuthorPersonId uniqueidentifier NOT NULL,
                CreatedBy nvarchar(200) NOT NULL, CreatedAt datetime2 NOT NULL,
                ReviewerPersonId uniqueidentifier NULL, ReviewedBy nvarchar(200) NULL,
                ReviewedAt datetime2 NULL, ReviewNote nvarchar(2000) NULL,
                CONSTRAINT FK_RequirementEnhancementProposals_ControlBaselines FOREIGN KEY(ControlBaselineId)
                    REFERENCES ControlBaselines(Id));
            CREATE UNIQUE INDEX IX_RequirementEnhancementProposals_TenantId_ActiveControlKey
                ON RequirementEnhancementProposals(TenantId,ActiveControlKey) WHERE ActiveControlKey IS NOT NULL;
            CREATE INDEX IX_RequirementEnhancementProposals_ControlBaselineId
                ON RequirementEnhancementProposals(ControlBaselineId);
        END;
        """;
}
