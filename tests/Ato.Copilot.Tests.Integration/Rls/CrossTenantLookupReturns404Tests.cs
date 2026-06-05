using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Rls;

/// <summary>
/// T106 [US5] Wave 2 (Epic #125 / Task #163): Cross-tenant GET-by-ID returns
/// a 404-equivalent at the data layer — the RLS FILTER predicate hides the
/// row so a Tenant B session cannot retrieve a Tenant A row.
///
/// Each test:
/// 1. Seeds a known row under Tenant A (uncontexted connection, no RLS).
/// 2. Opens a Tenant B session.
/// 3. Queries by the exact primary-key value of the Tenant A row.
/// 4. Asserts zero rows returned — data-layer 404.
///
/// Entities covered: OrgInheritanceDefault, SecurityCapability,
/// CapabilityControlMapping, CapabilityHistoryEvent, SapTeamMember.
/// CspInheritedCapability is [GlobalReference] — excluded by design.
/// </summary>
[Collection("RLS")]
public class CrossTenantLookupReturns404Tests
{
    private readonly RlsIntegrationFixture _fx;

    public CrossTenantLookupReturns404Tests(RlsIntegrationFixture fx)
    {
        _fx = fx;
    }

    // ─── OrgInheritanceDefault (Feature 044) ─────────────────────────────

