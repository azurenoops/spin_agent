using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Models.Workspaces;
using Ato.Copilot.Core.Services.Workspaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

public static class WorkspaceOperationsSchemaAdditions
{
    public static async Task ApplyAsync(AtoCopilotContext db, ILogger logger, CancellationToken ct = default)
    {
        if (db.Database.IsSqlite())
        {
            await db.Database.ExecuteSqlRawAsync(SqliteScript, ct);
            await AddSqliteSupportColumnsAsync(db, ct);
            await AddSqliteSetupColumnsAsync(db, ct);
            await AddSqliteWorkspaceColumnsAsync(db, ct);
        }
        else if (db.Database.IsSqlServer())
        {
            foreach (var batch in SqlServerBatches)
                await db.Database.ExecuteSqlRawAsync(batch, ct);
        }
        else
        {
            throw new NotSupportedException($"Workspace schema does not support {db.Database.ProviderName}.");
        }
        await BackfillPublishedAsync(db, ct);
        await BackfillOrganizationNamesAsync(db, ct);
        await CleanupAbandonedPreparedSetupsAsync(db, ct);
        logger.LogInformation("Verified workspace operations schema on {Provider}", db.Database.ProviderName);
    }

    private static async Task CleanupAbandonedPreparedSetupsAsync(
        AtoCopilotContext db, CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(
            CapabilitySetupRetentionPolicy.AbandonedPreparationRetention);
        var abandoned = await db.CapabilitySetupOperations.IgnoreQueryFilters()
            .Where(x => x.RecordState == "Pending"
                && x.ComponentLinksState == "Pending"
                && (x.SubscriptionState == "Pending" || x.SubscriptionState == "NotRequested")
                && x.ExecutionClaimId == null
                && x.ClaimedAt == null
                && x.LastError == null)
            .ToListAsync(ct);
        abandoned = abandoned
            .Where(x => x.CreatedAt < cutoff && x.UpdatedAt < cutoff)
            .ToList();
        if (abandoned.Count == 0)
            return;
        var ids = abandoned.Select(x => x.Id).ToArray();
        await db.CapabilitySetupOperations.IgnoreQueryFilters()
            .Where(x => ids.Contains(x.Id)
                && x.RecordState == "Pending"
                && x.ComponentLinksState == "Pending"
                && (x.SubscriptionState == "Pending" || x.SubscriptionState == "NotRequested")
                && x.ExecutionClaimId == null
                && x.ClaimedAt == null
                && x.LastError == null)
            .ExecuteDeleteAsync(ct);
    }

