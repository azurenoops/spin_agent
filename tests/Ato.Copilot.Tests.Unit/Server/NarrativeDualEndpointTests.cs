using System.Text;
using Ato.Copilot.Core.Interfaces.Auth;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Mcp.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Server;

public class NarrativeDualEndpointTests
{
    [Theory]
    [InlineData("UNDER_REVIEW")]
    [InlineData("CONCURRENCY_CONFLICT")]
    public async Task PatchDualNarrative_GovernanceConflict_Returns409(string errorCode)
    {
        // Arrange
        var service = new Mock<IDualNarrativeService>();
        service.Setup(item => item.UpdateAsync("system-1", "AC-1", "Policy", true, null, false,
                "Compliance.Analyst", "user-1", It.IsAny<CancellationToken>(), 7))
            .ThrowsAsync(new InvalidOperationException($"{errorCode}: Cannot save."));
        var user = Mock.Of<IUserContext>(context => context.Role == "Compliance.Analyst" && context.UserId == "user-1");
        var request = CreateJsonRequest("""{"policyNarrative":"Policy","expectedVersion":7}""");

        // Act
        var result = await NarrativeDualEndpoints.PatchDualNarrativeAsync(
            "system-1", "AC-1", request, service.Object, user, CancellationToken.None);

        // Assert
        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(409);
        service.VerifyAll();
    }

    [Fact]
    public async Task PatchDualNarrative_PolicyOnly_PreservesTechnicalFieldOmission()
    {
        // Arrange
        var service = new Mock<IDualNarrativeService>();
        service.Setup(item => item.UpdateAsync(
                "system-1", "AC-1", "Policy update", true, null, false,
                "Compliance.Analyst", "user-1", It.IsAny<CancellationToken>(), null))
            .ReturnsAsync(EmptyResponse());
        var userContext = Mock.Of<IUserContext>(user =>
            user.Role == "Compliance.Analyst" && user.UserId == "user-1");
        var request = CreateJsonRequest("""{"policyNarrative":"Policy update"}""");

        // Act
        var result = await NarrativeDualEndpoints.PatchDualNarrativeAsync(
            "system-1", "AC-1", request, service.Object, userContext, CancellationToken.None);

        // Assert
        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status200OK);
        service.VerifyAll();
    }

    [Fact]
    public async Task PatchDualNarrative_ForbiddenServiceResult_Returns403()
    {
        // Arrange
        var service = new Mock<IDualNarrativeService>();
        service.Setup(item => item.UpdateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), true,
                It.IsAny<string?>(), false, It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), null))
            .ThrowsAsync(new UnauthorizedAccessException("Policy write denied."));
        var userContext = Mock.Of<IUserContext>(user =>
            user.Role == "Compliance.PlatformEngineer" && user.UserId == "user-1");
        var request = CreateJsonRequest("""{"policyNarrative":"Denied"}""");

        // Act
        var result = await NarrativeDualEndpoints.PatchDualNarrativeAsync(
            "system-1", "AC-1", request, service.Object, userContext, CancellationToken.None);

        // Assert
        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    private static HttpRequest CreateJsonRequest(string json)
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return context.Request;
    }

    private static DualNarrativeResponse EmptyResponse() => new(
        "system-1", "AC-1", null, null, null, false,
        [], [], [], false, false, null, null);
}