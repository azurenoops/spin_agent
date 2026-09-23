using System.Reflection;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Configuration.Auth;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Auth;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Auth;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Mcp.Endpoints.Auth;
using Ato.Copilot.Mcp.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Auth;

public class SimulationAuditTenantTests
{
    [Theory]
    [InlineData(false, true, true, "Development")]
    [InlineData(true, true, true, "Development")]
    [InlineData(false, false, true, "Development")]
    [InlineData(false, true, false, "Development")]
    [InlineData(false, true, true, "Production")]
    [InlineData(false, true, true, "Staging")]
    public async Task ConfiguredIdentity_AuditsOnlyToAnExistingTenant_WithoutProvisioningMembership(
        bool provisioned, bool systemTenantExists, bool simulationMode, string environmentName)
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options;
        var tenant = Guid.NewGuid();
        await using (var db = new AtoCopilotContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            if (systemTenantExists) db.Tenants.Add(new Tenant { Id = Guid.Empty, DisplayName = "System" });
            if (provisioned) db.Tenants.Add(new Tenant { Id = tenant, DisplayName = "Configured tenant" });
            await db.SaveChangesAsync();
        }
        var factory = new Mock<IDbContextFactory<AtoCopilotContext>>();
        factory.Setup(value => value.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AtoCopilotContext(options));
        var environment = Mock.Of<IHostEnvironment>(value => value.EnvironmentName == environmentName);
        var identity = new SimulatedIdentityDescriptor
        {
            IdentityId = "synthetic", DisplayName = "Synthetic user", Persona = "CspAdmin",
            Oid = Guid.NewGuid().ToString(), Tid = Guid.NewGuid().ToString(), TenantId = tenant, Roles = ["CSP.Admin"],
        };
        var http = new DefaultHttpContext();
        var method = typeof(AuthEndpoints).GetMethod("PostSimulateAsync", BindingFlags.NonPublic | BindingFlags.Static)!;

        // Act
        var attempt = () => (Task<IResult>)method.Invoke(null,
        [
            http, environment, Options.Create(new CacAuthOptions { SimulationMode = simulationMode, SimulatedIdentities = [identity] }),
            factory.Object, new LoginAuditService(NullLogger<LoginAuditService>.Instance), new TenantContextAccessor(),
            new LoginAuditContextAccessor(), NullLoggerFactory.Instance, CancellationToken.None, identity.IdentityId,
        ])!;

        // Assert
        if (!provisioned && !systemTenantExists)
        {
            await attempt.Should().ThrowAsync<DbUpdateException>();
            http.Response.Headers.SetCookie.Should().BeEmpty("a failed audit write must not issue a session");
            return;
        }
        var result = await attempt();
        await using var verify = new AtoCopilotContext(options);
        if (!simulationMode || environmentName != Environments.Development)
        {
            ((IStatusCodeHttpResult)result).StatusCode.Should().Be(StatusCodes.Status404NotFound);
            http.Response.Headers.SetCookie.Should().BeEmpty();
            (await verify.LoginAuditEvents.SingleAsync()).EventType.Should().Be(LoginAuditEventType.SimulationBlocked);
            (await verify.OrganizationMemberships.CountAsync()).Should().Be(0);
            return;
        }
        ((IStatusCodeHttpResult)result).StatusCode.Should().Be(StatusCodes.Status204NoContent);
        var audit = await verify.LoginAuditEvents.SingleAsync(value => value.EventType == LoginAuditEventType.SimulatedLogin);
        audit.EffectiveTenantId.Should().Be(provisioned ? tenant : Guid.Empty);
        (await verify.Tenants.CountAsync()).Should().Be(provisioned ? 2 : 1);
        (await verify.OrganizationMemberships.CountAsync()).Should().Be(0);
        http.Response.Headers.SetCookie.ToString().Should().Contain("ato-simulation=");
    }
}
