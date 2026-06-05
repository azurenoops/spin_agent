using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Rls;

/// <summary>
/// T103 [US5]: RLS FILTER predicate tests for all [TenantScoped] entities.
/// Wave 2 (Epic #125 / Task #164): extended to cover Features 044-050 entities:
/// OrgInheritanceDefault, SecurityCapability, CapabilityControlMapping,
/// SapTeamMember, and CapabilityHistoryEvent.
/// CspInheritedCapability is [GlobalReference] (no TenantId column) — excluded.
/// </summary>
[Collection("RLS")]
public class RlsFilterPredicateTests
{
    private readonly RlsIntegrationFixture _fx;

    public RlsFilterPredicateTests(RlsIntegrationFixture fx)
    {
        _fx = fx;
    }

    // ─── Original (pre-Wave-2) ────────────────────────────────────────────

    [SkippableFact]
    public async Task TenantA_Session_SeesOnlyTenantARows_OrganizationContexts()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason ?? "Docker not available.");
        await using var conn = await _fx.OpenConnectionAsync();
        await SetSessionContextAsync(conn, "TenantId", _fx.TenantA.ToString());
        var ids = await SelectTenantIdsAsync(conn, "OrganizationContexts");
        ids.Should().Contain(_fx.TenantA, "RLS FILTER must allow the session's own tenant");
        ids.Should().NotContain(_fx.TenantB, "RLS FILTER must hide other tenants' rows (FR-030)");
    }

    [SkippableFact]
    public async Task CspAdminSession_SeesAllTenants_OrganizationContexts()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason ?? "Docker not available.");
        await using var conn = await _fx.OpenConnectionAsync();
        await SetSessionContextAsync(conn, "TenantId", _fx.TenantA.ToString());
        await SetSessionContextAsync(conn, "IsCspAdmin", "true");
        var ids = await SelectTenantIdsAsync(conn, "OrganizationContexts");
        ids.Should().Contain(_fx.TenantA);
        ids.Should().Contain(_fx.TenantB, "CSP-Admin sessions bypass the FILTER predicate (FR-009)");
    }

    // ─── Wave 2: OrgInheritanceDefault (Feature 044) ─────────────────────

    [SkippableFact]
    public async Task TenantA_Session_SeesOnlyTenantARows_OrgInheritanceDefault()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason ?? "Docker not available.");
        await using var conn = await _fx.OpenConnectionAsync();
        await SetSessionContextAsync(conn, "TenantId", _fx.TenantA.ToString());
        var ids = await SelectTenantIdsAsync(conn, "OrgInheritanceDefaults");
        ids.Should().Contain(_fx.TenantA, "RLS FILTER must allow session tenant's OrgInheritanceDefault rows");
        ids.Should().NotContain(_fx.TenantB, "RLS FILTER must hide OrgInheritanceDefault rows for other tenants (FR-030)");
    }

    [SkippableFact]
    public async Task CspAdmin_SeesAllTenants_OrgInheritanceDefault()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason ?? "Docker not available.");
        await using var conn = await _fx.OpenConnectionAsync();
        await SetSessionContextAsync(conn, "TenantId", _fx.TenantA.ToString());
        await SetSessionContextAsync(conn, "IsCspAdmin", "true");
        var ids = await SelectTenantIdsAsync(conn, "OrgInheritanceDefaults");
        ids.Should().Contain(_fx.TenantA);
        ids.Should().Contain(_fx.TenantB, "CSP-Admin bypass must expose all OrgInheritanceDefault rows (FR-009)");
    }

    // ─── Wave 2: SecurityCapability (Feature 046) ─────────────────────────

    [SkippableFact]
    public async Task TenantA_Session_SeesOnlyTenantARows_SecurityCapability()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason ?? "Docker not available.");
        await using var conn = await _fx.OpenConnectionAsync();
        await SetSessionContextAsync(conn, "TenantId", _fx.TenantA.ToString());
        var ids = await SelectTenantIdsAsync(conn, "SecurityCapabilities");
        ids.Should().Contain(_fx.TenantA);
        ids.Should().NotContain(_fx.TenantB, "RLS FILTER must isolate SecurityCapability rows per tenant");
    }

    [SkippableFact]
    public async Task CspAdmin_SeesAllTenants_SecurityCapability()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason ?? "Docker not available.");
        await using var conn = await _fx.OpenConnectionAsync();
        await SetSessionContextAsync(conn, "TenantId", _fx.TenantA.ToString());
        await SetSessionContextAsync(conn, "IsCspAdmin", "true");
        var ids = await SelectTenantIdsAsync(conn, "SecurityCapabilities");
        ids.Should().Contain(_fx.TenantA);
        ids.Should().Contain(_fx.TenantB, "CSP-Admin bypass must expose all SecurityCapability rows (FR-009)");
    }

    // ─── Wave 2: CapabilityControlMapping (Feature 046) ──────────────────

    [SkippableFact]
    public async Task TenantA_Session_SeesOnlyTenantARows_CapabilityControlMapping()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason ?? "Docker not available.");
        await using var conn = await _fx.OpenConnectionAsync();
        await SetSessionContextAsync(conn, "TenantId", _fx.TenantA.ToString());
        var ids = await SelectTenantIdsAsync(conn, "CapabilityControlMappings");
        ids.Should().Contain(_fx.TenantA);
        ids.Should().NotContain(_fx.TenantB, "RLS FILTER must isolate CapabilityControlMapping rows per tenant");
    }

    [SkippableFact]
    public async Task CspAdmin_SeesAllTenants_CapabilityControlMapping()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason ?? "Docker not available.");
        await using var conn = await _fx.OpenConnectionAsync();
        await SetSessionContextAsync(conn, "TenantId", _fx.TenantA.ToString());
        await SetSessionContextAsync(conn, "IsCspAdmin", "true");
        var ids = await SelectTenantIdsAsync(conn, "CapabilityControlMappings");
        ids.Should().Contain(_fx.TenantA);
        ids.Should().Contain(_fx.TenantB, "CSP-Admin bypass must expose all CapabilityControlMapping rows (FR-009)");
    }

    // ─── Wave 2: CapabilityHistoryEvent (Feature 050) ────────────────────

    [SkippableFact]
    public async Task TenantA_Session_SeesOnlyTenantARows_CapabilityHistoryEvent()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason ?? "Docker not available.");
        await using var conn = await _fx.OpenConnectionAsync();
        await SetSessionContextAsync(conn, "TenantId", _fx.TenantA.ToString());
        var ids = await SelectTenantIdsAsync(conn, "CapabilityHistoryEvents");
        ids.Should().Contain(_fx.TenantA);
        ids.Should().NotContain(_fx.TenantB, "RLS FILTER must isolate CapabilityHistoryEvent rows per tenant (FR-013)");
    }

    [SkippableFact]
    public async Task CspAdmin_SeesAllTenants_CapabilityHistoryEvent()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason ?? "Docker not available.");
        await using var conn = await _fx.OpenConnectionAsync();
        await SetSessionContextAsync(conn, "TenantId", _fx.TenantA.ToString());
        await SetSessionContextAsync(conn, "IsCspAdmin", "true");
        var ids = await SelectTenantIdsAsync(conn, "CapabilityHistoryEvents");
        ids.Should().Contain(_fx.TenantA);
        ids.Should().Contain(_fx.TenantB, "CSP-Admin bypass must expose all CapabilityHistoryEvent rows (FR-009)");
    }

    // ─── Wave 2: SapTeamMember (Feature 018) — inline seed required ───────
    // SapTeamMember FK requires SecurityAssessmentPlan, which requires RegisteredSystem.
    // Seed all three on a no-SESSION_CONTEXT connection, then assert Tenant B cannot see
    // the Tenant A member.

    [SkippableFact]
    public async Task TenantA_Session_SeesOnlyTenantARows_SapTeamMember()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason ?? "Docker not available.");

        await using var seedConn = await _fx.OpenConnectionAsync();
        var sysA = Guid.NewGuid().ToString(); var sysB = Guid.NewGuid().ToString();
        var sapA = Guid.NewGuid().ToString(); var sapB = Guid.NewGuid().ToString();
        var mbrA = Guid.NewGuid().ToString(); var mbrB = Guid.NewGuid().ToString();

        await using (var cmd = seedConn.CreateCommand())
        {
            cmd.CommandText =
                "INSERT INTO RegisteredSystems" +
                " (Id,TenantId,Name,SystemType,MissionCriticality,HostingEnvironment,CurrentRmfStep,RmfStepUpdatedAt,CreatedBy,CreatedAt,IsActive)" +
                " VALUES (@sA,@ta,'SAP-Filter-SysA',0,0,'Azure',0,SYSUTCDATETIME(),'seed',SYSUTCDATETIME(),1)," +
                "        (@sB,@tb,'SAP-Filter-SysB',0,0,'Azure',0,SYSUTCDATETIME(),'seed',SYSUTCDATETIME(),1);";
            cmd.Parameters.AddWithValue("@sA", sysA); cmd.Parameters.AddWithValue("@sB", sysB);
            cmd.Parameters.AddWithValue("@ta", _fx.TenantA); cmd.Parameters.AddWithValue("@tb", _fx.TenantB);
            await cmd.ExecuteNonQueryAsync();
        }
        await using (var cmd = seedConn.CreateCommand())
        {
            cmd.CommandText =
                "INSERT INTO SecurityAssessmentPlans" +
                " (Id,TenantId,RegisteredSystemId,Status,Title,BaselineLevel,Content,TotalControls,CustomerControls,InheritedControls,SharedControls,StigBenchmarkCount,GeneratedBy,GeneratedAt,Format)" +
                " VALUES (@sApA,@ta,@sA,0,'SAP-Filter-A','Moderate','c',10,5,3,2,0,'seed',SYSUTCDATETIME(),'markdown')," +
                "        (@sApB,@tb,@sB,0,'SAP-Filter-B','Moderate','c',10,5,3,2,0,'seed',SYSUTCDATETIME(),'markdown');";
            cmd.Parameters.AddWithValue("@sApA", sapA); cmd.Parameters.AddWithValue("@sApB", sapB);
            cmd.Parameters.AddWithValue("@sA", sysA); cmd.Parameters.AddWithValue("@sB", sysB);
            cmd.Parameters.AddWithValue("@ta", _fx.TenantA); cmd.Parameters.AddWithValue("@tb", _fx.TenantB);
            await cmd.ExecuteNonQueryAsync();
        }
        await using (var cmd = seedConn.CreateCommand())
        {
            cmd.CommandText =
                "INSERT INTO SapTeamMembers (Id,TenantId,SecurityAssessmentPlanId,Name,Organization,Role)" +
                " VALUES (@mA,@ta,@sApA,'Alice A','Org A','Lead Assessor')," +
                "        (@mB,@tb,@sApB,'Bob B','Org B','Lead Assessor');";
            cmd.Parameters.AddWithValue("@mA", mbrA); cmd.Parameters.AddWithValue("@mB", mbrB);
            cmd.Parameters.AddWithValue("@sApA", sapA); cmd.Parameters.AddWithValue("@sApB", sapB);
            cmd.Parameters.AddWithValue("@ta", _fx.TenantA); cmd.Parameters.AddWithValue("@tb", _fx.TenantB);
            await cmd.ExecuteNonQueryAsync();
        }

        await using var queryConn = await _fx.OpenConnectionAsync();
        await SetSessionContextAsync(queryConn, "TenantId", _fx.TenantA.ToString());
        await using var q = queryConn.CreateCommand();
        q.CommandText = "SELECT TenantId FROM SapTeamMembers WHERE Id IN (@mA, @mB);";
        q.Parameters.AddWithValue("@mA", mbrA); q.Parameters.AddWithValue("@mB", mbrB);
        var ids = new List<Guid>();
        await using var reader = await q.ExecuteReaderAsync();
        while (await reader.ReadAsync()) ids.Add(reader.GetGuid(0));
        ids.Should().Contain(_fx.TenantA, "RLS FILTER must allow Tenant A's SapTeamMember row");
        ids.Should().NotContain(_fx.TenantB, "RLS FILTER must hide Tenant B's SapTeamMember row (FR-030)");
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static async Task<List<Guid>> SelectTenantIdsAsync(SqlConnection conn, string table)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT TenantId FROM {table};";
        var ids = new List<Guid>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) ids.Add(reader.GetGuid(0));
        return ids;
    }

    private static async Task SetSessionContextAsync(SqlConnection conn, string key, string value)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC sp_set_session_context @key, @value;";
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value);
        await cmd.ExecuteNonQueryAsync();
    }
}
