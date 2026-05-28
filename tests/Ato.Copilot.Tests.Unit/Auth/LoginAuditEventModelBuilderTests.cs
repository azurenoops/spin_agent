using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Auth;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Auth;

/// <summary>
/// Feature 051 T016 — model-builder shape contract for
/// <see cref="LoginAuditEvent"/>. Verifies the three indexes, FK
/// configuration, enum-as-string conversion, and the leading-column
/// invariant for the tenant query filter (data-model.md § 1.9 / § 2).
/// </summary>
/// <remarks>
/// Uses a SQLite in-memory connection because the EF Core InMemory provider
/// does not surface <c>HasConversion&lt;string&gt;()</c> via
/// <see cref="Microsoft.EntityFrameworkCore.Metadata.IProperty.GetValueConverter"/>.
/// SQLite honors the converter and lets us assert the contract.
/// </remarks>
public sealed class LoginAuditEventModelBuilderTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AtoCopilotContext _ctx;

    public LoginAuditEventModelBuilderTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseSqlite(_connection)
            .Options;
        _ctx = new AtoCopilotContext(options);
    }

    public void Dispose()
    {
        _ctx.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public void Entity_IsRegistered_OnContext()
    {
        // Arrange
        var ctx = _ctx;

        // Act
        var entityType = ctx.Model.FindEntityType(typeof(LoginAuditEvent));

        // Assert
        entityType.Should().NotBeNull("LoginAuditEvent must be mapped on AtoCopilotContext");
    }

    [Fact]
    public void Index_TenantOccurred_Exists_AndLeadsWithEffectiveTenantId()
    {
        // Arrange
        var ctx = _ctx;
        var entityType = ctx.Model.FindEntityType(typeof(LoginAuditEvent))!;

        // Act
        var index = entityType.GetIndexes()
            .FirstOrDefault(i => i.GetDatabaseName() == "IX_LoginAuditEvents_Tenant_Occurred");

        // Assert
        index.Should().NotBeNull("the primary read-path index ships in OnModelCreating per data-model.md § 1.9");
        index!.Properties.Should().HaveCount(2);
        index.Properties[0].Name.Should().Be(nameof(LoginAuditEvent.EffectiveTenantId),
            "leading column MUST be EffectiveTenantId so the tenant query filter is index-served");
        index.Properties[1].Name.Should().Be(nameof(LoginAuditEvent.OccurredAt));
    }

    [Fact]
    public void Index_Occurred_Exists_ForArchiveJob()
    {
        // Arrange
        var ctx = _ctx;
        var entityType = ctx.Model.FindEntityType(typeof(LoginAuditEvent))!;

        // Act
        var index = entityType.GetIndexes()
            .FirstOrDefault(i => i.GetDatabaseName() == "IX_LoginAuditEvents_Occurred");

        // Assert
        index.Should().NotBeNull("daily archive job (FR-036a) scans by OccurredAt");
        index!.Properties.Should().HaveCount(1);
        index.Properties[0].Name.Should().Be(nameof(LoginAuditEvent.OccurredAt));
    }

    [Fact]
    public void Index_Oid_Exists_ForForensicReads()
    {
        // Arrange
        var ctx = _ctx;
        var entityType = ctx.Model.FindEntityType(typeof(LoginAuditEvent))!;

        // Act
        var index = entityType.GetIndexes()
            .FirstOrDefault(i => i.GetDatabaseName() == "IX_LoginAuditEvents_Oid");

        // Assert
        index.Should().NotBeNull("forensic 'everything for user X' index ships per data-model.md § 1.9");
        index!.Properties.Should().HaveCount(2);
        index.Properties[0].Name.Should().Be(nameof(LoginAuditEvent.Oid));
        index.Properties[1].Name.Should().Be(nameof(LoginAuditEvent.OccurredAt));
    }

    [Theory]
    [InlineData(nameof(LoginAuditEvent.EventType))]
    [InlineData(nameof(LoginAuditEvent.ErrorClass))]
    [InlineData(nameof(LoginAuditEvent.Surface))]
    public void EnumProperties_UseStringConversion(string propertyName)
    {
        // Arrange
        var ctx = _ctx;
        var entityType = ctx.Model.FindEntityType(typeof(LoginAuditEvent))!;

        // Act — EF Core SqlServer/SQLite providers materialize the converter
        // lazily on type-mapping resolution. Fetch the type mapping explicitly
        // to force resolution, then assert the provider CLR type is string.
        var property = entityType.FindProperty(propertyName)!;
        var typeMapping = property.GetTypeMapping();

        // Assert
        typeMapping.Should().NotBeNull($"{propertyName} must have a resolved type mapping");
        typeMapping.Converter.Should().NotBeNull(
            $"{propertyName} must be persisted with a value converter per data-model.md § 2");
        typeMapping.Converter!.ProviderClrType.Should().Be(typeof(string),
            $"{propertyName} converter target must be string for human-readable raw-SQL audit queries");
    }

    [Fact]
    public void EffectiveTenantId_ForeignKey_To_Tenants_IsCascade()
    {
        // Arrange
        var ctx = _ctx;
        var entityType = ctx.Model.FindEntityType(typeof(LoginAuditEvent))!;

        // Act
        var fk = entityType.GetForeignKeys()
            .FirstOrDefault(f => f.Properties.Any(p => p.Name == nameof(LoginAuditEvent.EffectiveTenantId)));

        // Assert
        fk.Should().NotBeNull("FK on EffectiveTenantId to Tenants must exist for offboarding cascade");
        fk!.DeleteBehavior.Should().Be(DeleteBehavior.Cascade,
            "tenant offboarding sweeps hot audit rows; cold archive (FR-036a) persists independently");
    }
}
