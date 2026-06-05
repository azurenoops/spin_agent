using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.MsSql;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Rls;

/// <summary>
/// T102 [US5]: shared SQL Server testcontainer fixture used by the RLS integration tests.
/// Wave 2 (Epic #125 / Tasks #162-164): extended to seed Features 044-050 [TenantScoped]
/// entities across both test tenants.
/// CspInheritedCapability is [GlobalReference] (no TenantId column) — excluded by design.
/// </summary>
public sealed class RlsIntegrationFixture : IAsyncLifetime
{
    public Guid TenantA { get; } = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public Guid TenantB { get; } = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    // Wave 2 seed IDs — stable, deterministic, exposed for test assertions.
    public Guid OrgInheritanceDefaultTenantAId   { get; } = Guid.Parse("a1a1a1a1-0000-0000-0000-000000000001");
    public Guid OrgInheritanceDefaultTenantBId   { get; } = Guid.Parse("b2b2b2b2-0000-0000-0000-000000000002");
    public Guid SecurityCapabilityTenantAId       { get; } = Guid.Parse("a1a1a1a1-0000-0000-0000-000000000003");
    public Guid SecurityCapabilityTenantBId       { get; } = Guid.Parse("b2b2b2b2-0000-0000-0000-000000000004");
    public Guid CapabilityControlMappingTenantAId { get; } = Guid.Parse("a1a1a1a1-0000-0000-0000-000000000005");
    public Guid CapabilityControlMappingTenantBId { get; } = Guid.Parse("b2b2b2b2-0000-0000-0000-000000000006");
    public Guid CapabilityHistoryEventTenantAId   { get; } = Guid.Parse("a1a1a1a1-0000-0000-0000-000000000007");
    public Guid CapabilityHistoryEventTenantBId   { get; } = Guid.Parse("b2b2b2b2-0000-0000-0000-000000000008");

    public bool DockerAvailable { get; private set; }
    public string ConnectionString { get; private set; } = string.Empty;
    public string? SkipReason { get; private set; }

