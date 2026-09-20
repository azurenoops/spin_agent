using System.Reflection;
using System.Text.Json;
using Ato.Copilot.Agents.Compliance.Services.Onboarding;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Observability;
using Ato.Copilot.Core.Services.Roles;
using Ato.Copilot.Mcp.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Roles;

/// <summary>
/// T031 [US2] — Failing integration test pinning the unified
/// <see cref="SystemRolesEndpoints"/> contract.
///
/// <para>
/// Exercises the 5 new endpoint handlers + <c>GET /api/roles/effective</c>:
/// <list type="bullet">
///   <item><c>GET /api/roles/system/{systemId}</c> — envelope shape, all 7 roles, source enum values.</item>
///   <item><c>POST /api/roles/system/{systemId}</c> — override write, RBAC matrix denial path.</item>
///   <item><c>DELETE /api/roles/system/{systemId}/{role}/{personId}</c> — inherited rejection (409, ROLE_INHERITED_NOT_REMOVABLE).</item>
///   <item><c>POST /api/roles/organization</c> — Org write, SoD warning render, RBAC matrix denial path.</item>
///   <item><c>DELETE /api/roles/organization/{role}/{personId}</c> — soft-remove + cascade.</item>
///   <item><c>GET /api/roles/effective</c> — caller resolution.</item>
/// </list>
/// </para>
///
/// <para>
/// Drives FR-008, FR-010, FR-011, FR-012, FR-026, FR-027; SC-001, SC-002, SC-007, SC-009.
/// </para>
/// </summary>
public class SystemRolesEndpointsTests
{
    private sealed class StaticFactory : IDbContextFactory<AtoCopilotContext>
    {
        private readonly string _dbName;
        public StaticFactory(string dbName) => _dbName = dbName;
        public AtoCopilotContext CreateDbContext() => new(
            new DbContextOptionsBuilder<AtoCopilotContext>().UseInMemoryDatabase(_dbName).Options);
    }

    // ── World builder ───────────────────────────────────────────────────────

    private sealed record World(
        Guid TenantId,
        string SystemId,
        Guid AdminPersonId,
        Guid IssmPersonId,
        Guid IssoPersonId,
        Guid OtherPersonId,
        IDbContextFactory<AtoCopilotContext> Factory,
        IUnifiedRoleReader Reader,
        ICallerEffectiveRoleResolver Resolver,
        IRoleAuthorizationService Authz,
        ISoDConflictDetector Sod,
        IOrganizationRoleAssignmentService OrgService,
        IOrganizationRoleFanoutQueue Queue,
        RoleMetrics Metrics);

