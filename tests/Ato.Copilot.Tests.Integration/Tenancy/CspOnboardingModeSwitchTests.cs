using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Mcp;
using Ato.Copilot.Mcp.Configuration;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

/// <summary>
/// T159 [US7]: Boot an existing <c>SingleTenant</c> database with NO
/// <c>CspProfile</c> row, switch to <c>MultiTenant</c>, restart — the CSP
/// onboarding wizard appears, the existing tenant data is preserved but
/// locked behind <c>503 CSP_ONBOARDING_INCOMPLETE</c> until the wizard
/// completes. Acceptance scenario 5 from spec.md US7.
/// </summary>
/// <remarks>
/// RED until T160–T164 are implemented (CspProfile entity, service, endpoints,
/// gate). Uses two sequential factories sharing the same SQLite file.
/// </remarks>
[Collection("Tenancy")]
public class CspOnboardingModeSwitchTests : IAsyncLifetime
{
    private string _sqliteFile = null!;

    public Task InitializeAsync()
    {
        _sqliteFile = Path.Combine(
            Path.GetTempPath(),
            $"ato-copilot-cspmodeswitch-{Guid.NewGuid():N}.db");
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        try
        {
            if (File.Exists(_sqliteFile)) File.Delete(_sqliteFile);
        }
        catch { /* best-effort */ }
        return Task.CompletedTask;
    }

    [Fact]
    public async Task SingleTenantThenMultiTenant_WizardAppears_ExistingDataLockedBy503()
    {
        // ───── First boot: SingleTenant — populate one tenant row ───────
        Guid existingTenantId;
        await using (var single = new ModeFactory(_sqliteFile, DeploymentMode.SingleTenant))
        {
            using var scope = single.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var any = await db.Tenants.FirstOrDefaultAsync();
            any.Should().NotBeNull("SingleTenant boot must create the default tenant");
            existingTenantId = any!.Id;

            // Confirm there is no CspProfile row.
            var cspCount = await db.Set<CspProfile>().IgnoreQueryFilters().CountAsync();
            cspCount.Should().Be(0, "SingleTenant mode never creates a CspProfile");
        }

        // ───── Second boot: MultiTenant — wizard expected ───────────────
        await using var multi = new ModeFactory(_sqliteFile, DeploymentMode.MultiTenant);
        var ctx = multi.GetActiveContext();
        ctx.IsCspAdmin = true;
        ctx.TenantId = existingTenantId;
        ctx.Status = TenantStatus.Active;

        using var multiClient = multi.CreateClient();

        // Wizard endpoint reachable
        var stateResp = await multiClient.GetAsync("/api/csp/onboarding/state");
        stateResp.StatusCode.Should().Be(HttpStatusCode.OK,
            "/api/csp/onboarding/state must be reachable for the CSP-Admin");
        var stateBody = await stateResp.Content.ReadFromJsonAsync<JsonElement>();
        stateBody.GetProperty("data").GetProperty("onboardingState").GetString()
            .Should().BeOneOf("Pending", "InWizard");

        // /api/tenants gated behind 503 until the wizard finishes
        var tenantsResp = await multiClient.GetAsync("/api/tenants");
        tenantsResp.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var tenantsBody = await tenantsResp.Content.ReadFromJsonAsync<JsonElement>();
        tenantsBody.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("CSP_ONBOARDING_INCOMPLETE");

        // Existing single-tenant data preserved (queried directly through DbContext —
        // the gate is HTTP-level only).
        using var verifyScope = multi.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var preservedTenant = await verifyDb.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == existingTenantId);
        preservedTenant.Should().NotBeNull(
            "switching to MultiTenant must not delete the SingleTenant tenant row");
    }

    /// <summary>
    /// Test factory that pins a shared SQLite file and a chosen
    /// <see cref="DeploymentMode"/>. Two instances created sequentially
    /// simulate a host restart with new env vars.
    /// </summary>
    private sealed class ModeFactory : WebApplicationFactory<McpProgram>
    {
        private readonly TenantContext _activeContext = new()
        {
            TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            IsCspAdmin = true,
            Status = TenantStatus.Active,
        };

        public TenantContext GetActiveContext() => _activeContext;

        public ModeFactory(string sqliteFile, DeploymentMode mode)
        {
            Environment.SetEnvironmentVariable("ATO_Database__Provider", "Sqlite");
            Environment.SetEnvironmentVariable("ATO_ConnectionStrings__DefaultConnection",
                $"Data Source={sqliteFile};Mode=ReadWriteCreate");
            Environment.SetEnvironmentVariable("ATO_Deployment__Mode", mode.ToString());
            Environment.SetEnvironmentVariable("ATO_Auth__Impersonation__SigningKey",
                "ato-copilot-tests-impersonation-signing-key-stable-32B!");
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
            Environment.SetEnvironmentVariable("ATO_Tenant__Resolution__BypassForTests", "true");
            Environment.SetEnvironmentVariable("ATO_Auth__BypassForTests", "true");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.Configure<HostOptions>(o =>
                    o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);

                // Replace the scoped tenant context with the test-controlled one.
                for (var i = services.Count - 1; i >= 0; i--)
                {
                    if (services[i].ServiceType == typeof(Ato.Copilot.Core.Interfaces.Tenancy.ITenantContext))
                    {
                        services.RemoveAt(i);
                    }
                }
                services.AddScoped<Ato.Copilot.Core.Interfaces.Tenancy.ITenantContext>(_ => _activeContext);
            });
        }
    }
}
