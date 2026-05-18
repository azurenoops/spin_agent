using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Mcp;
using Ato.Copilot.Mcp.Configuration;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

/// <summary>
/// T080 [US3]: Verifies that switching <c>ATO_Deployment__Mode</c> from
/// <c>SingleTenant</c> to <c>MultiTenant</c> across a host restart:
/// <list type="bullet">
///   <item>preserves the data created in single-tenant mode (no rows lost),</item>
///   <item>reveals the multi-tenant administrative surface to a CSP-Admin
///         caller after the second boot.</item>
/// </list>
/// </summary>
/// <remarks>
/// RED until T081–T084 are implemented. The test uses a <em>shared</em>
/// SQLite file across two host instances so the second host sees the rows
/// the first one wrote.
/// </remarks>
[Collection("Tenancy")]
public class ModeSwitchTests : IAsyncLifetime
{
    private string _sqliteFile = null!;

    public Task InitializeAsync()
    {
        _sqliteFile = Path.Combine(Path.GetTempPath(), $"ato-copilot-modeswitch-{Guid.NewGuid():N}.db");
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
    public async Task ModeSwitch_PreservesDataAndExposesMultiTenantSurface()
    {
        // ───── First boot: SingleTenant ────────────────────────────────
        await using (var single = new SharedSqliteFactory<McpProgram>(_sqliteFile, DeploymentMode.SingleTenant))
        {
            using var client = single.CreateClient();
            var modeResp = await client.GetAsync("/api/deployment/mode");
            modeResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await modeResp.Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("data").GetProperty("mode").GetString().Should().Be("SingleTenant");

            // Confirm at least one tenant row exists (the bootstrap default tenant).
            using var scope = single.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var count = await db.Tenants.CountAsync();
            count.Should().BeGreaterThan(0,
                "TenantBootstrapService should have created at least the default tenant in SingleTenant mode");
        }

        // ───── Second boot: MultiTenant ───────────────────────────────
        await using var multi = new SharedSqliteFactory<McpProgram>(_sqliteFile, DeploymentMode.MultiTenant);
        var ctx = multi.GetActiveContext();
        ctx.IsCspAdmin = true;
        ctx.TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;

        using var multiClient = multi.CreateClient();

        var modeBody = await (await multiClient.GetAsync("/api/deployment/mode"))
            .Content.ReadFromJsonAsync<JsonElement>();
        modeBody.GetProperty("data").GetProperty("mode").GetString().Should().Be("MultiTenant");

        // Tenants list should be reachable for the CSP.Admin and contain the
        // rows seeded by both boots (default + A + B at minimum).
        var listResp = await multiClient.GetAsync("/api/tenants");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var listBody = await listResp.Content.ReadFromJsonAsync<JsonElement>();
        listBody.GetProperty("data").GetProperty("items").GetArrayLength()
            .Should().BeGreaterOrEqualTo(2,
                "switching to MultiTenant must not destroy the rows from the SingleTenant boot");
    }

    /// <summary>
    /// <see cref="MultiTenantWebApplicationFactory{TStartup}"/> variant that
    /// pins the SQLite file path AND the deployment mode. Used to simulate a
    /// "restart with different env vars" by spinning two factories that share
    /// the same on-disk database.
    /// </summary>
    private sealed class SharedSqliteFactory<TStartup> : MultiTenantWebApplicationFactory<TStartup>
        where TStartup : class
    {
        private readonly DeploymentMode _mode;

        public SharedSqliteFactory(string sqliteFile, DeploymentMode mode)
        {
            // Override env vars set by the parent ctor.
            Environment.SetEnvironmentVariable("ATO_ConnectionStrings__DefaultConnection",
                $"Data Source={sqliteFile};Mode=ReadWriteCreate");
            // Env-var fallback only — IOptions<DeploymentOptions> is pinned
            // via the DeploymentModeOverride hook below.
            Environment.SetEnvironmentVariable("ATO_Deployment__Mode", mode.ToString());
            _mode = mode;
        }

        protected override string DeploymentModeOverride => _mode.ToString();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
        }
    }
}
