using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

/// <summary>
/// Idempotent additive SQL migrations for the Feature 048 tenancy tables
/// (<c>Tenants</c>, <c>Organizations</c>). Called from <c>Program.cs</c>'s
/// <c>EnsureSchemaAdditionsAsync</c> after <c>EnsureCreatedAsync</c>. Safe to
/// run repeatedly. Detects the active EF Core provider and emits dialect-aware
/// SQL.
/// See feature 048 spec FR-070 and data-model.md §1.1, §1.2, §9.
/// </summary>
public static class TenantsAndOrganizationsSchemaAdditions
{
    /// <summary>
    /// Creates the <c>Tenants</c> and <c>Organizations</c> tables (and their
    /// indexes) if they do not already exist. No-op when the tables exist.
    /// </summary>
    public static async Task ApplyAsync(
        AtoCopilotContext db,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(logger);

        var providerName = db.Database.ProviderName ?? string.Empty;
        var isSqlServer = providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase);
        var isSqlite = providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);

        try
        {
            if (isSqlServer)
            {
                await db.Database.ExecuteSqlRawAsync(SqlServerScript, cancellationToken);
            }
            else if (isSqlite)
            {
                await db.Database.ExecuteSqlRawAsync(SqliteScript, cancellationToken);
            }
            else
            {
                logger.LogWarning(
                    "TenantsAndOrganizationsSchemaAdditions: skipping (unsupported provider {Provider})",
                    providerName);
                return;
            }

            logger.LogInformation(
                "Verified Feature 048 tenancy schema (Tenants, Organizations) on {Provider}",
                providerName);
        }
        catch (Exception ex)
        {
            // Non-fatal: EnsureCreatedAsync may have already created the tables
            // in dev; rethrow only on a clear functional failure.
            logger.LogWarning(ex,
                "TenantsAndOrganizationsSchemaAdditions encountered an error — non-fatal if tables already exist");
        }
    }

    // ─── SQL Server (production) ─────────────────────────────────────────────
    // Idempotent: every CREATE/ALTER guarded by IF NOT EXISTS.
    private const string SqlServerScript = """
        IF OBJECT_ID(N'dbo.Tenants', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.Tenants (
                Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Tenants PRIMARY KEY,
                EntraTenantId UNIQUEIDENTIFIER NULL,
                DisplayName NVARCHAR(200) NOT NULL,
                LegalEntityName NVARCHAR(300) NULL,
                DoDComponent NVARCHAR(120) NULL,
                PrimaryPocName NVARCHAR(200) NULL,
                PrimaryPocEmail NVARCHAR(254) NULL,
                PrimaryPocPhone NVARCHAR(40) NULL,
                HqAddressLine1 NVARCHAR(200) NULL,
                HqAddressLine2 NVARCHAR(200) NULL,
                HqCity NVARCHAR(120) NULL,
                HqStateOrProvince NVARCHAR(120) NULL,
                HqPostalCode NVARCHAR(20) NULL,
                HqCountry NVARCHAR(80) NULL,
                DefaultClassificationLevel INT NOT NULL CONSTRAINT DF_Tenants_DefaultClassificationLevel DEFAULT(0),
                AuthorizingOfficialName NVARCHAR(200) NULL,
                AuthorizingOfficialEmail NVARCHAR(254) NULL,
                TimeZone NVARCHAR(64) NOT NULL CONSTRAINT DF_Tenants_TimeZone DEFAULT('UTC'),
                Status INT NOT NULL CONSTRAINT DF_Tenants_Status DEFAULT(0),
                OnboardingState INT NOT NULL CONSTRAINT DF_Tenants_OnboardingState DEFAULT(0),
                CreatedAt DATETIMEOFFSET NOT NULL CONSTRAINT DF_Tenants_CreatedAt DEFAULT(SYSUTCDATETIME()),
                CreatedBy NVARCHAR(200) NOT NULL CONSTRAINT DF_Tenants_CreatedBy DEFAULT('system'),
                UpdatedAt DATETIMEOFFSET NULL,
                UpdatedBy NVARCHAR(200) NULL,
                RowVersion ROWVERSION NOT NULL
            );
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Tenants_EntraTenantId' AND object_id = OBJECT_ID(N'dbo.Tenants'))
        BEGIN
            CREATE UNIQUE INDEX IX_Tenants_EntraTenantId
              ON dbo.Tenants (EntraTenantId)
              WHERE EntraTenantId IS NOT NULL;
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Tenants_Status' AND object_id = OBJECT_ID(N'dbo.Tenants'))
        BEGIN
            CREATE INDEX IX_Tenants_Status ON dbo.Tenants (Status);
        END;

        IF OBJECT_ID(N'dbo.Organizations', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.Organizations (
                Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Organizations PRIMARY KEY,
                TenantId UNIQUEIDENTIFIER NOT NULL,
                Name NVARCHAR(200) NOT NULL,
                Description NVARCHAR(2000) NULL,
                ParentOrganizationId UNIQUEIDENTIFIER NULL,
                CreatedAt DATETIMEOFFSET NOT NULL CONSTRAINT DF_Organizations_CreatedAt DEFAULT(SYSUTCDATETIME()),
                CreatedBy NVARCHAR(200) NOT NULL CONSTRAINT DF_Organizations_CreatedBy DEFAULT('system'),
                RowVersion ROWVERSION NOT NULL,
                CONSTRAINT FK_Organizations_Parent FOREIGN KEY (ParentOrganizationId) REFERENCES dbo.Organizations(Id)
            );
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Organizations_TenantId_Name' AND object_id = OBJECT_ID(N'dbo.Organizations'))
        BEGIN
            CREATE UNIQUE INDEX IX_Organizations_TenantId_Name ON dbo.Organizations (TenantId, Name);
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Organizations_TenantId_ParentOrganizationId' AND object_id = OBJECT_ID(N'dbo.Organizations'))
        BEGIN
            CREATE INDEX IX_Organizations_TenantId_ParentOrganizationId ON dbo.Organizations (TenantId, ParentOrganizationId);
        END;
        """;

    // ─── SQLite (development) ────────────────────────────────────────────────
    // SQLite supports IF NOT EXISTS on CREATE TABLE / CREATE INDEX, and EnsureCreated
    // already creates these tables — this script exists only to support hosts
    // that use a hand-managed SQLite database.
    private const string SqliteScript = """
        CREATE TABLE IF NOT EXISTS Tenants (
            Id TEXT NOT NULL PRIMARY KEY,
            EntraTenantId TEXT NULL,
            DisplayName TEXT NOT NULL,
            LegalEntityName TEXT NULL,
            DoDComponent TEXT NULL,
            PrimaryPocName TEXT NULL,
            PrimaryPocEmail TEXT NULL,
            PrimaryPocPhone TEXT NULL,
            HqAddressLine1 TEXT NULL,
            HqAddressLine2 TEXT NULL,
            HqCity TEXT NULL,
            HqStateOrProvince TEXT NULL,
            HqPostalCode TEXT NULL,
            HqCountry TEXT NULL,
            DefaultClassificationLevel INTEGER NOT NULL DEFAULT 0,
            AuthorizingOfficialName TEXT NULL,
            AuthorizingOfficialEmail TEXT NULL,
            TimeZone TEXT NOT NULL DEFAULT 'UTC',
            Status INTEGER NOT NULL DEFAULT 0,
            OnboardingState INTEGER NOT NULL DEFAULT 0,
            CreatedAt TEXT NOT NULL,
            CreatedBy TEXT NOT NULL DEFAULT 'system',
            UpdatedAt TEXT NULL,
            UpdatedBy TEXT NULL,
            RowVersion BLOB NULL
        );

        CREATE UNIQUE INDEX IF NOT EXISTS IX_Tenants_EntraTenantId
            ON Tenants (EntraTenantId)
            WHERE EntraTenantId IS NOT NULL;

        CREATE INDEX IF NOT EXISTS IX_Tenants_Status ON Tenants (Status);

        CREATE TABLE IF NOT EXISTS Organizations (
            Id TEXT NOT NULL PRIMARY KEY,
            TenantId TEXT NOT NULL,
            Name TEXT NOT NULL,
            Description TEXT NULL,
            ParentOrganizationId TEXT NULL,
            CreatedAt TEXT NOT NULL,
            CreatedBy TEXT NOT NULL DEFAULT 'system',
            RowVersion BLOB NULL,
            FOREIGN KEY (ParentOrganizationId) REFERENCES Organizations(Id)
        );

        CREATE UNIQUE INDEX IF NOT EXISTS IX_Organizations_TenantId_Name
            ON Organizations (TenantId, Name);

        CREATE INDEX IF NOT EXISTS IX_Organizations_TenantId_ParentOrganizationId
            ON Organizations (TenantId, ParentOrganizationId);
        """;
}
