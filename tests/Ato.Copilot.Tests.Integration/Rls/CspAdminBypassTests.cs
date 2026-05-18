using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Rls;

/// <summary>
/// T105 [US5]: when the session sets <c>SESSION_CONTEXT(N'IsCspAdmin') = N'true'</c>,
/// cross-tenant reads AND writes succeed (FR-009 / acceptance scenario 3).
/// The CSP-Admin claim must be honored by both the FILTER and BLOCK
/// predicates installed by <c>RlsPolicyInstaller</c>.
/// </summary>
[Collection("RLS")]
public class CspAdminBypassTests
{
    private readonly RlsIntegrationFixture _fx;

    public CspAdminBypassTests(RlsIntegrationFixture fx)
    {
        _fx = fx;
    }

    [SkippableFact]
    public async Task CspAdmin_CanInsertOnBehalfOfAnyTenant()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason ?? "Docker not available — skipping RLS testcontainer test.");

        await using var conn = await _fx.OpenConnectionAsync();
        await SetSessionContextAsync(conn, "TenantId", _fx.TenantA.ToString());
        await SetSessionContextAsync(conn, "IsCspAdmin", "true");

        var cacheKey = $"rls-csp-{Guid.NewGuid():N}";
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO CachedResponses
                (TenantId, CacheKey, ToolName, Response, CachedAt, TtlSeconds, Source, HitCount, SubscriptionId)
            VALUES
                (@forTenant, @key, 'rls.csp-admin', '{}', SYSUTCDATETIME(), 60, 'online', 0, 'sub-test');
            """;
        cmd.Parameters.AddWithValue("@key", cacheKey);
        cmd.Parameters.AddWithValue("@forTenant", _fx.TenantB);

        var act = async () => await cmd.ExecuteNonQueryAsync();
        await act.Should().NotThrowAsync(
            "CSP-Admin sessions bypass the BLOCK predicate (FR-009)");

        // Cleanup so subsequent tests don't see the extra row.
        await using var del = conn.CreateCommand();
        del.CommandText = "DELETE FROM CachedResponses WHERE CacheKey = @key;";
        del.Parameters.AddWithValue("@key", cacheKey);
        await del.ExecuteNonQueryAsync();
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
