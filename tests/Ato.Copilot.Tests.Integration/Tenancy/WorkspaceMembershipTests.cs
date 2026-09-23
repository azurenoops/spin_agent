using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Mcp;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

/// <summary>Real HTTP pipeline; neither tenant resolution nor authorization is bypassed.</summary>
public class WorkspaceMembershipTests : IClassFixture<WorkspaceMembershipFactory>
{
    private readonly WorkspaceMembershipFactory _factory;
    private static readonly Guid DirectoryId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid ActorId = Guid.Parse("cccccccc-0000-0000-0000-000000000002");
    private static readonly Guid AdminDirectory = Guid.Parse("cccccccc-0000-0000-0000-000000000003");

    public WorkspaceMembershipTests(WorkspaceMembershipFactory factory) => _factory = factory;

    [Fact]
    public async Task ProductionHost_RegistersSingletonWorkspaceConversationIdentity()
    {
        // Arrange
        using var client = _factory.CreateClient();
        var identity = _factory.Services.GetRequiredService<Ato.Copilot.State.Abstractions.IConversationIdentityAccessor>();
        await using var first = _factory.Services.CreateAsyncScope();
        await using var second = _factory.Services.CreateAsyncScope();

        // Act
        var firstIdentity = first.ServiceProvider.GetRequiredService<Ato.Copilot.State.Abstractions.IConversationIdentityAccessor>();
        var secondIdentity = second.ServiceProvider.GetRequiredService<Ato.Copilot.State.Abstractions.IConversationIdentityAccessor>();

        // Assert
        identity.Should().BeOfType<Ato.Copilot.Mcp.Server.WorkspaceChatScope>();
        firstIdentity.Should().BeSameAs(identity);
        secondIdentity.Should().BeSameAs(identity);
        first.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>().Should().NotBeNull();
        first.ServiceProvider.GetRequiredService<ISystemWorkspaceAccessService>().Should().NotBeNull();
        first.ServiceProvider.GetRequiredService<Ato.Copilot.State.Abstractions.IConversationStateManager>().Should().NotBeNull();
    }

