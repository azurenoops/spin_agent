using System.Text.Json;
using Ato.Copilot.Agents.Compliance.Tools;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Compliance;

public sealed class EmassWorkflowToolsTests
{
    [Fact]
    public async Task WorkflowStatus_FormatsCategoryTableAndConflictCount()
    {
        // Arrange
        var service = new Mock<IEmassWorkflowStatusService>();
        service.Setup(item => item.GetStatusAsync("system-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmassWorkflowStatus(
                "system-1",
                EmassWorkflowOverallStatus.HasConflicts,
                DateTimeOffset.Parse("2026-03-01T12:00:00Z"),
                DateTimeOffset.Parse("2026-03-02T12:00:00Z"),
                2,
                [new EmassExportCategorySummary("Controls", 100, 4, DateTimeOffset.Parse("2026-03-01T12:00:00Z"))],
                new EmassReadinessSummary(true, 0, 1)));
        var tool = new EmassGetWorkflowStatusTool(
            service.Object,
            Mock.Of<ILogger<EmassGetWorkflowStatusTool>>());

        // Act
        var result = await tool.ExecuteAsync(new Dictionary<string, object?> { ["system_id"] = "system-1" });

        // Assert
        var data = JsonDocument.Parse(result).RootElement.GetProperty("data");
        var markdown = data.GetProperty("markdown").GetString();
        markdown.Should().Contain("| Controls | 100 | 4 |");
        markdown.Should().Contain("Unresolved conflicts: 2");
    }

    [Fact]
    public async Task ExportReadiness_BoldsOnlyBlockingGaps()
    {
        // Arrange
        var service = new Mock<IEmassExportReadinessService>();
        service.Setup(item => item.CheckReadinessAsync("system-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmassExportReadinessResult(
                "system-1",
                false,
                [
                    new ReadinessGap("EmassSystemId", "Register the eMASS ID.", ReadinessGapSeverity.Blocking, "/settings"),
                    new ReadinessGap("ApprovedSsp", "Approve an SSP section.", ReadinessGapSeverity.Advisory, "/ssp"),
                ],
                DateTimeOffset.Parse("2026-03-03T12:00:00Z")));
        var tool = new EmassCheckExportReadinessTool(
            service.Object,
            Mock.Of<ILogger<EmassCheckExportReadinessTool>>());

        // Act
        var result = await tool.ExecuteAsync(new Dictionary<string, object?> { ["system_id"] = "system-1" });

        // Assert
        var markdown = JsonDocument.Parse(result).RootElement
            .GetProperty("data").GetProperty("markdown").GetString();
        markdown.Should().Contain("**EmassSystemId: Register the eMASS ID.**");
        markdown.Should().Contain("ApprovedSsp: Approve an SSP section.");
        markdown.Should().NotContain("**ApprovedSsp");
    }
}