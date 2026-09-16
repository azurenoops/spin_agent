using System.Text.Json;
using Ato.Copilot.Agents.Compliance.Tools;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Tools;

public sealed class GetControlValidationToolTests
{
    private readonly Mock<IControlValidationLinkService> _service = new();

    [Fact]
    public async Task Get_ReturnsStructuredLinks()
    {
        // Arrange
        _service.Setup(candidate => candidate.GetLinksAsync("system-1", "AC-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync([CreateLink()]);
        var tool = CreateTool();

        // Act
        var result = await tool.ExecuteCoreAsync(RequiredArguments());
        using var document = JsonDocument.Parse(result);

        // Assert
        document.RootElement.GetProperty("status").GetString().Should().Be("success");
        document.RootElement.GetProperty("data").GetProperty("total").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Add_AndDelete_InvokeServiceActions()
    {
        // Arrange
        _service.Setup(candidate => candidate.AddLinkAsync(
                "system-1", "AC-2", ControlValidationLinkType.ExternalUrl,
                "https://example.test/evidence", "Report", "mcp-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateLink());
        _service.Setup(candidate => candidate.DeleteLinkAsync("link-1", "mcp-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var tool = CreateTool();

        // Act
        var addResult = await tool.ExecuteCoreAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "system-1",
            ["control_id"] = "AC-2",
            ["action"] = "add",
            ["link_type"] = "ExternalUrl",
            ["link_target"] = "https://example.test/evidence",
            ["description"] = "Report",
        });
        var deleteResult = await tool.ExecuteCoreAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "system-1",
            ["control_id"] = "AC-2",
            ["action"] = "delete",
            ["link_id"] = "link-1",
        });

        // Assert
        JsonDocument.Parse(addResult).RootElement.GetProperty("status").GetString().Should().Be("success");
        JsonDocument.Parse(deleteResult).RootElement.GetProperty("status").GetString().Should().Be("success");
    }

    [Fact]
    public async Task Get_MissingControlReturnsNotFound_AndEmptyLinksRemainSuccess()
    {
        // Arrange
        _service.SetupSequence(candidate => candidate.GetLinksAsync("system-1", "AC-2", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ControlImplementationNotFoundException("system-1", "AC-2"))
            .ReturnsAsync([]);
        var tool = CreateTool();

        // Act
        var missingResult = await tool.ExecuteCoreAsync(RequiredArguments());
        var emptyResult = await tool.ExecuteCoreAsync(RequiredArguments());

        // Assert
        JsonDocument.Parse(missingResult).RootElement.GetProperty("errorCode").GetString().Should().Be("NOT_FOUND");
        JsonDocument.Parse(emptyResult).RootElement.GetProperty("data").GetProperty("total").GetInt32().Should().Be(0);
    }

    private GetControlValidationTool CreateTool() =>
        new(_service.Object, Mock.Of<ILogger<GetControlValidationTool>>());

    private static Dictionary<string, object?> RequiredArguments() => new()
    {
        ["system_id"] = "system-1",
        ["control_id"] = "AC-2",
    };

    private static ControlValidationLink CreateLink() => new()
    {
        Id = "link-1",
        ControlImplementationId = "implementation-1",
        LinkType = ControlValidationLinkType.ExternalUrl,
        LinkTarget = "https://example.test/evidence",
        Description = "Report",
        AddedBy = "mcp-user",
    };
}