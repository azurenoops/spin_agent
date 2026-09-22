using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Data;

public class NarrativeLibrarySchemaAdditionsTests
{
    [Fact]
    public async Task Sqlite_OrganizationOriginUpgradePreservesExistingSystemReference()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("""
            DROP TABLE "NarrativeReferences";
            CREATE TABLE "NarrativeReferences" (
                "Id" TEXT NOT NULL PRIMARY KEY, "TenantId" TEXT NOT NULL, "ReferenceKey" TEXT NOT NULL,
                "Title" TEXT NOT NULL, "Scope" TEXT NOT NULL, "ScopeId" TEXT NOT NULL,
                "ImportedForSystemId" TEXT NOT NULL, "SourceName" TEXT NOT NULL, "SourceSha256" TEXT NOT NULL,
                "OriginalPassagesJson" TEXT NOT NULL, "PassagesJson" TEXT NOT NULL,
                "Version" INTEGER NOT NULL, "Revision" INTEGER NOT NULL, "IsPublished" INTEGER NOT NULL,
                "CreatedAt" TEXT NOT NULL, "CreatedBy" TEXT NOT NULL, "PublishedAt" TEXT NULL, "PublishedBy" TEXT NULL
            );
            """);
        var tenantId = Guid.NewGuid();
        var legacy = new NarrativeReference { TenantId = tenantId, Title = "Existing", Scope = "System",
            ScopeId = "system-1", ImportedForSystemId = "system-1", OriginalPassagesJson = """[{"Content":"Original claim"}]""" };
        db.NarrativeReferences.Add(legacy);
        await db.SaveChangesAsync();

        // Act
        await NarrativeLibrarySchemaAdditions.ApplyAsync(db);
        await NarrativeLibrarySchemaAdditions.ApplyAsync(db);
        db.NarrativeReferences.Add(new() { TenantId = tenantId, Title = "Organization", Scope = "Organization", ScopeId = tenantId.ToString() });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var persisted = await db.NarrativeReferences.SingleAsync(item => item.Id == legacy.Id);

