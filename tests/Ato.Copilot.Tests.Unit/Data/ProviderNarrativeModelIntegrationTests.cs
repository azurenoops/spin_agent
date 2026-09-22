using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Data;

public class ProviderNarrativeModelIntegrationTests
{
    [Fact]
    public void ProductionModel_DiscoversProviderReferencesAndPublicationRelationships()
    {
        // Arrange
        using var db = new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseSqlite("Data Source=:memory:").Options);

        // Act
        var reference = db.Model.FindEntityType(typeof(ProviderNarrativeReference));
        var publication = db.Model.FindEntityType(typeof(ProviderNarrativeReferencePublication));
        var organizationPublication = db.Model.FindEntityType(typeof(NarrativeReferencePublication));

        // Assert
        reference.Should().NotBeNull("provider grounding uses the production context, not a test-derived model");
        publication.Should().NotBeNull();
        reference!.GetTableName().Should().Be("ProviderNarrativeReferences");
        reference.GetQueryFilter().Should().BeNull("provider references have explicit GlobalReference classification");
        reference.FindProperty(nameof(ProviderNarrativeReference.Revision))!.IsConcurrencyToken.Should().BeTrue();
        reference.GetIndexes().Should().Contain(index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(new[] { "CspProfileId", "ReferenceKey", "Version" }));
        publication!.GetForeignKeys().Should().ContainSingle(key =>
            key.PrincipalEntityType == reference && key.DeleteBehavior == DeleteBehavior.Restrict);
        organizationPublication.Should().NotBeNull();
        organizationPublication!.GetQueryFilter().Should().NotBeNull("organization publication rows remain tenant filtered");
    }

    [Fact]
    public async Task ProductionModelAndRepeatSchemaRollout_PersistProviderPublications()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        await NarrativeLibrarySchemaAdditions.ApplyAsync(db);
        await NarrativeLibrarySchemaAdditions.ApplyAsync(db);
        var reference = new ProviderNarrativeReference { CspProfileId = Guid.NewGuid(), Title = "Synthetic provider source" };
        var publication = new ProviderNarrativeReferencePublication
        {
            Id = Guid.NewGuid(), CspProfileId = reference.CspProfileId, ReferenceId = reference.Id,
            Reference = reference, SourceRevision = "synthetic-revision", RecordedBy = "synthetic-actor",
        };

        // Act
        db.Set<ProviderNarrativeReferencePublication>().Add(publication);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // Assert
        var stored = await db.Set<ProviderNarrativeReferencePublication>().Include(row => row.Reference).SingleAsync();
        stored.Reference.Id.Should().Be(reference.Id);
        stored.SourceRevision.Should().Be("synthetic-revision");
        var removeSource = async () =>
        {
            await db.Set<ProviderNarrativeReference>().Where(row => row.Id == reference.Id).ExecuteDeleteAsync();
        };
        await removeSource.Should().ThrowAsync<SqliteException>("durable publication records retain their source");
    }
}
