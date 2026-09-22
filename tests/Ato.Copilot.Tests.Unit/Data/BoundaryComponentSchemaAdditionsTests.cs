using System.Data.Common;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Data;

public class BoundaryComponentSchemaAdditionsTests
{
    [Fact]
    public void SqlServerUpgrade_AddsColumnsBeforeCompilingDependentDdl()
    {
        // Arrange
        var migration = new Feature936_BoundaryCspReferences
        {
            ActiveProvider = "Microsoft.EntityFrameworkCore.SqlServer",
        };

        // Act
        var commands = migration.UpOperations.Cast<SqlOperation>().ToArray();

        // Assert
        commands.Should().HaveCount(2, "SQL Server binds column references before executing a batch");
        commands[0].Sql.Should().Contain("ADD CspInheritedComponentId UNIQUEIDENTIFIER NULL")
            .And.NotContain("CREATE UNIQUE INDEX")
            .And.NotContain("CHECK (");
        commands[1].Sql.Should().Contain("IX_BCA_CspComponentBoundary")
            .And.Contain("CK_BCA_ExactlyOneComponent")
            .And.Contain("REFERENCES CspInheritedComponents(Id)");
        commands.Should().OnlyContain(command => !command.SuppressTransaction);
        commands.Select(command => command.Sql).Should().Equal(BoundaryComponentSchemaAdditions.SqlServerBatches);
    }

    [Fact]
    public void SqlServerUpgrade_RepairsOnlyLegacyIndexBeforeRelaxingNullability()
    {
        // Arrange
        var migration = new Feature936_BoundaryCspReferences
        {
            ActiveProvider = "Microsoft.EntityFrameworkCore.SqlServer",
        };

        // Act
        var script = string.Join("\n", migration.UpOperations.Cast<SqlOperation>().Select(x => x.Sql));

        // Assert
        script.IndexOf("DROP INDEX IX_BCA_ComponentBoundary", StringComparison.Ordinal)
            .Should().BeLessThan(script.IndexOf("ALTER COLUMN SystemComponentId", StringComparison.Ordinal));
        script.Should().Contain("has_filter = 0")
            .And.Contain("is_nullable = 0")
            .And.Contain("max_length / 2")
            .And.Contain("+ N') COLLATE ' + collation_name + N' NULL'")
            .And.NotContain("QUOTENAME(collation_name)")
            .And.NotContain("DELETE FROM")
            .And.NotContain("DROP TABLE")
            .And.NotContain("DROP CONSTRAINT");
    }

    [Fact]
    public void SqliteMigration_LeavesSchemaIntrospectiveStartupUpgradeUnchanged()
    {
        // Arrange
        var migration = new Feature936_BoundaryCspReferences
        {
            ActiveProvider = "Microsoft.EntityFrameworkCore.Sqlite",
        };

        // Act
        var commands = migration.UpOperations.Cast<SqlOperation>().ToArray();

        // Assert
        commands.Should().ContainSingle().Which.Sql.Should().Be("SELECT 1;");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Executor_CommitsBothCommandsOrPropagatesFailureAndRollsBack(bool failSecondBatch)
    {
        // Arrange
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using (var setup = connection.CreateCommand())
        {
            setup.CommandText = "CREATE TABLE TransactionProbe (Value INTEGER NOT NULL);";
            await setup.ExecuteNonQueryAsync();
        }
        var interceptor = new BatchTransactionProbe(failSecondBatch);
        await using var db = new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseSqlite(connection).AddInterceptors(interceptor).Options);

        // Act
        var act = () => BoundaryComponentSchemaAdditions.ApplySqlServerAsync(db);
        if (failSecondBatch)
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Injected DDL failure");
        else
            await act();

        // Assert
        interceptor.Commands.Should().Equal(BoundaryComponentSchemaAdditions.SqlServerBatches);
        db.Database.CurrentTransaction.Should().BeNull();
        await using var probe = connection.CreateCommand();
        probe.CommandText = "SELECT COUNT(*) FROM TransactionProbe;";
        Convert.ToInt32(await probe.ExecuteScalarAsync()).Should().Be(failSecondBatch ? 0 : 2);
    }

    private sealed class BatchTransactionProbe(bool failSecondBatch) : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            command.Transaction.Should().NotBeNull();
            if (failSecondBatch && Commands.Count == 2)
                throw new InvalidOperationException("Injected DDL failure");
            // Exercise the executor's real commit/rollback, not SQL Server DDL semantics.
            command.CommandText = "INSERT INTO TransactionProbe VALUES (1);";
            return ValueTask.FromResult(result);
        }
    }
}