    private HttpClient Client(Guid directory, Guid actor, Guid? tenant = null, bool csp = false)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Tid", directory.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Oid", actor.ToString());
        if (csp) client.DefaultRequestHeaders.Add("X-Test-Roles", "CSP.Admin");
        if (tenant.HasValue)
        {
            client.DefaultRequestHeaders.Add("X-Workspace-Kind", "organization");
            client.DefaultRequestHeaders.Add("X-Workspace-Tenant-Id", tenant.ToString());
            client.DefaultRequestHeaders.Add("X-Workspace-Mode", "ordinary");
        }
        else if (csp)
        {
            client.DefaultRequestHeaders.Add("X-Workspace-Kind", "csp");
            client.DefaultRequestHeaders.Add("X-Workspace-Mode", "ordinary");
        }
        return client;
    }

    private async Task<Guid> PersonAsync(Guid tenant)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var person = new Person { TenantId = tenant, DisplayName = "Explicit member", Email = $"{Guid.NewGuid():N}@example.invalid" };
        db.Persons.Add(person);
        await db.SaveChangesAsync();
        return person.Id;
    }

    [Fact]
    public async Task ExplicitGrant_AllowsIdentityWithoutLegacyHome_AndRevocationDeniesNextRequest()
    {
        // Arrange
        var personId = await PersonAsync(WorkspaceMembershipFactory.TenantAId);
        using var admin = Client(AdminDirectory, Guid.NewGuid(), csp: true);
        var actor = Guid.NewGuid();
        var grant = await admin.PostAsJsonAsync($"/api/tenants/{WorkspaceMembershipFactory.TenantAId}/memberships",
            new { directoryTenantId = DirectoryId, objectId = actor, personId });
        grant.StatusCode.Should().Be(HttpStatusCode.Created, await grant.Content.ReadAsStringAsync());
        (await admin.GetAsync(grant.Headers.Location)).StatusCode.Should().Be(HttpStatusCode.OK);
        var membership = (await grant.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        using var member = Client(DirectoryId, actor, WorkspaceMembershipFactory.TenantAId);

        // Act
        var response = await member.GetAsync("/api/auth/me");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, body.ToString());
        var data = body.GetProperty("data");
        data.TryGetProperty("directoryTenantId", out var directory).Should().BeTrue();
        directory.GetGuid().Should().Be(DirectoryId).And.NotBe(WorkspaceMembershipFactory.TenantAId);
        data.GetProperty("homeTenant").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("effectiveTenant").GetProperty("id").GetGuid().Should().Be(WorkspaceMembershipFactory.TenantAId);
        data.GetProperty("workspace").GetProperty("personId").GetGuid().Should().Be(personId);
        data.GetProperty("permissions").GetProperty("canManageMemberships").GetBoolean().Should().BeFalse();
        (await admin.DeleteAsync($"/api/tenants/{WorkspaceMembershipFactory.TenantAId}/memberships/{membership.GetProperty("id").GetGuid()}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await member.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DirectoryOrEmailAlone_DoesNotGrantAccess()
    {
        // Arrange
        using var client = Client(DirectoryId, ActorId, WorkspaceMembershipFactory.TenantAId);

        // Act
        var response = await client.GetAsync("/api/auth/me");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("errorCode")
            .GetString().Should().Be("WORKSPACE_ACCESS_DENIED");
    }

    private async Task<Guid> GrantAsync(Guid tenant, Guid actor, Guid? person = null)
    {
        using var admin = Client(AdminDirectory, Guid.NewGuid(), csp: true);
        var response = await admin.PostAsJsonAsync($"/api/tenants/{tenant}/memberships",
            new { directoryTenantId = DirectoryId, objectId = actor, personId = person ?? await PersonAsync(tenant) });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task SameObjectId_InAnotherDirectory_IsNotTheGrantedIdentity()
    {
        // Arrange
        var actor = Guid.NewGuid();
        await GrantAsync(WorkspaceMembershipFactory.TenantAId, actor);
        using var impostor = Client(Guid.NewGuid(), actor, WorkspaceMembershipFactory.TenantAId);

        // Act
        var response = await impostor.GetAsync("/api/auth/me");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ForeignPerson_AndUnprivilegedGrant_AreDenied()
    {
        // Arrange
        using var admin = Client(AdminDirectory, Guid.NewGuid(), csp: true);
        var foreignPerson = await PersonAsync(WorkspaceMembershipFactory.TenantBId);
        var actor = Guid.NewGuid();
        await GrantAsync(WorkspaceMembershipFactory.TenantAId, actor);
        using var member = Client(DirectoryId, actor, WorkspaceMembershipFactory.TenantAId);
        var body = new { directoryTenantId = DirectoryId, objectId = Guid.NewGuid(), personId = foreignPerson };

        // Act
        var foreign = await admin.PostAsJsonAsync($"/api/tenants/{WorkspaceMembershipFactory.TenantAId}/memberships", body);
        var unauthorized = await member.PostAsJsonAsync($"/api/tenants/{WorkspaceMembershipFactory.TenantAId}/memberships", body);

        // Assert
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unauthorized.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AuthorizedOrganizationAdministrator_CanGrantButCannotRevokeLastAdministrator()
    {
        // Arrange
        var actor = Guid.NewGuid();
        var person = await PersonAsync(WorkspaceMembershipFactory.TenantAId);
        var membership = await GrantAsync(WorkspaceMembershipFactory.TenantAId, actor, person);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.OrganizationRoleAssignments.Add(new() { TenantId = WorkspaceMembershipFactory.TenantAId,
                PersonId = person, Role = OrganizationRole.Administrator });
            db.OrganizationContexts.Add(new() { TenantId = WorkspaceMembershipFactory.TenantAId,
                OrganizationName = "Authorized profile", SubOrganization = "Not the isolation tenant" });
            await db.SaveChangesAsync();
        }
        using var admin = Client(DirectoryId, actor, WorkspaceMembershipFactory.TenantAId);
        var anotherPerson = await PersonAsync(WorkspaceMembershipFactory.TenantAId);

        // Act
        var grant = await admin.PostAsJsonAsync($"/api/tenants/{WorkspaceMembershipFactory.TenantAId}/memberships",
            new { directoryTenantId = DirectoryId, objectId = Guid.NewGuid(), personId = anotherPerson });
        var revoke = await admin.DeleteAsync($"/api/tenants/{WorkspaceMembershipFactory.TenantAId}/memberships/{membership}");

        // Assert
        grant.StatusCode.Should().Be(HttpStatusCode.Created, await grant.Content.ReadAsStringAsync());
        revoke.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await revoke.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("errorCode")
            .GetString().Should().Be("LAST_ADMIN_PROTECTED");
        (await admin.GetAsync("/api/onboarding/persons/")).StatusCode.Should().Be(HttpStatusCode.OK);
        var organization = await admin.GetFromJsonAsync<JsonElement>("/api/onboarding/organization-context/");
        organization.GetProperty("data").GetProperty("organizationName").GetString().Should().Be("Authorized profile");
    }

    [Fact]
    public async Task ContactCreationAndGrant_WorkWithoutDatabaseRepair()
    {
        // Arrange
        using var admin = Client(AdminDirectory, Guid.NewGuid(), csp: true);
        var path = $"/api/tenants/{WorkspaceMembershipFactory.TenantAId}/membership-persons";

        // Act
        var contact = await admin.PostAsJsonAsync(path,
            new { displayName = "New member", email = $"{Guid.NewGuid():N}@example.invalid" });
        contact.StatusCode.Should().Be(HttpStatusCode.Created, await contact.Content.ReadAsStringAsync());
        var person = (await contact.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("id").GetGuid();
        var actor = Guid.NewGuid();
        await GrantAsync(WorkspaceMembershipFactory.TenantAId, actor, person);
        using var member = Client(DirectoryId, actor, WorkspaceMembershipFactory.TenantAId);

        // Assert
        var me = await member.GetFromJsonAsync<JsonElement>("/api/auth/me");
        me.GetProperty("data").GetProperty("workspace").GetProperty("roles").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task ExplicitTabs_IgnoreSupportCookie_AndDiscoveryRequiresSelection()
    {
        // Arrange
        var actor = Guid.NewGuid();
        await GrantAsync(WorkspaceMembershipFactory.TenantAId, actor);
        await GrantAsync(WorkspaceMembershipFactory.TenantBId, actor);
        using var tabA = Client(DirectoryId, actor, WorkspaceMembershipFactory.TenantAId, csp: true);
        using var tabB = Client(DirectoryId, actor, WorkspaceMembershipFactory.TenantBId, csp: true);
        tabA.DefaultRequestHeaders.Add("Cookie", "ato-impersonate=invalid");
        tabB.DefaultRequestHeaders.Add("Cookie", "ato-impersonate=invalid");
        using var discovery = Client(DirectoryId, actor);

        // Act
        var a = await tabA.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var b = await tabB.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var choices = await discovery.GetFromJsonAsync<JsonElement>("/api/auth/me");

        // Assert
        a.GetProperty("data").GetProperty("effectiveTenant").GetProperty("id").GetGuid().Should().Be(WorkspaceMembershipFactory.TenantAId);
        b.GetProperty("data").GetProperty("effectiveTenant").GetProperty("id").GetGuid().Should().Be(WorkspaceMembershipFactory.TenantBId);
        a.GetProperty("data").GetProperty("isImpersonating").GetBoolean().Should().BeFalse();
        choices.GetProperty("data").GetProperty("workspace").ValueKind.Should().Be(JsonValueKind.Null);
        choices.GetProperty("data").GetProperty("availableWorkspacesTotal").GetInt32().Should().Be(2);
        (await discovery.GetAsync($"/api/tenants/{WorkspaceMembershipFactory.TenantAId}"))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task SupportCookie_IsActorAndDirectoryBound_AndRequiresExplicitMode()
    {
        // Arrange
        var actor = Guid.NewGuid();
        using var admin = Client(AdminDirectory, actor, csp: true);
        var providerMe = (await admin.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("data");
        providerMe.TryGetProperty("directoryTenantId", out var providerDirectory).Should().BeTrue();
        providerDirectory.GetGuid().Should().Be(AdminDirectory);
        var start = await admin.PostAsync($"/api/tenants/{WorkspaceMembershipFactory.TenantAId}/impersonate", null);
        start.StatusCode.Should().Be(HttpStatusCode.OK, await start.Content.ReadAsStringAsync());
        var cookie = start.Headers.GetValues("Set-Cookie").Single(h => h.StartsWith("ato-impersonate=")).Split(';')[0];
        using var support = Client(AdminDirectory, actor, WorkspaceMembershipFactory.TenantAId, csp: true);
        support.DefaultRequestHeaders.Remove("X-Workspace-Mode");
        support.DefaultRequestHeaders.Add("X-Workspace-Mode", "support");
        support.DefaultRequestHeaders.Add("Cookie", cookie);
        using var otherIssuer = Client(DirectoryId, actor, WorkspaceMembershipFactory.TenantAId, csp: true);
        otherIssuer.DefaultRequestHeaders.Remove("X-Workspace-Mode");
        otherIssuer.DefaultRequestHeaders.Add("X-Workspace-Mode", "support");
        otherIssuer.DefaultRequestHeaders.Add("Cookie", cookie);

        // Act
        var authorized = await support.GetAsync("/api/auth/me");
        var denied = await otherIssuer.GetAsync("/api/auth/me");

        // Assert
        authorized.StatusCode.Should().Be(HttpStatusCode.OK, await authorized.Content.ReadAsStringAsync());
        var supportMe = (await authorized.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        supportMe.GetProperty("directoryTenantId").GetGuid().Should().Be(AdminDirectory)
            .And.NotBe(WorkspaceMembershipFactory.TenantAId);
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var exit = await admin.DeleteAsync("/api/tenants/impersonation");
        exit.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Theory]
    [InlineData(OrganizationRole.MissionOwner, true)]
    [InlineData(OrganizationRole.Issm, true)]
    [InlineData(OrganizationRole.Isso, false)]
    [InlineData(OrganizationRole.Assessor, false)]
    [InlineData(OrganizationRole.AuthorizingOfficial, false)]
    [InlineData(OrganizationRole.Administrator, false)]
    public async Task ProfilePermission_UsesMembershipPerson_WithoutGlobalRoleClaims(OrganizationRole role, bool canEdit)
    {
        // Arrange
        var actor = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.Tenants.Add(new() { Id = tenant, DisplayName = "Profile role fixture",
                OnboardingState = Ato.Copilot.Core.Models.Tenancy.OnboardingState.Active });
            await db.SaveChangesAsync();
        }
        var person = await PersonAsync(tenant);
        await GrantAsync(tenant, actor, person);
        var systemId = Guid.NewGuid().ToString();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.RegisteredSystems.Add(new() { Id = systemId, TenantId = tenant,
                Name = "Assigned system", HostingEnvironment = "Test", CreatedBy = "fixture", IsActive = true });
            if (role == OrganizationRole.Administrator)
                db.OrganizationRoleAssignments.Add(new() { TenantId = tenant,
                    PersonId = person, Role = role });
            else
                db.SystemRoleAssignments.Add(new() { TenantId = tenant,
                    PersonId = person, RegisteredSystemId = systemId, Role = role });
            await db.SaveChangesAsync();
        }
        using var member = Client(DirectoryId, actor, tenant);

        // Act
        var response = await member.GetAsync($"/api/dashboard/systems/{systemId}/profile/MissionAndPurpose");
        var save = await member.PutAsJsonAsync($"/api/dashboard/systems/{systemId}/profile/MissionAndPurpose",
            new { content = "Authorized member draft" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("canEditProfile").GetBoolean().Should().Be(canEdit);
        save.StatusCode.Should().Be(canEdit ? HttpStatusCode.OK : HttpStatusCode.Forbidden, await save.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OrdinaryCspMember_CannotReadForeignOrUnassignedSystems()
    {
        // Arrange
        var actor = Guid.NewGuid();
        await GrantAsync(WorkspaceMembershipFactory.TenantAId, actor);
        var own = Guid.NewGuid().ToString();
        var foreign = Guid.NewGuid().ToString();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.RegisteredSystems.AddRange(
                new() { Id = own, TenantId = WorkspaceMembershipFactory.TenantAId, Name = "Unassigned", HostingEnvironment = "Test", CreatedBy = "fixture", IsActive = true },
                new() { Id = foreign, TenantId = WorkspaceMembershipFactory.TenantBId, Name = "Foreign", HostingEnvironment = "Test", CreatedBy = "fixture", IsActive = true });
            await db.SaveChangesAsync();
        }
        using var member = Client(DirectoryId, actor, WorkspaceMembershipFactory.TenantAId, csp: true);

        // Act
        var foreignResponse = await member.GetAsync($"/api/dashboard/systems/{foreign}/profile/MissionAndPurpose");
        var unassignedResponse = await member.GetAsync($"/api/dashboard/systems/{own}/profile/MissionAndPurpose");

        // Assert
        foreignResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unassignedResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var list = await member.GetAsync("/api/dashboard/systems");
        list.StatusCode.Should().Be(HttpStatusCode.OK, await list.Content.ReadAsStringAsync());
        (await list.Content.ReadAsStringAsync()).Should().NotContain(own).And.NotContain(foreign);
    }

    [Fact]
    public async Task RevokedGrant_CanBeRepaired_AndEveryTransitionIsAudited()
    {
        // Arrange
        var actor = Guid.NewGuid();
        var person = await PersonAsync(WorkspaceMembershipFactory.TenantAId);
        var membership = await GrantAsync(WorkspaceMembershipFactory.TenantAId, actor, person);
        using var admin = Client(AdminDirectory, Guid.NewGuid(), csp: true);
        var path = $"/api/tenants/{WorkspaceMembershipFactory.TenantAId}/memberships";
        var body = new { directoryTenantId = DirectoryId, objectId = actor, personId = person };

        // Act
        var idempotent = await admin.PostAsJsonAsync(path, body);
        var revoked = await admin.DeleteAsync($"{path}/{membership}");
        var repaired = await admin.PostAsJsonAsync(path, body);
        var selection = Client(DirectoryId, actor);
        var selected = await selection.PostAsJsonAsync("/api/auth/select-tenant",
            new { tenantId = WorkspaceMembershipFactory.TenantAId, remember = true });

        // Assert
        idempotent.StatusCode.Should().Be(HttpStatusCode.OK);
        revoked.StatusCode.Should().Be(HttpStatusCode.NoContent);
        repaired.StatusCode.Should().Be(HttpStatusCode.OK);
        selected.StatusCode.Should().Be(HttpStatusCode.NoContent, await selected.Content.ReadAsStringAsync());
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var events = await db.LoginAuditEvents.Where(e => e.MetadataJson != null && e.MetadataJson.Contains(membership.ToString())).ToListAsync();
        events.Count(e => e.EventType == Ato.Copilot.Core.Models.Auth.LoginAuditEventType.MembershipGranted).Should().Be(2);
        events.Count(e => e.EventType == Ato.Copilot.Core.Models.Auth.LoginAuditEventType.MembershipRevoked).Should().Be(1);
    }

    [Fact]
    public async Task DisabledOrganization_IsImmediatelyDeniedDespitePreviouslyResolvedScope()
    {
        // Arrange
        var actor = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.Tenants.Add(new() { Id = tenant, DisplayName = "Disabled fixture",
                OnboardingState = Ato.Copilot.Core.Models.Tenancy.OnboardingState.Active });
            await db.SaveChangesAsync();
        }
        await GrantAsync(tenant, actor);
        using var member = Client(DirectoryId, actor, tenant);
        (await member.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            (await db.Tenants.SingleAsync(t => t.Id == tenant)).Status = Ato.Copilot.Core.Models.Tenancy.TenantStatus.Disabled;
            await db.SaveChangesAsync();
        }

        // Act
        var response = await member.GetAsync("/api/auth/me");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MalformedContext_AndRouteMismatch_AreExplicitErrors()
    {
        // Arrange
        var actor = Guid.NewGuid();
        await GrantAsync(WorkspaceMembershipFactory.TenantAId, actor);
        using var malformed = Client(DirectoryId, actor, WorkspaceMembershipFactory.TenantAId);
        malformed.DefaultRequestHeaders.Add("X-Workspace-Kind", "csp");
        using var admin = Client(DirectoryId, actor, WorkspaceMembershipFactory.TenantAId, csp: true);

        // Act
        var invalid = await malformed.GetAsync("/api/auth/me");
        var mismatch = await admin.GetAsync($"/api/tenants/{WorkspaceMembershipFactory.TenantBId}/memberships");

        // Assert
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        mismatch.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task SupportCookie_CannotSwitchTargets_AndOrdinaryModeStillRequiresMembership()
    {
        // Arrange
        var actor = Guid.NewGuid();
        using var admin = Client(AdminDirectory, actor, csp: true);
        var start = await admin.PostAsync($"/api/tenants/{WorkspaceMembershipFactory.TenantAId}/impersonate", null);
        var cookie = start.Headers.GetValues("Set-Cookie").Single(h => h.StartsWith("ato-impersonate=")).Split(';')[0];
        using var support = Client(AdminDirectory, actor, WorkspaceMembershipFactory.TenantBId, csp: true);
        support.DefaultRequestHeaders.Remove("X-Workspace-Mode");
        support.DefaultRequestHeaders.Add("X-Workspace-Mode", "support");
        support.DefaultRequestHeaders.Add("Cookie", cookie);
        using var ordinary = Client(AdminDirectory, actor, WorkspaceMembershipFactory.TenantAId, csp: true);
        ordinary.DefaultRequestHeaders.Add("Cookie", cookie);

        // Act
        var mismatched = await support.GetAsync("/api/auth/me");
        var notMember = await ordinary.GetAsync("/api/auth/me");

        // Assert
        mismatched.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        notMember.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CapturedSupportCookie_IsRejectedAfterHttpExit()
    {
        // Arrange
        var actor = Guid.NewGuid();
        using var admin = Client(AdminDirectory, actor, csp: true);
        var started = await admin.PostAsync($"/api/tenants/{WorkspaceMembershipFactory.TenantAId}/impersonate", null);
        started.StatusCode.Should().Be(HttpStatusCode.OK, await started.Content.ReadAsStringAsync());
        var captured = started.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("ato-impersonate=")).Split(';')[0];
        using var replay = Client(AdminDirectory, actor, WorkspaceMembershipFactory.TenantAId, csp: true);
        replay.DefaultRequestHeaders.Remove("X-Workspace-Mode");
        replay.DefaultRequestHeaders.Add("X-Workspace-Mode", "support");
        replay.DefaultRequestHeaders.Add("Cookie", captured);
        (await replay.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
        admin.DefaultRequestHeaders.Add("Cookie", captured);

        // Act
        var ended = await admin.DeleteAsync("/api/tenants/impersonation");
        var replayed = await replay.GetAsync("/api/auth/me");

        // Assert
        ended.StatusCode.Should().Be(HttpStatusCode.NoContent);
        replayed.StatusCode.Should().Be(HttpStatusCode.Forbidden, "deleting a browser cookie must also revoke its captured authorization token");
        (await replayed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("errorCode")
            .GetString().Should().Be("SUPPORT_SESSION_INVALID");
    }

    [Theory]
    [InlineData("manual")]
    [InlineData("idle_timeout")]
    public async Task SignoutFromOrdinaryTab_RevokesCapturedSupportSessionForAnotherOrganization(string reason)
    {
        // Arrange
        var actor = Guid.NewGuid();
        using var admin = Client(AdminDirectory, actor, csp: true);
        var started = await admin.PostAsync($"/api/tenants/{WorkspaceMembershipFactory.TenantAId}/impersonate", null);
        started.StatusCode.Should().Be(HttpStatusCode.OK, await started.Content.ReadAsStringAsync());
        var captured = started.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("ato-impersonate=")).Split(';')[0];
        var person = await PersonAsync(WorkspaceMembershipFactory.TenantBId);
        (await admin.PostAsJsonAsync($"/api/tenants/{WorkspaceMembershipFactory.TenantBId}/memberships",
            new { directoryTenantId = AdminDirectory, objectId = actor, personId = person }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        using var ordinary = Client(AdminDirectory, actor, WorkspaceMembershipFactory.TenantBId, csp: true);
        ordinary.DefaultRequestHeaders.Add("Cookie", captured);
        using var replay = Client(AdminDirectory, actor, WorkspaceMembershipFactory.TenantAId, csp: true);
        replay.DefaultRequestHeaders.Remove("X-Workspace-Mode");
        replay.DefaultRequestHeaders.Add("X-Workspace-Mode", "support");
        replay.DefaultRequestHeaders.Add("Cookie", captured);

        // Act
        var signedOut = await ordinary.PostAsJsonAsync("/api/auth/signout", new { reason });
        var rejected = await replay.GetAsync("/api/auth/me");

        // Assert
        signedOut.StatusCode.Should().Be(HttpStatusCode.NoContent, await signedOut.Content.ReadAsStringAsync());
        rejected.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var closed = await db.LoginAuditEvents.SingleAsync(e => e.Oid == actor.ToString()
            && e.EventType == Ato.Copilot.Core.Models.Auth.LoginAuditEventType.ImpersonationEnd);
        closed.EffectiveTenantId.Should().Be(WorkspaceMembershipFactory.TenantAId);
    }

    [Fact]
    public async Task AnonymousBootstrap_RemainsPublic_WhileMembershipOperationsRemainProtected()
    {
        // Arrange
        using var client = _factory.CreateClient();

        // Act
        var config = await client.GetAsync("/api/auth/login-config");
        var membership = await client.GetAsync($"/api/tenants/{WorkspaceMembershipFactory.TenantAId}/memberships");

        // Assert
        config.StatusCode.Should().Be(HttpStatusCode.OK);
        membership.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ProvisioningOrganizations_DoesNotRequireInventedDirectoryIds()
    {
        // Arrange
        using var admin = Client(AdminDirectory, Guid.NewGuid(), csp: true);

        // Act
        var first = await admin.PostAsJsonAsync("/api/tenants", new { displayName = "Shared directory organization one" });
        var second = await admin.PostAsJsonAsync("/api/tenants", new { displayName = "Shared directory organization two" });
        var providerCreate = await admin.PostAsJsonAsync("/api/csp/dashboard/tenants",
            new { displayName = $"Provider UI organization {Guid.NewGuid():N}" });

        // Assert
        first.StatusCode.Should().Be(HttpStatusCode.Created, await first.Content.ReadAsStringAsync());
        second.StatusCode.Should().Be(HttpStatusCode.Created, await second.Content.ReadAsStringAsync());
        providerCreate.StatusCode.Should().Be(HttpStatusCode.Created, await providerCreate.Content.ReadAsStringAsync());
        var firstData = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        var secondData = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        firstData.GetProperty("id").GetGuid().Should().NotBe(secondData.GetProperty("id").GetGuid());
        firstData.GetProperty("entraTenantId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task RoleRemoval_DoesNotTreatUnlinkedAdministratorContactAsAnAccessibleReplacement()
    {
        // Arrange
        var tenant = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var administratorRole = Guid.NewGuid();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.Tenants.Add(new() { Id = tenant, DisplayName = "Last admin test",
                OnboardingState = Ato.Copilot.Core.Models.Tenancy.OnboardingState.Active });
            await db.SaveChangesAsync();
        }
        var person = await PersonAsync(tenant);
        var unlinked = await PersonAsync(tenant);
        await GrantAsync(tenant, actor, person);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.OrganizationRoleAssignments.AddRange(
                new() { Id = administratorRole, TenantId = tenant, PersonId = person, Role = OrganizationRole.Administrator },
                new() { TenantId = tenant, PersonId = unlinked, Role = OrganizationRole.Administrator });
            await db.SaveChangesAsync();
        }
        using var admin = Client(DirectoryId, actor, tenant);

        // Act
        var response = await admin.DeleteAsync($"/api/onboarding/role-assignments/{administratorRole}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
    }
}

public sealed class WorkspaceMembershipFactory : MultiTenantWebApplicationFactory<McpProgram>
{
    protected override bool AuthenticateRequestsByDefault => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tenant:Resolution:BypassForTests"] = "false",
            ["Auth:BypassForTests"] = "false",
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ITenantContext>();
            services.AddScoped<ITenantContext, TenantContext>();
            services.AddTransient<IStartupFilter, WorkspaceClaimsFilter>();
        });
    }

    private sealed class WorkspaceClaimsFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (http, run) =>
            {
                if (http.Request.Headers.TryGetValue("X-Test-Oid", out var oid))
                {
                    var claims = new List<Claim>
                    {
                        new("oid", oid.ToString()), new(ClaimTypes.NameIdentifier, oid.ToString()),
                        new("tid", http.Request.Headers["X-Test-Tid"].ToString())
                    };
                    if (http.Request.Headers["X-Test-Roles"] == "CSP.Admin")
                        claims.Add(new(ClaimTypes.Role, "CSP.Admin"));
                    http.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "WorkspaceTest"));
                }
                await run();
            });
            next(app);
        };
    }
}
