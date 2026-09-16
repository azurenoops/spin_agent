using System.Text.Json;
using Azure.ResourceManager;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Ato.Copilot.Agents.Compliance.Configuration;
using Ato.Copilot.Agents.Compliance.Services.Engines.Remediation;
using Ato.Copilot.Agents.Compliance.Tools;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Tests.Integration.Tools;

public class ArmRemediationExecutionIntegrationTests
{
    [Fact]
    public async Task ComplianceRemediate_LiveUnsupportedArmOperation_ReturnsExplicitFailure()
    {
        // Arrange
        var finding = new ComplianceFinding
        {
            Id = "finding-arm-live",
            ControlId = "SC-8",
            ControlFamily = "SC",
            Title = "TLS version too low",
            Description = "Synthetic integration finding",
            Severity = FindingSeverity.High,
            Status = FindingStatus.Open,
            ResourceId = "/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/test",
            ResourceType = "Microsoft.Storage/storageAccounts",
            RemediationType = RemediationType.ResourceConfiguration,
            AutoRemediable = true,
            SubscriptionId = "sub-1"
        };

        var complianceEngine = new Mock<IAtoComplianceEngine>();
        complianceEngine
            .Setup(service => service.GetFindingAsync(finding.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(finding);

        var aiGenerator = new Mock<IAiRemediationPlanGenerator>();
        aiGenerator.SetupGet(service => service.IsAvailable).Returns(false);

        var structuredRemediation = new Mock<IComplianceRemediationService>();
        structuredRemediation.Setup(service => service.CanHandle(finding)).Returns(false);

        using var services = new ServiceCollection().BuildServiceProvider();
        var armService = new AzureArmRemediationService(
            new Mock<ArmClient>().Object,
            Mock.Of<ILogger<AzureArmRemediationService>>());
        var engine = new AtoRemediationEngine(
            complianceEngine.Object,
            Mock.Of<IDbContextFactory<AtoCopilotContext>>(),
            armService,
            aiGenerator.Object,
            structuredRemediation.Object,
            Mock.Of<IRemediationScriptExecutor>(),
            Mock.Of<INistRemediationStepsService>(),
            Mock.Of<IScriptSanitizationService>(),
            Options.Create(new ComplianceAgentOptions()),
            Mock.Of<ILogger<AtoRemediationEngine>>(),
            services.GetRequiredService<IServiceScopeFactory>());
        var tool = new RemediationExecuteTool(
            engine,
            Mock.Of<ILogger<RemediationExecuteTool>>());
        var arguments = new Dictionary<string, object?>
        {
            ["finding_id"] = finding.Id,
            ["apply_remediation"] = true,
            ["dry_run"] = false,
            ["use_ai"] = false
        };

        // Act
        var json = await tool.ExecuteCoreAsync(arguments);
        using var result = JsonDocument.Parse(json);
        var root = result.RootElement;
        var data = root.GetProperty("data");

        // Assert
        root.GetProperty("status").GetString().Should().Be("error");
        data.GetProperty("executionStatus").GetString().Should().Be("Failed");
        data.GetProperty("tierUsed").GetInt32().Should().Be(3);
        data.GetProperty("stepsExecuted").GetInt32().Should().Be(0);
        data.GetProperty("changesApplied").GetArrayLength().Should().Be(0);
        data.GetProperty("error").GetString().Should().Contain("not implemented");
        finding.Status.Should().Be(FindingStatus.Open);
    }
}