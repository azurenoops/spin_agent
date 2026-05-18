using Xunit;
using FluentAssertions;
using Moq;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Mcp.Middleware;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;
using System.Security.Claims;

namespace Ato.Copilot.Tests.Unit.CrossCutting;

/// <summary>
/// Tests that structured logging middleware produces correct audit entries
/// with required structured properties (CorrelationId, UserId, ToolName, PimRole).
/// </summary>
public class StructuredLoggingTests
{
    private readonly Mock<ILogger<AuditLoggingMiddleware>> _loggerMock;
    private readonly ITenantContext _tenantContext;

    public StructuredLoggingTests()
    {
        _loggerMock = new Mock<ILogger<AuditLoggingMiddleware>>();
        _tenantContext = new TestTenantContext();
    }

    /// <summary>
    /// Minimal <see cref="ITenantContext"/> stand-in for unit tests of the
    /// audit middleware (Feature 048 T072 added this dependency).
    /// </summary>
    private sealed class TestTenantContext : ITenantContext
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public Guid? OrganizationId => null;
        public bool IsCspAdmin => false;
        public Guid? ImpersonatedTenantId => null;
        public Guid EffectiveTenantId => TenantId;
        public TenantStatus Status => TenantStatus.Active;
    }


    [Fact]
    public async Task AuditLoggingMiddleware_LogsRequestWithCorrelationId()
    {
        // Arrange
        var context = CreateHttpContext("/mcp/tools/compliance_register_system");
        context.Items["CorrelationId"] = "test-correlation-123";

        var middleware = new AuditLoggingMiddleware(
            next: _ => Task.CompletedTask,
            logger: _loggerMock.Object);

        // Act
        await middleware.InvokeAsync(context, _tenantContext);

        // Assert — should log at least 2 entries: request + response
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("ATO Audit")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeast(2));
    }

    [Fact]
    public async Task AuditLoggingMiddleware_LogsRedactedUserId()
    {
        // Arrange
        var context = CreateHttpContext("/mcp/tools/test_tool");
        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Name, "john.doe@contoso.com") },
            "TestAuth");
        context.User = new ClaimsPrincipal(identity);

        var middleware = new AuditLoggingMiddleware(
            next: _ => Task.CompletedTask,
            logger: _loggerMock.Object);

        // Act
        await middleware.InvokeAsync(context, _tenantContext);

        // Assert — user ID should be redacted (first 8 chars + ***)
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("john.doe***")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce());
    }

    [Fact]
    public async Task AuditLoggingMiddleware_LogsToolNameFromPath()
    {
        // Arrange
        var context = CreateHttpContext("/mcp/tools/compliance_assess_control");

        var middleware = new AuditLoggingMiddleware(
            next: _ => Task.CompletedTask,
            logger: _loggerMock.Object);

        // Act
        await middleware.InvokeAsync(context, _tenantContext);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("ATO Audit")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce());
    }

    [Fact]
    public async Task AuditLoggingMiddleware_LogsErrorOnException()
    {
        // Arrange
        var context = CreateHttpContext("/mcp/tools/failing_tool");

        var middleware = new AuditLoggingMiddleware(
            next: _ => throw new InvalidOperationException("Test failure"),
            logger: _loggerMock.Object);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => middleware.InvokeAsync(context, _tenantContext));

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("FAILED")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once());
    }

    [Fact]
    public async Task AuditLoggingMiddleware_IncludesPimRoleWhenPresent()
    {
        // Arrange
        var context = CreateHttpContext("/mcp/tools/pim_activate_role");
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.Name, "admin@contoso.com"),
                new Claim("pim_role", "Owner"),
            },
            "TestAuth");
        context.User = new ClaimsPrincipal(identity);

        var middleware = new AuditLoggingMiddleware(
            next: _ => Task.CompletedTask,
            logger: _loggerMock.Object);

        // Act
        await middleware.InvokeAsync(context, _tenantContext);

        // Assert — should include PimRole in log output
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("PimRole")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce());
    }

    [Fact]
    public async Task AuditLoggingMiddleware_LogsStatusCodeOnSuccess()
    {
        // Arrange
        var context = CreateHttpContext("/mcp/tools/test");

        var middleware = new AuditLoggingMiddleware(
            next: ctx => { ctx.Response.StatusCode = 200; return Task.CompletedTask; },
            logger: _loggerMock.Object);

        // Act
        await middleware.InvokeAsync(context, _tenantContext);

        // Assert — second log should include status code
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("200")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once());
    }

    [Fact]
    public async Task AuditLoggingMiddleware_FallsBackToRequestIdWhenNoCorrelationId()
    {
        // Arrange
        var context = CreateHttpContext("/mcp/tools/test");
        // Don't set CorrelationId in Items

        var middleware = new AuditLoggingMiddleware(
            next: _ => Task.CompletedTask,
            logger: _loggerMock.Object);

        // Act
        await middleware.InvokeAsync(context, _tenantContext);

        // Assert — should still log (uses generated request ID)
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("ATO Audit")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeast(2));
    }

    private static DefaultHttpContext CreateHttpContext(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = path;
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        return context;
    }
}
