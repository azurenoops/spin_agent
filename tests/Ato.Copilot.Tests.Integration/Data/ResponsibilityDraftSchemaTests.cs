using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Data;

public sealed class ResponsibilityDraftSchemaTests(BoundarySchemaSqlServerFixture fixture)
    : IClassFixture<BoundarySchemaSqlServerFixture>
{
    [SkippableFact]
    public async Task UpgradeCreatesDraftAndHistoryTables_AndReplayPreservesRecords()
    {
        // Arrange
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        await using var db = await fixture.CreateDatabaseAsync();
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("DROP TABLE dbo.ResponsibilityDraftHistory; DROP TABLE dbo.ResponsibilityDrafts;");
        var system = new RegisteredSystem { TenantId = Guid.NewGuid(), Name = "Synthetic draft schema system", CreatedBy = "fixture" };
        db.RegisteredSystems.Add(system);
        await db.SaveChangesAsync();
        // Act
        await ResponsibilityDraftSchemaAdditions.ApplyAsync(db);
        var draft = new ResponsibilityDraft { TenantId = system.TenantId, RegisteredSystemId = system.Id,
            ControlId = "AU-11", PreparedBy = "fixture", ValuesJson = """{"information":"Needs confirmation"}""" };
        db.Add(draft);
        db.Add(new ResponsibilityDraftHistory { TenantId = system.TenantId, DraftId = draft.Id, Revision = 1,
            Actor = "fixture", Action = "DraftEdited", ValuesJson = draft.ValuesJson, ReviewNotes = "Not accepted" });
        await db.SaveChangesAsync();
        await ResponsibilityDraftSchemaAdditions.ApplyAsync(db);
        db.ChangeTracker.Clear();
        // Assert
        (await db.Set<ResponsibilityDraft>().SingleAsync()).ValuesJson.Should().Be(draft.ValuesJson);
        (await db.Set<ResponsibilityDraftHistory>().SingleAsync()).ReviewNotes.Should().Be("Not accepted");
        (await db.ControlInheritances.CountAsync()).Should().Be(0);
    }
}
