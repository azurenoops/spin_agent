using System.Data;
using Ato.Copilot.Core.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

public static class OrganizationOnboardingSchemaAdditions
{
    public static async Task ApplyAsync(AtoCopilotContext db, ILogger logger, CancellationToken ct = default)
    {
        if (!db.Database.IsRelational()) return;
        if (db.Database.IsSqlite())
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS OrganizationOnboardingDrafts (
                    Id TEXT NOT NULL PRIMARY KEY, ProviderId TEXT NOT NULL REFERENCES CspProfiles(Id),
                    CreatedBy TEXT NOT NULL, UpdatedBy TEXT NOT NULL, Revision INTEGER NOT NULL DEFAULT 1,
                    SchemaVersion INTEGER NOT NULL DEFAULT 1, ValuesJson TEXT NOT NULL, CurrentStep TEXT NOT NULL,
                    State TEXT NOT NULL, CreationKey TEXT NOT NULL, ProvisioningKey TEXT NULL, ConfirmedIntentHash TEXT NULL,
                    ConfirmedRevision INTEGER NULL, TenantId TEXT NULL, OperationId TEXT NULL,
                    CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL, UpdatedAtTicks INTEGER NOT NULL);
                CREATE UNIQUE INDEX IF NOT EXISTS UX_OrganizationDraft_Key ON OrganizationOnboardingDrafts(ProviderId,CreationKey);
                CREATE INDEX IF NOT EXISTS IX_OrganizationDraft_List ON OrganizationOnboardingDrafts(ProviderId,State,UpdatedAtTicks,Id);
                """, ct);
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var command = db.Database.GetDbConnection().CreateCommand();
            if (command.Connection!.State != ConnectionState.Open) await command.Connection.OpenAsync(ct);
            command.CommandText = "PRAGMA table_info('Tenants')";
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) columns.Add(reader.GetString(1));
            await reader.CloseAsync();
            foreach (var (name, sql) in new[]
            {
                ("OnboardingDraftJson", "ALTER TABLE Tenants ADD COLUMN OnboardingDraftJson TEXT NULL"),
                ("OnboardingDraftSchemaVersion", "ALTER TABLE Tenants ADD COLUMN OnboardingDraftSchemaVersion INTEGER NOT NULL DEFAULT 1"),
                ("OnboardingDraftRevision", "ALTER TABLE Tenants ADD COLUMN OnboardingDraftRevision INTEGER NOT NULL DEFAULT 0"),
                ("OnboardingDraftStep", "ALTER TABLE Tenants ADD COLUMN OnboardingDraftStep TEXT NULL"),
                ("OnboardingFirstOrganizationId", "ALTER TABLE Tenants ADD COLUMN OnboardingFirstOrganizationId TEXT NULL")
            })
                if (!columns.Contains(name))
                    await db.Database.ExecuteSqlRawAsync(sql, ct);
        }
        else if (db.Database.IsSqlServer())
        {
            await db.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID(N'dbo.OrganizationOnboardingDrafts', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.OrganizationOnboardingDrafts (
                        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, ProviderId UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.CspProfiles(Id),
                        CreatedBy NVARCHAR(200) NOT NULL, UpdatedBy NVARCHAR(200) NOT NULL, Revision BIGINT NOT NULL DEFAULT 1,
                        SchemaVersion INT NOT NULL DEFAULT 1, ValuesJson NVARCHAR(MAX) NOT NULL, CurrentStep NVARCHAR(32) NOT NULL,
                        State NVARCHAR(24) NOT NULL, CreationKey NVARCHAR(100) NOT NULL, ProvisioningKey NVARCHAR(100) NULL, ConfirmedIntentHash NVARCHAR(64) NULL,
                        ConfirmedRevision BIGINT NULL, TenantId UNIQUEIDENTIFIER NULL, OperationId UNIQUEIDENTIFIER NULL,
                        CreatedAt DATETIMEOFFSET NOT NULL, UpdatedAt DATETIMEOFFSET NOT NULL, UpdatedAtTicks BIGINT NOT NULL);
                    CREATE UNIQUE INDEX UX_OrganizationDraft_Key ON dbo.OrganizationOnboardingDrafts(ProviderId,CreationKey);
                    CREATE INDEX IX_OrganizationDraft_List ON dbo.OrganizationOnboardingDrafts(ProviderId,State,UpdatedAtTicks,Id);
                END;
                IF COL_LENGTH(N'dbo.Tenants', N'OnboardingDraftJson') IS NULL ALTER TABLE dbo.Tenants ADD OnboardingDraftJson NVARCHAR(MAX) NULL;
                IF COL_LENGTH(N'dbo.Tenants', N'OnboardingDraftSchemaVersion') IS NULL ALTER TABLE dbo.Tenants ADD OnboardingDraftSchemaVersion INT NOT NULL DEFAULT 1;
                IF COL_LENGTH(N'dbo.Tenants', N'OnboardingDraftRevision') IS NULL ALTER TABLE dbo.Tenants ADD OnboardingDraftRevision BIGINT NOT NULL DEFAULT 0;
                IF COL_LENGTH(N'dbo.Tenants', N'OnboardingDraftStep') IS NULL ALTER TABLE dbo.Tenants ADD OnboardingDraftStep NVARCHAR(32) NULL;
                IF COL_LENGTH(N'dbo.Tenants', N'OnboardingFirstOrganizationId') IS NULL ALTER TABLE dbo.Tenants ADD OnboardingFirstOrganizationId UNIQUEIDENTIFIER NULL;
                """, ct);
        }
        else throw new NotSupportedException($"Organization onboarding schema does not support {db.Database.ProviderName}.");
        logger.LogInformation("Verified private organization onboarding draft schema");
    }
}
