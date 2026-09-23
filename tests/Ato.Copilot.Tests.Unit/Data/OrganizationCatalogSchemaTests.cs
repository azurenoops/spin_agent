using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Models.Tenancy.Attributes;
using Ato.Copilot.Core.Models.Workspaces;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Data;

public sealed class OrganizationCatalogSchemaTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherTenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task LegacyCapabilityNameIndex_BecomesTenantScopedWithoutLosingRows()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await InitializeAsync(db, legacy: true);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE SecurityCapabilities(Id TEXT PRIMARY KEY, TenantId TEXT NOT NULL, Name TEXT NOT NULL);
            CREATE UNIQUE INDEX IX_SecurityCapability_Name ON SecurityCapabilities(Name);
            INSERT INTO SecurityCapabilities VALUES ('original','first-tenant','Shared name');
            """);

        // Act
        await OrganizationCatalogSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await OrganizationCatalogSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO SecurityCapabilities VALUES ('second','second-tenant','Shared name')");

        // Assert
        Assert.Equal(2, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM SecurityCapabilities").SingleAsync());
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO SecurityCapabilities VALUES ('duplicate','first-tenant','Shared name')"));
    }

    [Fact]
    public async Task EnsureCreated_CreatesOrganizationCatalogTables()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options;
        await using var db = new AtoCopilotContext(options);

        // Act
        await db.Database.EnsureCreatedAsync();

        // Assert
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM OrganizationCatalogEntries";
        Assert.Equal(0L, await command.ExecuteScalarAsync());
        command.CommandText = "SELECT COUNT(*) FROM OrganizationCatalogAdditions";
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task ApplyAsync_UpgradesLegacyDatabaseAndPreservesDataOnRepeat()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await InitializeAsync(db, legacy: true);
        var entry = CreateEntry(TenantId);
        entry.OrganizationContribution = "Organization-operated key rotation";
        entry.OrganizationOwner = "Security operations";
        entry.SupportingComponentsJson = """[{"source":"provider","recordId":"component-1"}]""";
        entry.Revision = 7;
        var addition = CreateAddition(TenantId);
        addition.IntentJson = "{\"description\":\"" + new string('x', 5000) + "\"}";
        addition.ResultJson = """{"created":true,"recordId":"catalog-record"}""";
        db.OrganizationCatalogEntries.Add(entry);
        db.OrganizationCatalogAdditions.Add(addition);
        await db.SaveChangesAsync();
        var entryColumns = await ReadColumnsAsync(connection, "OrganizationCatalogEntries");
        var additionColumns = await ReadColumnsAsync(connection, "OrganizationCatalogAdditions");

        // Act
        await OrganizationCatalogSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await OrganizationCatalogSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        db.ChangeTracker.Clear();

        // Assert
        var storedEntry = await db.OrganizationCatalogEntries.SingleAsync();
        Assert.Equal(entry.Id, storedEntry.Id);
        Assert.Equal(entry.OrganizationContribution, storedEntry.OrganizationContribution);
        Assert.Equal(entry.OrganizationOwner, storedEntry.OrganizationOwner);
        Assert.Equal(entry.SupportingComponentsJson, storedEntry.SupportingComponentsJson);
        Assert.Equal(entry.Revision, storedEntry.Revision);
        Assert.Equal(entry.CreatedAt, storedEntry.CreatedAt);
        Assert.Equal(entry.UpdatedAt, storedEntry.UpdatedAt);
        Assert.Equal(entry.CreatedBy, storedEntry.CreatedBy);
        Assert.Equal(entry.UpdatedBy, storedEntry.UpdatedBy);
        var storedAddition = await db.OrganizationCatalogAdditions.SingleAsync();
        Assert.Equal(addition.Id, storedAddition.Id);
        Assert.Equal(addition.IdempotencyKey, storedAddition.IdempotencyKey);
        Assert.Equal(addition.IntentJson, storedAddition.IntentJson);
        Assert.Equal(addition.ResultJson, storedAddition.ResultJson);
        Assert.Equal(addition.CreatedAt, storedAddition.CreatedAt);
        Assert.Equal(addition.CreatedBy, storedAddition.CreatedBy);
        Assert.Equal(entryColumns, await ReadColumnsAsync(connection, "OrganizationCatalogEntries"));
        Assert.Equal(additionColumns, await ReadColumnsAsync(connection, "OrganizationCatalogAdditions"));
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DisplayName FROM Tenants ORDER BY Id LIMIT 1";
        Assert.Equal("Legacy organization", await command.ExecuteScalarAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CatalogEntries_EnforceTenantSourceTypeRecordUniqueness(bool legacy)
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await InitializeAsync(db, legacy);
        var otherSource = CreateEntry(TenantId);
        otherSource.Source = "local";
        var otherType = CreateEntry(TenantId);
        otherType.RecordType = "component";
        var otherRecord = CreateEntry(TenantId);
        otherRecord.RecordId = "another-record";
        db.OrganizationCatalogEntries.AddRange(
            CreateEntry(TenantId), CreateEntry(OtherTenantId), otherSource, otherType, otherRecord);
        await db.SaveChangesAsync();

        // Act
        db.OrganizationCatalogEntries.Add(CreateEntry(TenantId));
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        // Assert
        Assert.Equal(2067, Assert.IsType<SqliteException>(exception.InnerException).SqliteExtendedErrorCode);
        db.ChangeTracker.Clear();
        Assert.Equal(5, await db.OrganizationCatalogEntries.CountAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CatalogAdditions_EnforceTenantScopedIdempotency(bool legacy)
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await InitializeAsync(db, legacy);
        db.OrganizationCatalogAdditions.AddRange(CreateAddition(TenantId), CreateAddition(OtherTenantId));
        await db.SaveChangesAsync();

        // Act
        db.OrganizationCatalogAdditions.Add(CreateAddition(TenantId));
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        // Assert
        Assert.Equal(2067, Assert.IsType<SqliteException>(exception.InnerException).SqliteExtendedErrorCode);
        db.ChangeTracker.Clear();
        Assert.Equal(2, await db.OrganizationCatalogAdditions.CountAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CatalogTables_RequireExistingTenantAndCascadeOnlyItsRows(bool legacy)
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await InitializeAsync(db, legacy);
        db.OrganizationCatalogEntries.AddRange(CreateEntry(TenantId), CreateEntry(OtherTenantId));
        db.OrganizationCatalogAdditions.AddRange(CreateAddition(TenantId), CreateAddition(OtherTenantId));
        await db.SaveChangesAsync();

        // Act
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Tenants WHERE Id = {TenantId}");
        db.ChangeTracker.Clear();

        // Assert
        Assert.Equal(OtherTenantId, (await db.OrganizationCatalogEntries.SingleAsync()).TenantId);
        Assert.Equal(OtherTenantId, (await db.OrganizationCatalogAdditions.SingleAsync()).TenantId);
        db.OrganizationCatalogEntries.Add(CreateEntry(TenantId));
        var entryException = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(787, Assert.IsType<SqliteException>(entryException.InnerException).SqliteExtendedErrorCode);
        db.ChangeTracker.Clear();
        db.OrganizationCatalogAdditions.Add(CreateAddition(TenantId));
        var additionException = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(787, Assert.IsType<SqliteException>(additionException.InnerException).SqliteExtendedErrorCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CatalogEntry_RevisionRejectsStaleUpdates(bool legacy)
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await InitializeAsync(db, legacy);
        db.OrganizationCatalogEntries.Add(CreateEntry(TenantId));
        await db.SaveChangesAsync();
        await using var staleDb = CreateContext(connection);
        var staleEntry = await staleDb.OrganizationCatalogEntries.SingleAsync();
        var currentEntry = await db.OrganizationCatalogEntries.SingleAsync();
        currentEntry.OrganizationOwner = "Updated owner";
        currentEntry.Revision++;
        await db.SaveChangesAsync();

        // Act
        staleEntry.OrganizationOwner = "Stale owner";
        staleEntry.Revision++;
        var exception = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleDb.SaveChangesAsync());

        // Assert
        Assert.Single(exception.Entries);
        Assert.Equal("Updated owner",
            await db.OrganizationCatalogEntries.Select(x => x.OrganizationOwner).SingleAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Model_RegistersExactTenantScopedCatalogContract(bool sqlServer)
    {
        // Arrange
        var builder = new DbContextOptionsBuilder<AtoCopilotContext>();
        if (sqlServer)
            builder.UseSqlServer("Server=localhost;Database=CatalogModelOnly;Integrated Security=True");
        else
            builder.UseSqlite("Data Source=:memory:");
        using var db = new AtoCopilotContext(builder.Options);

        // Act
        var entry = db.Model.FindEntityType(typeof(OrganizationCatalogEntry))!;
        var addition = db.Model.FindEntityType(typeof(OrganizationCatalogAddition))!;

        // Assert
        AssertCatalogModel(entry, "OrganizationCatalogEntries",
            ["TenantId", "Source", "RecordType", "RecordId"], 13);
        AssertCatalogModel(addition, "OrganizationCatalogAdditions",
            ["TenantId", "IdempotencyKey"], 7);
        Assert.True(entry.FindProperty("Revision")!.IsConcurrencyToken);
        Assert.Equal(typeof(long), entry.FindProperty("Revision")!.ClrType);
        Assert.Equal(16, entry.FindProperty("Source")!.GetMaxLength());
        Assert.Equal(16, entry.FindProperty("RecordType")!.GetMaxLength());
        Assert.Equal(36, entry.FindProperty("RecordId")!.GetMaxLength());
        Assert.Equal(2000, entry.FindProperty("OrganizationContribution")!.GetMaxLength());
        Assert.Equal(200, entry.FindProperty("OrganizationOwner")!.GetMaxLength());
        Assert.Equal(200, entry.FindProperty("CreatedBy")!.GetMaxLength());
        Assert.Equal(200, entry.FindProperty("UpdatedBy")!.GetMaxLength());
        Assert.Equal(100, addition.FindProperty("IdempotencyKey")!.GetMaxLength());
        Assert.Equal(200, addition.FindProperty("CreatedBy")!.GetMaxLength());
        var jsonType = sqlServer ? "nvarchar(max)" : "TEXT";
        Assert.Equal(jsonType, entry.FindProperty("SupportingComponentsJson")!.GetColumnType());
        Assert.Equal(jsonType, addition.FindProperty("IntentJson")!.GetColumnType());
        Assert.Equal(jsonType, addition.FindProperty("ResultJson")!.GetColumnType());
    }

    [Fact]
    public async Task ApplyAsync_AfterEnsureCreatedPreservesColumnAndIndexParity()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await InitializeAsync(db, legacy: false);
        var entryColumns = await ReadColumnsAsync(connection, "OrganizationCatalogEntries");
        var additionColumns = await ReadColumnsAsync(connection, "OrganizationCatalogAdditions");

        // Act
        await OrganizationCatalogSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await OrganizationCatalogSchemaAdditions.ApplyAsync(db, NullLogger.Instance);

        // Assert
        Assert.Equal(entryColumns, await ReadColumnsAsync(connection, "OrganizationCatalogEntries"));
        Assert.Equal(additionColumns, await ReadColumnsAsync(connection, "OrganizationCatalogAdditions"));
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'index' AND sql LIKE 'CREATE UNIQUE INDEX%'
                AND tbl_name IN ('OrganizationCatalogEntries', 'OrganizationCatalogAdditions')
            """;
        Assert.Equal(2L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public void SqlServerScript_HasGuardedTablesIndexesAndMatchingColumnTypes()
    {
        // Arrange
        var script = OrganizationCatalogSchemaAdditions.SqlServerScript;

        // Act
        var normalized = script.ToUpperInvariant();

        // Assert
        foreach (var table in new[] { "ORGANIZATIONCATALOGENTRIES", "ORGANIZATIONCATALOGADDITIONS" })
        {
            Assert.Contains($"IF OBJECT_ID(N'DBO.{table}', N'U') IS NULL", normalized);
            Assert.Contains($"AND OBJECT_ID = OBJECT_ID(N'DBO.{table}')", normalized);
        }
        Assert.Contains("SUPPORTINGCOMPONENTSJSON NVARCHAR(MAX) NOT NULL", normalized);
        Assert.Contains("INTENTJSON NVARCHAR(MAX) NOT NULL", normalized);
        Assert.Contains("RESULTJSON NVARCHAR(MAX) NOT NULL", normalized);
        Assert.Contains("ORGANIZATIONCONTRIBUTION NVARCHAR(2000) NOT NULL", normalized);
        Assert.Contains("REVISION BIGINT NOT NULL", normalized);
        Assert.Contains("REFERENCES DBO.TENANTS(ID) ON DELETE CASCADE", normalized);
        Assert.DoesNotContain("SYSTEMID", normalized);
        Assert.DoesNotContain("DROP TABLE", normalized);
        Assert.Contains("DROP INDEX IX_SECURITYCAPABILITY_NAME ON DBO.SECURITYCAPABILITIES", normalized);
        Assert.Contains("ON DBO.SECURITYCAPABILITIES(TENANTID, NAME)", normalized);
    }

    [Fact]
    public void Models_InitializeIdentifiersAuditTimestampsAndSupportingComponents()
    {
        // Arrange
        var before = DateTimeOffset.UtcNow;

        // Act
        var entry = new OrganizationCatalogEntry();
        var addition = new OrganizationCatalogAddition();
        var after = DateTimeOffset.UtcNow;

        // Assert
        Assert.NotEqual(Guid.Empty, entry.Id);
        Assert.NotEqual(Guid.Empty, addition.Id);
        Assert.NotEqual(entry.Id, addition.Id);
        Assert.Equal("[]", entry.SupportingComponentsJson);
        Assert.Equal(0, entry.Revision);
        Assert.InRange(entry.CreatedAt, before, after);
        Assert.InRange(entry.UpdatedAt, before, after);
        Assert.InRange(addition.CreatedAt, before, after);
    }

    [Fact]
    public async Task ApplyAsync_RejectsUnsupportedProvider()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AtoCopilotContext(options);

        // Act
        var exception = await Assert.ThrowsAsync<NotSupportedException>(() =>
            OrganizationCatalogSchemaAdditions.ApplyAsync(db, NullLogger.Instance));

        // Assert
        Assert.Contains("Microsoft.EntityFrameworkCore.InMemory", exception.Message);
    }

    private static AtoCopilotContext CreateContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options);

    private static async Task InitializeAsync(AtoCopilotContext db, bool legacy)
    {
        if (legacy)
        {
            await db.Database.ExecuteSqlRawAsync(
                "CREATE TABLE Tenants (Id TEXT NOT NULL PRIMARY KEY, DisplayName TEXT NOT NULL)");
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO Tenants (Id, DisplayName)
                VALUES ({TenantId}, 'Legacy organization'), ({OtherTenantId}, 'Other organization')
                """);
            await OrganizationCatalogSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        }
        else
        {
            await db.Database.EnsureCreatedAsync();
            db.Tenants.AddRange(
                new Tenant { Id = TenantId, DisplayName = "Legacy organization" },
                new Tenant { Id = OtherTenantId, DisplayName = "Other organization" });
            await db.SaveChangesAsync();
        }
    }

    private static OrganizationCatalogEntry CreateEntry(Guid tenantId) => new()
    {
        TenantId = tenantId,
        Source = "provider",
        RecordType = "capability",
        RecordId = "catalog-record",
        CreatedBy = "catalog-author",
        UpdatedBy = "catalog-author"
    };

    private static OrganizationCatalogAddition CreateAddition(Guid tenantId) => new()
    {
        TenantId = tenantId,
        IdempotencyKey = "catalog-addition",
        IntentJson = "{}",
        ResultJson = "{}",
        CreatedBy = "catalog-author"
    };

    private static void AssertCatalogModel(
        IEntityType entity, string table, string[] uniqueProperties, int propertyCount)
    {
        Assert.Equal(table, entity.GetTableName());
        Assert.True(entity.ClrType.IsSealed);
        Assert.True(Attribute.IsDefined(entity.ClrType, typeof(TenantScopedAttribute)));
        Assert.NotNull(entity.GetQueryFilter());
        Assert.Equal(propertyCount, entity.GetProperties().Count());
        Assert.All(entity.GetProperties(), property => Assert.False(property.IsNullable));
        Assert.Equal("Id", Assert.Single(entity.FindPrimaryKey()!.Properties).Name);
        var index = Assert.Single(entity.GetIndexes());
        Assert.True(index.IsUnique);
        Assert.Equal(uniqueProperties, index.Properties.Select(x => x.Name));
        var foreignKey = Assert.Single(entity.GetForeignKeys());
        Assert.Equal(typeof(Tenant), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal("TenantId", Assert.Single(foreignKey.Properties).Name);
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
    }

    private static async Task<string[]> ReadColumnsAsync(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name || ':' || type || ':' || \"notnull\" || ':' || pk"
            + " FROM pragma_table_info($table) ORDER BY name";
        command.Parameters.AddWithValue("$table", table);
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new List<string>();
        while (await reader.ReadAsync())
            columns.Add(reader.GetString(0));
        return columns.ToArray();
    }
}