    private static Task BackfillOrganizationNamesAsync(
        AtoCopilotContext db, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null)
            return BackfillOrganizationNamesAttemptAsync(db, ct);
        return db.Database.CreateExecutionStrategy()
            .ExecuteAsync(() => BackfillOrganizationNamesAttemptAsync(db, ct));
    }

    private static async Task BackfillOrganizationNamesAttemptAsync(
        AtoCopilotContext db, CancellationToken ct)
    {
        IDbContextTransaction? transaction = null;
        if (db.Database.CurrentTransaction is null)
            transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var tenants = await db.Tenants.IgnoreQueryFilters().AsNoTracking()
                .Select(x => new { x.Id, x.DisplayName })
                .ToListAsync(ct);
            var normalizedNames = new HashSet<string>(StringComparer.Ordinal);
            var createdAt = DateTimeOffset.UtcNow;
            foreach (var tenant in tenants.OrderBy(x => x.Id))
            {
                var normalizedName = OrganizationNameNormalizer.Normalize(tenant.DisplayName);
                if (normalizedName.Length == 0 || !normalizedNames.Add(normalizedName))
                    continue;
                if (db.Database.IsSqlite())
                {
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT OR IGNORE INTO OrganizationNameReservations(
                            NormalizedName,TenantId,CreatedAt)
                        VALUES({normalizedName},{tenant.Id},{createdAt})
                        """, ct);
                }
                else
                {
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        IF NOT EXISTS (
                            SELECT 1
                            FROM dbo.OrganizationNameReservations WITH (UPDLOCK,HOLDLOCK)
                            WHERE NormalizedName={normalizedName})
                        INSERT INTO dbo.OrganizationNameReservations(
                            NormalizedName,TenantId,CreatedAt)
                        VALUES({normalizedName},{tenant.Id},{createdAt})
                        """, ct);
                }
            }
            if (transaction is not null)
                await transaction.CommitAsync(ct);
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(ct);
            throw;
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }
    }

    private static async Task BackfillPublishedAsync(AtoCopilotContext db, CancellationToken ct)
    {
        if (!await TableExistsAsync(db, "CspInheritedCapabilities", ct)
            || !await TableExistsAsync(db, "CspInheritedComponents", ct))
            return;
        var releasedIds = await db.ProviderCapabilityReleases.AsNoTracking()
            .Select(x => x.CapabilityId).ToListAsync(ct);
        var published = await db.CspInheritedCapabilities.AsNoTracking()
            .Where(x => x.Status == CspInheritedCapabilityStatus.Mapped
                && x.CspInheritedComponent.Status == CspInheritedComponentStatus.Published
                && !releasedIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id, x.Name, x.Description, x.MappedNistControlIds,
                ComponentId = x.CspInheritedComponentId
            }).ToListAsync(ct);
        foreach (var capability in published)
        {
            var duties = capability.MappedNistControlIds.Distinct(StringComparer.OrdinalIgnoreCase)
                .Order().ToDictionary(x => x, _ => "Provider");
            var snapshot = JsonSerializer.Serialize(new
            {
                capabilityId = capability.Id,
                revision = 1L,
                capability.Name,
                capability.Description,
                capability.ComponentId,
                classification = "NotRecorded",
                serviceCategory = "NotRecorded",
                contributors = Array.Empty<string>(),
                controlDuties = duties
            });
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot))).ToLowerInvariant();
            db.ProviderCapabilityReleases.Add(new ProviderCapabilityRelease
            {
                CapabilityId = capability.Id,
                Revision = 1,
                SnapshotHash = hash,
                SnapshotJson = snapshot,
                IdempotencyKey = $"schema-backfill:{capability.Id:N}",
                PublishedBy = "schema-backfill"
            });
        }
        if (published.Count != 0) await db.SaveChangesAsync(ct);
    }

    private static async Task<bool> TableExistsAsync(
        AtoCopilotContext db, string table, CancellationToken ct)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = db.Database.IsSqlite()
            ? "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name"
            : "SELECT CASE WHEN OBJECT_ID(@name, N'U') IS NULL THEN 0 ELSE 1 END";
        var parameter = command.CreateParameter();
        parameter.ParameterName = db.Database.IsSqlite() ? "$name" : "@name";
        parameter.Value = db.Database.IsSqlite() ? table : $"dbo.{table}";
        command.Parameters.Add(parameter);
        if (command.Connection!.State != System.Data.ConnectionState.Open)
            await command.Connection.OpenAsync(ct);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct)) == 1;
    }

    private static async Task AddSqliteSupportColumnsAsync(AtoCopilotContext db, CancellationToken ct)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA table_info('TenantSupportSessions')";
        if (command.Connection!.State != System.Data.ConnectionState.Open)
            await command.Connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) columns.Add(reader.GetString(1));
        await reader.CloseAsync();
        var additions = new[]
        {
            ("Reason", "TEXT NOT NULL DEFAULT 'Legacy authorized support session'"),
            ("Reference", "TEXT NULL"),
            ("Acknowledged", "INTEGER NOT NULL DEFAULT 1"),
            ("CorrelationId", "TEXT NOT NULL DEFAULT 'legacy'")
        };
        foreach (var (name, definition) in additions)
            if (!columns.Contains(name))
                await db.Database.ExecuteSqlRawAsync(
                    $"ALTER TABLE TenantSupportSessions ADD COLUMN \"{name}\" {definition}", ct);
    }

    private static async Task AddSqliteSetupColumnsAsync(AtoCopilotContext db, CancellationToken ct)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA table_info('CapabilitySetupOperations')";
        if (command.Connection!.State != System.Data.ConnectionState.Open)
            await command.Connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) columns.Add(reader.GetString(1));
        await reader.CloseAsync();
        if (!columns.Contains("SubscribeRequested"))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE CapabilitySetupOperations ADD COLUMN SubscribeRequested INTEGER NOT NULL DEFAULT 0", ct);
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE CapabilitySetupOperations SET SubscribeRequested = 1 WHERE SubscriptionState <> 'NotRequested'", ct);
        }
    }

    private static async Task AddSqliteWorkspaceColumnsAsync(AtoCopilotContext db, CancellationToken ct)
    {
        await AddSqliteColumnsAsync(db, "ProviderReleaseImpacts",
        [
            ("SourceEventId", "TEXT NULL"),
            ("SourceRevision", "TEXT NULL"),
            ("LastDeliveryOutcome", "TEXT NULL")
        ], ct);
        await AddSqliteColumnsAsync(db, "OrganizationProvisioningOperations",
        [
            ("DirectoryTenantId", "TEXT NULL"),
            ("ObjectId", "TEXT NULL"),
            ("PersonId", "TEXT NULL"),
            ("Revision", "INTEGER NOT NULL DEFAULT 0"),
            ("CreationIntentHash", "TEXT NULL")
        ], ct);
        await AddSqliteColumnsAsync(db, "ProviderCapabilityWorkingRevisions",
        [
            ("ApprovedPreviewId", "TEXT NULL"),
            ("ApprovedPreviewHash", "TEXT NULL")
        ], ct);
        await AddSqliteColumnsAsync(db, "ProviderCapabilityReleases",
        [
            ("PreviewId", "TEXT NULL"),
            ("PreviewHash", "TEXT NULL")
        ], ct);
        await AddSqliteColumnsAsync(db, "CapabilitySetupOperations",
        [
            ("LocalCapabilityJson", "TEXT NULL"),
            ("ExecutionClaimId", "TEXT NULL"),
            ("ClaimedAt", "TEXT NULL"),
            ("Revision", "INTEGER NOT NULL DEFAULT 0")
        ], ct);
    }

    private static async Task AddSqliteColumnsAsync(
        AtoCopilotContext db, string table, IReadOnlyList<(string Name, string Definition)> additions,
        CancellationToken ct)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"PRAGMA table_info('{table}')";
        if (command.Connection!.State != System.Data.ConnectionState.Open)
            await command.Connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) columns.Add(reader.GetString(1));
        await reader.CloseAsync();
        foreach (var (name, definition) in additions)
            if (!columns.Contains(name))
                await db.Database.ExecuteSqlRawAsync(
                    $"ALTER TABLE \"{table}\" ADD COLUMN \"{name}\" {definition}", ct);
    }

    public const string SqliteScript = """
        CREATE TABLE IF NOT EXISTS ProviderCapabilityWorkingRevisions (
            Id TEXT NOT NULL PRIMARY KEY, CapabilityId TEXT NOT NULL, Revision INTEGER NOT NULL,
            Classification TEXT NOT NULL, ServiceCategory TEXT NOT NULL,
            ContributorsJson TEXT NOT NULL, DutiesJson TEXT NOT NULL, SnapshotHash TEXT NOT NULL,
            UpdatedAt TEXT NOT NULL, UpdatedBy TEXT NOT NULL, ApprovedRevision INTEGER NULL,
            ApprovedSnapshotHash TEXT NULL, ApprovedPreviewId TEXT NULL,
            ApprovedPreviewHash TEXT NULL, ApprovedAt TEXT NULL, ApprovedBy TEXT NULL);
        CREATE UNIQUE INDEX IF NOT EXISTS UX_ProviderWorkingRevision_Capability
            ON ProviderCapabilityWorkingRevisions(CapabilityId);
        CREATE TABLE IF NOT EXISTS OrganizationNameReservations (
            NormalizedName TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL,
            CreatedAt TEXT NOT NULL);
        CREATE INDEX IF NOT EXISTS IX_OrganizationNameReservation_Tenant
            ON OrganizationNameReservations(TenantId);
        CREATE TABLE IF NOT EXISTS ProviderCapabilityContributors (
            Id TEXT NOT NULL PRIMARY KEY, WorkingRevisionId TEXT NOT NULL
                REFERENCES ProviderCapabilityWorkingRevisions(Id) ON DELETE CASCADE,
            ContributorId TEXT NOT NULL);
        CREATE UNIQUE INDEX IF NOT EXISTS UX_ProviderContributor
            ON ProviderCapabilityContributors(WorkingRevisionId, ContributorId);
        CREATE TABLE IF NOT EXISTS ProviderCapabilityDuties (
            Id TEXT NOT NULL PRIMARY KEY, WorkingRevisionId TEXT NOT NULL
                REFERENCES ProviderCapabilityWorkingRevisions(Id) ON DELETE CASCADE,
            ControlId TEXT NOT NULL, Duty TEXT NOT NULL);
        CREATE UNIQUE INDEX IF NOT EXISTS UX_ProviderDuty
            ON ProviderCapabilityDuties(WorkingRevisionId, ControlId);
        CREATE TABLE IF NOT EXISTS ProviderCapabilityReleases (
            Id TEXT NOT NULL PRIMARY KEY, CapabilityId TEXT NOT NULL, Revision INTEGER NOT NULL,
            SnapshotHash TEXT NOT NULL, SnapshotJson TEXT NOT NULL, IdempotencyKey TEXT NOT NULL,
            PreviewId TEXT NULL, PreviewHash TEXT NULL,
            PublishedAt TEXT NOT NULL, PublishedBy TEXT NOT NULL);
        CREATE UNIQUE INDEX IF NOT EXISTS UX_ProviderRelease_Revision
            ON ProviderCapabilityReleases(CapabilityId, Revision);
        CREATE UNIQUE INDEX IF NOT EXISTS UX_ProviderRelease_Idempotency
            ON ProviderCapabilityReleases(CapabilityId, IdempotencyKey);
        CREATE TABLE IF NOT EXISTS ProviderPublicationPreviews (
            Id TEXT NOT NULL PRIMARY KEY, CapabilityId TEXT NOT NULL, Revision INTEGER NOT NULL,
            WorkingSnapshotHash TEXT NOT NULL, PreviewHash TEXT NOT NULL, PayloadJson TEXT NOT NULL,
            GeneratedAt TEXT NOT NULL, ExpiresAt TEXT NOT NULL, InvalidatedAt TEXT NULL);
        CREATE INDEX IF NOT EXISTS IX_ProviderPublicationPreview_CapabilityRevision
            ON ProviderPublicationPreviews(CapabilityId, Revision);
        CREATE TABLE IF NOT EXISTS ProviderReleaseImpacts (
            Id TEXT NOT NULL PRIMARY KEY, ReleaseId TEXT NOT NULL, TenantId TEXT NOT NULL,
            RegisteredSystemId TEXT NOT NULL, SubscriptionId TEXT NULL, ControlId TEXT NOT NULL,
            ChangeKind TEXT NOT NULL, DeliveryState TEXT NOT NULL, CustomerReviewState TEXT NOT NULL,
            NarrativeState TEXT NOT NULL, Attempts INTEGER NOT NULL, CreatedAt TEXT NOT NULL,
            DeliveredAt TEXT NULL, SourceEventId TEXT NULL, SourceRevision TEXT NULL,
            LastDeliveryOutcome TEXT NULL);
        CREATE UNIQUE INDEX IF NOT EXISTS UX_ProviderImpact_Target
            ON ProviderReleaseImpacts(ReleaseId, TenantId, RegisteredSystemId, ControlId);
        CREATE TABLE IF NOT EXISTS OrganizationProvisioningOperations (
            Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL, IdempotencyKey TEXT NOT NULL,
            TenantState TEXT NOT NULL, AdministratorState TEXT NOT NULL, MembershipState TEXT NOT NULL,
            DirectoryTenantId TEXT NULL, ObjectId TEXT NULL, PersonId TEXT NULL,
            Revision INTEGER NOT NULL DEFAULT 0, CreationIntentHash TEXT NULL,
            LastError TEXT NULL, CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL);
        CREATE UNIQUE INDEX IF NOT EXISTS UX_OrganizationProvisioning_Idempotency
            ON OrganizationProvisioningOperations(IdempotencyKey);
        CREATE TABLE IF NOT EXISTS CapabilitySetupOperations (
            Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL REFERENCES Tenants(Id) ON DELETE CASCADE,
            IdempotencyKey TEXT NOT NULL, SourceKind TEXT NOT NULL, SourceRecordId TEXT NOT NULL,
            RegisteredSystemId TEXT NULL, RecordState TEXT NOT NULL, ComponentLinksState TEXT NOT NULL,
            SubscriptionState TEXT NOT NULL, SubscribeRequested INTEGER NOT NULL,
            ComponentIdsJson TEXT NOT NULL, OutcomesJson TEXT NOT NULL,
            LocalCapabilityJson TEXT NULL, ExecutionClaimId TEXT NULL,
            ClaimedAt TEXT NULL, Revision INTEGER NOT NULL DEFAULT 0,
            LastError TEXT NULL,
            CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL);
        CREATE UNIQUE INDEX IF NOT EXISTS UX_CapabilitySetup_Tenant_Idempotency
            ON CapabilitySetupOperations(TenantId, IdempotencyKey);
        """;

    public static IReadOnlyList<string> SqlServerBatches { get; } =
    [
        """
        IF OBJECT_ID(N'dbo.OrganizationNameReservations', N'U') IS NULL
        CREATE TABLE dbo.OrganizationNameReservations (
            NormalizedName NVARCHAR(256) NOT NULL PRIMARY KEY,
            TenantId UNIQUEIDENTIFIER NOT NULL, CreatedAt DATETIMEOFFSET NOT NULL);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes
            WHERE name=N'IX_OrganizationNameReservation_Tenant'
            AND object_id=OBJECT_ID(N'dbo.OrganizationNameReservations'))
        CREATE INDEX IX_OrganizationNameReservation_Tenant
            ON dbo.OrganizationNameReservations(TenantId);
        """,
        """
        IF OBJECT_ID(N'dbo.ProviderCapabilityWorkingRevisions', N'U') IS NULL
        CREATE TABLE dbo.ProviderCapabilityWorkingRevisions (
            Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, CapabilityId UNIQUEIDENTIFIER NOT NULL,
            Revision BIGINT NOT NULL, Classification NVARCHAR(64) NOT NULL,
            ServiceCategory NVARCHAR(120) NOT NULL, ContributorsJson NVARCHAR(MAX) NOT NULL,
            DutiesJson NVARCHAR(MAX) NOT NULL, SnapshotHash NVARCHAR(64) NOT NULL,
            UpdatedAt DATETIMEOFFSET NOT NULL, UpdatedBy NVARCHAR(200) NOT NULL,
            ApprovedRevision BIGINT NULL, ApprovedSnapshotHash NVARCHAR(64) NULL,
            ApprovedPreviewId UNIQUEIDENTIFIER NULL, ApprovedPreviewHash NVARCHAR(64) NULL,
            ApprovedAt DATETIMEOFFSET NULL, ApprovedBy NVARCHAR(200) NULL);
        IF COL_LENGTH(N'dbo.ProviderCapabilityWorkingRevisions', N'ApprovedPreviewId') IS NULL
            ALTER TABLE dbo.ProviderCapabilityWorkingRevisions ADD ApprovedPreviewId UNIQUEIDENTIFIER NULL;
        IF COL_LENGTH(N'dbo.ProviderCapabilityWorkingRevisions', N'ApprovedPreviewHash') IS NULL
            ALTER TABLE dbo.ProviderCapabilityWorkingRevisions ADD ApprovedPreviewHash NVARCHAR(64) NULL;
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'UX_ProviderWorkingRevision_Capability'
            AND object_id=OBJECT_ID(N'dbo.ProviderCapabilityWorkingRevisions'))
        CREATE UNIQUE INDEX UX_ProviderWorkingRevision_Capability ON dbo.ProviderCapabilityWorkingRevisions(CapabilityId);
        """,
        """
        IF OBJECT_ID(N'dbo.ProviderCapabilityContributors', N'U') IS NULL
        CREATE TABLE dbo.ProviderCapabilityContributors (
            Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, WorkingRevisionId UNIQUEIDENTIFIER NOT NULL
                REFERENCES dbo.ProviderCapabilityWorkingRevisions(Id) ON DELETE CASCADE,
            ContributorId NVARCHAR(200) NOT NULL);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'UX_ProviderContributor'
            AND object_id=OBJECT_ID(N'dbo.ProviderCapabilityContributors'))
        CREATE UNIQUE INDEX UX_ProviderContributor
            ON dbo.ProviderCapabilityContributors(WorkingRevisionId,ContributorId);
        IF OBJECT_ID(N'dbo.ProviderCapabilityDuties', N'U') IS NULL
        CREATE TABLE dbo.ProviderCapabilityDuties (
            Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, WorkingRevisionId UNIQUEIDENTIFIER NOT NULL
                REFERENCES dbo.ProviderCapabilityWorkingRevisions(Id) ON DELETE CASCADE,
            ControlId NVARCHAR(20) NOT NULL, Duty NVARCHAR(16) NOT NULL);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'UX_ProviderDuty'
            AND object_id=OBJECT_ID(N'dbo.ProviderCapabilityDuties'))
        CREATE UNIQUE INDEX UX_ProviderDuty
            ON dbo.ProviderCapabilityDuties(WorkingRevisionId,ControlId);
        """,
        """
        IF OBJECT_ID(N'dbo.ProviderCapabilityReleases', N'U') IS NULL
        CREATE TABLE dbo.ProviderCapabilityReleases (
            Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, CapabilityId UNIQUEIDENTIFIER NOT NULL,
            Revision BIGINT NOT NULL, SnapshotHash NVARCHAR(64) NOT NULL,
            SnapshotJson NVARCHAR(MAX) NOT NULL, IdempotencyKey NVARCHAR(100) NOT NULL,
            PreviewId UNIQUEIDENTIFIER NULL, PreviewHash NVARCHAR(64) NULL,
            PublishedAt DATETIMEOFFSET NOT NULL, PublishedBy NVARCHAR(200) NOT NULL);
        IF COL_LENGTH(N'dbo.ProviderCapabilityReleases', N'PreviewId') IS NULL
            ALTER TABLE dbo.ProviderCapabilityReleases ADD PreviewId UNIQUEIDENTIFIER NULL;
        IF COL_LENGTH(N'dbo.ProviderCapabilityReleases', N'PreviewHash') IS NULL
            ALTER TABLE dbo.ProviderCapabilityReleases ADD PreviewHash NVARCHAR(64) NULL;
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'UX_ProviderRelease_Revision'
            AND object_id=OBJECT_ID(N'dbo.ProviderCapabilityReleases'))
        CREATE UNIQUE INDEX UX_ProviderRelease_Revision ON dbo.ProviderCapabilityReleases(CapabilityId, Revision);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'UX_ProviderRelease_Idempotency'
            AND object_id=OBJECT_ID(N'dbo.ProviderCapabilityReleases'))
        CREATE UNIQUE INDEX UX_ProviderRelease_Idempotency ON dbo.ProviderCapabilityReleases(CapabilityId, IdempotencyKey);
        """,
        """
        IF OBJECT_ID(N'dbo.ProviderPublicationPreviews', N'U') IS NULL
        CREATE TABLE dbo.ProviderPublicationPreviews (
            Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, CapabilityId UNIQUEIDENTIFIER NOT NULL,
            Revision BIGINT NOT NULL, WorkingSnapshotHash NVARCHAR(64) NOT NULL,
            PreviewHash NVARCHAR(64) NOT NULL, PayloadJson NVARCHAR(MAX) NOT NULL,
            GeneratedAt DATETIMEOFFSET NOT NULL, ExpiresAt DATETIMEOFFSET NOT NULL,
            InvalidatedAt DATETIMEOFFSET NULL);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes
            WHERE name=N'IX_ProviderPublicationPreview_CapabilityRevision'
            AND object_id=OBJECT_ID(N'dbo.ProviderPublicationPreviews'))
        CREATE INDEX IX_ProviderPublicationPreview_CapabilityRevision
            ON dbo.ProviderPublicationPreviews(CapabilityId, Revision);
        """,
        """
        IF OBJECT_ID(N'dbo.ProviderReleaseImpacts', N'U') IS NULL
        CREATE TABLE dbo.ProviderReleaseImpacts (
            Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, ReleaseId UNIQUEIDENTIFIER NOT NULL,
            TenantId UNIQUEIDENTIFIER NOT NULL, RegisteredSystemId NVARCHAR(36) NOT NULL,
            SubscriptionId NVARCHAR(36) NULL, ControlId NVARCHAR(20) NOT NULL,
            ChangeKind NVARCHAR(16) NOT NULL, DeliveryState NVARCHAR(24) NOT NULL,
            CustomerReviewState NVARCHAR(24) NOT NULL, NarrativeState NVARCHAR(24) NOT NULL,
            Attempts INT NOT NULL, CreatedAt DATETIMEOFFSET NOT NULL, DeliveredAt DATETIMEOFFSET NULL,
            SourceEventId UNIQUEIDENTIFIER NULL, SourceRevision NVARCHAR(64) NULL,
            LastDeliveryOutcome NVARCHAR(40) NULL);
        IF COL_LENGTH(N'dbo.ProviderReleaseImpacts', N'SourceEventId') IS NULL
            ALTER TABLE dbo.ProviderReleaseImpacts ADD SourceEventId UNIQUEIDENTIFIER NULL;
        IF COL_LENGTH(N'dbo.ProviderReleaseImpacts', N'SourceRevision') IS NULL
            ALTER TABLE dbo.ProviderReleaseImpacts ADD SourceRevision NVARCHAR(64) NULL;
        IF COL_LENGTH(N'dbo.ProviderReleaseImpacts', N'LastDeliveryOutcome') IS NULL
            ALTER TABLE dbo.ProviderReleaseImpacts ADD LastDeliveryOutcome NVARCHAR(40) NULL;
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'UX_ProviderImpact_Target'
            AND object_id=OBJECT_ID(N'dbo.ProviderReleaseImpacts'))
        CREATE UNIQUE INDEX UX_ProviderImpact_Target
            ON dbo.ProviderReleaseImpacts(ReleaseId,TenantId,RegisteredSystemId,ControlId);
        """,
        """
        IF OBJECT_ID(N'dbo.OrganizationProvisioningOperations', N'U') IS NULL
        CREATE TABLE dbo.OrganizationProvisioningOperations (
            Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, TenantId UNIQUEIDENTIFIER NOT NULL,
            IdempotencyKey NVARCHAR(100) NOT NULL, TenantState NVARCHAR(24) NOT NULL,
            AdministratorState NVARCHAR(24) NOT NULL, MembershipState NVARCHAR(24) NOT NULL,
            DirectoryTenantId UNIQUEIDENTIFIER NULL, ObjectId UNIQUEIDENTIFIER NULL, PersonId UNIQUEIDENTIFIER NULL,
            Revision BIGINT NOT NULL CONSTRAINT DF_OrganizationProvisioningOperations_Revision DEFAULT 0,
            CreationIntentHash NVARCHAR(64) NULL,
            LastError NVARCHAR(200) NULL, CreatedAt DATETIMEOFFSET NOT NULL, UpdatedAt DATETIMEOFFSET NOT NULL);
        IF COL_LENGTH(N'dbo.OrganizationProvisioningOperations', N'DirectoryTenantId') IS NULL
            ALTER TABLE dbo.OrganizationProvisioningOperations ADD DirectoryTenantId UNIQUEIDENTIFIER NULL;
        IF COL_LENGTH(N'dbo.OrganizationProvisioningOperations', N'ObjectId') IS NULL
            ALTER TABLE dbo.OrganizationProvisioningOperations ADD ObjectId UNIQUEIDENTIFIER NULL;
        IF COL_LENGTH(N'dbo.OrganizationProvisioningOperations', N'PersonId') IS NULL
            ALTER TABLE dbo.OrganizationProvisioningOperations ADD PersonId UNIQUEIDENTIFIER NULL;
        IF COL_LENGTH(N'dbo.OrganizationProvisioningOperations', N'Revision') IS NULL
            ALTER TABLE dbo.OrganizationProvisioningOperations ADD Revision BIGINT NOT NULL
                CONSTRAINT DF_OrganizationProvisioningOperations_Revision_Add DEFAULT 0;
        IF COL_LENGTH(N'dbo.OrganizationProvisioningOperations', N'CreationIntentHash') IS NULL
            ALTER TABLE dbo.OrganizationProvisioningOperations ADD CreationIntentHash NVARCHAR(64) NULL;
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'UX_OrganizationProvisioning_Idempotency'
            AND object_id=OBJECT_ID(N'dbo.OrganizationProvisioningOperations'))
        CREATE UNIQUE INDEX UX_OrganizationProvisioning_Idempotency
            ON dbo.OrganizationProvisioningOperations(IdempotencyKey);
        """,
        """
        IF OBJECT_ID(N'dbo.CapabilitySetupOperations', N'U') IS NULL
        CREATE TABLE dbo.CapabilitySetupOperations (
            Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, TenantId UNIQUEIDENTIFIER NOT NULL
                REFERENCES dbo.Tenants(Id) ON DELETE CASCADE,
            IdempotencyKey NVARCHAR(100) NOT NULL, SourceKind NVARCHAR(16) NOT NULL,
            SourceRecordId NVARCHAR(64) NOT NULL, RegisteredSystemId NVARCHAR(36) NULL,
            RecordState NVARCHAR(24) NOT NULL, ComponentLinksState NVARCHAR(24) NOT NULL,
            SubscriptionState NVARCHAR(24) NOT NULL, SubscribeRequested BIT NOT NULL,
            ComponentIdsJson NVARCHAR(MAX) NOT NULL,
            OutcomesJson NVARCHAR(MAX) NOT NULL, LocalCapabilityJson NVARCHAR(MAX) NULL,
            ExecutionClaimId UNIQUEIDENTIFIER NULL, ClaimedAt DATETIMEOFFSET NULL,
            Revision BIGINT NOT NULL CONSTRAINT DF_CapabilitySetupOperations_Revision DEFAULT 0,
            LastError NVARCHAR(200) NULL,
            CreatedAt DATETIMEOFFSET NOT NULL, UpdatedAt DATETIMEOFFSET NOT NULL);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'UX_CapabilitySetup_Tenant_Idempotency'
            AND object_id=OBJECT_ID(N'dbo.CapabilitySetupOperations'))
        CREATE UNIQUE INDEX UX_CapabilitySetup_Tenant_Idempotency
            ON dbo.CapabilitySetupOperations(TenantId,IdempotencyKey);
        IF COL_LENGTH(N'dbo.CapabilitySetupOperations', N'SubscribeRequested') IS NULL
        BEGIN
            ALTER TABLE dbo.CapabilitySetupOperations ADD SubscribeRequested BIT NOT NULL
                CONSTRAINT DF_CapabilitySetupOperations_SubscribeRequested DEFAULT 0;
            UPDATE dbo.CapabilitySetupOperations SET SubscribeRequested=1
                WHERE SubscriptionState<>N'NotRequested';
        END;
        IF COL_LENGTH(N'dbo.CapabilitySetupOperations', N'LocalCapabilityJson') IS NULL
            ALTER TABLE dbo.CapabilitySetupOperations ADD LocalCapabilityJson NVARCHAR(MAX) NULL;
        IF COL_LENGTH(N'dbo.CapabilitySetupOperations', N'ExecutionClaimId') IS NULL
            ALTER TABLE dbo.CapabilitySetupOperations ADD ExecutionClaimId UNIQUEIDENTIFIER NULL;
        IF COL_LENGTH(N'dbo.CapabilitySetupOperations', N'ClaimedAt') IS NULL
            ALTER TABLE dbo.CapabilitySetupOperations ADD ClaimedAt DATETIMEOFFSET NULL;
        IF COL_LENGTH(N'dbo.CapabilitySetupOperations', N'Revision') IS NULL
            ALTER TABLE dbo.CapabilitySetupOperations ADD Revision BIGINT NOT NULL
                CONSTRAINT DF_CapabilitySetupOperations_Revision DEFAULT 0;
        """,
        """
        IF COL_LENGTH(N'dbo.TenantSupportSessions', N'Reason') IS NULL
            ALTER TABLE dbo.TenantSupportSessions ADD Reason NVARCHAR(500) NOT NULL
                CONSTRAINT DF_TenantSupportSessions_Reason DEFAULT N'Legacy authorized support session';
        IF COL_LENGTH(N'dbo.TenantSupportSessions', N'Reference') IS NULL
            ALTER TABLE dbo.TenantSupportSessions ADD Reference NVARCHAR(100) NULL;
        IF COL_LENGTH(N'dbo.TenantSupportSessions', N'Acknowledged') IS NULL
            ALTER TABLE dbo.TenantSupportSessions ADD Acknowledged BIT NOT NULL
                CONSTRAINT DF_TenantSupportSessions_Acknowledged DEFAULT 1;
        IF COL_LENGTH(N'dbo.TenantSupportSessions', N'CorrelationId') IS NULL
            ALTER TABLE dbo.TenantSupportSessions ADD CorrelationId NVARCHAR(64) NOT NULL
                CONSTRAINT DF_TenantSupportSessions_CorrelationId DEFAULT N'legacy';
        """
    ];
}
