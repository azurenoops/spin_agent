using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Agents.Compliance.Services.Onboarding;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Models.Tenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

public sealed partial class OrganizationCreationFlowTests
{
    [Fact]
    public async Task MissingProvisioning_DiscriminatesMissingOperationFromMissingOrganization()
    {
        // Arrange
        using var client = Client();
        var tenant = new Tenant { DisplayName = $"No operation {Guid.NewGuid():N}" };
        await VerifyAsync(async db =>
        {
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
        });

        // Act
        var current = await client.GetAsync($"/api/csp/organizations/{tenant.Id}/provisioning/current");
        var keyed = await client.GetAsync($"/api/csp/organizations/{tenant.Id}/provisioning?idempotencyKey=missing-operation");
        var wrongTarget = await client.GetAsync($"/api/csp/organizations/{Guid.NewGuid()}/provisioning/current");

        // Assert
        foreach (var response in new[] { current, keyed })
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("code")
                .GetString().Should().Be("PROVISIONING_NOT_FOUND");
        }
        wrongTarget.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await wrongTarget.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("code")
            .GetString().Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Recovery_OrphanedOperationIsConflictNotMissingCreation()
    {
        // Arrange
        using var client = Client();
        var key = Guid.NewGuid().ToString("N");
        var created = await DataAsync(await CreateAsync(client, key, Body()));
        var tenantId = created.GetProperty("tenantId").GetGuid();
        await VerifyAsync(async db =>
        {
            db.Tenants.Remove(await db.Tenants.SingleAsync(x => x.Id == tenantId));
            await db.SaveChangesAsync();
        });

        // Act
        var recovered = await client.GetAsync($"/api/csp/organization-creations/{key}");

        // Assert
        recovered.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await VerifyAsync(async db =>
            (await db.OrganizationProvisioningOperations.AnyAsync(x => x.IdempotencyKey == key)).Should().BeTrue());
    }

    [Fact]
    public async Task AuthorizedSupportSession_CannotUseProviderCreationOrProvisioning()
    {
        // Arrange
        using var admin = Client();
        var key = Guid.NewGuid().ToString("N");
        var created = await DataAsync(await CreateAsync(admin, key, Body()));
        var tenantId = created.GetProperty("tenantId").GetGuid();
        var started = await admin.PostAsJsonAsync($"/api/tenants/{tenantId}/impersonate",
            new { reason = "Verify support boundary for organization setup", acknowledged = true });
        started.StatusCode.Should().Be(HttpStatusCode.OK);
        var cookie = started.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("ato-impersonate=")).Split(';')[0];
        using var support = Client();
        support.DefaultRequestHeaders.Remove("X-Workspace-Kind");
        support.DefaultRequestHeaders.Add("X-Workspace-Kind", "organization");
        support.DefaultRequestHeaders.Add("X-Workspace-Tenant-Id", tenantId.ToString());
        support.DefaultRequestHeaders.Remove("X-Workspace-Mode");
        support.DefaultRequestHeaders.Add("X-Workspace-Mode", "support");
        support.DefaultRequestHeaders.Add("Cookie", cookie);
        (await support.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Act
        var denied = new[]
        {
            await CreateAsync(support, Guid.NewGuid().ToString("N"), Body()),
            await support.GetAsync($"/api/csp/organization-creations/{key}"),
            await support.GetAsync($"/api/csp/organizations/{tenantId}/provisioning/current"),
            await support.PatchAsJsonAsync($"/api/csp/organizations/{tenantId}/provisioning/{created.GetProperty("operationId").GetGuid()}", Identity())
        };

        // Assert
        foreach (var response in denied)
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        (await admin.DeleteAsync("/api/tenants/impersonation")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task LegacyCreationHash_ReplaysAndStoredCompletedFlagsDoNotInventEnrollment()
    {
        // Arrange
        using var client = Client();
        var tenant = new Tenant { DisplayName = $"Legacy {Guid.NewGuid():N}", OnboardingState = OnboardingState.Active };
        var key = Guid.NewGuid().ToString("N");
        var legacyJson = JsonSerializer.Serialize(new
        {
            tenant.DisplayName, LegalEntityName = (string?)null, PrimaryPocName = (string?)null, PrimaryPocEmail = (string?)null
        });
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(legacyJson))).ToLowerInvariant();
        await VerifyAsync(async db =>
        {
            db.Tenants.Add(tenant);
            db.OrganizationProvisioningOperations.Add(new Ato.Copilot.Core.Models.Workspaces.OrganizationProvisioningOperation
            {
                TenantId = tenant.Id, IdempotencyKey = key, CreationIntentHash = hash,
                AdministratorState = "Completed", MembershipState = "Completed"
            });
            await db.SaveChangesAsync();
        });

        // Act
        var replay = await CreateAsync(client, key, new { displayName = tenant.DisplayName });
        var current = await DataAsync(await client.GetAsync($"/api/csp/organizations/{tenant.Id}/provisioning/current"));
        var detail = await DataAsync(await client.GetAsync($"/api/csp/organizations/{tenant.Id}"));

        // Assert
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        (await DataAsync(replay)).GetProperty("tenantId").GetGuid().Should().Be(tenant.Id);
        current.GetProperty("administratorState").GetString().Should().Be("Pending");
        current.GetProperty("membershipState").GetString().Should().Be("Pending");
        current.GetProperty("overallState").GetString().Should().Be("InProgress");
        detail.GetProperty("setupState").GetString().Should().Be("Pending");
    }

