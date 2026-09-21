using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.MsSql;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Data;

public sealed class BoundaryComponentSqlServerSchemaTests(BoundarySchemaSqlServerFixture fixture)
    : IClassFixture<BoundarySchemaSqlServerFixture>
{
    [SkippableFact]
    public async Task SingleBatch_OnLegacySchema_ReproducesInvalidColumnError()
    {
        // Arrange
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        await using var db = await fixture.CreateDatabaseAsync();
        await db.Database.ExecuteSqlRawAsync(BoundaryComponentSchemaTestData.LegacySchema);

        // Act
        var act = () => db.Database.ExecuteSqlRawAsync(
            string.Join("\n", BoundaryComponentSchemaAdditions.SqlServerBatches));

        // Assert
        (await act.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(207);
    }

    [SkippableFact]
    public async Task UpgradeAndSecondStartup_PreserveDataAndEnforceSourceAndTenantConstraints()
    {
        // Arrange
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        await using var db = await fixture.CreateDatabaseAsync();
        await db.Database.ExecuteSqlRawAsync(BoundaryComponentSchemaTestData.LegacySchema);
        var rows = await ScalarAsync(db, BoundaryComponentSchemaTestData.SnapshotRows);
        var foreignKeys = await ScalarAsync(db, BoundaryComponentSchemaTestData.SnapshotForeignKeys);
        var collation = await ScalarAsync(db, BoundaryComponentSchemaTestData.ComponentCollation);

        // Act
        await BoundaryComponentSchemaAdditions.ApplySqlServerAsync(db);
        var indexes = await ScalarAsync(db, BoundaryComponentSchemaTestData.SnapshotIndexes);
        await BoundaryComponentSchemaAdditions.ApplySqlServerAsync(db);

        // Assert
        (await ScalarAsync(db, BoundaryComponentSchemaTestData.SnapshotRows)).Should().Be(rows);
        (await ScalarAsync(db, BoundaryComponentSchemaTestData.SnapshotForeignKeys)).Should().Be(foreignKeys);
        (await ScalarAsync(db, BoundaryComponentSchemaTestData.SnapshotIndexes)).Should().Be(indexes);
        (await ScalarAsync(db, "SELECT COL_LENGTH('BoundaryComponentAssignments', 'SystemComponentId');"))
            .Should().Be(72);
        (await ScalarAsync(db, BoundaryComponentSchemaTestData.ComponentCollation)).Should().Be(collation);
        (await ScalarAsync(db, """
            SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID('BoundaryComponentAssignments')
              AND is_unique = 1 AND has_filter = 1
              AND name IN ('IX_BCA_ComponentBoundary', 'IX_BCA_CspComponentBoundary');
            """)).Should().Be(2);

        await AssertRejectedAsync(db, "component-1", null, 2601);
        await AssertRejectedAsync(db, null, null, 547);
        await AssertRejectedAsync(db, "component-2", Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), 547);
        await AssertRejectedAsync(db, null, Guid.NewGuid(), 547);
        await AssertRejectedAsync(db, null, Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), 547, Guid.NewGuid());
        await InsertAsync(db, null, Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));
        await AssertRejectedAsync(db, null, Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), 2601);
    }

    [SkippableFact]
    public async Task EfMigration_OnLegacySchema_ExecutesSeparateCommandsBeforeStartupRerun()
    {
        // Arrange
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        await using var db = await fixture.CreateDatabaseAsync();
        await db.Database.ExecuteSqlRawAsync(BoundaryComponentSchemaTestData.LegacySchemaWithNonDefaultCollation);
        var rows = await ScalarAsync(db, BoundaryComponentSchemaTestData.SnapshotRows);
        var foreignKeys = await ScalarAsync(db, BoundaryComponentSchemaTestData.SnapshotForeignKeys);
        var collation = await ScalarAsync(db, BoundaryComponentSchemaTestData.ComponentCollation);
        var migration = new Feature936_BoundaryCspReferences
        {
            ActiveProvider = db.Database.ProviderName!,
        };
        var commands = db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations, db.Model);

        // Act
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            foreach (var command in commands)
                await db.Database.ExecuteSqlRawAsync(command.CommandText);
            await transaction.CommitAsync();
        });
        var indexes = await ScalarAsync(db, BoundaryComponentSchemaTestData.SnapshotIndexes);
        await BoundaryComponentSchemaAdditions.ApplySqlServerAsync(db);

        // Assert
        commands.Should().HaveCount(2);
        commands.Should().OnlyContain(command => !command.TransactionSuppressed);
        collation.Should().Be("Latin1_General_100_BIN2");
        (await ScalarAsync(db, BoundaryComponentSchemaTestData.SnapshotRows)).Should().Be(rows);
        (await ScalarAsync(db, BoundaryComponentSchemaTestData.SnapshotForeignKeys)).Should().Be(foreignKeys);
        (await ScalarAsync(db, BoundaryComponentSchemaTestData.SnapshotIndexes)).Should().Be(indexes);
        (await ScalarAsync(db, BoundaryComponentSchemaTestData.ComponentCollation)).Should().Be(collation);
        await InsertAsync(db, null, Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));
    }

    [SkippableFact]
    public async Task MissingTableAndLaterCspParent_AreIdempotent()
    {
        // Arrange
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        await using var db = await fixture.CreateDatabaseAsync();

        // Act
        await BoundaryComponentSchemaAdditions.ApplySqlServerAsync(db);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE CspInheritedComponents (Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY);");
        await BoundaryComponentSchemaAdditions.ApplySqlServerAsync(db);
        var indexes = await ScalarAsync(db, BoundaryComponentSchemaTestData.SnapshotIndexes);
        await BoundaryComponentSchemaAdditions.ApplySqlServerAsync(db);

        // Assert
        (await ScalarAsync(db, BoundaryComponentSchemaTestData.SnapshotIndexes)).Should().Be(indexes);
        (await ScalarAsync(db, """
            SELECT COUNT(*) FROM sys.foreign_keys
            WHERE name = 'FK_BoundaryComponentAssignments_CspInheritedComponents_CspInheritedComponentId'
              AND is_disabled = 0 AND is_not_trusted = 0;
            """)).Should().Be(1);
        (await ScalarAsync(db, """
            SELECT COUNT(*) FROM sys.check_constraints
            WHERE name = 'CK_BCA_ExactlyOneComponent' AND is_disabled = 0 AND is_not_trusted = 0;
            """)).Should().Be(1);
    }

    [SkippableFact]
    public async Task FreshEfModelAndSecondStartup_DoNotChangeColumnWidthsOrIndexes()
    {
        // Arrange
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        await using var db = await fixture.CreateDatabaseAsync();
        await db.Database.EnsureCreatedAsync();
        var indexes = await ScalarAsync(db, BoundaryComponentSchemaTestData.SnapshotIndexes);

        // Act
        await BoundaryComponentSchemaAdditions.ApplySqlServerAsync(db);
        await BoundaryComponentSchemaAdditions.ApplySqlServerAsync(db);

        // Assert
        (await ScalarAsync(db, BoundaryComponentSchemaTestData.SnapshotIndexes)).Should().Be(indexes);
        (await ScalarAsync(db, "SELECT COL_LENGTH('BoundaryComponentAssignments', 'SystemComponentId');"))
            .Should().Be(72, "the current EF model uses NVARCHAR(36), not the legacy bootstrap width");
    }

    [SkippableFact]
    public async Task InvalidLegacyData_ThrowsAndRollsBackColumnAndIndexChanges()
    {
        // Arrange
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        await using var db = await fixture.CreateDatabaseAsync();
        await db.Database.ExecuteSqlRawAsync(BoundaryComponentSchemaTestData.LegacySchema);
        await db.Database.ExecuteSqlRawAsync("""
            DROP INDEX IX_BCA_ComponentBoundary ON BoundaryComponentAssignments;
            ALTER TABLE BoundaryComponentAssignments ALTER COLUMN SystemComponentId NVARCHAR(36) NULL;
            UPDATE BoundaryComponentAssignments SET SystemComponentId = NULL;
            CREATE UNIQUE INDEX IX_BCA_ComponentBoundary
                ON BoundaryComponentAssignments (SystemComponentId, AuthorizationBoundaryDefinitionId);
            """);
        var rows = await ScalarAsync(db, BoundaryComponentSchemaTestData.SnapshotRows);

        // Act
        var act = () => BoundaryComponentSchemaAdditions.ApplySqlServerAsync(db);

        // Assert
        (await act.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(547);
        (await ScalarAsync(db, "SELECT COL_LENGTH('BoundaryComponentAssignments', 'CspInheritedComponentId');"))
            .Should().Be(DBNull.Value);
        (await ScalarAsync(db, BoundaryComponentSchemaTestData.SnapshotRows)).Should().Be(rows);
        (await ScalarAsync(db, """
            SELECT has_filter FROM sys.indexes
            WHERE object_id = OBJECT_ID('BoundaryComponentAssignments') AND name = 'IX_BCA_ComponentBoundary';
            """)).Should().Be(false);
    }

    private static async Task AssertRejectedAsync(
        AtoCopilotContext db, string? component, Guid? csp, int error, Guid? tenant = null)
    {
        Func<Task> insert = () => InsertAsync(db, component, csp, tenant);
        (await insert.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(error);
    }

    private static Task InsertAsync(AtoCopilotContext db, string? component, Guid? csp, Guid? tenant = null) =>
        db.Database.ExecuteSqlRawAsync(BoundaryComponentSchemaTestData.InsertAssignment,
            new SqlParameter("@id", Guid.NewGuid().ToString()),
            new SqlParameter("@tenant", tenant ?? Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")),
            new SqlParameter("@component", (object?)component ?? DBNull.Value),
            new SqlParameter("@csp", (object?)csp ?? DBNull.Value));

    private static async Task<object?> ScalarAsync(AtoCopilotContext db, string sql)
    {
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            return await command.ExecuteScalarAsync();
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}

public sealed class BoundarySchemaSqlServerFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;
    public bool Available { get; private set; }
    public string UnavailableReason { get; private set; } = "Local SQL Server testcontainer is unavailable.";

    public async Task InitializeAsync()
    {
        try
        {
            _container = new MsSqlBuilder()
                .WithImage("mcr.microsoft.com/mssql/server:2022-CU16-ubuntu-22.04")
                .Build();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await _container.StartAsync(timeout.Token);
            Available = true;
        }
        catch (Exception ex)
        {
            UnavailableReason = $"Local SQL Server unavailable: {ex.GetType().Name}: {ex.Message}";
            if (Environment.GetEnvironmentVariable("ATO_REQUIRE_DOCKER_TESTS") == "1")
                throw new InvalidOperationException(UnavailableReason, ex);
        }
    }

    public async Task<AtoCopilotContext> CreateDatabaseAsync()
    {
        var connectionString = new SqlConnectionStringBuilder(_container!.GetConnectionString())
        {
            InitialCatalog = $"Issue987_{Guid.NewGuid():N}",
        };
        await using var connection = new SqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{connectionString.InitialCatalog}];";
        await command.ExecuteNonQueryAsync();
        return new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseSqlServer(connectionString.ConnectionString, options => options.EnableRetryOnFailure())
            .Options);
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}
