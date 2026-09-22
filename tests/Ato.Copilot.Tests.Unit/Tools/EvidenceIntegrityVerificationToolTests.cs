using System.Text.Json;
using Ato.Copilot.Agents.Compliance.Tools;
using Ato.Copilot.Core.Interfaces.Compliance;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Tools;

public sealed class EvidenceIntegrityVerificationToolTests
{
    [Fact]
    public void Contract_DescribesIntegrityPolicyWithoutAcceptingVerifierInput()
    {
        // Arrange
        var tool = new VerifyEvidenceTool(Mock.Of<IAssessmentArtifactService>(), NullLogger<VerifyEvidenceTool>.Instance);

        // Act
        var description = tool.Description;
        var parameters = tool.Parameters;

        // Assert
        description.Should().Contain("effective assigned SCA").And.Contain("evidence-management permission")
            .And.Contain("not assessment approval or evidence authorship");
        parameters.Keys.Should().Equal("evidence_id");
        parameters["evidence_id"].Required.Should().BeTrue();
    }

    [Fact]
    public async Task MissingEvidenceId_ReturnsInvalidInputWithoutCallingDomain()
    {
        // Arrange
        var service = new Mock<IAssessmentArtifactService>(MockBehavior.Strict);
        var tool = new VerifyEvidenceTool(service.Object, NullLogger<VerifyEvidenceTool>.Instance);

        // Act
        var json = await tool.ExecuteCoreAsync(new());

        // Assert
        using var result = JsonDocument.Parse(json);
        result.RootElement.GetProperty("errorCode").GetString().Should().Be("INVALID_INPUT");
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Success_UsesDomainVerifierIdentityNotBrowserArguments()
    {
        // Arrange
        var actor = $"{Guid.NewGuid():D}/{Guid.NewGuid():D}";
        var service = new Mock<IAssessmentArtifactService>();
        service.Setup(s => s.VerifyEvidenceAsync("evidence", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EvidenceVerificationResult
            {
                EvidenceId = "evidence", Status = "verified",
                CollectorIdentity = "collector", VerifierIdentity = actor
            });
        var tool = new VerifyEvidenceTool(service.Object, NullLogger<VerifyEvidenceTool>.Instance);

        // Act
        var json = await tool.ExecuteCoreAsync(new()
        {
            ["evidence_id"] = "evidence", ["verifier_identity"] = "forged", ["user_id"] = "mcp-user"
        });

        // Assert
        using var result = JsonDocument.Parse(json);
        var data = result.RootElement.GetProperty("data");
        data.GetProperty("verifier_identity").GetString().Should().Be(actor);
        data.GetProperty("collector_identity").GetString().Should().Be("collector");
    }

    [Fact]
    public async Task DomainDenial_ReturnsForbiddenEnvelope()
    {
        // Arrange
        var service = new Mock<IAssessmentArtifactService>();
        service.Setup(s => s.VerifyEvidenceAsync("evidence", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("Evidence integrity verification is not authorized."));
        var tool = new VerifyEvidenceTool(service.Object, NullLogger<VerifyEvidenceTool>.Instance);

        // Act
        var json = await tool.ExecuteCoreAsync(new() { ["evidence_id"] = "evidence", ["user_role"] = "Compliance.Auditor" });

        // Assert
        using var result = JsonDocument.Parse(json);
        result.RootElement.GetProperty("status").GetString().Should().Be("error");
        result.RootElement.GetProperty("errorCode").GetString().Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task DomainCancellation_IsNotConvertedToErrorEnvelope()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var service = new Mock<IAssessmentArtifactService>();
        service.Setup(s => s.VerifyEvidenceAsync("evidence", cts.Token))
            .ThrowsAsync(new OperationCanceledException(cts.Token));
        var tool = new VerifyEvidenceTool(service.Object, NullLogger<VerifyEvidenceTool>.Instance);

        // Act
        var execute = () => tool.ExecuteCoreAsync(new() { ["evidence_id"] = "evidence" }, cts.Token);

        // Assert
        await execute.Should().ThrowAsync<OperationCanceledException>();
    }
}
