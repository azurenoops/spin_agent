using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

/// <summary>Assignment/operation tests using the real HTTP pipeline without tenancy or auth bypasses.</summary>
public class SystemWorkspaceAccessTests : IClassFixture<WorkspaceMembershipFactory>
{
    private readonly WorkspaceMembershipFactory _factory;
    private static readonly Guid Directory = Guid.Parse("acab0000-0000-0000-0000-000000000001");
    private static readonly Guid Tenant = WorkspaceMembershipFactory.TenantAId;
    public SystemWorkspaceAccessTests(WorkspaceMembershipFactory factory) => _factory = factory;

    private HttpClient Client(Guid actor, bool csp = false)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Tid", Directory.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Oid", actor.ToString());
        client.DefaultRequestHeaders.Add("X-Workspace-Kind", csp ? "csp" : "organization");
        client.DefaultRequestHeaders.Add("X-Workspace-Mode", "ordinary");
        if (csp) client.DefaultRequestHeaders.Add("X-Test-Roles", "CSP.Admin");
        else client.DefaultRequestHeaders.Add("X-Workspace-Tenant-Id", Tenant.ToString());
        return client;
    }

    private async Task<(Guid Actor, Guid Person, string System)> SeedAsync(params OrganizationRole[] roles)
    {
        using var hostClient = _factory.CreateClient();
        var actor = Guid.NewGuid();
        var person = new Person { TenantId = Tenant, DisplayName = "Assigned user", Email = $"{actor:N}@example.invalid" };
        var system = new RegisteredSystem { TenantId = Tenant, Name = $"System {actor:N}", HostingEnvironment = "Test", CreatedBy = "fixture", IsActive = true };
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(db.Tenants, t => t.Id == Tenant))
            .Should().BeTrue($"the host fixture must seed {Tenant}; found {string.Join(",", db.Tenants.Select(t => t.Id))}");
        db.Persons.Add(person);
        db.RegisteredSystems.Add(system);
        await db.SaveChangesAsync();
        db.OrganizationMemberships.Add(new() { TenantId = Tenant, DirectoryTenantId = Directory, ObjectId = actor,
            PersonId = person.Id, GrantedBy = "fixture" });
        foreach (var role in roles)
            db.SystemRoleAssignments.Add(new() { TenantId = Tenant, PersonId = person.Id,
                RegisteredSystemId = system.Id, Role = role, IsInherited = false });
        await db.SaveChangesAsync();
        return (actor, person.Id, system.Id);
    }

    [Theory]
    [InlineData(OrganizationRole.MissionOwner, null, HttpStatusCode.Forbidden, false)]
    [InlineData(OrganizationRole.Issm, null, HttpStatusCode.Created, false)]
    [InlineData(OrganizationRole.Issm, "AuthorizingOfficial", HttpStatusCode.Forbidden, false)]
    [InlineData(OrganizationRole.Issm, "AuthorizingOfficial", HttpStatusCode.Forbidden, true)]
    [InlineData(OrganizationRole.Issm, "SystemOwner", HttpStatusCode.Created, false)]
    public async Task ComponentCreation_RequiresManagementAndTargetRoleAuthority(
        OrganizationRole role, string? targetRole, HttpStatusCode expected, bool numericType)
    {
        // Arrange
        var fixture = await SeedAsync(role);
        using var client = Client(fixture.Actor);
        var name = $"Synthetic component {Guid.NewGuid():N}";

        // Act
        using var response = await client.PostAsJsonAsync($"/api/dashboard/systems/{fixture.System}/components", new
        {
            name, componentType = targetRole is null ? "Thing" : numericType
                ? ((int)ComponentType.Person).ToString(System.Globalization.CultureInfo.InvariantCulture) : "Person", status = "Active",
            personName = targetRole is null ? null : "Synthetic target",
            email = targetRole is null ? null : $"{Guid.NewGuid():N}@example.invalid", rmfRole = targetRole,
        });

        // Assert
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.SystemComponents.AnyAsync(component => component.RegisteredSystemId == fixture.System && component.Name == name))
            .Should().Be(expected == HttpStatusCode.Created);
        if (expected == HttpStatusCode.Forbidden)
            (await db.RmfRoleAssignments.AnyAsync(assignment => assignment.RegisteredSystemId == fixture.System))
                .Should().BeFalse("a rejected component cannot create a role assignment as a side effect");
    }

    [Theory]
    [InlineData(OrganizationRole.MissionOwner, true, false, false, false)]
    [InlineData(OrganizationRole.SystemOwner, true, false, false, false)]
    [InlineData(OrganizationRole.Isso, false, true, false, false)]
    [InlineData(OrganizationRole.Issm, true, true, true, false)]
    [InlineData(OrganizationRole.Assessor, false, false, false, false)]
    [InlineData(OrganizationRole.AuthorizingOfficial, false, false, false, true)]
    public async Task AccessContract_DistinguishesDomainOperations(
        OrganizationRole role, bool profile, bool author, bool reviewer, bool ao)
    {
        // Arrange
        var fixture = await SeedAsync(role);
        using var client = Client(fixture.Actor);

        // Act
        var response = await client.GetAsync($"/api/dashboard/systems/{fixture.System}/workspace-access");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        data.GetProperty("systemId").GetString().Should().Be(fixture.System);
        var permissions = data.GetProperty("permissions");
        permissions.GetProperty("canRead").GetBoolean().Should().BeTrue();
        permissions.GetProperty("canEditProfile").GetBoolean().Should().Be(profile);
        permissions.GetProperty("canAuthorNarratives").GetBoolean().Should().Be(author);
        permissions.GetProperty("canReviewNarratives").GetBoolean().Should().Be(reviewer);
        permissions.GetProperty("canDecideAuthorization").GetBoolean().Should().Be(ao);
    }

    [Fact]
    public async Task SystemOverride_RemovesOrganizationFallback_FromListsAndDirectArtifacts()
    {
        // Arrange
        var original = await SeedAsync();
        var replacement = await SeedAsync();
        var artifact = new ControlImplementation { RegisteredSystemId = original.System, TenantId = Tenant, ControlId = "AC-1", Narrative = "Private" };
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.OrganizationRoleAssignments.Add(new() { TenantId = Tenant, PersonId = original.Person, Role = OrganizationRole.MissionOwner });
            db.SystemRoleAssignments.Add(new() { TenantId = Tenant, RegisteredSystemId = original.System,
                PersonId = replacement.Person, Role = OrganizationRole.MissionOwner, IsInherited = false });
            db.ControlImplementations.Add(artifact);
            await db.SaveChangesAsync();
        }
        using var client = Client(original.Actor);

        // Act
        var system = await client.GetAsync($"/api/dashboard/systems/{original.System}/profile/MissionAndPurpose");
        var narrative = await client.GetAsync($"/api/systems/{original.System}/controls/AC-1/narrative");
        var portfolio = await client.GetAsync("/api/dashboard/portfolio");

        // Assert
        system.StatusCode.Should().Be(HttpStatusCode.NotFound);
        narrative.StatusCode.Should().Be(HttpStatusCode.NotFound);
        portfolio.StatusCode.Should().Be(HttpStatusCode.OK, await portfolio.Content.ReadAsStringAsync());
        (await portfolio.Content.ReadAsStringAsync()).Should().NotContain(original.System);
    }

    [Fact]
    public async Task CspCanEnrollInitialAdministrator_WithoutMakingMembershipAnImplicitRoleGrant()
    {
        // Arrange
        var fixture = await SeedAsync();
        using var csp = Client(Guid.NewGuid(), csp: true);

        // Act
        var response = await csp.PostAsJsonAsync($"/api/tenants/{Tenant}/administrator-assignments", new { personId = fixture.Person });
        using var member = Client(fixture.Actor);
        var access = await member.GetAsync($"/api/dashboard/systems/{fixture.System}/workspace-access");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        (await csp.GetAsync(response.Headers.Location)).StatusCode.Should().Be(HttpStatusCode.OK);
        access.StatusCode.Should().Be(HttpStatusCode.OK, await access.Content.ReadAsStringAsync());
        var permissions = (await access.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("permissions");
        permissions.GetProperty("canRead").GetBoolean().Should().BeTrue();
        permissions.GetProperty("canReviewNarratives").GetBoolean().Should().BeFalse();
        permissions.GetProperty("canDecideAuthorization").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task RootCreation_RequiresOrganizationIssm_AndBatchProjectionIsBounded()
    {
        // Arrange
        var fixture = await SeedAsync(OrganizationRole.Issm);
        using var client = Client(fixture.Actor);
        var before = (await client.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("data");
        before.GetProperty("permissions").GetProperty("canCreateSystem").GetBoolean().Should().BeFalse();
        var request = new { name = "Scoped creation", systemType = "MajorApplication", missionCriticality = "MissionSupport" };
        (await client.PostAsJsonAsync("/api/dashboard/systems", request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.OrganizationRoleAssignments.Add(new() { TenantId = Tenant, PersonId = fixture.Person, Role = OrganizationRole.Issm });
            await db.SaveChangesAsync();
        }

        // Act
        var me = (await client.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("data");
        var created = await client.PostAsJsonAsync("/api/dashboard/systems", request);
        var batch = await client.GetFromJsonAsync<JsonElement>($"/api/dashboard/workspace-access?systemIds={fixture.System}&systemIds=missing");
        var oversized = await client.GetAsync("/api/dashboard/workspace-access?" + string.Join("&", Enumerable.Range(0, 101).Select(i => $"systemIds={i}")));

        // Assert
        me.GetProperty("permissions").GetProperty("canCreateSystem").GetBoolean().Should().BeTrue();
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var items = batch.GetProperty("data").GetProperty("items").EnumerateArray().ToArray();
        items.Should().HaveCount(2);
        items.Single(i => i.GetProperty("systemId").GetString() == fixture.System).GetProperty("permissions").GetProperty("canManageSystem").GetBoolean().Should().BeTrue();
        items.Single(i => i.GetProperty("systemId").GetString() == "missing").GetProperty("permissions").GetProperty("canRead").GetBoolean().Should().BeFalse();
        oversized.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync("/api/dashboard/workspace-access")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(OrganizationRole.MissionOwner, false)]
    [InlineData(OrganizationRole.Isso, true)]
    [InlineData(OrganizationRole.Issm, true)]
    [InlineData(OrganizationRole.Assessor, false)]
    [InlineData(OrganizationRole.AuthorizingOfficial, false)]
    public async Task CanonicalNarrativeAndEvidenceWrites_EnforceScopedOperationRoles(OrganizationRole role, bool allowed)
    {
        // Arrange
        var fixture = await SeedAsync(role);
        var evidence = new EvidenceArtifact { TenantId = Tenant, RegisteredSystemId = fixture.System,
            FileName = "synthetic.txt", ContentType = "text/plain", StoragePath = "fixture", ContentHash = "fixture", UploadedBy = "fixture" };
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.ControlImplementations.Add(new() { TenantId = Tenant, RegisteredSystemId = fixture.System, ControlId = "AC-1",
                TechnicalNarrative = "Approved text", ApprovedVersionId = null, CurrentVersion = 1 });
            db.EvidenceArtifacts.Add(evidence);
            await db.SaveChangesAsync();
        }
        using var client = Client(fixture.Actor);

        // Act
        var narrative = await client.PatchAsJsonAsync($"/api/systems/{fixture.System}/controls/AC-1/narrative",
            new { technicalNarrative = "Scoped draft", expectedVersion = 1 });
        var classified = await client.PatchAsJsonAsync($"/api/evidence/{evidence.Id}/classify",
            new { narrativeType = EvidenceNarrativeType.Technical, rationale = "Synthetic classification" });

        // Assert
        narrative.StatusCode.Should().Be(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, await narrative.Content.ReadAsStringAsync());
        classified.StatusCode.Should().Be(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, await classified.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ReferenceAuthors_DoNotAutomaticallyBecomeNarrativeReviewers()
    {
        // Arrange
        var fixture = await SeedAsync(OrganizationRole.MissionOwner);
        using var client = Client(fixture.Actor);
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("Synthetic reference"), "title");
        form.Add(new StringContent("System"), "scope");
        form.Add(new StringContent(fixture.System), "scopeId");
        form.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("AC-1\nPolicy Narrative:\nAn unverified reference claim.")), "file", "reference.txt");

        // Act
        var access = await client.GetFromJsonAsync<JsonElement>($"/api/systems/{fixture.System}/narrative-library/access");
        var imported = await client.PostAsync($"/api/systems/{fixture.System}/narrative-library/imports", form);
        var systemAccess = await client.GetFromJsonAsync<JsonElement>($"/api/dashboard/systems/{fixture.System}/workspace-access");

        // Assert
        access.GetProperty("canAuthor").GetBoolean().Should().BeTrue();
        imported.StatusCode.Should().Be(HttpStatusCode.Created, await imported.Content.ReadAsStringAsync());
        var permissions = systemAccess.GetProperty("data").GetProperty("permissions");
        permissions.GetProperty("canAuthorNarratives").GetBoolean().Should().BeFalse();
        permissions.GetProperty("canReviewNarratives").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ArtifactOnlyRoute_CannotReadOrModifyAnOverriddenSystem()
    {
        // Arrange
        var fixture = await SeedAsync(OrganizationRole.MissionOwner);
        var foreign = await SeedAsync(OrganizationRole.Isso);
        var evidence = new EvidenceArtifact { TenantId = Tenant, RegisteredSystemId = foreign.System,
            FileName = "private.txt", ContentType = "text/plain", StoragePath = "fixture", ContentHash = "fixture", UploadedBy = "fixture" };
        var poam = new PoamItem { TenantId = Tenant, RegisteredSystemId = foreign.System,
            Weakness = "Private weakness", SecurityControlNumber = "AC-1", PointOfContact = "fixture", WeaknessSource = "Manual" };
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.EvidenceArtifacts.Add(evidence);
            db.PoamItems.Add(poam);
            await db.SaveChangesAsync();
        }
        using var client = Client(fixture.Actor);

        // Act
        var read = await client.GetAsync($"/api/dashboard/poam/{poam.Id}");
        var write = await client.PatchAsJsonAsync($"/api/evidence/{evidence.Id}/classify",
            new { narrativeType = EvidenceNarrativeType.Policy, rationale = "Attempted cross-system write" });

        // Assert
        read.StatusCode.Should().Be(HttpStatusCode.NotFound);
        write.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(OrganizationRole.Issm, true, false)]
    [InlineData(OrganizationRole.Isso, false, false)]
    [InlineData(OrganizationRole.MissionOwner, false, false)]
    [InlineData(OrganizationRole.AuthorizingOfficial, false, true)]
    public async Task ManagementAndAuthorization_AreIndependent(OrganizationRole role, bool management, bool authorization)
    {
        // Arrange
        var fixture = await SeedAsync(role);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.AuthorizationDecisions.Add(new() { TenantId = Tenant, RegisteredSystemId = fixture.System,
                DecisionType = AuthorizationDecisionType.Ato, IsActive = true, IssuedBy = "fixture", IssuedByName = "Fixture" });
            await db.SaveChangesAsync();
        }
        using var client = Client(fixture.Actor);

        // Act
        var updated = await client.PutAsJsonAsync($"/api/dashboard/systems/{fixture.System}", new { name = "Managed system" });
        var decision = await client.PostAsJsonAsync($"/api/dashboard/systems/{fixture.System}/authorization/override",
            new { overrideStatus = "ATO", justification = "Synthetic authority test", expirationDate = DateTime.UtcNow.AddDays(30) });

        // Assert
        updated.StatusCode.Should().Be(management ? HttpStatusCode.OK : HttpStatusCode.Forbidden, await updated.Content.ReadAsStringAsync());
        decision.StatusCode.Should().Be(authorization ? HttpStatusCode.Created : HttpStatusCode.Forbidden, await decision.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(OrganizationRole.Isso, true)]
    [InlineData(OrganizationRole.Issm, true)]
    [InlineData(OrganizationRole.AuthorizingOfficial, true)]
    [InlineData(OrganizationRole.MissionOwner, false)]
    [InlineData(OrganizationRole.Assessor, false)]
    public async Task PoamMutation_FollowsExistingFeature039Policy(OrganizationRole role, bool allowed)
    {
        // Arrange
        var fixture = await SeedAsync(role);
        using var client = Client(fixture.Actor);

        // Act
        var created = await client.PostAsJsonAsync($"/api/dashboard/systems/{fixture.System}/poam",
            new { weakness = "Synthetic remediation item", controlId = "AC-1", poc = "Fixture",
                catSeverity = "II", scheduledCompletionDate = DateTime.UtcNow.AddDays(30) });

        // Assert
        created.StatusCode.Should().Be(allowed ? HttpStatusCode.Created : HttpStatusCode.Forbidden, await created.Content.ReadAsStringAsync());
        if (allowed)
        {
            var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
            (await client.DeleteAsync($"/api/dashboard/poam/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        }
    }

    [Fact]
    public async Task EnrollmentAndRevocation_DoNotAcceptUnlinkedContactsOrOrdinaryMembers()
    {
        // Arrange
        var fixture = await SeedAsync(OrganizationRole.MissionOwner);
        using var member = Client(fixture.Actor);
        using var csp = Client(Guid.NewGuid(), csp: true);
        Guid unlinked;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var person = new Person { TenantId = Tenant, DisplayName = "Contact only", Email = $"{Guid.NewGuid():N}@example.invalid" };
            db.Persons.Add(person);
            await db.SaveChangesAsync();
            unlinked = person.Id;
        }

        // Act
        var unauthorized = await member.PostAsJsonAsync($"/api/tenants/{Tenant}/administrator-assignments", new { personId = fixture.Person });
        var contactOnly = await csp.PostAsJsonAsync($"/api/tenants/{Tenant}/administrator-assignments", new { personId = unlinked });
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            (await db.SystemRoleAssignments.SingleAsync(r => r.RegisteredSystemId == fixture.System)).RemovedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }
        var revoked = await member.GetAsync($"/api/dashboard/systems/{fixture.System}/workspace-access");

        // Assert
        unauthorized.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        contactOnly.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        revoked.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task LegacyOidAuthoredProposal_CannotBeSelfReviewedAfterPersonBinding()
    {
        // Arrange
        var fixture = await SeedAsync(OrganizationRole.Issm);
        var proposal = new NarrativeProposal { TenantId = Tenant, RegisteredSystemId = fixture.System,
            ControlId = "AC-1", NarrativeType = "Technical", CreatedBy = fixture.Actor.ToString(),
            BaseVersion = 1, Revision = 1, DeduplicationKey = Guid.NewGuid().ToString("N") };
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.ControlImplementations.Add(new() { TenantId = Tenant, RegisteredSystemId = fixture.System,
                ControlId = "AC-1", TechnicalNarrative = "Active content", CurrentVersion = 1 });
            db.NarrativeProposals.Add(proposal);
            await db.SaveChangesAsync();
        }
        using var client = Client(fixture.Actor);

        // Act
        var response = await client.PostAsJsonAsync($"/api/systems/{fixture.System}/narrative-library/proposals/{proposal.Id}/review",
            new { expectedRevision = 1, decision = "RequestRevision", note = "Attempted self review using migrated identity." });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        await using var check = _factory.Services.CreateAsyncScope();
        var context = check.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await context.NarrativeProposals.SingleAsync(p => p.Id == proposal.Id)).Status.Should().Be("Draft");
        (await context.ControlImplementations.SingleAsync(c => c.RegisteredSystemId == fixture.System)).TechnicalNarrative.Should().Be("Active content");
    }

    [Theory]
    [InlineData("unassigned", "omitted")]
    [InlineData("unassigned", "empty-kind")]
    [InlineData("unassigned", "empty-tenant")]
    [InlineData("unassigned", "empty-mode")]
    [InlineData("membership-revoked", "omitted")]
    [InlineData("membership-revoked", "empty-kind")]
    [InlineData("membership-revoked", "empty-tenant")]
    [InlineData("membership-revoked", "empty-mode")]
    [InlineData("member-without-role", "omitted")]
    [InlineData("member-without-role", "empty-kind")]
    [InlineData("member-without-role", "empty-tenant")]
    [InlineData("member-without-role", "empty-mode")]
    [InlineData("role-revoked", "omitted")]
    [InlineData("role-revoked", "empty-kind")]
    [InlineData("role-revoked", "empty-tenant")]
    [InlineData("role-revoked", "empty-mode")]
    public async Task MappedHomeTenant_CannotRestoreAccessThroughMissingSelectorsOrLegacyUrls(
        string assignmentState, string selectors)
    {
        // Arrange
        using var client = _factory.CreateClient();
        var configuration = _factory.Services.GetRequiredService<IConfiguration>();
        configuration["Deployment:Mode"].Should().Be("MultiTenant");
        configuration.GetValue<bool>("Tenant:Resolution:BypassForTests").Should().BeFalse();
        configuration.GetValue<bool>("Auth:BypassForTests").Should().BeFalse();
        var tenantId = Guid.NewGuid();
        var directoryId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var person = new Person { TenantId = tenantId, DisplayName = "Mapped-directory member",
            Email = $"{actorId:N}@example.invalid", EntraObjectId = actorId, IsLinkedToDirectory = true };
        var system = new RegisteredSystem { TenantId = tenantId, Name = "Mapped-home private system",
            HostingEnvironment = "Test", CreatedBy = "fixture", IsActive = true };
        var membership = new Ato.Copilot.Core.Models.Tenancy.OrganizationMembership
        {
            TenantId = tenantId, DirectoryTenantId = directoryId, ObjectId = actorId,
            PersonId = person.Id, GrantedBy = "explicit-fixture-grant"
        };
        var role = new SystemRoleAssignment { TenantId = tenantId, RegisteredSystemId = system.Id,
            PersonId = person.Id, Role = OrganizationRole.MissionOwner };
        var poam = new PoamItem { TenantId = tenantId, RegisteredSystemId = system.Id,
            Weakness = "Mapped-home private artifact", WeaknessSource = "Manual",
            SecurityControlNumber = "AC-1", PointOfContact = "Fixture" };
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.Tenants.Add(new() { Id = tenantId, EntraTenantId = directoryId, DisplayName = "Mapped home",
                PrimaryPocEmail = person.Email, OnboardingState = Ato.Copilot.Core.Models.Tenancy.OnboardingState.Active });
            db.Persons.Add(person);
            db.RegisteredSystems.Add(system);
            await db.SaveChangesAsync();
            if (assignmentState != "unassigned") db.OrganizationMemberships.Add(membership);
            if (assignmentState != "member-without-role") db.SystemRoleAssignments.Add(role);
            db.PoamItems.Add(poam);
            await db.SaveChangesAsync();
            (await db.Tenants.SingleAsync(t => t.Id == tenantId)).EntraTenantId.Should().Be(directoryId);
        }
        client.DefaultRequestHeaders.Add("X-Test-Tid", directoryId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Oid", actorId.ToString());
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString());

        if (assignmentState is "membership-revoked" or "role-revoked")
        {
            var previousMe = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
            previousMe.GetProperty("data").GetProperty("homeTenant").GetProperty("id").GetGuid().Should().Be(tenantId);
            (await client.GetAsync($"/api/dashboard/systems/{system.Id}/workspace-access")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await client.GetStringAsync("/api/dashboard/systems")).Should().Contain(system.Id);
            if (assignmentState == "membership-revoked")
            {
                using var administrator = Client(Guid.NewGuid(), csp: true);
                (await administrator.DeleteAsync($"/api/tenants/{tenantId}/memberships/{membership.Id}"))
                    .StatusCode.Should().Be(HttpStatusCode.NoContent);
            }
            else
            {
                await using var scope = _factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
                (await db.SystemRoleAssignments.SingleAsync(r => r.Id == role.Id)).RemovedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
            }
        }
        // Act
        if (selectors != "omitted")
        {
            // Preserve a present empty header, rather than an omitted header (tested separately).
            foreach (var path in new[] { "/api/auth/me", "/api/dashboard/systems",
                $"/api/dashboard/systems/{system.Id}", $"/api/dashboard/poam/{poam.Id}" })
            {
                var response = await _factory.Server.SendAsync(http =>
                {
                    http.Request.Method = "GET";
                    http.Request.Path = path;
                    http.Request.Headers["X-Test-Tid"] = directoryId.ToString();
                    http.Request.Headers["X-Test-Oid"] = actorId.ToString();
                    http.Request.Headers["X-Tenant-Id"] = tenantId.ToString();
                    http.Request.Headers["X-Workspace-Kind"] = selectors == "empty-kind" ? "" : "organization";
                    http.Request.Headers["X-Workspace-Tenant-Id"] = selectors == "empty-tenant" ? "" : tenantId.ToString();
                    http.Request.Headers["X-Workspace-Mode"] = selectors == "empty-mode" ? "" : "ordinary";
                    http.Request.Headers["X-Workspace-Mode"].Count.Should().Be(1);
                });

                // Assert
                response.Response.StatusCode.Should().Be(400, $"a present empty selector must not authorize {path}");
            }
            return;
        }

        using var me = await client.GetAsync("/api/auth/me");
        using var list = await client.GetAsync("/api/dashboard/systems");
        using var detail = await client.GetAsync($"/api/dashboard/systems/{system.Id}");
        using var artifact = await client.GetAsync($"/api/dashboard/poam/{poam.Id}");

        // Assert
        if (assignmentState is "unassigned" or "membership-revoked")
        {
            foreach (var response in new[] { me, list, detail, artifact })
            {
                response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
                (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("errorCode")
                    .GetString().Should().Be("NO_TENANT_ASSIGNMENT");
            }
        }
        else
        {
            me.StatusCode.Should().Be(HttpStatusCode.OK);
            list.StatusCode.Should().Be(HttpStatusCode.OK);
            var portfolio = await list.Content.ReadFromJsonAsync<JsonElement>();
            portfolio.GetProperty("items").GetArrayLength().Should().Be(0);
            portfolio.GetProperty("totalCount").GetInt32().Should().Be(0);
            detail.StatusCode.Should().Be(HttpStatusCode.NotFound);
            artifact.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }

    [Theory]
    [InlineData(OrganizationRole.MissionOwner, false, false, false, false, false)]
    [InlineData(OrganizationRole.SystemOwner, false, false, false, false, false)]
    [InlineData(OrganizationRole.Assessor, true, false, false, false, true)]
    [InlineData(OrganizationRole.Issm, true, true, true, true, false)]
    [InlineData(OrganizationRole.Isso, false, false, true, true, false)]
    [InlineData(OrganizationRole.AuthorizingOfficial, false, false, false, false, false)]
    public async Task AdditionalProjection_UsesNarrowPolicies(
        OrganizationRole role, bool assessmentDocuments, bool createTasks, bool moveTasks, bool assignRoles, bool validation)
    {
        // Arrange
        var fixture = await SeedAsync(role);
        using var client = Client(fixture.Actor);

        // Act
        var response = await client.GetAsync($"/api/dashboard/systems/{fixture.System}/workspace-access");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        var permissions = data.GetProperty("permissions");
        foreach (var key in new[] { "canGenerateSap", "canFinalizeSap", "canGenerateSar" })
            permissions.GetProperty(key).GetBoolean().Should().Be(assessmentDocuments);
        permissions.GetProperty("canCreateRemediationTasks").GetBoolean().Should().Be(createTasks);
        permissions.GetProperty("canMoveRemediationTasks").GetBoolean().Should().Be(moveTasks);
        permissions.GetProperty("canAssignSystemRoles").GetBoolean().Should().Be(assignRoles);
        permissions.GetProperty("canManageValidationLinks").GetBoolean().Should().Be(validation);
        var targets = data.GetProperty("assignableSystemRoles").EnumerateArray().Select(r => r.GetString()).ToArray();
        targets.Should().NotContain("AuthorizingOfficial", "only explicit organization Administrators may assign AO");
        if (role == OrganizationRole.Isso) targets.Should().BeEquivalentTo("MissionOwner", "SystemOwner");
    }
}
