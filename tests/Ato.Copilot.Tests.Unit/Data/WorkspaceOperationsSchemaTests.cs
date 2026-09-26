using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Data;

public sealed class WorkspaceOperationsSchemaTests
{
    [Fact]
    public async Task ApplyAsync_UpgradesLegacySupportTableAndIsIdempotent()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE Tenants (
                    Id TEXT NOT NULL PRIMARY KEY, DisplayName TEXT NOT NULL);
                CREATE TABLE TenantSupportSessions (
                    Id TEXT NOT NULL PRIMARY KEY, DirectoryTenantId TEXT NOT NULL,
                    ObjectId TEXT NOT NULL, TargetTenantId TEXT NOT NULL,
                    IssuedAt TEXT NOT NULL, ExpiresAt TEXT NOT NULL,
                    RevokedAt TEXT NULL, RevocationReason TEXT NULL);
                CREATE TABLE CapabilitySetupOperations (
                    Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL,
                    IdempotencyKey TEXT NOT NULL, SourceKind TEXT NOT NULL,
                    SourceRecordId TEXT NOT NULL, RegisteredSystemId TEXT NULL,
                    RecordState TEXT NOT NULL, ComponentLinksState TEXT NOT NULL,
                    SubscriptionState TEXT NOT NULL, ComponentIdsJson TEXT NOT NULL,
                    OutcomesJson TEXT NOT NULL, LastError TEXT NULL,
                    CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL);
                CREATE TABLE SystemCapabilityLinks (Id TEXT NOT NULL PRIMARY KEY);
                INSERT INTO SystemCapabilityLinks(Id) VALUES ('preserved-link');
                CREATE TABLE ProviderReleaseImpacts (
                    Id TEXT NOT NULL PRIMARY KEY, ReleaseId TEXT NOT NULL, TenantId TEXT NOT NULL,
                    RegisteredSystemId TEXT NOT NULL, SubscriptionId TEXT NULL, ControlId TEXT NOT NULL,
                    ChangeKind TEXT NOT NULL, DeliveryState TEXT NOT NULL, CustomerReviewState TEXT NOT NULL,
                    NarrativeState TEXT NOT NULL, Attempts INTEGER NOT NULL, CreatedAt TEXT NOT NULL,
                    DeliveredAt TEXT NULL);
                CREATE TABLE OrganizationProvisioningOperations (
                    Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL, IdempotencyKey TEXT NOT NULL,
                    TenantState TEXT NOT NULL, AdministratorState TEXT NOT NULL, MembershipState TEXT NOT NULL,
                    LastError TEXT NULL, CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL);
                CREATE TABLE OrganizationNameReservations (
                    NormalizedName TEXT NOT NULL PRIMARY KEY,
                    TenantId TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL);
                INSERT INTO Tenants(Id,DisplayName) VALUES
                    ('11111111-1111-1111-1111-111111111111','Mission Alpha'),
                    ('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',' mission alpha '),
                    ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',char(8195) || 'école' || char(8195)),
                    ('cccccccc-cccc-cccc-cccc-cccccccccccc','ÉCOLE');
                INSERT INTO OrganizationNameReservations(NormalizedName,TenantId,CreatedAt)
                VALUES(
                    'ÉCOLE','cccccccc-cccc-cccc-cccc-cccccccccccc',
                    '2026-01-01T00:00:00+00:00');
                INSERT INTO TenantSupportSessions(
                    Id,DirectoryTenantId,ObjectId,TargetTenantId,IssuedAt,ExpiresAt)
                VALUES(
                    '22222222-2222-2222-2222-222222222222',
                    '33333333-3333-3333-3333-333333333333',
                    '44444444-4444-4444-4444-444444444444',
                    '11111111-1111-1111-1111-111111111111',
                    '2026-01-01T00:00:00+00:00','2026-01-01T01:00:00+00:00');
                INSERT INTO CapabilitySetupOperations(
                    Id,TenantId,IdempotencyKey,SourceKind,SourceRecordId,
                    RecordState,ComponentLinksState,SubscriptionState,
                    ComponentIdsJson,OutcomesJson,CreatedAt,UpdatedAt)
                VALUES(
                    '55555555-5555-5555-5555-555555555555',
                    '11111111-1111-1111-1111-111111111111',
                    'legacy-setup','provider','66666666-6666-6666-6666-666666666666',
                    'Completed','Completed','Completed','[]','[]',
                    '2026-01-01T00:00:00+00:00','2026-01-01T00:00:00+00:00');
                INSERT INTO ProviderReleaseImpacts(
                    Id,ReleaseId,TenantId,RegisteredSystemId,ControlId,ChangeKind,
                    DeliveryState,CustomerReviewState,NarrativeState,Attempts,CreatedAt)
                VALUES(
                    '77777777-7777-7777-7777-777777777777',
                    '88888888-8888-8888-8888-888888888888',
                    '11111111-1111-1111-1111-111111111111','system','AC-2','Changed',
                    'Pending','Pending','Pending',2,'2026-01-01T00:00:00+00:00');
                INSERT INTO OrganizationProvisioningOperations(
                    Id,TenantId,IdempotencyKey,TenantState,AdministratorState,MembershipState,
                    CreatedAt,UpdatedAt)
                VALUES(
                    '99999999-9999-9999-9999-999999999999',
                    '11111111-1111-1111-1111-111111111111','legacy-provision',
                    'Completed','Pending','Pending',
                    '2026-01-01T00:00:00+00:00','2026-01-01T00:00:00+00:00');
                """;
            await command.ExecuteNonQueryAsync();
        }
        var options = new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options;
        await using var db = new AtoCopilotContext(options);

        // Act
        await WorkspaceOperationsSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await WorkspaceOperationsSchemaAdditions.ApplyAsync(db, NullLogger.Instance);

        // Assert
        var row = await db.Set<Ato.Copilot.Core.Models.Tenancy.TenantSupportSession>()
            .IgnoreQueryFilters().SingleAsync();
        Assert.Equal("Legacy authorized support session", row.Reason);
        Assert.True(row.Acknowledged);
        Assert.Equal("legacy", row.CorrelationId);
        var setup = await db.Set<Ato.Copilot.Core.Models.Workspaces.CapabilitySetupOperation>()
            .IgnoreQueryFilters().SingleAsync();
        Assert.Equal("legacy-setup", setup.IdempotencyKey);
        Assert.True(setup.SubscribeRequested);
        Assert.Null(setup.ClaimedAt);
        Assert.Null(setup.ExecutionClaimId);
        Assert.Equal(0, setup.Revision);
        var impact = await db.ProviderReleaseImpacts.SingleAsync();
        Assert.Equal(2, impact.DeliveryAttempts);
        Assert.Null(impact.SourceEventId);
        var provisioning = await db.OrganizationProvisioningOperations.SingleAsync();
        Assert.Equal("legacy-provision", provisioning.IdempotencyKey);
        Assert.Null(provisioning.PersonId);
        Assert.Null(provisioning.CreationIntentHash);
        Assert.Equal(0, provisioning.Revision);
        Assert.True(await TableExistsAsync(connection, "ProviderCapabilityReleases"));
        Assert.True(await TableExistsAsync(connection, "ProviderPublicationPreviews"));
        Assert.True(await TableExistsAsync(connection, "OrganizationNameReservations"));
        Assert.True(await TableExistsAsync(connection, "CapabilitySetupOperations"));
        Assert.Null(setup.LocalCapabilityJson);
        Assert.Null(setup.SystemIntentJson);
        Assert.Null(setup.SystemPlanJson);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT SupportingProviderCapabilityIdsJson FROM SystemCapabilityLinks WHERE Id='preserved-link'";
            Assert.Equal("[]", await command.ExecuteScalarAsync());
        }
        Assert.Contains(WorkspaceOperationsSchemaAdditions.SqlServerBatches,
            batch => batch.Contains("SupportingProviderCapabilityIdsJson", StringComparison.Ordinal));
        Assert.Contains(WorkspaceOperationsSchemaAdditions.SqlServerBatches,
            batch => batch.Contains("ProviderPublicationPreviews", StringComparison.Ordinal));
        Assert.Contains(WorkspaceOperationsSchemaAdditions.SqlServerBatches,
            batch => batch.Contains("CreationIntentHash", StringComparison.Ordinal));
        Assert.Contains(WorkspaceOperationsSchemaAdditions.SqlServerBatches,
            batch => batch.Contains("LocalCapabilityJson", StringComparison.Ordinal));
        Assert.Contains(WorkspaceOperationsSchemaAdditions.SqlServerBatches,
            batch => batch.Contains("ExecutionClaimId", StringComparison.Ordinal));
        Assert.Equal(2, await db.OrganizationNameReservations.CountAsync());
        var unicodeReservation = await db.OrganizationNameReservations
            .SingleAsync(x => x.NormalizedName == "ÉCOLE");
        Assert.Equal(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            unicodeReservation.TenantId);
        Assert.Equal(4, await db.Tenants.IgnoreQueryFilters().CountAsync());
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name";
        command.Parameters.AddWithValue("$name", table);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }
}
