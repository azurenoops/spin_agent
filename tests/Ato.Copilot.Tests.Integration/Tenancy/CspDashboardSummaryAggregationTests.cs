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
/// T173 [US8]: seeds three tenants with mixed systems, ATO decisions, findings,
/// POA&amp;Ms, and deviations, then asserts <c>GET /api/csp/dashboard/summary</c>
/// rollups equal the cross-tenant sums (acceptance scenario 1, SC-007 in the
/// feature spec).
/// </summary>
/// <remarks>
/// Will RED until T177–T181 (CspDashboardService + endpoints) are implemented.
/// The interceptor permits cross-tenant inserts when <c>IsCspAdmin = true</c>
/// (set in the ctor), so the seed below assigns explicit <c>TenantId</c>
/// values to span Tenants A, B, and a freshly-introduced Tenant C.
/// </remarks>
[Collection("Tenancy")]
public class CspDashboardSummaryAggregationTests
    : IClassFixture<MultiTenantWebApplicationFactory<McpProgram>>
{
    private static readonly Guid TenantCId = Guid.Parse("c0c0c0c0-cccc-cccc-cccc-c0c0c0c0c0c0");

    private readonly MultiTenantWebApplicationFactory<McpProgram> _factory;
    private readonly HttpClient _client;

    public CspDashboardSummaryAggregationTests(MultiTenantWebApplicationFactory<McpProgram> factory)
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
    public async Task Get_Summary_TotalsRollUpAcrossThreeTenants()
    {
        // Act
        var resp = await _client.GetAsync("/api/csp/dashboard/summary");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var data = body.GetProperty("data");

        // Tenant counts: 3 active (A, B, C), 0 disabled.
        var counts = data.GetProperty("tenantCounts");
        counts.GetProperty("active").GetInt32().Should().Be(3);
        counts.GetProperty("disabled").GetInt32().Should().Be(0);
        counts.GetProperty("total").GetInt32().Should().Be(3);
        data.GetProperty("disabledTenantCount").GetInt32().Should().Be(0);

        // Organizations: 2 (A) + 1 (B) + 3 (C) = 6.
        data.GetProperty("organizationCount").GetInt32().Should().Be(6);

        // Systems: 2 (A) + 1 (B) + 2 (C) = 5.
        data.GetProperty("systemCount").GetInt32().Should().Be(5);

        // ATO statuses (only IsActive decisions): authorized=2 (Ato + ATC),
        // inProcess=1 (Iatt), denied=1 (Dato). System C2 has no decision → skipped.
        var ato = data.GetProperty("atoStatusCounts");
        ato.GetProperty("authorized").GetInt32().Should().Be(2);
        ato.GetProperty("inProcess").GetInt32().Should().Be(1);
        ato.GetProperty("denied").GetInt32().Should().Be(1);

        // Open findings (Open|InProgress) by severity:
        //   Critical=2 (1 Open in A + 1 Open in C),
        //   High=1 (InProgress in A),
        //   Moderate=1 (Open Medium in B → Moderate per contract),
        //   Low=0 (one was Remediated).
        var sev = data.GetProperty("openFindingsBySeverity");
        sev.GetProperty("critical").GetInt32().Should().Be(2);
        sev.GetProperty("high").GetInt32().Should().Be(1);
        sev.GetProperty("moderate").GetInt32().Should().Be(1);
        sev.GetProperty("low").GetInt32().Should().Be(0);

        // Open POA&Ms (Status != Completed && Status != RiskAccepted):
        //   2 Ongoing + 1 Delayed - 1 Completed - 1 RiskAccepted ⇒ 3.
        data.GetProperty("openPoamCount").GetInt32().Should().Be(3);

        // Open deviations (Pending|Approved): 1 Pending + 1 Approved = 2.
        data.GetProperty("openDeviationCount").GetInt32().Should().Be(2);
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

        // Idempotent reset of all tables this test touches. The shared fixture
        // uses a per-fixture SQLite file, but `[Collection("Tenancy")]` is
        // serialized, so a clean slate keeps assertions deterministic across
        // re-runs of the same fixture.
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"Findings\";");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"PoamItems\";");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"Deviations\";");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"AuthorizationDecisions\";");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"RegisteredSystems\";");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"Organizations\";");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM \"Tenants\" WHERE \"Id\" = {TenantCId}");

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

        // Tenant C — third Active tenant.
        db.Tenants.Add(new Tenant
        {
            Id = TenantCId,
            DisplayName = "Test Tenant C",
            Status = TenantStatus.Active,
            OnboardingState = OnboardingState.Active,
            CreatedBy = "test",
        });

        var aId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        var bId = MultiTenantWebApplicationFactory<McpProgram>.TenantBId;
        var cId = TenantCId;

        // Organizations: 2 / 1 / 3 = 6 total.
        db.Organizations.AddRange(
            new Organization { TenantId = aId, Name = "A-Org-1", CreatedBy = "test" },
            new Organization { TenantId = aId, Name = "A-Org-2", CreatedBy = "test" },
            new Organization { TenantId = bId, Name = "B-Org-1", CreatedBy = "test" },
            new Organization { TenantId = cId, Name = "C-Org-1", CreatedBy = "test" },
            new Organization { TenantId = cId, Name = "C-Org-2", CreatedBy = "test" },
            new Organization { TenantId = cId, Name = "C-Org-3", CreatedBy = "test" });

        // Systems: 2 / 1 / 2 = 5 total.
        var sysA1 = NewSystem(aId, "A-System-1");
        var sysA2 = NewSystem(aId, "A-System-2");
        var sysB1 = NewSystem(bId, "B-System-1");
        var sysC1 = NewSystem(cId, "C-System-1");
        var sysC2 = NewSystem(cId, "C-System-2");
        db.RegisteredSystems.AddRange(sysA1, sysA2, sysB1, sysC1, sysC2);

        // ATO decisions: 4 active (Ato, ATC, Iatt, Dato). C2 has none.
        db.AuthorizationDecisions.AddRange(
            new AuthorizationDecision
            {
                TenantId = aId,
                RegisteredSystemId = sysA1.Id,
                DecisionType = AuthorizationDecisionType.Ato,
                IsActive = true,
                IssuedBy = "ao", IssuedByName = "AO",
            },
            new AuthorizationDecision
            {
                TenantId = aId,
                RegisteredSystemId = sysA2.Id,
                DecisionType = AuthorizationDecisionType.AtoWithConditions,
                IsActive = true,
                IssuedBy = "ao", IssuedByName = "AO",
            },
            new AuthorizationDecision
            {
                TenantId = bId,
                RegisteredSystemId = sysB1.Id,
                DecisionType = AuthorizationDecisionType.Iatt,
                IsActive = true,
                IssuedBy = "ao", IssuedByName = "AO",
            },
            new AuthorizationDecision
            {
                TenantId = cId,
                RegisteredSystemId = sysC1.Id,
                DecisionType = AuthorizationDecisionType.Dato,
                IsActive = true,
                IssuedBy = "ao", IssuedByName = "AO",
            });

        // Findings: 5 rows; 4 should be counted as "open" by severity.
        db.Findings.AddRange(
            NewFinding(aId, sysA1.Id, FindingSeverity.Critical, FindingStatus.Open),
            NewFinding(aId, sysA1.Id, FindingSeverity.High, FindingStatus.InProgress),
            NewFinding(bId, sysB1.Id, FindingSeverity.Medium, FindingStatus.Open),
            NewFinding(bId, sysB1.Id, FindingSeverity.Low, FindingStatus.Remediated),
            NewFinding(cId, sysC1.Id, FindingSeverity.Critical, FindingStatus.Open));

        // POA&Ms: 4 rows; 3 are "open" (Ongoing + Delayed are open).
        db.PoamItems.AddRange(
            NewPoam(aId, sysA1.Id, PoamStatus.Ongoing),
            NewPoam(bId, sysB1.Id, PoamStatus.Ongoing),
            NewPoam(cId, sysC1.Id, PoamStatus.Delayed),
            NewPoam(cId, sysC1.Id, PoamStatus.Completed),
            NewPoam(aId, sysA1.Id, PoamStatus.RiskAccepted));

        // Deviations: 4 rows; 2 are open (Pending + Approved).
        db.Deviations.AddRange(
            NewDeviation(aId, sysA1.Id, DeviationStatus.Pending),
            NewDeviation(bId, sysB1.Id, DeviationStatus.Approved),
            NewDeviation(cId, sysC1.Id, DeviationStatus.Denied),
            NewDeviation(cId, sysC1.Id, DeviationStatus.Expired));

        await db.SaveChangesAsync();

        // Re-enable FK enforcement (SQLite default).
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON;");
    }

    private static RegisteredSystem NewSystem(Guid tenantId, string name) => new()
    {
        TenantId = tenantId,
        Name = name,
        SystemType = SystemType.MajorApplication,
        MissionCriticality = MissionCriticality.MissionEssential,
        HostingEnvironment = "Azure Government",
    };

    private static ComplianceFinding NewFinding(
        Guid tenantId,
        string systemId,
        FindingSeverity sev,
        FindingStatus status) => new()
    {
        TenantId = tenantId,
        ControlId = "AC-2",
        ControlFamily = "AC",
        Title = "Test finding",
        Severity = sev,
        Status = status,
    };

    private static PoamItem NewPoam(Guid tenantId, string systemId, PoamStatus status) => new()
    {
        TenantId = tenantId,
        RegisteredSystemId = systemId,
        Weakness = "Test",
        WeaknessSource = "Manual",
        SecurityControlNumber = "AC-2",
        PointOfContact = "POC",
        Status = status,
    };

    private static Deviation NewDeviation(Guid tenantId, string systemId, DeviationStatus status) => new()
    {
        TenantId = tenantId,
        RegisteredSystemId = systemId,
        DeviationType = DeviationType.Waiver,
        Status = status,
        ControlId = "AC-2",
        Justification = "test seed",
        RequestedBy = "test",
        ExpirationDate = DateTime.UtcNow.AddYears(1),
    };
}
