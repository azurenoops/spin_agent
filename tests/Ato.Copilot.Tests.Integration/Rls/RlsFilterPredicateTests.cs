using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Rls;

/// <summary>
/// T103 [US5]: with <c>SESSION_CONTEXT(N'TenantId')</c> set to Tenant A,
/// a <c>SELECT</c> against a <c>[TenantScoped]</c> table returns only
/// Tenant A's rows even though both tenants are seeded (acceptance scenario
/// 1 / FR-030). Uses <c>OrganizationContexts</c> as the canonical
/// tenant-scoped table for this assertion (the <c>Tenants</c> table itself
/// is the tenant root and has no <c>TenantId</c> column).
/// </summary>
[Collection("RLS")]
public class RlsFilterPredicateTests
{
    private readonly RlsIntegrationFixture _fx;

    public RlsFilterPredicateTests(RlsIntegrationFixture fx)
    {
        _fx = fx;
    }

    [SkippableFact]
    public async Task TenantA_Session_SeesOnlyTenantARows()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason ?? "Docker not available — skipping RLS testcontainer test.");

        await using var conn = await _fx.OpenConnectionAsync();
        await SetSessionContextAsync(conn, "TenantId", _fx.TenantA.ToString());

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT TenantId FROM OrganizationContexts;";
        var ids = new List<Guid>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetGuid(0));
        }

        ids.Should().Contain(_fx.TenantA, "RLS FILTER must allow the session's own tenant");
        ids.Should().NotContain(_fx.TenantB, "RLS FILTER must hide other tenants' rows (FR-030)");
    }

    [SkippableFact]
    public async Task CspAdminSession_SeesAllTenants()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason ?? "Docker not available — skipping RLS testcontainer test.");

        await using var conn = await _fx.OpenConnectionAsync();
        await SetSessionContextAsync(conn, "TenantId", _fx.TenantA.ToString());
        await SetSessionContextAsync(conn, "IsCspAdmin", "true");

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT TenantId FROM OrganizationContexts;";
        var ids = new List<Guid>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetGuid(0));
        }

        ids.Should().Contain(_fx.TenantA);
        ids.Should().Contain(_fx.TenantB, "CSP-Admin sessions bypass the FILTER predicate (FR-009)");
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