    [SkippableFact]
    public async Task OrgInheritanceDefault_TenantBSession_CannotSeeTenantARow()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason ?? "Docker not available — skipping RLS testcontainer test.");

        // The fixture already seeded a Tenant A row with a well-known ID.
        await using var conn = await _fx.OpenConnectionAsync();
        await SetSessionContextAsync(conn, "TenantId", _fx.TenantB.ToString());

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM OrgInheritanceDefaults WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@id", _fx.OrgInheritanceDefaultTenantAId.ToString());
        var count = (int)(await cmd.ExecuteScalarAsync())!;

        count.Should().Be(0, "RLS FILTER must hide Tenant A's OrgInheritanceDefault row from a Tenant B session (data-layer 404)");
    }

    // ─── SecurityCapability (Feature 046) ────────────────────────────────

    [SkippableFact]
    public async Task SecurityCapability_TenantBSession_CannotSeeTenantARow()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason ?? "Docker not available — skipping RLS testcontainer test.");

        await using var conn = await _fx.OpenConnectionAsync();
        await SetSessionContextAsync(conn, "TenantId", _fx.TenantB.ToString());

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM SecurityCapabilities WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@id", _fx.SecurityCapabilityTenantAId.ToString());
        var count = (int)(await cmd.ExecuteScalarAsync())!;

        count.Should().Be(0, "RLS FILTER must hide Tenant A's SecurityCapability row from a Tenant B session (data-layer 404)");
    }

    // ─── CapabilityControlMapping (Feature 046) ──────────────────────────

    [SkippableFact]
    public async Task CapabilityControlMapping_TenantBSession_CannotSeeTenantARow()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason ?? "Docker not available — skipping RLS testcontainer test.");

        await using var conn = await _fx.OpenConnectionAsync();
        await SetSessionContextAsync(conn, "TenantId", _fx.TenantB.ToString());

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM CapabilityControlMappings WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@id", _fx.CapabilityControlMappingTenantAId.ToString());
        var count = (int)(await cmd.ExecuteScalarAsync())!;

        count.Should().Be(0, "RLS FILTER must hide Tenant A's CapabilityControlMapping row from a Tenant B session (data-layer 404)");
    }

    // ─── CapabilityHistoryEvent (Feature 050) ────────────────────────────

    [SkippableFact]
    public async Task CapabilityHistoryEvent_TenantBSession_CannotSeeTenantARow()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason ?? "Docker not available — skipping RLS testcontainer test.");

        await using var conn = await _fx.OpenConnectionAsync();
        await SetSessionContextAsync(conn, "TenantId", _fx.TenantB.ToString());

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM CapabilityHistoryEvents WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@id", _fx.CapabilityHistoryEventTenantAId);
        var count = (int)(await cmd.ExecuteScalarAsync())!;

        count.Should().Be(0, "RLS FILTER must hide Tenant A's CapabilityHistoryEvent row from a Tenant B session (FR-013 / data-layer 404)");
    }

    // ─── SapTeamMember (Feature 018) ─────────────────────────────────────
    // Requires inline SAP + RegisteredSystem seed on a no-context connection.

    [SkippableFact]
    public async Task SapTeamMember_TenantBSession_CannotSeeTenantARow()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason ?? "Docker not available — skipping RLS testcontainer test.");

        // Seed a Tenant A system → SAP → team member via uncontexted connection.
        await using var seedConn = await _fx.OpenConnectionAsync();
        var sysA = Guid.NewGuid().ToString();
        var sapA = Guid.NewGuid().ToString();
        var memberA = Guid.NewGuid().ToString();

        await using (var cmd = seedConn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO RegisteredSystems
                    (Id, TenantId, Name, SystemType, MissionCriticality, HostingEnvironment, CurrentRmfStep, RmfStepUpdatedAt, CreatedBy, CreatedAt, IsActive)
                VALUES (@sys, @ta, 'RLS-404-SysA', 0, 0, 'Azure', 0, SYSUTCDATETIME(), 'seed', SYSUTCDATETIME(), 1);
                """;
            cmd.Parameters.AddWithValue("@sys", sysA);
            cmd.Parameters.AddWithValue("@ta", _fx.TenantA);
            await cmd.ExecuteNonQueryAsync();
        }
        await using (var cmd = seedConn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO SecurityAssessmentPlans
                    (Id, TenantId, RegisteredSystemId, Status, Title, BaselineLevel, Content, TotalControls, CustomerControls, InheritedControls, SharedControls, StigBenchmarkCount, GeneratedBy, GeneratedAt, Format)
                VALUES (@sap, @ta, @sys, 0, 'SAP-404', 'Moderate', 'c', 10, 5, 3, 2, 0, 'seed', SYSUTCDATETIME(), 'markdown');
                """;
            cmd.Parameters.AddWithValue("@sap", sapA);
            cmd.Parameters.AddWithValue("@sys", sysA);
            cmd.Parameters.AddWithValue("@ta", _fx.TenantA);
            await cmd.ExecuteNonQueryAsync();
        }
        await using (var cmd = seedConn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO SapTeamMembers (Id, TenantId, SecurityAssessmentPlanId, Name, Organization, Role)
                VALUES (@m, @ta, @sap, 'Alice 404', 'Org A', 'Lead Assessor');
                """;
            cmd.Parameters.AddWithValue("@m", memberA);
            cmd.Parameters.AddWithValue("@sap", sapA);
            cmd.Parameters.AddWithValue("@ta", _fx.TenantA);
            await cmd.ExecuteNonQueryAsync();
        }

        // Query from Tenant B — must see zero rows for the Tenant A member.
        await using var queryConn = await _fx.OpenConnectionAsync();
        await SetSessionContextAsync(queryConn, "TenantId", _fx.TenantB.ToString());

        await using var cmd2 = queryConn.CreateCommand();
        cmd2.CommandText = "SELECT COUNT(1) FROM SapTeamMembers WHERE Id = @m;";
        cmd2.Parameters.AddWithValue("@m", memberA);
        var count = (int)(await cmd2.ExecuteScalarAsync())!;

        count.Should().Be(0, "RLS FILTER must hide Tenant A's SapTeamMember row from a Tenant B session (data-layer 404)");
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static async Task SetSessionContextAsync(SqlConnection conn, string key, string value)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC sp_set_session_context @key, @value;";
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value);
        await cmd.ExecuteNonQueryAsync();
    }
}
