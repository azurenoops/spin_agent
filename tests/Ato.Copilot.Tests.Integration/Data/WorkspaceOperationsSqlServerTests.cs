using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Workspaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Data;

public sealed class WorkspaceOperationsSqlServerTests(BoundarySchemaSqlServerFixture fixture)
    : IClassFixture<BoundarySchemaSqlServerFixture>
{
    [SkippableFact]
    public async Task ApplyAsync_WithSqlServerRetries_PreservesLegacyRowsAcrossRepeatStartup()
    {
        // Arrange
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        await using var db = await fixture.CreateDatabaseAsync();
        db.Database.CreateExecutionStrategy().RetriesOnFailure.Should().BeTrue();
        await CreateLegacyTablesAsync(db);

        // Act
        await WorkspaceOperationsSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        var first = await db.OrganizationNameReservations.AsNoTracking().ToListAsync();
        await WorkspaceOperationsSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        var second = await db.OrganizationNameReservations.AsNoTracking().ToListAsync();

        // Assert
        second.Should().BeEquivalentTo(first);
        second.Should().ContainSingle(x => x.NormalizedName == "ÉCOLE");
        second.Should().ContainSingle(x => x.NormalizedName == "MISSION");
        (await db.Tenants.IgnoreQueryFilters().CountAsync()).Should().Be(3);
        var session = await db.Set<TenantSupportSession>().IgnoreQueryFilters().SingleAsync();
        session.Id.Should().Be(Guid.Parse("44444444-4444-4444-4444-444444444444"));
        session.Reason.Should().Be("Legacy authorized support session");
        session.Acknowledged.Should().BeTrue();
        db.Database.CurrentTransaction.Should().BeNull();
    }

    [SkippableFact]
    public async Task PublishAsync_WithSqlServerRetries_ReachesRevisionValidation()
    {
        // Arrange
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        await using var setup = await fixture.CreateDatabaseAsync();
        await CreateLegacyTablesAsync(setup);
        foreach (var batch in WorkspaceOperationsSchemaAdditions.SqlServerBatches)
            await setup.Database.ExecuteSqlRawAsync(batch);
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseSqlServer(setup.Database.GetConnectionString(), sql => sql.EnableRetryOnFailure())
            .Options;
        var factory = new Mock<IDbContextFactory<AtoCopilotContext>>();
        factory.Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AtoCopilotContext(options));
        var service = new WorkspaceOperationsService(factory.Object);

        // Act
        var act = () => service.PublishAsync(Guid.NewGuid(),
            new(1, 1, Guid.NewGuid(), new string('a', 64), "retry-strategy-regression"),
            "schema-test", default);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("Working revision was not found.");
        (await setup.ProviderCapabilityReleases.CountAsync()).Should().Be(0);
    }

    private static Task CreateLegacyTablesAsync(AtoCopilotContext db) =>
        db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE dbo.Tenants (
                Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, DisplayName NVARCHAR(256) NOT NULL);
            CREATE TABLE dbo.TenantSupportSessions (
                Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                DirectoryTenantId UNIQUEIDENTIFIER NOT NULL,
                ObjectId UNIQUEIDENTIFIER NOT NULL,
                TargetTenantId UNIQUEIDENTIFIER NOT NULL,
                IssuedAt DATETIMEOFFSET NOT NULL, ExpiresAt DATETIMEOFFSET NOT NULL,
                RevokedAt DATETIMEOFFSET NULL, RevocationReason NVARCHAR(500) NULL);
            INSERT INTO dbo.Tenants(Id,DisplayName) VALUES
                ('11111111-1111-1111-1111-111111111111',NCHAR(160) + N'école' + NCHAR(160)),
                ('22222222-2222-2222-2222-222222222222',N'ÉCOLE'),
                ('33333333-3333-3333-3333-333333333333',N'Mission');
            INSERT INTO dbo.TenantSupportSessions(
                Id,DirectoryTenantId,ObjectId,TargetTenantId,IssuedAt,ExpiresAt)
            VALUES(
                '44444444-4444-4444-4444-444444444444',
                '55555555-5555-5555-5555-555555555555',
                '66666666-6666-6666-6666-666666666666',
                '11111111-1111-1111-1111-111111111111',
                '2026-01-01T00:00:00+00:00','2026-01-01T01:00:00+00:00');
            """);
}