    [Fact]
    public async Task EnrollmentFailure_ResumesSavedPersonAndMembershipWithoutRepeatingEither()
    {
        // Arrange
        var attempts = 0;
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddScoped<IOrganizationRoleAssignmentService>(sp =>
                new FailOnceRoles(ActivatorUtilities.CreateInstance<OrganizationRoleAssignmentService>(sp),
                    () => Interlocked.Increment(ref attempts) == 1))));
        using var client = Client(host.CreateClient());
        var identity = Identity();
        var creation = await DataAsync(await CreateAsync(client, Guid.NewGuid().ToString("N"), Body(identity)));
        var tenantId = creation.GetProperty("tenantId").GetGuid();
        var operationId = creation.GetProperty("operationId").GetGuid();
        var route = $"/api/csp/organizations/{tenantId}/provisioning/{operationId}";

        // Act
        var failed = await client.PatchAsJsonAsync(route, identity);
        var partial = await DataAsync(await client.GetAsync($"/api/csp/organizations/{tenantId}/provisioning/current"));
        var persisted = partial.GetProperty("initialAdministrator");
        persisted.GetProperty("personId").ValueKind.Should().Be(JsonValueKind.Null);
        var retry = await client.PatchAsJsonAsync(route, persisted);

        // Assert
        failed.StatusCode.Should().Be(HttpStatusCode.Conflict);
        partial.GetProperty("personState").GetString().Should().Be("Completed");
        partial.GetProperty("membershipState").GetString().Should().Be("Completed");
        partial.GetProperty("administratorState").GetString().Should().Be("Pending");
        partial.GetProperty("overallState").GetString().Should().Be("Failed");
        partial.GetProperty("canEditAdministrator").GetBoolean().Should().BeFalse();
        var final = await DataAsync(retry);
        final.GetProperty("overallState").GetString().Should().Be("Completed");
        await VerifyAsync(async db =>
        {
            (await db.Persons.IgnoreQueryFilters().CountAsync(x => x.TenantId == tenantId)).Should().Be(1);
            (await db.OrganizationMemberships.IgnoreQueryFilters().CountAsync(x => x.TenantId == tenantId)).Should().Be(1);
            (await db.OrganizationRoleAssignments.IgnoreQueryFilters().CountAsync(x => x.TenantId == tenantId)).Should().Be(1);
        });
        await AssertNoSystemWritesAsync(tenantId);
    }

    [Fact]
    public async Task BeginningAgainWithDifferentKey_DoesNotResetCompletedSetup()
    {
        // Arrange
        using var client = Client();
        var identity = Identity();
        var created = await DataAsync(await CreateAsync(client, Guid.NewGuid().ToString("N"), Body(identity)));
        var tenantId = created.GetProperty("tenantId").GetGuid();
        var operationId = created.GetProperty("operationId").GetGuid();
        (await client.PatchAsJsonAsync($"/api/csp/organizations/{tenantId}/provisioning/{operationId}", identity))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        using var begin = new HttpRequestMessage(HttpMethod.Post, $"/api/csp/organizations/{tenantId}/provisioning");
        begin.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        // Act
        var result = await DataAsync(await client.SendAsync(begin));
        var listed = await DataAsync(await client.GetAsync($"/api/csp/organizations?search={created.GetProperty("displayName").GetString()}"));

        // Assert
        result.GetProperty("operationId").GetGuid().Should().Be(operationId);
        result.GetProperty("overallState").GetString().Should().Be("Completed");
        var item = listed.GetProperty("items").EnumerateArray().Single();
        item.GetProperty("setupState").GetString().Should().Be("Completed");
        item.GetProperty("memberCount").GetInt32().Should().Be(1);
        await VerifyAsync(async db =>
            (await db.OrganizationProvisioningOperations.CountAsync(x => x.TenantId == tenantId)).Should().Be(1));
    }

    [Fact]
    public async Task CrossTenantPersonAndIdentityConflicts_DoNotFreezeUnboundIntent()
    {
        // Arrange
        using var client = Client();
        var created = await DataAsync(await CreateAsync(client, Guid.NewGuid().ToString("N"), Body()));
        var tenantId = created.GetProperty("tenantId").GetGuid();
        var operationId = created.GetProperty("operationId").GetGuid();
        var foreign = Guid.NewGuid();
        var local = Guid.NewGuid();
        var directoryId = Guid.NewGuid();
        var objectId = Guid.NewGuid();
        await VerifyAsync(async db =>
        {
            db.Persons.AddRange(new Person { Id = foreign, TenantId = WorkspaceMembershipFactory.TenantAId, DisplayName = "Foreign", Email = "foreign@example.invalid" },
                new Person { Id = local, TenantId = tenantId, DisplayName = "Local", Email = "local@example.invalid" });
            db.OrganizationMemberships.Add(new OrganizationMembership
            {
                TenantId = tenantId, PersonId = local, DirectoryTenantId = directoryId,
                ObjectId = objectId, GrantedBy = "test"
            });
            await db.SaveChangesAsync();
        });
        var route = $"/api/csp/organizations/{tenantId}/provisioning/{operationId}";

        // Act
        var foreignResult = await client.PatchAsJsonAsync(route, new { directoryTenantId = directoryId, objectId, personId = foreign });
        var conflict = await client.PatchAsJsonAsync(route, new { directoryTenantId = directoryId, objectId = Guid.NewGuid(), personId = local });
        var state = await DataAsync(await client.GetAsync($"/api/csp/organizations/{tenantId}/provisioning/current"));
        var valid = await client.PatchAsJsonAsync(route, new { directoryTenantId = directoryId, objectId, personId = local });

        // Assert
        foreignResult.StatusCode.Should().Be(HttpStatusCode.NotFound);
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        state.GetProperty("canEditAdministrator").GetBoolean().Should().BeTrue();
        valid.StatusCode.Should().Be(HttpStatusCode.OK, await valid.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("ordinary-organization")]
    [InlineData("not-csp")]
    public async Task NonProviderWorkspace_CannotCreateReadBeginOrResume(string scope)
    {
        // Arrange
        using var admin = Client();
        var created = await DataAsync(await CreateAsync(admin, Guid.NewGuid().ToString("N"), Body()));
        var tenantId = created.GetProperty("tenantId").GetGuid();
        using var denied = Client();
        if (scope == "not-csp") denied.DefaultRequestHeaders.Remove("X-Test-Roles");
        else
        {
            await VerifyAsync(async db =>
            {
                var person = new Person { TenantId = tenantId, DisplayName = "Member", Email = "member@example.invalid" };
                db.Persons.Add(person);
                db.OrganizationMemberships.Add(new OrganizationMembership
                {
                    TenantId = tenantId, PersonId = person.Id, DirectoryTenantId = DirectoryId,
                    ObjectId = ActorId, GrantedBy = "test"
                });
                await db.SaveChangesAsync();
            });
            denied.DefaultRequestHeaders.Remove("X-Workspace-Kind");
            denied.DefaultRequestHeaders.Add("X-Workspace-Kind", "organization");
            denied.DefaultRequestHeaders.Add("X-Workspace-Tenant-Id", tenantId.ToString());
        }

        // Act
        var create = await CreateAsync(denied, Guid.NewGuid().ToString("N"), Body());
        var recovery = await denied.GetAsync($"/api/csp/organization-creations/{Guid.NewGuid():N}");
        var current = await denied.GetAsync($"/api/csp/organizations/{tenantId}/provisioning/current");
        var resume = await denied.PatchAsJsonAsync($"/api/csp/organizations/{tenantId}/provisioning/{created.GetProperty("operationId").GetGuid()}", Identity());

        // Assert
        foreach (var response in new[] { create, recovery, current, resume })
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task InactiveTarget_DeniesRecoveryBeginReadAndResume()
    {
        // Arrange
        using var client = Client();
        var key = Guid.NewGuid().ToString("N");
        var created = await DataAsync(await CreateAsync(client, key, Body()));
        var tenantId = created.GetProperty("tenantId").GetGuid();
        await VerifyAsync(async db =>
        {
            var tenant = await db.Tenants.SingleAsync(x => x.Id == tenantId);
            tenant.Status = TenantStatus.Suspended;
            await db.SaveChangesAsync();
        });
        using var begin = new HttpRequestMessage(HttpMethod.Post, $"/api/csp/organizations/{tenantId}/provisioning");
        begin.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        // Act
        var recovery = await client.GetAsync($"/api/csp/organization-creations/{key}");
        var current = await client.GetAsync($"/api/csp/organizations/{tenantId}/provisioning/current");
        var started = await client.SendAsync(begin);
        var resumed = await client.PatchAsJsonAsync($"/api/csp/organizations/{tenantId}/provisioning/{created.GetProperty("operationId").GetGuid()}", Identity());

        // Assert
        foreach (var response in new[] { recovery, current, started, resumed })
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        await VerifyAsync(async db =>
            (await db.Persons.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenantId)).Should().BeFalse());
    }

    private sealed class FailOnceRoles(IOrganizationRoleAssignmentService inner, Func<bool> fail)
        : IOrganizationRoleAssignmentService
    {
        public Task<RoleAssignmentResult> StageAdministratorAsync(AtoCopilotContext db, Guid tenantId,
            Guid personId, Guid actorUserId, Guid correlationId, CancellationToken ct = default)
            => fail() ? Task.FromException<RoleAssignmentResult>(new InvalidOperationException("Injected Administrator-stage failure."))
                : inner.StageAdministratorAsync(db, tenantId, personId, actorUserId, correlationId, ct);
        public Task<IReadOnlyList<OrganizationRoleAssignment>> ListAsync(Guid tenantId, CancellationToken ct = default)
            => inner.ListAsync(tenantId, ct);
        public Task<RoleAssignmentResult> AddAsync(Guid tenantId, OrganizationRole role, Guid personId, Guid actorUserId,
            Guid correlationId, CancellationToken ct = default) => inner.AddAsync(tenantId, role, personId, actorUserId, correlationId, ct);
        public Task RemoveAsync(Guid tenantId, Guid assignmentId, Guid actorUserId, Guid correlationId, CancellationToken ct = default)
            => inner.RemoveAsync(tenantId, assignmentId, actorUserId, correlationId, ct);
    }
}
