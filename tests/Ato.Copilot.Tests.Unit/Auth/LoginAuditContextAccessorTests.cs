using System.Diagnostics;
using Ato.Copilot.Mcp.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Auth;

/// <summary>
/// Feature 051 T040 — contract test for
/// <see cref="LoginAuditContextAccessor"/>. Verifies the forensic-field
/// extraction rules pinned by <c>data-model.md § 1.6</c>:
/// <list type="bullet">
///   <item>X-Forwarded-For first IP wins (IPv4 + IPv6).</item>
///   <item>UA header values longer than 512 chars are truncated.</item>
///   <item>Missing UA defaults to <c>"unknown"</c>.</item>
///   <item>CorrelationId fallback chain:
///         <c>HttpContext.Items["CorrelationId"]</c> →
///         <c>Activity.Current.Id</c> → <c>TraceIdentifier</c> →
///         synthesised 32-char id.</item>
/// </list>
/// </summary>
public sealed class LoginAuditContextAccessorTests
{
    private static LoginAuditContextAccessor Sut() => new();

    [Fact]
    public void FromHttpContext_HonorsXForwardedForFirstHopForIpv4()
    {
        // Arrange
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Forwarded-For"] = "203.0.113.42, 10.0.0.1";
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.1");

        // Act
        var result = Sut().FromHttpContext(ctx);

        // Assert — the leftmost IP in the chain is the originating client.
        result.SourceIp.Should().Be("203.0.113.42");
    }

    [Fact]
    public void FromHttpContext_HonorsXForwardedForForIpv6()
    {
        // Arrange — full IPv6 with port-style brackets stripped.
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Forwarded-For"] = "2001:db8::1234:5678";

        // Act
        var result = Sut().FromHttpContext(ctx);

        // Assert
        result.SourceIp.Should().Be("2001:db8::1234:5678");
    }

    [Fact]
    public void FromHttpContext_FallsBackToRemoteIpAddress()
    {
        // Arrange
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.7");

        // Act
        var result = Sut().FromHttpContext(ctx);

        // Assert
        result.SourceIp.Should().Be("10.0.0.7");
    }

    [Fact]
    public void FromHttpContext_NoIpAvailable_ReturnsUnknown()
    {
        // Arrange
        var ctx = new DefaultHttpContext();

        // Act
        var result = Sut().FromHttpContext(ctx);

        // Assert
        result.SourceIp.Should().Be("unknown");
    }

    [Fact]
    public void FromHttpContext_UserAgentLongerThan512Chars_IsTruncated()
    {
        // Arrange
        var oversized = new string('U', 1000);
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.UserAgent = oversized;

        // Act
        var result = Sut().FromHttpContext(ctx);

        // Assert
        result.UserAgent.Length.Should().Be(512);
        result.UserAgent.Should().Be(new string('U', 512));
    }

    [Fact]
    public void FromHttpContext_MissingUserAgent_ReturnsUnknown()
    {
        // Arrange
        var ctx = new DefaultHttpContext();

        // Act
        var result = Sut().FromHttpContext(ctx);

        // Assert
        result.UserAgent.Should().Be("unknown");
    }

    [Fact]
    public void FromHttpContext_HonorsExistingCorrelationIdInHttpContextItems()
    {
        // Arrange
        var ctx = new DefaultHttpContext();
        ctx.Items["CorrelationId"] = "preset-corr-id-1234";

        // Act
        var result = Sut().FromHttpContext(ctx);

        // Assert — matches the CorrelationIdMiddleware contract used by
        // AuditLoggingMiddleware (Feature 048 T118 [US6]).
        result.CorrelationId.Should().Be("preset-corr-id-1234");
    }

    [Fact]
    public void FromHttpContext_NoCorrelationId_SynthesisesA32CharId()
    {
        // Arrange — TraceIdentifier on DefaultHttpContext is empty by default,
        // and no Activity is current in unit-test context.
        var ctx = new DefaultHttpContext { TraceIdentifier = string.Empty };

        // Act
        var result = Sut().FromHttpContext(ctx);

        // Assert
        result.CorrelationId.Length.Should().Be(32);
        Guid.TryParseExact(result.CorrelationId, "N", out _).Should().BeTrue(
            "synthesised id is `Guid.NewGuid().ToString(\"N\")[..32]`.");
    }

    [Fact]
    public void FromHttpContext_FallsBackToActivityCurrentId()
    {
        // Arrange — start an Activity so Activity.Current is populated.
        using var activity = new Activity("login-test").Start();
        var ctx = new DefaultHttpContext { TraceIdentifier = "trace-not-used" };

        // Act
        var result = Sut().FromHttpContext(ctx);

        // Assert — Activity.Current.Id wins over TraceIdentifier when no
        // Items["CorrelationId"] is set.
        result.CorrelationId.Should().Be(activity.Id, "Activity.Current wins ahead of TraceIdentifier.");
    }

    [Fact]
    public void FromHttpContext_FallsBackToTraceIdentifier_WhenNoActivity()
    {
        // Arrange
        // Force Activity.Current to null so the TraceIdentifier fallback runs.
        Activity.Current = null;
        var ctx = new DefaultHttpContext { TraceIdentifier = "trace-id-xyz" };

        // Act
        var result = Sut().FromHttpContext(ctx);

        // Assert
        result.CorrelationId.Should().Be("trace-id-xyz");
    }

    [Fact]
    public void FromHttpContext_NullContext_Throws()
    {
        // Arrange + Act
        Action act = () => Sut().FromHttpContext(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void FromHttpContext_IpvLongerThan45Chars_IsCappedAt45()
    {
        // Arrange — pathological forwarded value to confirm the cap.
        var oversized = new string('a', 100);
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Forwarded-For"] = oversized;

        // Act
        var result = Sut().FromHttpContext(ctx);

        // Assert
        result.SourceIp.Length.Should().Be(45);
    }
}
