using System.Text.Json;
using Ato.Copilot.Agents.Compliance.Tools;
using Ato.Copilot.Core.Interfaces.Auth;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Tools;

public class DualNarrativeToolTests
{
    [Fact]
    public async Task NarrativeSetPolicy_UpdatesOnlyPolicyAsCurrentUser()
    {
        // Arrange
        var service = new Mock<IDualNarrativeService>();
        service.Setup(item => item.UpdateAsync(
                "system-1", "AC-1", "Policy text", true, null, false,
                "Compliance.Analyst", "user-1", It.IsAny<CancellationToken>(), 7))
            .ReturnsAsync(EmptyResponse());
        using var provider = CreateProvider();
        var tool = new NarrativePolicyTool(
            service.Object,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Mock.Of<ILogger<NarrativePolicyTool>>());

        // Act
        var json = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "system-1",
            ["control_id"] = "AC-1",
            ["policy_narrative"] = "Policy text",
            ["expected_version"] = 7,
        });

        // Assert
        JsonDocument.Parse(json).RootElement.GetProperty("status").GetString().Should().Be("success");
        service.VerifyAll();
    }

    [Fact]
    public async Task NarrativeSetTechnical_UpdatesOnlyTechnicalAsCurrentUser()
    {
        // Arrange
        var service = new Mock<IDualNarrativeService>();
        service.Setup(item => item.UpdateAsync(
                "system-1", "AC-1", null, false, "Technical text", true,
                "Compliance.Analyst", "user-1", It.IsAny<CancellationToken>(), 7))
            .ReturnsAsync(EmptyResponse());
        using var provider = CreateProvider();
        var tool = new NarrativeTechnicalTool(
            service.Object,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Mock.Of<ILogger<NarrativeTechnicalTool>>());

        // Act
        var json = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "system-1",
            ["control_id"] = "AC-1",
            ["technical_narrative"] = "Technical text",
            ["expected_version"] = 7,
        });

        // Assert
        JsonDocument.Parse(json).RootElement.GetProperty("status").GetString().Should().Be("success");
        service.VerifyAll();
    }

    [Fact]
    public async Task EvidenceClassify_ParsesNarrativeTypeCaseInsensitively()
    {
        // Arrange
        var service = new Mock<IDualNarrativeService>();
        service.Setup(item => item.ClassifyEvidenceAsync(
                "evidence-1", EvidenceNarrativeType.Policy, "Confirmed", "user-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EvidenceArtifactSummary(
                "evidence-1", "policy.pdf", "application/pdf", 1,
                EvidenceNarrativeType.Policy, "Confirmed", "user-1", DateTime.UtcNow));
        using var provider = CreateProvider();
        var tool = new EvidenceClassifyTool(
            service.Object,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Mock.Of<ILogger<EvidenceClassifyTool>>());

        // Act
        var json = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["evidence_artifact_id"] = "evidence-1",
            ["narrative_type"] = "policy",
            ["rationale"] = "Confirmed"
        });

        // Assert
        JsonDocument.Parse(json).RootElement.GetProperty("status").GetString().Should().Be("success");
        service.VerifyAll();
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddScoped<IUserContext>(_ => Mock.Of<IUserContext>(user =>
            user.Role == "Compliance.Analyst" && user.UserId == "user-1"));
        return services.BuildServiceProvider();
    }

    private static DualNarrativeResponse EmptyResponse() => new(
        "system-1", "AC-1", null, null, null, false,
        [], [], [], false, false, null, null);
}