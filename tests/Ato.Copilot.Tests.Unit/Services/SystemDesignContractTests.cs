using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public class SystemDesignContractTests
{
    [Fact]
    public void AutomaticAssembly_ExposesRoleAndVersionCheckedBuildContract()
    {
        // Arrange
        var node = new Ato.Copilot.Core.Dtos.SystemDesign.DesignNode();
        // Act
        var role = node.GetType().GetProperty("DiagramRole");
        var build = typeof(Ato.Copilot.Core.Interfaces.Compliance.ISystemDesignService).GetMethod("BuildFromRecordedAsync");
        // Assert
        role.Should().NotBeNull();
        role!.GetValue(node).Should().BeNull();
        System.Text.Json.JsonSerializer.Serialize(node, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))
            .Should().NotContain("diagramRole");
        build.Should().NotBeNull();
    }

    [Fact]
    public void GovernedDesign_HasDedicatedContract_NotGenericProfileAuthority()
    {
        // Arrange
        var assembly = typeof(RegisteredSystem).Assembly;
        // Act
        var graph = assembly.GetType("Ato.Copilot.Core.Dtos.SystemDesign.SystemDesignGraph");
        // Assert
        graph.Should().NotBeNull();
        Enum.GetNames<ProfileSectionType>().Should().HaveCount(6);
    }
}
