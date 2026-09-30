using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Queries;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ato.Copilot.Tests.Integration;

public sealed class AssessmentPlanOrderingTests
{
    [Fact]
    public async Task RetainedDetails_LargePlan_PreservesChildrenAndRejectsChangedRevision()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options;
        await using var db = new AtoCopilotContext(options);
        await db.Database.EnsureCreatedAsync();
        var system = new RegisteredSystem { Name = "Plan read regression" };
        var plan = new SecurityAssessmentPlan
        {
            RegisteredSystemId = system.Id, Title = "Large retained draft",
            Content = new string('x', 220_000), BaselineLevel = "Moderate"
        };
        plan.ControlEntries = Enumerable.Range(1, 339).Select(index => new SapControlEntry
        {
            SecurityAssessmentPlanId = plan.Id, ControlId = $"AC-{index}",
            ControlTitle = $"Control {index}", ControlFamily = "AC"
        }).ToList();
        plan.TeamMembers.Add(new SapTeamMember
        {
            SecurityAssessmentPlanId = plan.Id, Name = "Named assessor",
            Organization = "Test organization", Role = "Assessor"
        });
        db.AddRange(system, plan);
        await db.SaveChangesAsync();
        var query = db.SecurityAssessmentPlans.AsNoTracking().Where(value => value.Id == plan.Id);

        // Act
        var loaded = await query.LoadRetainedDetailsAsync();

        // Assert
        loaded.Should().ContainSingle();
        loaded[0].Content.Should().Be(plan.Content);
        loaded[0].ControlEntries.Should().HaveCount(339);
        loaded[0].TeamMembers.Should().ContainSingle();

        // Act
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE SecurityAssessmentPlans SET Revision = Revision + 1 WHERE Id = {plan.Id}");
        var verify = () => query.VerifyRetainedRevisionsAsync(loaded);

        // Assert
        await verify.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public void RetainedDetails_SqlServer_DoesNotRepeatDocumentAcrossChildJoins()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseSqlServer("Server=localhost;Database=AssessmentPlanTranslation;Integrated Security=true;TrustServerCertificate=true")
            .Options;
        using var db = new AtoCopilotContext(options);

        // Act
        var sql = db.SecurityAssessmentPlans.Where(plan => plan.Id == "selected-plan")
            .WithRetainedDetails().ToQueryString();

        // Assert
        sql.Should().Contain("[Content]");
        sql.Should().NotContain("[SapControlEntries]", "the document must be loaded once, not once per control row");
        sql.Should().NotContain("[SapTeamMembers]", "collection rows must not multiply the document payload");
    }

    [Fact]
    public void WorkingPlanOrder_SqlServer_UsesNumericCaseForStringConvertedStatus()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseSqlServer("Server=localhost;Database=AssessmentPlanTranslation;Integrated Security=true;TrustServerCertificate=true")
            .Options;
        using var db = new AtoCopilotContext(options);

        // Act
        var sql = db.SecurityAssessmentPlans
            .Where(plan => plan.RegisteredSystemId == "selected-system")
            .OrderWorkingFirst().ToQueryString();

        // Assert
        sql.Should().Contain("ORDER BY CASE");
        sql.Should().Contain("N'Draft'");
        sql.Should().NotContain("^", "SQL Server cannot XOR nvarchar SAP statuses");
    }

    [Fact]
    public void WorkingPlanOrder_PreservesDraftPriorityAndDeterministicHistory()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var plans = new[]
        {
            new SecurityAssessmentPlan { Id = "final", Status = SapStatus.Finalized, GeneratedAt = now },
            new SecurityAssessmentPlan { Id = "draft-b", Status = SapStatus.Draft, GeneratedAt = now.AddDays(-1) },
            new SecurityAssessmentPlan { Id = "draft-a", Status = SapStatus.Draft, GeneratedAt = now.AddDays(-1) },
            new SecurityAssessmentPlan { Id = "draft-old", Status = SapStatus.Draft, GeneratedAt = now.AddDays(-2) }
        }.AsQueryable();

        // Act
        var ids = plans.OrderWorkingFirst().Select(plan => plan.Id).ToArray();

        // Assert
        ids.Should().Equal("draft-a", "draft-b", "draft-old", "final");
    }
}
