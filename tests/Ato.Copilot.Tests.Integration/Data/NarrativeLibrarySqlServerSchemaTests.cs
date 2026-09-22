using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Data;

public sealed class NarrativeLibrarySqlServerSchemaTests(BoundarySchemaSqlServerFixture fixture)
    : IClassFixture<BoundarySchemaSqlServerFixture>
{
    [SkippableFact]
    public async Task FreshSchemaAndRepeatStartup_PreserveEightThousandCharacterUnicodeProposals()
    {
        // Arrange
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        await using var db = await fixture.CreateDatabaseAsync();
        var proposal = new NarrativeProposal
        {
            TenantId = Guid.NewGuid(),
            RegisteredSystemId = "schema-test-system",
            ControlId = "AC-2",
            NarrativeType = "Technical",
            BeforeContent = new string('\u03a9', 8000),
            ProposedContent = new string('\u0416', 8000),
            DeduplicationKey = "schema-startup-regression",
            CreatedBy = "schema-test"
        };

        // Act
        await NarrativeLibrarySchemaAdditions.ApplyAsync(db);
        db.NarrativeProposals.Add(proposal);
        await db.SaveChangesAsync();
        await NarrativeLibrarySchemaAdditions.ApplyAsync(db);
        db.ChangeTracker.Clear();
        var persisted = await db.NarrativeProposals.IgnoreQueryFilters().SingleAsync(p => p.Id == proposal.Id);
        var contentColumns = await db.Database.SqlQueryRaw<string>("""
            SELECT c.name AS Value FROM sys.columns c
            JOIN sys.types t ON c.user_type_id = t.user_type_id
            WHERE c.object_id = OBJECT_ID('dbo.NarrativeProposals')
              AND c.name IN ('BeforeContent', 'ProposedContent')
              AND t.name = 'nvarchar' AND c.max_length = -1 AND c.is_nullable = 0
            """).ToListAsync();

        // Assert
        persisted.BeforeContent.Should().Be(proposal.BeforeContent);
        persisted.ProposedContent.Should().Be(proposal.ProposedContent);
        persisted.TenantId.Should().Be(proposal.TenantId);
        persisted.Revision.Should().Be(1);
        contentColumns.Should().BeEquivalentTo(["BeforeContent", "ProposedContent"]);
        var model = db.Model.FindEntityType(typeof(NarrativeProposal))!;
        foreach (var name in new[] { nameof(NarrativeProposal.BeforeContent), nameof(NarrativeProposal.ProposedContent) })
        {
            model.FindProperty(name)!.GetMaxLength().Should().Be(8000);
            model.FindProperty(name)!.GetColumnType().Should().Be("nvarchar(max)");
        }
    }
}
