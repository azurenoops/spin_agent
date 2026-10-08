using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

public sealed class ProviderAccessContractTests : IClassFixture<ProviderAccessFactory>
{
    private readonly ProviderAccessFactory _factory;
    private readonly HttpClient _client;

    public ProviderAccessContractTests(ProviderAccessFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        factory.ResetLegacyTenantContext(MultiTenantWebApplicationFactory<McpProgram>.TenantAId);
    }

    [Fact]
    public async Task InvitationContract_ReturnsNoDeliveryClaim_AndAcceptsMatchingIdentityOnce()
    {
        // Arrange
        var providerId = await ResetProviderAsync(OnboardingState.Active);
        var create = await _client.PostAsJsonAsync("/api/csp/invitations", new
        {
            providerId,
            targetEmail = "provider-user@example.invalid",
            targetDirectoryTenantId = "44444444-4444-4444-8444-444444444444",
            targetObjectId = "33333333-3333-4333-8333-333333333333",
            requestedRoles = new[] { "Assessor" },
            portfolioId = (Guid?)null,
            offeringId = (Guid?)null,
            expiresAt = DateTimeOffset.UtcNow.AddHours(1)
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await Data(create);
        var invitation = created.GetProperty("invitation");
        var id = invitation.GetProperty("invitationId").GetGuid();
        var token = created.GetProperty("token").GetString()!;

        // Act
        var read = await _client.GetAsync($"/api/csp/invitations/{id}");
        var accepted = await _client.PostAsJsonAsync(
            $"/api/csp/invitations/{id}/accept", new { token });
        var replay = await _client.PostAsJsonAsync(
            $"/api/csp/invitations/{id}/accept", new { token });
        var access = await _client.GetAsync("/api/auth/effective-access");

        // Assert
        created.GetProperty("deliveryState").GetString().Should().Be("NotDelivered");
        created.GetRawText().Should().NotContain("tokenHash");
        read.StatusCode.Should().Be(HttpStatusCode.OK);
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Data(accepted)).GetProperty("destination").GetString()
            .Should().Be("/workspaces/csp/authorizations");
        replay.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var effective = await Data(access);
        effective.GetProperty("version").GetString().Should().Be("2");
        effective.GetProperty("entryRoute").GetProperty("kind").GetString()
            .Should().Be("ProviderMember");
        effective.GetProperty("destinations").EnumerateArray().SelectMany(x =>
                x.GetProperty("badges").EnumerateArray())
            .Should().Contain(x => x.GetProperty("label").GetString() == "SCA");
    }

    [Fact]
    public async Task AccessRequestAndMembershipContracts_PreserveDecisionAndRevocationState()
    {
        // Arrange
        var providerId = await ResetProviderAsync(OnboardingState.InWizard);
        var request = await _client.PostAsJsonAsync("/api/csp/access-requests", new
        {
            justification = "Need provider setup responsibility",
            requestedProviderId = providerId
        });
        request.StatusCode.Should().Be(HttpStatusCode.Created,
            await request.Content.ReadAsStringAsync());
        var requestId = (await Data(request)).GetProperty("id").GetGuid();

        // Act
        var current = await _client.GetAsync("/api/csp/access-requests/current");
        var denied = await _client.PostAsJsonAsync(
            $"/api/csp/access-requests/{requestId}/decision",
            new { approved = false, reason = "Use explicit bootstrap grant", roles = Array.Empty<string>() });
        var grant = await _client.PostAsJsonAsync("/api/csp/memberships", new
        {
            providerId,
            directoryTenantId = "44444444-4444-4444-8444-444444444444",
            objectId = "33333333-3333-4333-8333-333333333333",
            displayName = "Synthetic provider user",
            email = "provider-user@example.invalid",
            roles = new[] { "PortalAdministrator" },
            portfolioId = (Guid?)null,
            offeringId = (Guid?)null
        });
        grant.StatusCode.Should().Be(HttpStatusCode.Created,
            await grant.Content.ReadAsStringAsync());
        var membershipId = (await Data(grant)).GetProperty("id").GetGuid();
        var effective = await Data(await _client.GetAsync("/api/auth/effective-access"));
        var revoked = await _client.DeleteAsync($"/api/csp/memberships/{membershipId}?reason=Test%20revocation");
        var blocked = await Data(await _client.GetAsync("/api/auth/effective-access"));

        // Assert
        current.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Data(current)).GetProperty("status").GetString().Should().Be("Pending");
        denied.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Data(denied)).GetProperty("status").GetString().Should().Be("Denied");
        effective.GetProperty("entryRoute").GetProperty("kind").GetString()
            .Should().Be("ProviderSetup");
        revoked.StatusCode.Should().Be(HttpStatusCode.NoContent);
        blocked.GetProperty("entryRoute").GetProperty("kind").GetString()
            .Should().Be("Blocked");
    }

    private async Task<Guid> ResetProviderAsync(OnboardingState state)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        db.ProviderRoleAssignments.RemoveRange(await db.ProviderRoleAssignments.ToListAsync());
        db.ProviderMemberships.RemoveRange(await db.ProviderMemberships.ToListAsync());
        db.ProviderDirectoryMatches.RemoveRange(await db.ProviderDirectoryMatches.ToListAsync());
        db.ProviderInvitations.RemoveRange(await db.ProviderInvitations.ToListAsync());
        db.ProviderAccessRequests.RemoveRange(await db.ProviderAccessRequests.ToListAsync());
        db.ProviderContacts.RemoveRange(await db.ProviderContacts.ToListAsync());
        db.ProviderPrincipals.RemoveRange(await db.ProviderPrincipals.ToListAsync());
        db.ProviderSetupDrafts.RemoveRange(await db.ProviderSetupDrafts.ToListAsync());
        var profiles = await db.CspProfiles.ToListAsync();
        db.CspProfiles.RemoveRange(profiles);
        await db.SaveChangesAsync();
        var provider = new CspProfile
        {
            DisplayName = "Synthetic provider",
            LegalEntityName = "Synthetic provider",
            OnboardingState = state,
            CreatedBy = "test"
        };
        db.Add(provider);
        await db.SaveChangesAsync();
        return provider.Id;
    }

    private static async Task<JsonElement> Data(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").Clone();
}

public sealed class ProviderAccessFactory : MultiTenantWebApplicationFactory<McpProgram>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
            services.PostConfigure<PlatformOperationsOptions>(options =>
                options.AuthorizedObjectIds.Add(
                    Guid.Parse("33333333-3333-4333-8333-333333333333"))));
    }
}
