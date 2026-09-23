using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Data;

public sealed class OrganizationCatalogSqlServerTests(BoundarySchemaSqlServerFixture fixture)
    : IClassFixture<BoundarySchemaSqlServerFixture>
{
    [SkippableFact]
    public async Task ApplyAsync_UpgradesLegacyCatalogAndNameIndexAcrossRepeatedStartup()
    {
        // Arrange
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        await using var db = await fixture.CreateDatabaseAsync();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE dbo.Tenants(Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY);
            CREATE TABLE dbo.SecurityCapabilities(
                Id NVARCHAR(36) NOT NULL PRIMARY KEY,
                TenantId UNIQUEIDENTIFIER NOT NULL, Name NVARCHAR(200) NOT NULL);
            CREATE UNIQUE INDEX IX_SecurityCapability_Name ON dbo.SecurityCapabilities(Name);
            INSERT INTO dbo.SecurityCapabilities VALUES
                ('original','11111111-1111-1111-1111-111111111111',N'Reusable service');
            """);

        // Act
        await OrganizationCatalogSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await OrganizationCatalogSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO dbo.SecurityCapabilities VALUES
                ('second','22222222-2222-2222-2222-222222222222',N'Reusable service');
            """);

        // Assert
        (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM dbo.SecurityCapabilities").SingleAsync())
            .Should().Be(2);
        (await db.OrganizationCatalogEntries.CountAsync()).Should().Be(0);
        (await db.OrganizationCatalogAdditions.CountAsync()).Should().Be(0);
    }
}
