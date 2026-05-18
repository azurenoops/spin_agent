using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

/// <summary>
/// T174 [US8]: validates that <see cref="TenantStatus.Disabled"/> tenants are
/// excluded from <c>summary</c> rollups (per FR-098 / acceptance scenario 4)
/// but still appear in the <c>tenants</c> list with their <c>Disabled</c>
/// status; the <c>summary.disabledTenantCount</c> equals 1.
/// </summary>
/// <remarks>
/// Will RED until T177–T181 are implemented and the rollups honor FR-098.
/// </remarks>
[Collection("Tenancy")]
public class CspDashboardDisabledTenantTests
    : IClassFixture<MultiTenantWebApplicationFactory<McpProgram>>
{
    private static readonly Guid DisabledTenantId =
        Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private readonly MultiTenantWebApplicationFactory<McpProgram> _factory;
    private readonly HttpClient _client;

    public CspDashboardDisabledTenantTests(MultiTenantWebApplicationFactory<McpProgram> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();

        var ctx = factory.GetActiveContext();
        ctx.TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        ctx.IsCspAdmin = true;
        ctx.ImpersonatedTenantId = null;
        ctx.Status = TenantStatus.Active;

        SeedAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Get_Summary_DisabledTenantData_IsExcludedFromRollups()
    {
        // Act
        var resp = await _client.GetAsync("/api/csp/dashboard/summary");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var data = body.GetProperty("data");

        // Tenant counts: A, B Active + 1 Disabled = 3 total.
        var counts = data.GetProperty("tenantCounts");
        counts.GetProperty("active").GetInt32().Should().Be(2);
        counts.GetProperty("disabled").GetInt32().Should().Be(1);
        counts.GetProperty("total").GetInt32().Should().Be(3);
        data.GetProperty("disabledTenantCount").GetInt32().Should().Be(1,
            "FR-098 / acceptance scenario 4: disabledTenantCount mirrors the disabled bucket.");

        // The disabled tenant has 2 organizations + 1 system + 1 active ATO
        // decision + 1 open Critical finding + 1 open POA&M + 1 pending
        // deviation, but every one of those rollups MUST exclude it.
        data.GetProperty("organizationCount").GetInt32().Should().Be(0);
        data.GetProperty("systemCount").GetInt32().Should().Be(0);

        var ato = data.GetProperty("atoStatusCounts");
        ato.GetProperty("authorized").GetInt32().Should().Be(0);
        ato.GetProperty("inProcess").GetInt32().Should().Be(0);
        ato.GetProperty("denied").GetInt32().Should().Be(0);

        var sev = data.GetProperty("openFindingsBySeverity");
        sev.GetProperty("critical").GetInt32().Should().Be(0);
        sev.GetProperty("high").GetInt32().Should().Be(0);
        sev.GetProperty("moderate").GetInt32().Should().Be(0);
        sev.GetProperty("low").GetInt32().Should().Be(0);

        data.GetProperty("openPoamCount").GetInt32().Should().Be(0);
        data.GetProperty("openDeviationCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Get_Tenants_DisabledTenant_AppearsInListWithStatus()
    {
        // Act
        var resp = await _client.GetAsync("/api/csp/dashboard/tenants?pageSize=200");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("data").GetProperty("items");

        var disabled = items.EnumerateArray()
            .FirstOrDefault(t => t.GetProperty("tenantId").GetGuid() == DisabledTenantId);
        disabled.ValueKind.Should().NotBe(JsonValueKind.Undefined,
            "FR-098: disabled tenants are visible in the tenants list.");
        disabled.GetProperty("status").GetString().Should().Be("Disabled");
        disabled.GetProperty("displayName").GetString().Should().Be("Disabled Tenant");
    }

    private async Task SeedAsync()
    {
        var factory = _factory.Services.GetRequiredService<IDbContextFactory<AtoCopilotContext>>();
        await using var db = await factory.CreateDbContextAsync();

        // Suspend FK enforcement for the seed: Feature 048's SQLite-only
        // test infrastructure has accumulated cross-feature schema additions
        // (Components, ScanImportRecord, etc.) whose target tables are not
        // part of this fixture's seed graph. Production runs on SQL Server
        // where this is enforced via SESSION_CONTEXT + RLS (T107/T109);
        // the C#-layer EF filter is exercised by TenantQueryFilterTests at
        // unit level. See data-model.md §"Test Isolation Notes".
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");

        // Idempotent reset.
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"Findings\";");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"PoamItems\";");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"Deviations\";");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"AuthorizationDecisions\";");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"RegisteredSystems\";");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"Organizations\";");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM \"Tenants\" WHERE \"Id\" = {DisabledTenantId}");

        // Findings.ControlId is a FK → NistControl.Id (Restrict, optional).
        // Insert the referenced row idempotently before any finding lands.
        if (!await db.NistControls.AnyAsync(c => c.Id == "AC-2"))
        {
            db.NistControls.Add(new NistControl
            {
                Id = "AC-2",
                Family = "AC",
                Title = "Account Management",
                Description = "Test seed.",
                ImpactLevel = "Moderate",
            });
        }

        // Disabled tenant.
        db.Tenants.Add(new Tenant
        {
            Id = DisabledTenantId,
            DisplayName = "Disabled Tenant",
            Status = TenantStatus.Disabled,
            OnboardingState = OnboardingState.Active,
            CreatedBy = "test",
        });

        // Seed the disabled tenant with one of every aggregated entity so the
        // assertion that rollups EXCLUDE it is meaningful.
        var sys = new RegisteredSystem
        {
            TenantId = DisabledTenantId,
            Name = "Disabled-System",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure Government",
        };
        db.RegisteredSystems.Add(sys);

        db.Organizations.AddRange(
            new Organization { TenantId = DisabledTenantId, Name = "D-Org-1", CreatedBy = "test" },
            new Organization { TenantId = DisabledTenantId, Name = "D-Org-2", CreatedBy = "test" });

        db.AuthorizationDecisions.Add(new AuthorizationDecision
        {
            TenantId = DisabledTenantId,
            RegisteredSystemId = sys.Id,
            DecisionType = AuthorizationDecisionType.Ato,
            IsActive = true,
            IssuedBy = "ao", IssuedByName = "AO",
        });

        db.Findings.Add(new ComplianceFinding
        {
            TenantId = DisabledTenantId,
            ControlId = "AC-2", ControlFamily = "AC",
            Title = "Disabled finding",
            Severity = FindingSeverity.Critical,
            Status = FindingStatus.Open,
        });

        db.PoamItems.Add(new PoamItem
        {
            TenantId = DisabledTenantId,
            RegisteredSystemId = sys.Id,
            Weakness = "W", WeaknessSource = "Manual",
            SecurityControlNumber = "AC-2",
            PointOfContact = "POC",
            Status = PoamStatus.Ongoing,
        });

        db.Deviations.Add(new Deviation
        {
            TenantId = DisabledTenantId,
            RegisteredSystemId = sys.Id,
            DeviationType = DeviationType.Waiver,
            Status = DeviationStatus.Pending,
            ControlId = "AC-2",
            Justification = "test",
            RequestedBy = "test",
            ExpirationDate = DateTime.UtcNow.AddYears(1),
        });

        await db.SaveChangesAsync();

        // Re-enable FK enforcement (SQLite default). Subsequent connections
        // get a fresh PRAGMA scope, but be explicit to avoid surprising
        // contributors who reuse this DbContext.
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON;");
    }
}
