using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Rls;

/// <summary>
/// T104 [US5]: With <c>SESSION_CONTEXT(N'TenantId')</c> set to Tenant A,
/// an <c>INSERT</c> that supplies <c>TenantId = TenantB</c> must be
/// rejected by the SECURITY POLICY's BLOCK predicate (FR-031, acceptance
/// scenario 2). The error number is 33504 (security predicate violation).
/// </summary>
[Collection("RLS")]
public class RlsBlockPredicateTests
{
    private readonly RlsIntegrationFixture _fx;

    public RlsBlockPredicateTests(RlsIntegrationFixture fx)
    {
        _fx = fx;
    }

    [SkippableFact]
    public async Task InsertCrossTenant_FromTenantASession_IsBlocked()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason ?? "Docker not available — skipping RLS testcontainer test.");

        await using var conn = await _fx.OpenConnectionAsync();
        await SetSessionContextAsync(conn, "TenantId", _fx.TenantA.ToString());

        // Try to insert a CachedResponses row owned by Tenant B from Tenant A's session.
        // CachedResponses is non-singleton so we don't trip the unique-key path; the
        // BLOCK predicate AFTER INSERT must reject this with SQL error 33504.
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO CachedResponses
                (TenantId, CacheKey, ToolName, Response, CachedAt, TtlSeconds, Source, HitCount, SubscriptionId)
            VALUES
                (@badTenantId, @key, 'rls.test', '{}', SYSUTCDATETIME(), 60, 'online', 0, 'sub-test');
            """;
        cmd.Parameters.AddWithValue("@badTenantId", _fx.TenantB);
        cmd.Parameters.AddWithValue("@key", $"rls-insert-{Guid.NewGuid():N}");

        var act = async () => await cmd.ExecuteNonQueryAsync();

        var ex = await act.Should()
            .ThrowAsync<SqlException>("RLS BLOCK predicate must reject cross-tenant inserts (FR-031)");
        ex.Which.Number.Should().Be(33504, "SQL Server error 33504 = security predicate violation");
    }

    [SkippableFact]
    public async Task UpdateCrossTenant_FromTenantASession_IsBlocked()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason ?? "Docker not available — skipping RLS testcontainer test.");

        await using var conn = await _fx.OpenConnectionAsync();

        // First seed a Tenant A-owned CachedResponses row via no-session-context
        // (so the BLOCK predicate's `IS NULL` clause permits it).
        var seedKey = $"rls-update-{Guid.NewGuid():N}";
        await using (var seed = conn.CreateCommand())
        {
            seed.CommandText = """
                INSERT INTO CachedResponses
                    (TenantId, CacheKey, ToolName, Response, CachedAt, TtlSeconds, Source, HitCount, SubscriptionId)
                VALUES
                    (@ta, @key, 'rls.test', '{}', SYSUTCDATETIME(), 60, 'online', 0, 'sub-test');
                """;
            seed.Parameters.AddWithValue("@ta", _fx.TenantA);
            seed.Parameters.AddWithValue("@key", seedKey);
            await seed.ExecuteNonQueryAsync();
        }

        // Now switch to Tenant A's session and try to "reassign" the row to Tenant B.
        await SetSessionContextAsync(conn, "TenantId", _fx.TenantA.ToString());
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE CachedResponses SET TenantId = @b WHERE CacheKey = @key;";
        cmd.Parameters.AddWithValue("@b", _fx.TenantB);
        cmd.Parameters.AddWithValue("@key", seedKey);

        var act = async () => await cmd.ExecuteNonQueryAsync();
        var ex = await act.Should().ThrowAsync<SqlException>();
        ex.Which.Number.Should().Be(33504);
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
