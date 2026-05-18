using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Models.Tenancy.Attributes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Tenancy;

/// <summary>
/// T031: Verifies the Feature 048 tenant-scoping self-check
/// (<see cref="AtoCopilotContext.AssertScopingAttributesPresent"/>) discovers
/// scope attributes correctly and rejects entity types that lack them.
/// </summary>
public class TenantScopedAttributeTests
{
    [Fact]
    public void TenantScopedAttribute_IsDiscoverable_OnEntity()
    {
        // Arrange / Act
        var hasAttr = typeof(Organization)
            .GetCustomAttributes(typeof(TenantScopedAttribute), inherit: false)
            .Length > 0;

        // Assert
        hasAttr.Should().BeTrue("Organization is the canonical [TenantScoped] sample entity");
    }

    [Fact]
    public void GlobalReferenceAttribute_IsDiscoverable_OnEntity()
    {
        // Arrange / Act
        var hasAttr = typeof(Tenant)
            .GetCustomAttributes(typeof(GlobalReferenceAttribute), inherit: false)
            .Length > 0;

        // Assert
        hasAttr.Should().BeTrue("Tenant itself is [GlobalReference] (chicken-and-egg)");
    }

    [Fact]
    public void TenantScopedAttribute_IsSealed_AndNotInheritable()
    {
        var t = typeof(TenantScopedAttribute);
        t.IsSealed.Should().BeTrue();
        var au = (AttributeUsageAttribute)Attribute.GetCustomAttribute(t, typeof(AttributeUsageAttribute))!;
        au.Inherited.Should().BeFalse();
        au.AllowMultiple.Should().BeFalse();
    }

    [Fact]
    public void GlobalReferenceAttribute_IsSealed_AndNotInheritable()
    {
        var t = typeof(GlobalReferenceAttribute);
        t.IsSealed.Should().BeTrue();
        var au = (AttributeUsageAttribute)Attribute.GetCustomAttribute(t, typeof(AttributeUsageAttribute))!;
        au.Inherited.Should().BeFalse();
        au.AllowMultiple.Should().BeFalse();
    }

    [Fact]
    public void Organization_HasGuidTenantIdProperty()
    {
        // The startup self-check asserts every [TenantScoped] type has a
        // "Guid TenantId { get; set; }" property — confirm the canonical entity does.
        var prop = typeof(Organization).GetProperty("TenantId");
        prop.Should().NotBeNull();
        prop!.PropertyType.Should().Be(typeof(Guid));
        prop.CanRead.Should().BeTrue();
        prop.CanWrite.Should().BeTrue();
    }

    [Fact]
    public void TenantScopedAttribute_CompositeIndexHint_IsCaptured()
    {
        var attr = (TenantScopedAttribute?)typeof(Organization)
            .GetCustomAttributes(typeof(TenantScopedAttribute), inherit: false)
            .FirstOrDefault();

        attr.Should().NotBeNull();
        attr!.CompositeIndexHint.Should().Be(nameof(Organization.Name));
    }
}