        // Assert
        persisted.ImportedForSystemId.Should().Be("system-1");
        persisted.OriginalPassagesJson.Should().Contain("Original claim");
        (await db.NarrativeReferences.SingleAsync(item => item.Title == "Organization")).ImportedForSystemId.Should().BeNull();
        (await db.Database.SqlQueryRaw<string>("""SELECT name AS Value FROM pragma_index_list('NarrativeReferences')""").ToListAsync())
            .Should().Contain("IX_NarrativeReferences_TenantId_Scope_ScopeId_Title_Version");
    }

    [Fact]
    public async Task Sqlite_QueueAndDeliveryReceiptRollBackWithCallerSourceTransaction()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options;
        await using var db = new AtoCopilotContext(options);
        await db.Database.EnsureCreatedAsync();
        var tenant = new TenantContext(Guid.NewGuid());
        db.RegisteredSystems.Add(new() { Id = "system-1", TenantId = tenant.TenantId, Name = "Synthetic", HostingEnvironment = "Original" });
        db.ControlImplementations.Add(new() { TenantId = tenant.TenantId, RegisteredSystemId = "system-1", ControlId = "AC-2" });
        await db.SaveChangesAsync();
        var service = new NarrativeProposalService(db, tenant, new NarrativeLibraryService(db, tenant), Mock.Of<IControlNarrativeService>());

        // Act
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            (await db.RegisteredSystems.SingleAsync()).HostingEnvironment = "Changed";
            await db.SaveChangesAsync();
            var result = await service.QueueAsync(new(tenant.TenantId, "system-1", ["AC-2"], ["Technical"],
                "System", "system-1", "source-editor", ImpactId: "transactional-impact"));
            result.ProposalIds.Should().ContainSingle();
            (await db.Set<NarrativeImpactReceipt>().CountAsync()).Should().Be(1);
            await transaction.RollbackAsync();
        }

        // Assert
        db.ChangeTracker.Clear();
        (await db.RegisteredSystems.SingleAsync()).HostingEnvironment.Should().Be("Original");
        (await db.NarrativeProposals.CountAsync()).Should().Be(0);
        (await db.Set<NarrativeImpactReceipt>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Sqlite_ConcurrentReferenceLineageCannotCreateTwoVersionOnes()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options;
        await using var db = new AtoCopilotContext(options);
        await db.Database.EnsureCreatedAsync();
        var tenant = Guid.NewGuid();
        db.NarrativeReferences.Add(new() { TenantId = tenant, Scope = "System", ScopeId = "synthetic-system",
            Title = "Same reference", Version = 1 });
        await db.SaveChangesAsync();
        await using var other = new AtoCopilotContext(options);
        other.NarrativeReferences.Add(new() { TenantId = tenant, Scope = "System", ScopeId = "synthetic-system",
            Title = "Same reference", Version = 1 });

        // Act
        var save = () => other.SaveChangesAsync();

        // Assert
        await save.Should().ThrowAsync<DbUpdateException>();
        (await db.NarrativeReferences.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Sqlite_ConcurrentProposalRevisionsCannotOverwriteWinner()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options;
        await using var first = new AtoCopilotContext(options);
        await first.Database.EnsureCreatedAsync();
        first.NarrativeProposals.Add(new() { TenantId = Guid.NewGuid(), RegisteredSystemId = "synthetic-system",
            ControlId = "AC-2", NarrativeType = "Policy", DeduplicationKey = "concurrent" });
        await first.SaveChangesAsync();
        await using var second = new AtoCopilotContext(options);
        var winner = await first.NarrativeProposals.SingleAsync();
        var loser = await second.NarrativeProposals.SingleAsync();
        winner.Revision++;
        winner.Status = "NeedsRevision";
        await first.SaveChangesAsync();
        loser.Revision++;
        loser.Status = "Approved";

        // Act
        var save = () => second.SaveChangesAsync();

        // Assert
        await save.Should().ThrowAsync<DbUpdateConcurrencyException>();
        first.ChangeTracker.Clear();
        (await first.NarrativeProposals.SingleAsync()).Status.Should().Be("NeedsRevision");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sqlite_FreshAndRerunPersistChangeImpactMetadata(bool upgradeExistingReceipt)
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("""DROP TABLE "NarrativeImpactReceipts"; DROP TABLE "NarrativeProposals"; DROP TABLE "NarrativeReferences";""");
        await NarrativeLibrarySchemaAdditions.ApplyAsync(db);
        if (upgradeExistingReceipt)
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "NarrativeImpactReceipts" DROP COLUMN "SourceContextJson";""");
        await NarrativeLibrarySchemaAdditions.ApplyAsync(db);
        var row = new NarrativeProposal { TenantId = Guid.NewGuid(), RegisteredSystemId = "synthetic-system",
            ControlId = "AC-2", NarrativeType = "Technical", Status = "GenerationFailed", DeduplicationKey = "synthetic-dedup",
            ChangeSourceKind = "CspCapability", ChangeSourceId = Guid.NewGuid().ToString(), GenerationErrorCode = "GENERATION_FAILED" };

        // Act
        db.NarrativeProposals.Add(row);
        db.Set<NarrativeImpactReceipt>().Add(new() { Id = "synthetic-receipt", TenantId = row.TenantId,
            ImpactId = "synthetic-impact", RegisteredSystemId = row.RegisteredSystemId,
            ControlId = row.ControlId, NarrativeType = row.NarrativeType, NarrativeProposalId = row.Id,
            SourceKind = "CspCapability", SourceId = "historical-capability", SourceActor = "source-editor",
            SourceContextJson = """{"Cause":"SubscriptionRemoved","SourceRevision":"synthetic-hash"}""" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var persisted = await db.NarrativeProposals.SingleAsync();

        // Assert
        persisted.ChangeSourceKind.Should().Be("CspCapability");
        persisted.GenerationErrorCode.Should().Be("GENERATION_FAILED");
        persisted.Id.Should().Be(row.Id);
        (await db.Set<NarrativeImpactReceipt>().SingleAsync()).ImpactId.Should().Be("synthetic-impact");
        (await db.Set<NarrativeImpactReceipt>().SingleAsync()).SourceContextJson.Should().Contain("SubscriptionRemoved");
        (await db.Set<NarrativeImpactReceipt>().SingleAsync()).SourceActor.Should().Be("source-editor");
    }
}
