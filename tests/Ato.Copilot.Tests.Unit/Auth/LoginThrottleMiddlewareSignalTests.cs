using Ato.Copilot.Mcp.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Auth;

/// <summary>
/// Feature 051 T143 [Phase 13.1] — unit coverage for
/// <see cref="LoginThrottleMiddleware.IsFailedAuthSignal"/>. Per analysis
/// C17 the throttle counter MUST increment ONLY on:
/// <list type="bullet">
///   <item><c>401 Unauthorized</c></item>
///   <item><c>403 Forbidden</c> WITH the items sentinel
///         <c>Ato.LoginThrottle.FailureSignal == "NO_TENANT_ASSIGNMENT"</c></item>
/// </list>
/// Any other status (2xx, 4xx-validation, plain 403 without the
/// sentinel, 5xx) MUST return <c>false</c> so the middleware does NOT
/// register an attempt.
/// </summary>
public sealed class LoginThrottleMiddlewareSignalTests
{
    private static HttpContext MakeContext(int statusCode, string? sentinel = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.StatusCode = statusCode;
        if (sentinel is not null)
        {
            ctx.Items[LoginThrottleMiddleware.FailureSignalKey] = sentinel;
        }
        return ctx;
    }

    [Fact]
    public void IsFailedAuthSignal_401_ReturnsTrue()
    {
        // Arrange
        var ctx = MakeContext(StatusCodes.Status401Unauthorized);

        // Act + Assert
        LoginThrottleMiddleware.IsFailedAuthSignal(ctx).Should().BeTrue(
            "401 is the canonical unauthenticated-access response and MUST count as a failed-auth signal.");
    }

    [Fact]
    public void IsFailedAuthSignal_403_WithoutSentinel_ReturnsFalse()
    {
        // Arrange — generic 403 (e.g. role guard rejection) has no
        // FailureSignalKey set; per analysis C17 it MUST NOT count.
        var ctx = MakeContext(StatusCodes.Status403Forbidden);

        // Act + Assert
        LoginThrottleMiddleware.IsFailedAuthSignal(ctx).Should().BeFalse(
            "Plain 403 (without the NO_TENANT_ASSIGNMENT sentinel) is an authorization failure, " +
            "not an authentication failure, and MUST NOT increment the throttle counter.");
    }

    [Fact]
    public void IsFailedAuthSignal_403_WithNoTenantAssignmentSentinel_ReturnsTrue()
    {
        // Arrange
        var ctx = MakeContext(
            StatusCodes.Status403Forbidden,
            LoginThrottleMiddleware.FailureSignal_NoTenantAssignment);

        // Act + Assert
        LoginThrottleMiddleware.IsFailedAuthSignal(ctx).Should().BeTrue(
            "403 NO_TENANT_ASSIGNMENT (FR-015 path from /api/auth/me) is a failed-auth signal " +
            "because the caller is authenticated but cannot be mapped to a tenant.");
    }

    [Theory]
    [InlineData(StatusCodes.Status200OK)]
    [InlineData(StatusCodes.Status201Created)]
    [InlineData(StatusCodes.Status204NoContent)]
    public void IsFailedAuthSignal_Success_ReturnsFalse(int statusCode)
    {
        // Arrange
        var ctx = MakeContext(statusCode);

        // Act + Assert — analysis C17: 2xx MUST NOT increment.
        LoginThrottleMiddleware.IsFailedAuthSignal(ctx).Should().BeFalse();
    }

    [Theory]
    [InlineData(StatusCodes.Status400BadRequest)]
    [InlineData(StatusCodes.Status404NotFound)]
    [InlineData(StatusCodes.Status409Conflict)]
    [InlineData(StatusCodes.Status422UnprocessableEntity)]
    [InlineData(StatusCodes.Status429TooManyRequests)]
    public void IsFailedAuthSignal_OtherClientErrors_ReturnFalse(int statusCode)
    {
        // Arrange
        var ctx = MakeContext(statusCode);

        // Act + Assert — analysis C17: only 401 and tagged 403 count.
        LoginThrottleMiddleware.IsFailedAuthSignal(ctx).Should().BeFalse();
    }

    [Theory]
    [InlineData(StatusCodes.Status500InternalServerError)]
    [InlineData(StatusCodes.Status503ServiceUnavailable)]
    public void IsFailedAuthSignal_ServerErrors_ReturnFalse(int statusCode)
    {
        // Arrange
        var ctx = MakeContext(statusCode);

        // Act + Assert
        LoginThrottleMiddleware.IsFailedAuthSignal(ctx).Should().BeFalse();
    }

    [Fact]
    public void IsFailedAuthSignal_403_WithUnknownSentinelValue_ReturnsFalse()
    {
        // Arrange — sentinel set but value isn't the recognised
        // NO_TENANT_ASSIGNMENT marker. Defensive: only the documented
        // value should count.
        var ctx = MakeContext(StatusCodes.Status403Forbidden, "SOMETHING_ELSE");

        // Act + Assert
        LoginThrottleMiddleware.IsFailedAuthSignal(ctx).Should().BeFalse();
    }
}
