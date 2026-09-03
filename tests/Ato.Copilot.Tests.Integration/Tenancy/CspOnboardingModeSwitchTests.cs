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
using Microsoft.Extensions.Configuration;
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
/// gate). Two factories share one SQLite file. The SingleTenant host stays
/// alive until the MultiTenant client is created (CI 33768856347: dispose
/// then CreateClient returned a disposed IServiceProvider).
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
        // CI 33768856347: disposing the first WebApplicationFactory before
        // CreateClient on the second returned a disposed IServiceProvider
        // (ConfigureHostBuilder → GetRequiredService<IServer>). Stack was
        // RunHttpModeAsync — not stdio. Keep the SingleTenant host alive
        // until the MultiTenant client is created; call CreateClient on
        // both (same pattern as ModeSwitchTests, which passes on that run).
        Guid existingTenantId;
        await using var single = new ModeFactory(_sqliteFile, DeploymentMode.SingleTenant);
        using (single.CreateClient())
        {
            using var scope = single.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            await db.Database.OpenConnectionAsync();
            await db.Database.EnsureCreatedAsync();
            await TenancySeedHostedService
                .CreateTenancyTablesIfMissingPublicAsync(db, CancellationToken.None);
            // #region agent log
            try
            {
                var payload = System.Text.Json.JsonSerializer.Serialize(new
                {
                    sessionId = "225414",
                    hypothesisId = "H-sequential-waf",
                    location = "CspOnboardingModeSwitchTests.first-boot",
                    message = "after first WAF CreateClient+DDL; first host still alive",
                    data = new { cs = db.Database.GetConnectionString(), firstDisposed = false },
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
                await File.AppendAllTextAsync("/Volumes/Internal/repos/ato-copilot/.cursor/debug-225414.log", payload + Environment.NewLine);
            }
            catch { /* debug ingest must not fail the fixture */ }
            // #endregion
            var any = await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync();
            if (any is null)
            {
                any = new Tenant
                {
                    DisplayName = "Mode-switch default tenant",
                    Status = TenantStatus.Active,
                    CreatedAt = DateTimeOffset.UtcNow,
                    CreatedBy = "modeswitch-test",
                };
                db.Tenants.Add(any);
                await db.SaveChangesAsync();
            }
            existingTenantId = any.Id;

            int cspCount;
            try
            {
                cspCount = await db.Set<CspProfile>().IgnoreQueryFilters().CountAsync();
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex)
                when (ex.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase))
            {
                cspCount = 0;
            }
            cspCount.Should().Be(0, "SingleTenant mode never creates a CspProfile");
        }

        // ───── Second boot: MultiTenant — wizard expected ───────────────
        Environment.SetEnvironmentVariable("ATO_RUN_MODE", "http");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        await using var multi = new ModeFactory(_sqliteFile, DeploymentMode.MultiTenant);
        var ctx = multi.GetActiveContext();
        ctx.IsCspAdmin = true;
        ctx.TenantId = existingTenantId;
        ctx.Status = TenantStatus.Active;

        // #region agent log
        try
        {
            var payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                sessionId = "225414",
                hypothesisId = "H-disposed-host",
                location = "CspOnboardingModeSwitchTests.before-CreateClient",
                message = "env before MultiTenant CreateClient; SingleTenant host still alive",
                data = new
                {
                    runMode = Environment.GetEnvironmentVariable("ATO_RUN_MODE"),
                    aspEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
                    stdinRedirected = Console.IsInputRedirected,
                    fileExists = File.Exists(_sqliteFile)
                },
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
            await File.AppendAllTextAsync("/Volumes/Internal/repos/ato-copilot/.cursor/debug-225414.log", payload + Environment.NewLine);
        }
        catch { /* debug ingest must not fail the fixture */ }
        // #endregion
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
            // DB env vars are a fallback only — ConnectionStrings + Provider
            // are pinned per-host below. CI sets ATO_CONNECTIONSTRINGS__DEFAULTCONNECTION
            // to :memory:; on Linux that is a distinct env var from the
            // mixed-case name and can win the configuration race.
            var fileCs = $"Data Source={sqliteFile};Mode=ReadWriteCreate";
            Environment.SetEnvironmentVariable("ATO_RUN_MODE", "http");
            Environment.SetEnvironmentVariable("ATO_Database__Provider", "Sqlite");
            Environment.SetEnvironmentVariable("ATO_DATABASE__PROVIDER", "Sqlite");
            Environment.SetEnvironmentVariable("ATO_ConnectionStrings__DefaultConnection", fileCs);
            // Linux env vars are case-sensitive. CI sets the all-caps name to
            // :memory:; leaving it in place lets AddEnvironmentVariables("ATO_")
            // race and boot an empty in-memory database (33768856347).
            Environment.SetEnvironmentVariable("ATO_CONNECTIONSTRINGS__DEFAULTCONNECTION", fileCs);
            Environment.SetEnvironmentVariable("ATO_Auth__Impersonation__SigningKey",
                "ato-copilot-tests-impersonation-signing-key-stable-32B!");
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
            Environment.SetEnvironmentVariable("ATO_Tenant__Resolution__BypassForTests", "true");
            Environment.SetEnvironmentVariable("ATO_Auth__BypassForTests", "true");
            Environment.SetEnvironmentVariable("ATO_AZUREAI__ENABLED", "false");
            _sqliteFile = sqliteFile;
            _mode = mode;
        }

        private readonly string _sqliteFile;
        private readonly DeploymentMode _mode;

        protected override IHost CreateHost(IHostBuilder builder)
        {
            // Re-assert immediately before Program.Main — parallel collections
            // can overwrite process-global env between the ctor and host build.
            Environment.SetEnvironmentVariable("ATO_RUN_MODE", "http");
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
            Environment.SetEnvironmentVariable("ATO_CONNECTIONSTRINGS__DEFAULTCONNECTION",
                $"Data Source={_sqliteFile};Mode=ReadWriteCreate");
            Environment.SetEnvironmentVariable("ATO_DATABASE__PROVIDER", "Sqlite");
            var host = base.CreateHost(builder);
            // #region agent log
            try
            {
                var serverOk = false;
                var disposed = false;
                try
                {
                    _ = host.Services.GetService(typeof(Microsoft.AspNetCore.Hosting.Server.IServer));
                    serverOk = true;
                }
                catch (ObjectDisposedException)
                {
                    disposed = true;
                }
                var payload = System.Text.Json.JsonSerializer.Serialize(new
                {
                    sessionId = "225414",
                    hypothesisId = "H-disposed-host",
                    location = "CspOnboardingModeSwitchTests.ModeFactory.CreateHost",
                    message = "after base.CreateHost",
                    data = new
                    {
                        mode = _mode.ToString(),
                        serverOk,
                        disposed,
                        runMode = Environment.GetEnvironmentVariable("ATO_RUN_MODE"),
                        aspEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                    },
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
                File.AppendAllText("/Volumes/Internal/repos/ato-copilot/.cursor/debug-225414.log", payload + Environment.NewLine);
            }
            catch { /* debug ingest must not fail the fixture */ }
            // #endregion
            return host;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            // Pin Deployment:Mode AND the SQLite file for THIS host.
            // Process-global ATO_* connection env vars race with CI's
            // ATO_CONNECTIONSTRINGS__DEFAULTCONNECTION=:memory: (33768856347).
            builder.ConfigureAppConfiguration(cfg =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Deployment:Mode"] = _mode.ToString(),
                    ["Database:Provider"] = "Sqlite",
                    ["ConnectionStrings:DefaultConnection"] =
                        $"Data Source={_sqliteFile};Mode=ReadWriteCreate",
                    // Unique listen URL so two overlapping testhosts (this
                    // test keeps SingleTenant alive during MultiTenant boot)
                    // do not fight Program.cs app.Urls.Add(Server:Urls).
                    ["Server:Urls"] = $"http://127.0.0.1:{Random.Shared.Next(41000, 49000)}",
                });
            });

            builder.ConfigureServices(services =>
            {
                services.Configure<HostOptions>(o =>
                    o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);

                // Strip the SQL-Server-only BoundaryMigrationService — its
                // StartAsync hard-fails on SQLite which would tear down the
                // test host before WebApplicationFactory can capture it.
                for (var i = services.Count - 1; i >= 0; i--)
                {
                    var d = services[i];
                    if (d.ServiceType == typeof(IHostedService) &&
                        (d.ImplementationType == typeof(Ato.Copilot.Core.Services.BoundaryMigrationService) ||
                         d.ImplementationInstance?.GetType() == typeof(Ato.Copilot.Core.Services.BoundaryMigrationService) ||
                         d.ImplementationType == typeof(Ato.Copilot.Mcp.Server.McpStdioService) ||
                         d.ImplementationInstance?.GetType() == typeof(Ato.Copilot.Mcp.Server.McpStdioService)))
                    {
                        services.RemoveAt(i);
                    }
                }

                // Replace the scoped tenant context with the test-controlled one.
                for (var i = services.Count - 1; i >= 0; i--)
                {
                    if (services[i].ServiceType == typeof(Ato.Copilot.Core.Interfaces.Tenancy.ITenantContext))
                    {
                        services.RemoveAt(i);
                    }
                }
                services.AddScoped<Ato.Copilot.Core.Interfaces.Tenancy.ITenantContext>(_ => _activeContext);

                // Issue the SQLite tenancy DDL so CspProfiles + Tenants tables
                // exist on first boot, BUT do NOT auto-seed an Active
                // CspProfile (the way the standard MultiTenantWebApplicationFactory
                // does). The mode-switch test asserts that the second
                // (MultiTenant) boot finds Pending/InWizard, so seeding here
                // would invalidate the scenario.
                services.AddHostedService<TenancyTablesOnlyHostedService>();
            });
        }
    }

    /// <summary>Creates the tenancy tables but does NOT seed any rows.</summary>
    private sealed class TenancyTablesOnlyHostedService : IHostedService
    {
        private readonly IServiceProvider _services;
        public TenancyTablesOnlyHostedService(IServiceProvider services) => _services = services;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await using var scope = _services.CreateAsyncScope();
            var factory = scope.ServiceProvider.GetService<IDbContextFactory<AtoCopilotContext>>();
            var scoped = scope.ServiceProvider.GetService<AtoCopilotContext>();
            // #region agent log
            try
            {
                var payload = System.Text.Json.JsonSerializer.Serialize(new
                {
                    sessionId = "225414",
                    hypothesisId = "H-modeswitch-conn",
                    location = "CspOnboardingModeSwitchTests.TenancyTablesOnlyHostedService.StartAsync",
                    message = "tenancy DDL targets",
                    data = new
                    {
                        factoryNull = factory is null,
                        scopedNull = scoped is null,
                        scopedCs = scoped?.Database.GetConnectionString()
                    },
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
                await File.AppendAllTextAsync("/Volumes/Internal/repos/ato-copilot/.cursor/debug-225414.log", payload + Environment.NewLine, cancellationToken);
            }
            catch { /* debug ingest must not fail the fixture */ }
            // #endregion
            // CI 33655198294 / 33655171786: factory-only DDL left the scoped
            // AtoCopilotContext on a different SQLite file (no Tenants table).
            if (factory is not null)
            {
                await using var db = await factory.CreateDbContextAsync(cancellationToken);
                await TenancySeedHostedService
                    .CreateTenancyTablesIfMissingPublicAsync(db, cancellationToken);
            }
            if (scoped is not null)
            {
                await TenancySeedHostedService
                    .CreateTenancyTablesIfMissingPublicAsync(scoped, cancellationToken);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
