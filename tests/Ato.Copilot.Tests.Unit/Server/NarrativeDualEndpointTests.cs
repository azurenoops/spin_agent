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
    [Fact]
    public async Task PatchDualNarrative_PolicyOnly_PreservesTechnicalFieldOmission()
    {
        // Arrange
        var service = new Mock<IDualNarrativeService>();
        service.Setup(item => item.UpdateAsync(
                "system-1", "AC-1", "Policy update", true, null, false,
                "Compliance.Analyst", "user-1", It.IsAny<CancellationToken>()))
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
                It.IsAny<CancellationToken>()))
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