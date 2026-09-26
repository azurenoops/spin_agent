using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Models.PackageImports;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Models.Tenancy.Attributes;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Data;

public sealed class ProviderAuthorizationSchemaTests
{
    private static readonly Type[] RowTypes = typeof(ProviderOwnedRow).Assembly.GetTypes()
        .Where(t => t.IsSubclassOf(typeof(ProviderOwnedRow)) && !t.IsAbstract).ToArray();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Model_MapsEveryAggregateAsAnIndependentScopedRoot(bool sqlServer)
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AtoCopilotContext>();
        if (sqlServer)
            options.UseSqlServer("Server=unused;Database=metadata-only;Integrated Security=true");
        else
            options.UseSqlite("Data Source=:memory:");
        using var db = new AtoCopilotContext(options.Options);

        // Act
        var model = db.Model;

        // Assert
        model.FindEntityType(typeof(ProviderOwnedRow)).Should().BeNull();
        RowTypes.Should().HaveCount(20);
        foreach (var type in RowTypes)
        {
            var entity = model.FindEntityType(type);
            entity.Should().NotBeNull(type.Name);
            entity!.BaseType.Should().BeNull(type.Name);
            entity.GetTableName().Should().Be(type.Name + "s");
            entity.GetProperties().Select(p => p.Name).Should().BeEquivalentTo(
                type.GetProperties().Where(p => p.GetCustomAttribute<NotMappedAttribute>() is null).Select(p => p.Name));
            entity.FindProperty(nameof(ProviderOwnedRow.Revision))!.IsConcurrencyToken.Should().BeTrue();
            entity.GetQueryFilter().Should().NotBeNull(type.Name);
            entity.GetForeignKeys().Should().OnlyContain(f => f.DeleteBehavior == DeleteBehavior.Restrict);
            type.GetCustomAttribute<GlobalReferenceAttribute>().Should().BeNull();
        }
    }

    [Fact]
    public async Task ApplyAsync_UpgradesPopulatedBaseline_RepeatedStartupPreservesAllRowsAndColumns()
    {
        // Arrange
        await using var connection = await OpenBaselineAsync();
        await using var db = CreateDb(connection);
        var package = new CspPackage { ProviderId = Guid.NewGuid(), IdempotencyKey = "original", Name = "Original receipt" };
        db.CspPackages.Add(package);
        await db.SaveChangesAsync();

        // Act
        await ApplyAsync(db);
        (await db.Set<ProviderOffering>().CountAsync()).Should().Be(0, "legacy receipts must not receive fabricated offerings");
        var offering = await AddOfferingAsync(db, package.ProviderId);
        var finding = new ProviderFinding
        {
            ProviderId = offering.ProviderId, OfferingId = offering.Id, Title = "Retained finding"
        };
        db.Add(finding);
        await db.SaveChangesAsync();
        await ApplyAsync(db);
        db.ChangeTracker.Clear();

        // Assert
        var retainedPackage = await db.CspPackages.SingleAsync();
        retainedPackage.Name.Should().Be("Original receipt");
        retainedPackage.PublicationState.Should().Be("Unpublished");
        retainedPackage.ProcessingState.Should().Be("Received");
        (await db.Set<ProviderFinding>().SingleAsync()).Title.Should().Be("Retained finding");
        foreach (var type in RowTypes)
        {
            var table = db.Model.FindEntityType(type)!.GetTableName();
            var columns = await db.Database.SqlQuery<string>(
                $"SELECT name AS Value FROM pragma_table_info({table})").ToListAsync();
            columns.Should().BeEquivalentTo(db.Model.FindEntityType(type)!.GetProperties().Select(p => p.Name));
        }
        (await db.Database.SqlQueryRaw<string>("SELECT Name AS Value FROM LegacyBaseline").SingleAsync())
            .Should().Be("Retained baseline");
    }

    [Fact]
    public async Task ApplyAsync_EnforcesOperationScopeVersionAndPackageAssociationUniqueness()
    {
        // Arrange
        await using var connection = await OpenBaselineAsync();
        await using var db = CreateDb(connection);
        await ApplyAsync(db);
        var offering = await AddOfferingAsync(db);
        db.Add(new ProviderAuthorizationOperation
        {
            ProviderId = offering.ProviderId, OfferingId = offering.Id, OperationScope = "create", IdempotencyKey = "key"
        });
        var boundary = new ProviderBoundaryRevision { ProviderId = offering.ProviderId, OfferingId = offering.Id };
        db.Add(boundary);
        var package = new CspPackage { ProviderId = offering.ProviderId, IdempotencyKey = "receipt" };
        db.Add(package);
        await db.SaveChangesAsync();
        var series = Guid.NewGuid();
        db.Add(new ProviderPackageVersion
        {
            ProviderId = offering.ProviderId, OfferingId = offering.Id, PackageId = package.Id,
            BoundaryRevisionId = boundary.Id, SeriesId = series, Version = 1
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // Act
        Func<Task> DuplicateAsync(object row) => async () =>
        {
            db.Add(row);
            try { await db.SaveChangesAsync(); }
            finally { db.ChangeTracker.Clear(); }
        };

        // Assert
        await DuplicateAsync(new ProviderAuthorizationOperation
        {
            ProviderId = offering.ProviderId, OfferingId = offering.Id, OperationScope = "create", IdempotencyKey = "key"
        }).Should().ThrowAsync<DbUpdateException>();
        await DuplicateAsync(new ProviderBoundaryRevision
        {
            ProviderId = offering.ProviderId, OfferingId = offering.Id, Revision = 1
        }).Should().ThrowAsync<DbUpdateException>();
        await DuplicateAsync(new ProviderPackageVersion
        {
            ProviderId = offering.ProviderId, OfferingId = offering.Id, PackageId = package.Id,
            BoundaryRevisionId = boundary.Id, SeriesId = Guid.NewGuid(), Version = 2
        }).Should().ThrowAsync<DbUpdateException>();
        var replacementPackage = new CspPackage { ProviderId = offering.ProviderId, IdempotencyKey = "replacement" };
        db.Add(replacementPackage);
        await db.SaveChangesAsync();
        await DuplicateAsync(new ProviderPackageVersion
        {
            ProviderId = offering.ProviderId, OfferingId = offering.Id, PackageId = replacementPackage.Id,
            BoundaryRevisionId = boundary.Id, SeriesId = series, Version = 1
        }).Should().ThrowAsync<DbUpdateException>();
        db.Add(new ProviderAuthorizationOperation
        {
            ProviderId = offering.ProviderId, OfferingId = offering.Id, OperationScope = "other", IdempotencyKey = "key"
        });
        await db.SaveChangesAsync();
        (await db.Set<ProviderAuthorizationOperation>().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task ApplyAsync_RejectsCrossProviderAndCrossOfferingReferences_AndRestrictsDeletes()
    {
        // Arrange
        await using var connection = await OpenBaselineAsync();
        await using var db = CreateDb(connection);
        await ApplyAsync(db);
        var first = await AddOfferingAsync(db);
        var second = await AddOfferingAsync(db);
        var boundary = new ProviderBoundaryRevision { ProviderId = first.ProviderId, OfferingId = first.Id };
        db.Add(boundary);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // Act
        db.Add(new ProviderFinding { ProviderId = second.ProviderId, OfferingId = first.Id });
        var crossProvider = () => db.SaveChangesAsync();

        // Assert
        await crossProvider.Should().ThrowAsync<DbUpdateException>();
        db.ChangeTracker.Clear();
        db.Add(new ProviderBoundaryRevision
        {
            ProviderId = second.ProviderId, OfferingId = second.Id, PredecessorId = boundary.Id
        });
        var crossOffering = () => db.SaveChangesAsync();
        await crossOffering.Should().ThrowAsync<DbUpdateException>();
        db.ChangeTracker.Clear();
        var delete = () => db.Set<ProviderOffering>().Where(x => x.Id == first.Id).ExecuteDeleteAsync();
        await delete.Should().ThrowAsync<SqliteException>();
        var deleteProvider = () => db.CspProfiles.Where(x => x.Id == first.ProviderId).ExecuteDeleteAsync();
        await deleteProvider.Should().ThrowAsync<SqliteException>();
    }

    [Fact]
    public async Task MigratedSchema_RevisionTokenRejectsStaleWriters()
    {
        // Arrange
        await using var connection = await OpenBaselineAsync();
        await using var first = CreateDb(connection);
        await ApplyAsync(first);
        var offering = await AddOfferingAsync(first);
        await using var second = CreateDb(connection);
        var stale = await second.Set<ProviderOffering>().SingleAsync();
        offering.Revision++;
        offering.Name = "Updated";
        await first.SaveChangesAsync();
        stale.Name = "Stale overwrite";
        stale.Revision++;

        // Act
        var write = () => second.SaveChangesAsync();

        // Assert
        await write.Should().ThrowAsync<DbUpdateConcurrencyException>();
        first.ChangeTracker.Clear();
        (await first.Set<ProviderOffering>().SingleAsync()).Name.Should().Be("Updated");
    }

    [Fact]
    public async Task QueryFilters_KeepProviderRowsPrivate_AndMissionRowsTenantScoped()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        var accessor = new TenantContextAccessor();
        await using var db = new AtoCopilotContext(
            new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options, accessor);
        await db.Database.EnsureCreatedAsync();
        await ApplyAsync(db);
        var profile = new CspProfile { DisplayName = "Provider", LegalEntityName = "Provider" };
        var offering = new ProviderOffering { ProviderId = profile.Id, Name = "Offering" };
        offering.OfferingId = offering.Id;
        db.AddRange(profile, offering);
        await db.SaveChangesAsync();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var scope = new ProviderHostingScopeRevision { ProviderId = offering.ProviderId, OfferingId = offering.Id };
        db.Add(scope);
        await db.SaveChangesAsync();
        foreach (var tenantId in new[] { tenantA, tenantB })
        {
            db.Tenants.Add(new Tenant { Id = tenantId, DisplayName = "Mission" });
            await db.SaveChangesAsync();
            var assignment = new ProviderHostingAssignment
            {
                ProviderId = offering.ProviderId, OfferingId = offering.Id, TargetTenantId = tenantId,
                SystemId = "mission", HostingScopeRevisionId = scope.Id
            };
            db.Add(assignment);
            await db.SaveChangesAsync();
            db.Add(new MissionProviderRelationshipReview
            {
                ProviderId = offering.ProviderId, OfferingId = offering.Id, TenantId = tenantId,
                SystemId = "mission", AssignmentId = assignment.Id, AssignmentRevision = assignment.Revision
            });
            await db.SaveChangesAsync();
        }
        db.ChangeTracker.Clear();

        // Act
        using (accessor.Push(new TenantContext(tenantA)))
        {
            // Assert
            (await db.Set<ProviderOffering>().CountAsync()).Should().Be(0);
            (await db.Set<MissionProviderRelationshipReview>().ToListAsync()).Should().ContainSingle(x => x.TenantId == tenantA);
        }
        using (accessor.Push(new TenantContext(tenantB)))
        {
            (await db.Set<ProviderOffering>().CountAsync()).Should().Be(0);
            (await db.Set<MissionProviderRelationshipReview>().ToListAsync()).Should().ContainSingle(x => x.TenantId == tenantB);
        }
        using (accessor.Push(new TenantContext(Guid.Empty, isCspAdmin: true)))
            (await db.Set<ProviderOffering>().CountAsync()).Should().Be(1);
        using (accessor.Push(new TenantContext(Guid.Empty, isCspAdmin: true, impersonatedTenantId: tenantA)))
            (await db.Set<ProviderOffering>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task QueryFilters_MissionRowsRequireCurrentWorkspaceSystemAccess()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        var accessor = new TenantContextAccessor();
        await using var db = new AtoCopilotContext(
            new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options, accessor);
        await db.Database.EnsureCreatedAsync();
        var tenant = new Tenant { DisplayName = "Mission" };
        var provider = new CspProfile { DisplayName = "Provider" };
        var offering = new ProviderOffering { ProviderId = provider.Id };
        offering.OfferingId = offering.Id;
        var scope = new ProviderHostingScopeRevision { ProviderId = provider.Id, OfferingId = offering.Id };
        var person = new Person { TenantId = tenant.Id, DisplayName = "Reviewer", Email = "reviewer@example.invalid" };
        db.AddRange(tenant, provider, offering, scope, person);
        await db.SaveChangesAsync();
        foreach (var systemId in new[] { "allowed", "denied" })
        {
            db.RegisteredSystems.Add(new RegisteredSystem { Id = systemId, TenantId = tenant.Id, Name = systemId });
            var assignment = new ProviderHostingAssignment
            {
                ProviderId = provider.Id, OfferingId = offering.Id, TargetTenantId = tenant.Id, SystemId = systemId,
                HostingScopeRevisionId = scope.Id
            };
            db.Add(assignment);
            await db.SaveChangesAsync();
            db.Add(new MissionProviderRelationshipReview
            {
                TenantId = tenant.Id, SystemId = systemId, ProviderId = provider.Id,
                OfferingId = offering.Id, AssignmentId = assignment.Id
            });
            await db.SaveChangesAsync();
        }
        db.SystemRoleAssignments.Add(new SystemRoleAssignment
        {
            TenantId = tenant.Id, PersonId = person.Id, RegisteredSystemId = "allowed",
            Role = OrganizationRole.AuthorizingOfficial
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // Act
        using var context = accessor.Push(new TenantContext(tenant.Id)
        {
            PersonId = person.Id, IsWorkspaceRequest = true
        });
        var rows = await db.Set<MissionProviderRelationshipReview>().ToListAsync();

        // Assert
        rows.Should().ContainSingle(x => x.SystemId == "allowed");
    }

    [Fact]
    public void ScopeAssertion_RecognizesPrivateProviderRowsWithoutGlobalExemptions()
    {
        // Arrange
        using var db = new AtoCopilotContext(
            new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite("Data Source=:memory:").Options);

        // Act
        var error = Record.Exception(db.AssertScopingAttributesPresent);

        // Assert
        // Existing package entities have a separate scoping rollout owned by their implementer.
        if (error is not null)
            error.Message.Should().NotContain("Ato.Copilot.Core.Models.ProviderAuthorizations.");
        RowTypes.Should().OnlyContain(type => !AtoCopilotContext.TenantScopingExceptions.Contains(type));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Scripts_MatchExactModelColumnTypesNullabilityAndIndexes_WithoutDestructiveStatements(bool sqlServer)
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AtoCopilotContext>();
        if (sqlServer)
            options.UseSqlServer("Server=unused;Database=metadata-only;Integrated Security=true");
        else
            options.UseSqlite("Data Source=:memory:");
        using var db = new AtoCopilotContext(options.Options);

        // Act
        var scripts = ProviderAuthorizationSchemaAdditions.Scripts(sqlServer);

        // Assert
        foreach (var type in RowTypes)
        {
            var entity = db.Model.FindEntityType(type)!;
            var table = entity.GetTableName()!;
            var create = scripts.Single(s => s.Contains($"CREATE TABLE {(sqlServer ? "dbo." : "IF NOT EXISTS ")}[{table}]"));
            foreach (var property in entity.GetProperties())
                create.Should().Contain($"{property.Name} {property.GetColumnType()} {(property.IsNullable ? "NULL" : "NOT NULL")}");
            foreach (var index in entity.GetIndexes())
                scripts.Should().Contain(s => s.Contains($"[{index.GetDatabaseName()}]")
                    && s.Contains(index.IsUnique ? "CREATE UNIQUE INDEX" : "CREATE INDEX"));
            create.Should().Contain("ON DELETE NO ACTION");
        }
        var sql = string.Join("\n", scripts);
        sql.Should().NotContain("DROP ").And.NotContain("DELETE FROM").And.NotContain("UPDATE ");
        sql.Should().Contain(sqlServer ? "IF OBJECT_ID" : "IF NOT EXISTS");
    }

    private static AtoCopilotContext CreateDb(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options);

    private static async Task<SqliteConnection> OpenBaselineAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE LegacyBaseline (Name TEXT NOT NULL);
            INSERT INTO LegacyBaseline(Name) VALUES ('Retained baseline');
            """;
        await command.ExecuteNonQueryAsync();
        await using var db = CreateDb(connection);
        await TenantsAndOrganizationsSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await CspPackageSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        return connection;
    }

    private static async Task<ProviderOffering> AddOfferingAsync(AtoCopilotContext db, Guid? providerId = null)
    {
        var offering = new ProviderOffering { ProviderId = providerId ?? Guid.NewGuid(), Name = "Offering" };
        offering.OfferingId = offering.Id;
        db.CspProfiles.Add(new CspProfile { Id = offering.ProviderId, DisplayName = "Provider" });
        db.Add(offering);
        await db.SaveChangesAsync();
        return offering;
    }

    private static Task ApplyAsync(AtoCopilotContext db) =>
        ProviderAuthorizationSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
}
