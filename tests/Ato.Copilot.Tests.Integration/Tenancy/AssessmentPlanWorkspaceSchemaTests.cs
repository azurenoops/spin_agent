using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

public sealed class AssessmentPlanWorkspaceSchemaTests
{
    [Fact]
    public async Task SqliteLegacySchema_IsAdditiveIdempotent_AndEnforcesOneWorkingDraft()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE SecurityAssessmentPlans (Id TEXT PRIMARY KEY, TenantId TEXT NOT NULL,
                RegisteredSystemId TEXT NOT NULL, Status TEXT NOT NULL, Content TEXT NOT NULL);
            CREATE TABLE SapControlEntries (Id TEXT PRIMARY KEY, SecurityAssessmentPlanId TEXT NOT NULL);
            INSERT INTO SecurityAssessmentPlans VALUES ('plan', 'tenant', 'system', 'Draft', 'retained content');
            INSERT INTO SapControlEntries VALUES ('entry', 'plan');
            """);

        // Act
        await AssessmentPlanWorkspaceSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await AssessmentPlanWorkspaceSchemaAdditions.ApplyAsync(db, NullLogger.Instance);

        // Assert
        (await db.Database.SqlQueryRaw<long>("SELECT Revision AS Value FROM SecurityAssessmentPlans").SingleAsync()).Should().Be(1);
        (await db.Database.SqlQueryRaw<string>("SELECT Content AS Value FROM SecurityAssessmentPlans").SingleAsync()).Should().Be("retained content");
        (await db.Database.SqlQueryRaw<int>("SELECT IsExcluded AS Value FROM SapControlEntries").SingleAsync()).Should().Be(0);
        await FluentActions.Awaiting(() => db.Database.ExecuteSqlRawAsync("""
            INSERT INTO SecurityAssessmentPlans (Id, TenantId, RegisteredSystemId, Status, Content)
            VALUES ('duplicate', 'tenant', 'system', 'Draft', 'must not replace')
            """)).Should().ThrowAsync<SqliteException>();
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO SecurityAssessmentPlans (Id, TenantId, RegisteredSystemId, Status, Content)
            VALUES ('history', 'tenant', 'system', 'Finalized', 'immutable history')
            """);
        (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM SecurityAssessmentPlans").SingleAsync()).Should().Be(2);
    }
}