    private static async Task<World> SeedAsync(
        string testName,
        bool seedIssmCaller = true,
        bool seedIssoCaller = false,
        bool seedAdminCaller = false)
    {
        var dbName = $"sys-roles-ep-{testName}-{Guid.NewGuid():N}";
        var factory = new StaticFactory(dbName);
        var tenantId = Guid.NewGuid();
        var systemId = Guid.NewGuid().ToString();
        var adminId = Guid.NewGuid();
        var issmId = Guid.NewGuid();
        var issoId = Guid.NewGuid();
        var otherId = Guid.NewGuid();

        await using (var db = factory.CreateDbContext())
        {
            db.Persons.AddRange(
                new Person { Id = adminId, TenantId = tenantId, DisplayName = "Admin Adams",  Email = "admin@x.mil" },
                new Person { Id = issmId,  TenantId = tenantId, DisplayName = "Iris ISSM",    Email = "issm@x.mil" },
                new Person { Id = issoId,  TenantId = tenantId, DisplayName = "Owen ISSO",    Email = "isso@x.mil" },
                new Person { Id = otherId, TenantId = tenantId, DisplayName = "Other Person", Email = "other@x.mil" });

            db.RegisteredSystems.Add(new RegisteredSystem
            {
                Id = systemId,
                TenantId = tenantId,
                Name = "Test System",
                SystemType = SystemType.MajorApplication,
                MissionCriticality = MissionCriticality.MissionEssential,
            });

            if (seedAdminCaller)
            {
                db.OrganizationRoleAssignments.Add(new OrganizationRoleAssignment
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Role = OrganizationRole.Administrator,
                    PersonId = adminId,
                    IsPrimary = true,
                });
            }
            if (seedIssmCaller)
            {
                db.OrganizationRoleAssignments.Add(new OrganizationRoleAssignment
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Role = OrganizationRole.Issm,
                    PersonId = issmId,
                    IsPrimary = true,
                });
            }
            if (seedIssoCaller)
            {
                db.OrganizationRoleAssignments.Add(new OrganizationRoleAssignment
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Role = OrganizationRole.Isso,
                    PersonId = issoId,
                    IsPrimary = true,
                });
            }
            await db.SaveChangesAsync();
        }

        var reader = new UnifiedRoleReader(factory);
        var resolver = new CallerEffectiveRoleResolver(factory);
        var authz = new RoleAuthorizationService();
        var sod = new SoDConflictDetector(factory);
        var queue = new OrganizationRoleFanoutQueue();
        var metrics = new RoleMetrics();
        var audit = new Mock<IWizardAuditService>();
        var orgService = new OrganizationRoleAssignmentService(
            factory, audit.Object,
            NullLogger<OrganizationRoleAssignmentService>.Instance,
            sod, queue, metrics);

        return new World(tenantId, systemId, adminId, issmId, issoId, otherId,
            factory, reader, resolver, authz, sod, orgService, queue, metrics);
    }

    // ── IResult inspection helpers ──────────────────────────────────────────

    /// <summary>
    /// Extracts the status code from an <see cref="IResult"/> regardless of the
    /// concrete wrapper type (Ok, Json, JsonHttpResult&lt;T&gt;, etc.). Each
    /// helper here is the bridge between Minimal-API <c>IResult</c> objects and
    /// FluentAssertions on JSON shape — without spinning up a TestServer.
    /// </summary>
    private static int StatusOf(IResult result)
    {
        if (result is IStatusCodeHttpResult sc && sc.StatusCode is int code)
            return code;
        // Some Results (e.g. Results.Ok(...)) implement IStatusCodeHttpResult with null code; fall back to 200.
        return 200;
    }

    /// <summary>
    /// Pulls the strongly-typed <c>Value</c> out of a Minimal-API <see cref="IResult"/>
    /// returned from <c>Results.Json</c> / <c>Results.Ok</c> / typed-results, then
    /// round-trips it through System.Text.Json so tests can assert on the exact
    /// envelope JSON keys (status, data, metadata, warnings, error).
    /// </summary>
    private static JsonElement BodyOf(IResult result)
    {
        var prop = result.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
        var value = prop?.GetValue(result);
        var json = JsonSerializer.Serialize(value);
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    // ── Tests ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GET_system_roles_returns_envelope_with_seven_rows()
    {
        // Arrange
        var w = await SeedAsync(nameof(GET_system_roles_returns_envelope_with_seven_rows));

        // Act
        var result = await SystemRolesEndpoints.GetSystemRolesAsync(
            w.SystemId, w.TenantId, w.Reader, w.Factory, CancellationToken.None);

        // Assert — status + envelope shape
        StatusOf(result).Should().Be(200);
        var body = BodyOf(result);
        body.GetProperty("status").GetString().Should().Be("success");
        body.TryGetProperty("metadata", out _).Should().BeTrue("envelope MUST include metadata");
        body.TryGetProperty("data", out var data).Should().BeTrue();

        // 7 rows: 6 RmfRoles + Administrator
        var roles = data.GetProperty("roles").EnumerateArray().ToList();
        roles.Should().HaveCount(7,
            "FR-020: panel must render 7 rows (6 RmfRoles + Administrator)");
        var roleNames = roles.Select(r => r.GetProperty("role").GetString()).ToArray();
        roleNames.Should().BeEquivalentTo(new[]
        {
            "AuthorizingOfficial", "Issm", "Isso", "Sca",
            "SystemOwner", "MissionOwner", "Administrator",
        });

        // Every row has `source` with one of the documented enum values.
        var validSources = new[] { "not-assigned", "override", "inherited", "org-fallback", "legacy" };
        foreach (var r in roles)
        {
            var src = r.GetProperty("source").GetString();
            validSources.Should().Contain(src!,
                "FR-008/FR-010: source must be one of the closed enum values");
        }
    }

    [Fact]
    public async Task POST_system_role_writes_override_and_returns_envelope()
    {
        // Arrange — ISSM caller assigns Isso (allowed per FR-027)
        var w = await SeedAsync(nameof(POST_system_role_writes_override_and_returns_envelope));
        var body = new AssignSystemRoleBody("Isso", w.OtherPersonId);

        // Act
        var result = await SystemRolesEndpoints.AssignSystemRoleAsync(
            w.SystemId, body, w.TenantId, w.IssmPersonId,
            w.Factory, w.Resolver, w.Authz, w.Sod, w.Metrics, CancellationToken.None);

        // Assert — envelope shape + persistence
        StatusOf(result).Should().Be(200);
        var json = BodyOf(result);
        json.GetProperty("status").GetString().Should().Be("success");

        await using var db = w.Factory.CreateDbContext();
        var row = await db.SystemRoleAssignments
            .AsNoTracking()
            .SingleAsync(s => s.TenantId == w.TenantId
                           && s.RegisteredSystemId == w.SystemId
                           && s.Role == OrganizationRole.Isso
                           && s.PersonId == w.OtherPersonId
                           && s.RemovedAt == null);
        row.IsInherited.Should().BeFalse(
            "FR-010: per-system writes via this endpoint MUST produce an override row (IsInherited=false)");
        row.SourceOrganizationRoleAssignmentId.Should().BeNull();
    }

    [Fact]
    public async Task POST_system_role_returns_403_with_RBAC_ROLE_ASSIGN_DENIED_when_caller_lacks_permission()
    {
        // Arrange — ISSO caller tries to assign AuthorizingOfficial (denied per FR-027)
        var w = await SeedAsync(nameof(POST_system_role_returns_403_with_RBAC_ROLE_ASSIGN_DENIED_when_caller_lacks_permission),
            seedIssmCaller: false, seedIssoCaller: true);
        var body = new AssignSystemRoleBody("AuthorizingOfficial", w.OtherPersonId);

        // Act
        var result = await SystemRolesEndpoints.AssignSystemRoleAsync(
            w.SystemId, body, w.TenantId, w.IssoPersonId,
            w.Factory, w.Resolver, w.Authz, w.Sod, w.Metrics, CancellationToken.None);

        // Assert
        StatusOf(result).Should().Be(403);
        var json = BodyOf(result);
        json.GetProperty("status").GetString().Should().Be("error");
        json.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("RBAC_ROLE_ASSIGN_DENIED",
                "FR-027: denied write returns RBAC_ROLE_ASSIGN_DENIED");
        json.GetProperty("error").GetProperty("callerEffectiveRole").GetString().Should().Be("Isso");
        json.GetProperty("error").GetProperty("targetRole").GetString().Should().Be("AuthorizingOfficial");
    }

    [Fact]
    public async Task POST_system_role_with_invalid_role_returns_400_INVALID_ROLE_with_all_seven_names()
    {
        // Arrange
        var w = await SeedAsync(nameof(POST_system_role_with_invalid_role_returns_400_INVALID_ROLE_with_all_seven_names));
        var body = new AssignSystemRoleBody("misionowner", w.OtherPersonId);

        // Act
        var result = await SystemRolesEndpoints.AssignSystemRoleAsync(
            w.SystemId, body, w.TenantId, w.IssmPersonId,
            w.Factory, w.Resolver, w.Authz, w.Sod, w.Metrics, CancellationToken.None);

        // Assert
        StatusOf(result).Should().Be(400);
        var json = BodyOf(result);
        var err = json.GetProperty("error");
        err.GetProperty("code").GetString().Should().Be("INVALID_ROLE");
        var suggestion = err.GetProperty("suggestion").GetString() ?? string.Empty;
        var allSeven = new[]
        {
            "AuthorizingOfficial", "Issm", "Isso", "Sca",
            "SystemOwner", "MissionOwner", "Administrator",
        };
        foreach (var name in allSeven)
        {
            suggestion.Should().Contain(name,
                $"FR-012/SC-007: INVALID_ROLE suggestion MUST list all 7 role names verbatim (missing '{name}')");
        }
    }

    [Fact]
    public async Task DELETE_system_role_returns_409_ROLE_INHERITED_NOT_REMOVABLE_when_row_is_inherited()
    {
        // Arrange — Org-level MissionOwner exists; its inherited per-system row is the target of the DELETE.
        var w = await SeedAsync(nameof(DELETE_system_role_returns_409_ROLE_INHERITED_NOT_REMOVABLE_when_row_is_inherited));
        Guid orgRoleId;
        await using (var db = w.Factory.CreateDbContext())
        {
            orgRoleId = Guid.NewGuid();
            db.OrganizationRoleAssignments.Add(new OrganizationRoleAssignment
            {
                Id = orgRoleId,
                TenantId = w.TenantId,
                Role = OrganizationRole.MissionOwner,
                PersonId = w.OtherPersonId,
                IsPrimary = true,
            });
            db.SystemRoleAssignments.Add(new SystemRoleAssignment
            {
                TenantId = w.TenantId,
                RegisteredSystemId = w.SystemId,
                Role = OrganizationRole.MissionOwner,
                PersonId = w.OtherPersonId,
                IsInherited = true,
                SourceOrganizationRoleAssignmentId = orgRoleId,
            });
            await db.SaveChangesAsync();
        }

        // Act — ISSM caller (allowed) tries to delete an inherited row
        var result = await SystemRolesEndpoints.RemoveSystemRoleAsync(
            w.SystemId, "MissionOwner", w.OtherPersonId, w.TenantId, w.IssmPersonId,
            w.Factory, w.Resolver, w.Authz, w.Metrics, CancellationToken.None);

        // Assert
        StatusOf(result).Should().Be(409);
        var json = BodyOf(result);
        json.GetProperty("status").GetString().Should().Be("error");
        var err = json.GetProperty("error");
        err.GetProperty("code").GetString().Should().Be("ROLE_INHERITED_NOT_REMOVABLE",
            "FR-011: deleting an inherited row through the per-system endpoint returns 409.");
        err.GetProperty("orgRoleId").GetString().Should().Be(orgRoleId.ToString());
    }

    [Fact]
    public async Task POST_org_role_writes_row_and_emits_SoD_warning_when_applicable()
    {
        // Arrange — Same person already holds ISSM; assigning AuthorizingOfficial creates a
        // DoDI 8510.01 SoD warning (AO target with existing ISSM is in the closed conflict
        // table per SoDConflictDetectorTests).
        var w = await SeedAsync(
            nameof(POST_org_role_writes_row_and_emits_SoD_warning_when_applicable),
            seedAdminCaller: true);
        await using (var db = w.Factory.CreateDbContext())
        {
            db.OrganizationRoleAssignments.Add(new OrganizationRoleAssignment
            {
                Id = Guid.NewGuid(),
                TenantId = w.TenantId,
                Role = OrganizationRole.Issm,
                PersonId = w.OtherPersonId,
                IsPrimary = true,
            });
            await db.SaveChangesAsync();
        }

        var body = new AssignOrgRoleBody("AuthorizingOfficial", w.OtherPersonId, IsPrimary: false, Bootstrap: false);

        // Act — Administrator caller (allowed to assign AO per FR-027)
        var result = await SystemRolesEndpoints.AssignOrgRoleAsync(
            body, w.TenantId, w.AdminPersonId,
            w.Factory, w.Resolver, w.Authz, w.Sod, w.OrgService, CancellationToken.None);

        // Assert
        StatusOf(result).Should().Be(200);
        var json = BodyOf(result);
        json.GetProperty("status").GetString().Should().Be("success");
        json.TryGetProperty("warnings", out var warnings).Should().BeTrue(
            "FR-026: SoD warnings MUST surface in the envelope's `warnings` array when conflicts exist.");
        warnings.GetArrayLength().Should().BeGreaterThan(0);
        var first = warnings[0];
        first.GetProperty("code").GetString().Should().Be("SOD_VIOLATION");
        first.GetProperty("dodiReference").GetString().Should().Contain("DoDI 8510.01");
    }

    [Fact]
    public async Task POST_org_role_success_path_has_no_warnings_property()
    {
        // Arrange — fresh tenant, no conflicts, ISSM caller assigns MissionOwner (allowed).
        var w = await SeedAsync(nameof(POST_org_role_success_path_has_no_warnings_property));
        var body = new AssignOrgRoleBody("MissionOwner", w.OtherPersonId, IsPrimary: true, Bootstrap: false);

        // Act
        var result = await SystemRolesEndpoints.AssignOrgRoleAsync(
            body, w.TenantId, w.IssmPersonId,
            w.Factory, w.Resolver, w.Authz, w.Sod, w.OrgService, CancellationToken.None);

        // Assert
        StatusOf(result).Should().Be(200);
        var json = BodyOf(result);
        json.GetProperty("status").GetString().Should().Be("success");
        json.TryGetProperty("warnings", out _).Should().BeFalse(
            "Contract: warnings is OMITTED (not []) when empty, to keep envelopes lean.");
    }

    [Fact]
    public async Task POST_org_role_bootstrap_flag_is_ignored_when_assignments_already_exist()
    {
        // Arrange — Org already has an active row → bootstrap precondition fails.
        // Even if the body sets bootstrap=true, the handler MUST ignore it and
        // fall through to the FR-027 matrix. With ISSO caller assigning AO,
        // that's a denial.
        var w = await SeedAsync(
            nameof(POST_org_role_bootstrap_flag_is_ignored_when_assignments_already_exist),
            seedIssmCaller: false, seedIssoCaller: true);
        var body = new AssignOrgRoleBody("AuthorizingOfficial", w.OtherPersonId, IsPrimary: true, Bootstrap: true);

        // Act
        var result = await SystemRolesEndpoints.AssignOrgRoleAsync(
            body, w.TenantId, w.IssoPersonId,
            w.Factory, w.Resolver, w.Authz, w.Sod, w.OrgService, CancellationToken.None);

        // Assert — security: bootstrap flag MUST be ignored when row count > 0.
        StatusOf(result).Should().Be(403,
            "Security guard: bootstrap=true MUST be ignored when ≥1 active OrganizationRoleAssignment exists for the tenant.");
        var json = BodyOf(result);
        json.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("RBAC_ROLE_ASSIGN_DENIED");
    }

    [Fact]
    public async Task GET_effective_returns_callers_highest_role()
    {
        // Arrange — ISSM caller
        var w = await SeedAsync(nameof(GET_effective_returns_callers_highest_role));

        // Act
        var result = await SystemRolesEndpoints.GetCallerEffectiveRoleAsync(
            w.TenantId, w.IssmPersonId, w.Resolver, CancellationToken.None);

        // Assert
        StatusOf(result).Should().Be(200);
        var json = BodyOf(result);
        json.GetProperty("status").GetString().Should().Be("success");
        json.GetProperty("data").GetProperty("effectiveRole").GetString().Should().Be("Issm");
    }

    [Fact]
    public async Task EntraOid_resolves_effective_role_and_authorizes_MissionOwner_override()
    {
        // Arrange
        var w = await SeedAsync(nameof(EntraOid_resolves_effective_role_and_authorizes_MissionOwner_override));
        var entraObjectId = Guid.NewGuid();
        await using (var db = w.Factory.CreateDbContext())
        {
            var issm = await db.Persons.SingleAsync(p => p.Id == w.IssmPersonId);
            issm.EntraObjectId = entraObjectId;
            await db.SaveChangesAsync();
        }

        // Act
        var effectiveResult = await SystemRolesEndpoints.GetCallerEffectiveRoleAsync(
            w.TenantId, entraObjectId, w.Resolver, CancellationToken.None);
        var assignmentResult = await SystemRolesEndpoints.AssignSystemRoleAsync(
            w.SystemId,
            new AssignSystemRoleBody("MissionOwner", w.OtherPersonId),
            w.TenantId,
            entraObjectId,
            w.Factory,
            w.Resolver,
            w.Authz,
            w.Sod,
            w.Metrics,
            CancellationToken.None);

        // Assert
        StatusOf(effectiveResult).Should().Be(200);
        BodyOf(effectiveResult).GetProperty("data").GetProperty("effectiveRole")
            .GetString().Should().Be("Issm");
        StatusOf(assignmentResult).Should().Be(200);

        await using var verificationDb = w.Factory.CreateDbContext();
        var missionOwner = await verificationDb.SystemRoleAssignments
            .AsNoTracking()
            .SingleAsync(r => r.TenantId == w.TenantId
                           && r.RegisteredSystemId == w.SystemId
                           && r.Role == OrganizationRole.MissionOwner
                           && r.RemovedAt == null);
        missionOwner.PersonId.Should().Be(w.OtherPersonId);
        missionOwner.IsInherited.Should().BeFalse();
    }

    [Fact]
    public async Task GET_effective_returns_null_when_caller_has_no_roles()
    {
        // Arrange — caller with no role rows
        var w = await SeedAsync(nameof(GET_effective_returns_null_when_caller_has_no_roles));

        // Act — query OtherPersonId who has no role rows
        var result = await SystemRolesEndpoints.GetCallerEffectiveRoleAsync(
            w.TenantId, w.OtherPersonId, w.Resolver, CancellationToken.None);

        // Assert
        StatusOf(result).Should().Be(200);
        var json = BodyOf(result);
        var role = json.GetProperty("data").GetProperty("effectiveRole");
        role.ValueKind.Should().Be(JsonValueKind.Null,
            "FR-027: getEffectiveRole returns null when the caller holds no roles.");
    }
}
