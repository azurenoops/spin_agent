using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class SystemDecisionDraftSchemaTests
{
    [Fact]
    public async Task AdditiveSchema_ReplaysOnOldTables_WithoutInventingHistoricalSourceMetadata()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE AuthorizationDecisions (Id TEXT PRIMARY KEY);
            INSERT INTO AuthorizationDecisions VALUES ('old-decision');
            CREATE TABLE SecurityAssessmentPlans (Id TEXT PRIMARY KEY);
            INSERT INTO SecurityAssessmentPlans VALUES ('old-plan');
            """);
        // Act
        await SystemDecisionDraftSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await SystemDecisionDraftSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        // Assert
        (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM AuthorizationDecisions WHERE Id='old-decision' AND SourceEvidenceId IS NULL AND RecordedAt IS NULL").SingleAsync()).Should().Be(1);
        (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM SecurityAssessmentPlans WHERE Id='old-plan' AND AssessmentLead IS NULL AND AssessmentApproach IS NULL").SingleAsync()).Should().Be(1);
        db.Model.FindEntityType(typeof(SecurityAssessmentPlan))!.FindProperty(nameof(SecurityAssessmentPlan.Content))!.IsConcurrencyToken.Should().BeTrue();
        db.Model.FindEntityType(typeof(SecurityAssessmentPlan))!.FindProperty(nameof(SecurityAssessmentPlan.Status))!.IsConcurrencyToken.Should().BeTrue();
    }
}
