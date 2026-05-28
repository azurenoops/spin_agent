using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Ato.Copilot.Core.Models.Auth;
using Ato.Copilot.Core.Models.Tenancy.Attributes;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Auth;

/// <summary>
/// Feature 051 T013 — entity-shape contract for <see cref="LoginAuditEvent"/>.
/// Verifies the attributes that drive the tenant-scoped query filter, the
/// validation length caps, and the default value contracts called out by
/// data-model.md § 1.6.
/// </summary>
public class LoginAuditEventTests
{
    [Fact]
    public void Entity_HasTenantScopedAttribute()
    {
        // Arrange
        var entityType = typeof(LoginAuditEvent);

        // Act
        var attr = entityType.GetCustomAttribute<TenantScopedAttribute>(inherit: false);

        // Assert
        attr.Should().NotBeNull(
            "[TenantScoped] is required so AtoCopilotContext.OnModelCreating's tenant filter applies (data-model.md § 1)");
    }

    [Theory]
    [InlineData(nameof(LoginAuditEvent.Oid), 254)]
    [InlineData(nameof(LoginAuditEvent.Tid), 254)]
    [InlineData(nameof(LoginAuditEvent.CorrelationId), 64)]
    [InlineData(nameof(LoginAuditEvent.SourceIp), 45)]
    [InlineData(nameof(LoginAuditEvent.UserAgent), 512)]
    [InlineData(nameof(LoginAuditEvent.MetadataJson), 2000)]
    public void Property_HasExpectedMaxLength(string propertyName, int expectedMax)
    {
        // Arrange
        var prop = typeof(LoginAuditEvent).GetProperty(propertyName)!;

        // Act
        var maxLengthAttr = prop.GetCustomAttribute<MaxLengthAttribute>();

        // Assert
        maxLengthAttr.Should().NotBeNull($"{propertyName} must declare [MaxLength] per data-model.md § 1.2");
        maxLengthAttr!.Length.Should().Be(expectedMax);
    }

    [Theory]
    [InlineData(nameof(LoginAuditEvent.CorrelationId))]
    [InlineData(nameof(LoginAuditEvent.SourceIp))]
    [InlineData(nameof(LoginAuditEvent.UserAgent))]
    public void RequiredProperty_HasRequiredAttribute(string propertyName)
    {
        // Arrange
        var prop = typeof(LoginAuditEvent).GetProperty(propertyName)!;

        // Act
        var requiredAttr = prop.GetCustomAttribute<RequiredAttribute>();

        // Assert
        requiredAttr.Should().NotBeNull($"{propertyName} must declare [Required] per data-model.md § 1.2");
    }

    [Fact]
    public void Id_DefaultsToNewGuid()
    {
        // Arrange / Act
        var a = new LoginAuditEvent();
        var b = new LoginAuditEvent();

        // Assert
        a.Id.Should().NotBeEmpty();
        b.Id.Should().NotBeEmpty();
        a.Id.Should().NotBe(b.Id, "each instance must get its own Guid.NewGuid()");
    }

    [Fact]
    public void OccurredAt_DefaultsToUtcNow()
    {
        // Arrange
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        // Act
        var entity = new LoginAuditEvent();
        var after = DateTimeOffset.UtcNow.AddSeconds(1);

        // Assert
        entity.OccurredAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        entity.OccurredAt.Offset.Should().Be(TimeSpan.Zero, "OccurredAt is always UTC per data-model.md § 1.6");
    }

    [Fact]
    public void Optional_Properties_DefaultToNull()
    {
        // Arrange / Act
        var entity = new LoginAuditEvent();

        // Assert
        entity.Oid.Should().BeNull();
        entity.Tid.Should().BeNull();
        entity.ErrorClass.Should().BeNull();
        entity.MetadataJson.Should().BeNull();
    }

    [Fact]
    public void EventTypeEnum_HasNineMembers()
    {
        // FR-032 — exactly nine event-type values (data-model.md § 1.3).
        Enum.GetValues<LoginAuditEventType>().Should().HaveCount(9);
    }

    [Fact]
    public void ErrorClassEnum_HasTenMembers()
    {
        // FR-014 + FR-015 — exactly ten error-class values (data-model.md § 1.4).
        Enum.GetValues<LoginErrorClass>().Should().HaveCount(10);
    }

    [Fact]
    public void SurfaceEnum_HasFourMembers()
    {
        // Four surfaces — Dashboard, VSCode, M365, Chat (data-model.md § 1.2).
        Enum.GetValues<LoginSurface>().Should().HaveCount(4);
    }
}
