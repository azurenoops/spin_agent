using Ato.Copilot.Mcp.Services.Tenancy;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Tenancy;

public class WorkspaceMeIdentityContractTests
{
    [Fact]
    public void CanonicalMe_DeclaresRequiredSubjectDirectoryIndependentOfOrganization()
    {
        // Arrange
        var response = typeof(WorkspaceMeResponse);

        // Act
        var directory = response.GetProperty("DirectoryTenantId");

        // Assert
        directory.Should().NotBeNull("canonical cache keys require the authenticated directory, not an inferred home or organization ID");
        directory!.PropertyType.Should().Be(typeof(Guid));
    }

    [Fact]
    public void Serialization_PreservesSubjectDirectory_WhenHomeIsNullAndOrganizationDiffers()
    {
        // Arrange
        var directory = Guid.NewGuid();
        var organization = Guid.NewGuid();
        var response = new WorkspaceMeResponse(Guid.NewGuid(), directory, "Member", "User",
            null, new(organization, "Organization", "Active"), false, null, [], false, false,
            [], null, [], 1, new(false, false, false));

        // Act
        var json = JsonSerializer.SerializeToElement(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        // Assert
        json.GetProperty("directoryTenantId").GetGuid().Should().Be(directory).And.NotBe(organization);
        json.GetProperty("homeTenant").ValueKind.Should().Be(JsonValueKind.Null);
        json.GetProperty("effectiveTenant").GetProperty("id").GetGuid().Should().Be(organization);
    }
}
