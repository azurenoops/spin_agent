using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Rls;

/// <summary>
/// Regression test for the idempotency bug observed in production
/// (Feature 048, redeploy of <c>ca-ato-copilot-mcp-v2</c>):
/// <c>RlsPolicyInstaller.ApplyAsync</c> issued
/// <c>DROP FUNCTION dbo.fn_TenantPredicate</c> before dropping the
/// dependent <c>SECURITY POLICY dbo.TenantSecurityPolicy</c>, so the
/// second startup against a database where the policy already existed
/// failed with:
/// <code>
/// Cannot DROP FUNCTION 'dbo.fn_TenantPredicate' because it is being
/// referenced by object 'TenantSecurityPolicy'.
/// </code>
/// The catch block downgraded that to a warning and the deployment
/// fell back to app-level filters — defense-in-depth was silently
/// lost on every redeploy.
/// </summary>
[Collection("RLS")]
public class RlsPolicyInstallerIdempotencyTests
{
    private readonly RlsIntegrationFixture _fx;

    public RlsPolicyInstallerIdempotencyTests(RlsIntegrationFixture fx)
    {
        _fx = fx;
    }

    /// <summary>
    /// Applying the installer twice against the same database must
    /// succeed and must NOT log a warning. The fixture already applies
    /// the installer once during <c>InitializeAsync</c>, so a single
    /// call here is the second invocation against this database.
    /// </summary>
    [SkippableFact]
    public async Task ApplyAsync_SecondInvocation_DoesNotLogWarning()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason ?? "Docker not available — skipping RLS testcontainer test.");

        // Arrange
        var optsBuilder = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseSqlServer(_fx.ConnectionString);

        var loggerMock = new Mock<ILogger>();

        // Act
        await using (var db = new AtoCopilotContext(optsBuilder.Options))
        {
            await RlsPolicyInstaller.ApplyAsync(db, loggerMock.Object);
        }

        // Assert — the installer is wrapped in a try/catch that downgrades
        // failures to a single LogWarning. A clean idempotent run emits
        // only the LogInformation "Verified Feature 048 RLS policy on N
        // table(s)" line.
        loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()),
            Times.Never,
            "the installer must be idempotent — re-running it against a database where the SECURITY POLICY already exists must not fail");

        loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Verified Feature 048 RLS policy", StringComparison.Ordinal)),
                It.IsAny<Exception?>(),
                (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()),
            Times.Once,
            "a successful idempotent re-apply must emit the verification log line exactly once");
    }
}