    private MsSqlContainer? _container;

    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("ATO_SKIP_DOCKER_TESTS") == "1")
        {
            DockerAvailable = false;
            SkipReason = "ATO_SKIP_DOCKER_TESTS=1";
            return;
        }
        try
        {
            _container = new MsSqlBuilder()
                .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
                .Build();
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            await _container.StartAsync(cts.Token);
            ConnectionString = _container.GetConnectionString();

            var optsBuilder = new DbContextOptionsBuilder<AtoCopilotContext>()
                .UseSqlServer(ConnectionString);
            await using (var db = new AtoCopilotContext(optsBuilder.Options))
            {
                await db.Database.EnsureCreatedAsync();
                await RlsPolicyInstaller.ApplyAsync(db, NullLogger.Instance);
            }
            await SeedAsync();
            DockerAvailable = true;
        }
        catch (Exception ex)
        {
            DockerAvailable = false;
            SkipReason = $"Container start failed: {ex.GetType().Name}: {ex.Message}";
            if (_container is not null)
            {
                try { await _container.DisposeAsync(); } catch { /* swallow */ }
                _container = null;
            }
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    public async Task<SqlConnection> OpenConnectionAsync()
    {
        var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    private async Task SeedAsync()
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();

        // OrganizationContexts (original baseline)
        await ExecAsync(conn,
            "IF NOT EXISTS (SELECT 1 FROM OrganizationContexts WHERE TenantId = @ta)" +
            " INSERT INTO OrganizationContexts" +
            " (Id, TenantId, OrganizationName, Branch, ClassificationPosture, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)" +
            " VALUES (NEWID(), @ta, 'Tenant A Org', 6, NULL, SYSUTCDATETIME()," +
            " '00000000-0000-0000-0000-000000000000', SYSUTCDATETIME(), '00000000-0000-0000-0000-000000000000');" +
            " IF NOT EXISTS (SELECT 1 FROM OrganizationContexts WHERE TenantId = @tb)" +
            " INSERT INTO OrganizationContexts" +
            " (Id, TenantId, OrganizationName, Branch, ClassificationPosture, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)" +
            " VALUES (NEWID(), @tb, 'Tenant B Org', 6, NULL, SYSUTCDATETIME()," +
            " '00000000-0000-0000-0000-000000000000', SYSUTCDATETIME(), '00000000-0000-0000-0000-000000000000');",
            ("@ta", (object)TenantA), ("@tb", (object)TenantB));

        // OrgInheritanceDefault (Feature 044) — InheritanceType:0=Inherited, CapabilityMappingRole:0=Primary
        await ExecAsync(conn,
            "IF NOT EXISTS (SELECT 1 FROM OrgInheritanceDefaults WHERE Id = @idA)" +
            " INSERT INTO OrgInheritanceDefaults (Id,TenantId,ControlId,InheritanceType,Provider,SourceCapabilityIds,SourceCapabilityNames,MappingRole,DerivedAt)" +
            " VALUES (@idA,@ta,'AC-2',0,'Azure AD','cap-a-1','MFA',0,SYSUTCDATETIME());" +
            " IF NOT EXISTS (SELECT 1 FROM OrgInheritanceDefaults WHERE Id = @idB)" +
            " INSERT INTO OrgInheritanceDefaults (Id,TenantId,ControlId,InheritanceType,Provider,SourceCapabilityIds,SourceCapabilityNames,MappingRole,DerivedAt)" +
            " VALUES (@idB,@tb,'AC-2',0,'Azure AD','cap-b-1','MFA',0,SYSUTCDATETIME());",
            ("@idA", (object)OrgInheritanceDefaultTenantAId), ("@idB", (object)OrgInheritanceDefaultTenantBId),
            ("@ta", (object)TenantA), ("@tb", (object)TenantB));

        // SecurityCapability (Feature 046) — CapabilityStatus:0=Planned
        await ExecAsync(conn,
            "IF NOT EXISTS (SELECT 1 FROM SecurityCapabilities WHERE Id = @idA)" +
            " INSERT INTO SecurityCapabilities (Id,TenantId,Name,Provider,Category,Description,ImplementationStatus,Owner,CreatedAt,CreatedBy)" +
            " VALUES (@idA,@ta,'MFA TenantA','Entra ID','IA','Multi-factor auth',0,'ISSO',SYSUTCDATETIME(),'seed');" +
            " IF NOT EXISTS (SELECT 1 FROM SecurityCapabilities WHERE Id = @idB)" +
            " INSERT INTO SecurityCapabilities (Id,TenantId,Name,Provider,Category,Description,ImplementationStatus,Owner,CreatedAt,CreatedBy)" +
            " VALUES (@idB,@tb,'MFA TenantB','Entra ID','IA','Multi-factor auth',0,'ISSO',SYSUTCDATETIME(),'seed');",
            ("@idA", (object)SecurityCapabilityTenantAId), ("@idB", (object)SecurityCapabilityTenantBId),
            ("@ta", (object)TenantA), ("@tb", (object)TenantB));

        // CapabilityControlMapping (Feature 046) — Role:0=Primary
        await ExecAsync(conn,
            "IF NOT EXISTS (SELECT 1 FROM CapabilityControlMappings WHERE Id = @idA)" +
            " INSERT INTO CapabilityControlMappings (Id,TenantId,SecurityCapabilityId,ControlId,RegisteredSystemId,Role,CreatedAt,CreatedBy)" +
            " VALUES (@idA,@ta,@capA,'IA-2',NULL,0,SYSUTCDATETIME(),'seed');" +
            " IF NOT EXISTS (SELECT 1 FROM CapabilityControlMappings WHERE Id = @idB)" +
            " INSERT INTO CapabilityControlMappings (Id,TenantId,SecurityCapabilityId,ControlId,RegisteredSystemId,Role,CreatedAt,CreatedBy)" +
            " VALUES (@idB,@tb,@capB,'IA-2',NULL,0,SYSUTCDATETIME(),'seed');",
            ("@idA", (object)CapabilityControlMappingTenantAId), ("@idB", (object)CapabilityControlMappingTenantBId),
            ("@capA", (object)SecurityCapabilityTenantAId.ToString()), ("@capB", (object)SecurityCapabilityTenantBId.ToString()),
            ("@ta", (object)TenantA), ("@tb", (object)TenantB));

        // CapabilityHistoryEvent (Feature 050) — EventType:0=Created; CapabilityId is logical FK (NoAction)
        var fakeCapabilityId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
        await ExecAsync(conn,
            "IF NOT EXISTS (SELECT 1 FROM CapabilityHistoryEvents WHERE Id = @idA)" +
            " INSERT INTO CapabilityHistoryEvents (Id,CapabilityId,TenantId,EventType,ActorOid,OccurredAt,Summary)" +
            " VALUES (@idA,@capId,@ta,0,'actor-a@dev.mil',SYSUTCDATETIME(),'Created by seed');" +
            " IF NOT EXISTS (SELECT 1 FROM CapabilityHistoryEvents WHERE Id = @idB)" +
            " INSERT INTO CapabilityHistoryEvents (Id,CapabilityId,TenantId,EventType,ActorOid,OccurredAt,Summary)" +
            " VALUES (@idB,@capId,@tb,0,'actor-b@dev.mil',SYSUTCDATETIME(),'Created by seed');",
            ("@idA", (object)CapabilityHistoryEventTenantAId), ("@idB", (object)CapabilityHistoryEventTenantBId),
            ("@capId", (object)fakeCapabilityId), ("@ta", (object)TenantA), ("@tb", (object)TenantB));
    }

    private static async Task ExecAsync(SqlConnection conn, string sql, params (string Name, object Value)[] parameters)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition("RLS")]
public sealed class RlsCollection : ICollectionFixture<RlsIntegrationFixture> { }
