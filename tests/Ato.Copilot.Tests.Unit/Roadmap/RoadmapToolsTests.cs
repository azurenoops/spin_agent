using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FluentAssertions;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Agents.Compliance.Tools;
using Ato.Copilot.Core.Interfaces.Roadmap;
using Ato.Copilot.Core.Models.Auth;
using Ato.Copilot.Core.Models.Roadmap;

namespace Ato.Copilot.Tests.Unit.Roadmap;

/// <summary>
/// Unit tests for roadmap MCP tools — parameter validation, result envelope.
/// </summary>
public class RoadmapToolsTests
{
    private readonly Mock<IRoadmapService> _roadmapService = new();
    private readonly IServiceScopeFactory _scopeFactory;

    public RoadmapToolsTests()
    {
        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider.GetService(typeof(IRoadmapService)))
            .Returns(_roadmapService.Object);

        var factory = new Mock<IServiceScopeFactory>();
        factory.Setup(f => f.CreateScope()).Returns(scope.Object);
        _scopeFactory = factory.Object;
    }

    [Fact]
    public void GenerateRoadmapTool_Name_MatchesExpected()
    {
        var tool = new GenerateRoadmapTool(_scopeFactory, Mock.Of<ILogger<GenerateRoadmapTool>>());

        tool.Name.Should().Be("compliance_generate_roadmap");
    }

    [Fact]
    public void GenerateRoadmapTool_RequiresPimTier()
    {
        var tool = new GenerateRoadmapTool(_scopeFactory, Mock.Of<ILogger<GenerateRoadmapTool>>());

        tool.RequiredPimTier.Should().NotBe(PimTier.None);
    }

    [Fact]
    public void GetRoadmapTool_NoRequiredPimTier()
    {
        var tool = new GetRoadmapTool(_scopeFactory, Mock.Of<ILogger<GetRoadmapTool>>());

        tool.RequiredPimTier.Should().Be(PimTier.None);
    }

    [Fact]
    public void GetRoadmapProgressTool_NoRequiredPimTier()
    {
        var tool = new GetRoadmapProgressTool(_scopeFactory, Mock.Of<ILogger<GetRoadmapProgressTool>>());

        tool.RequiredPimTier.Should().Be(PimTier.None);
    }

    [Fact]
    public void ExportRoadmapPdfTool_NoRequiredPimTier()
    {
        var tool = new ExportRoadmapPdfTool(_scopeFactory, Mock.Of<ILogger<ExportRoadmapPdfTool>>());

        tool.RequiredPimTier.Should().Be(PimTier.None);
    }

    [Fact]
    public void UpdateRoadmapTool_RequiresPimTier()
    {
        var tool = new UpdateRoadmapTool(_scopeFactory, Mock.Of<ILogger<UpdateRoadmapTool>>());

        tool.RequiredPimTier.Should().NotBe(PimTier.None);
    }

    [Fact]
    public void CreateBoardFromRoadmapTool_RequiresPimTier()
    {
        var tool = new CreateBoardFromRoadmapTool(_scopeFactory, Mock.Of<ILogger<CreateBoardFromRoadmapTool>>());

        tool.RequiredPimTier.Should().NotBe(PimTier.None);
    }

    [Fact]
    public async Task GetRoadmapTool_NoRoadmap_ReturnsError()
    {
        _roadmapService.Setup(s => s.GetRoadmapAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ImplementationRoadmap?)null);

        var tool = new GetRoadmapTool(_scopeFactory, Mock.Of<ILogger<GetRoadmapTool>>());
        var args = new Dictionary<string, object?> { ["system_id"] = "test-sys" };

        var result = await tool.ExecuteCoreAsync(args, CancellationToken.None);

        result.Should().Contain("error");
        result.Should().Contain("NOT_FOUND");
    }

    [Fact]
    public async Task GetRoadmapTool_HasRoadmap_ReturnsSuccess()
    {
        var roadmap = new ImplementationRoadmap
        {
            SystemId = "sys-1",
            Name = "Test",
            Status = RoadmapStatus.Active,
            BaselineLevel = "Moderate",
            TotalGaps = 3,
            TotalEstimatedEffort = 15,
            Phases = [new RoadmapPhase { Name = "P1", DisplayOrder = 1, TotalItemCount = 3 }],
        };
        _roadmapService.Setup(s => s.GetRoadmapAsync("sys-1", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(roadmap);

        var tool = new GetRoadmapTool(_scopeFactory, Mock.Of<ILogger<GetRoadmapTool>>());
        var args = new Dictionary<string, object?> { ["system_id"] = "sys-1" };

        var result = await tool.ExecuteCoreAsync(args, CancellationToken.None);

        result.Should().Contain("success");
        result.Should().Contain("sys-1");
    }

    [Fact]
    public void AllRoadmapTools_HaveParameters()
    {
        var tools = new BaseTool[]
        {
            new GenerateRoadmapTool(_scopeFactory, Mock.Of<ILogger<GenerateRoadmapTool>>()),
            new GetRoadmapTool(_scopeFactory, Mock.Of<ILogger<GetRoadmapTool>>()),
            new GetRoadmapProgressTool(_scopeFactory, Mock.Of<ILogger<GetRoadmapProgressTool>>()),
            new UpdateRoadmapTool(_scopeFactory, Mock.Of<ILogger<UpdateRoadmapTool>>()),
            new CreateBoardFromRoadmapTool(_scopeFactory, Mock.Of<ILogger<CreateBoardFromRoadmapTool>>()),
            new ExportRoadmapPdfTool(_scopeFactory, Mock.Of<ILogger<ExportRoadmapPdfTool>>()),
        };

        foreach (var tool in tools)
        {
            tool.Parameters.Should().NotBeEmpty($"{tool.Name} should have parameters");
            tool.Description.Should().NotBeNullOrWhiteSpace($"{tool.Name} should have a description");
        }
    }
}
