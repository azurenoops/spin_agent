using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Models.Workspaces;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Services.ProviderAuthorizations;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authentication;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

[Collection("Tenancy")]
public sealed class SystemEnvironmentHttpTests(MultiTenantWebApplicationFactory<McpProgram> factory)
{
    [Theory]
    [InlineData(OrganizationRole.MissionOwner)]
    [InlineData(OrganizationRole.SystemOwner)]
    [InlineData(OrganizationRole.Issm)]
    public async Task AssignedEnvironmentManager_ReadsCanonicalChoicesWithoutReceivingRegistrationAdminRights(OrganizationRole role)
    {
        // Arrange
        var (systemId, registrationId) = await SeedAsync(role);
        using var client = factory.CreateClient();
        // Act
        var response = await client.GetAsync($"/api/dashboard/systems/{systemId}/environments/choices");
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var choices = await response.Content.ReadFromJsonAsync<EnvironmentChoicesResponse>();
        choices!.SystemId.Should().Be(systemId);
        choices.Permissions.CanManageEnvironments.Should().BeTrue();
        choices.Permissions.CanRegisterSubscriptions.Should().BeFalse();
        choices.RegistrationHref.Should().BeEmpty();
        choices.Choices.Should().Contain(x => x.Registration.RegistrationId == registrationId && x.Source == "OrganizationOwned");
    }

    [Fact]
    public async Task AssessorCannotDiscoverOrApply_EvenWhenAssessmentPermissionExists()
    {
        // Arrange
        var (systemId, registrationId) = await SeedAsync(OrganizationRole.Assessor);
        using var client = factory.CreateClient();
        var root = $"/api/dashboard/systems/{systemId}/environments";
        var selection = new EnvironmentSourceSelection("OrganizationOwned", registrationId, null, null);
        // Act
        var denied = await client.PostAsJsonAsync(root + "/discover", new DiscoverEnvironmentResourcesRequest(0, selection));
        using var apply = new HttpRequestMessage(HttpMethod.Post, root + "/apply");
        apply.Headers.Add("Idempotency-Key", "synthetic-replay-key");
        apply.Content = JsonContent.Create(new ApplySystemEnvironmentRequest(0, selection, Guid.NewGuid().ToString(), [], [], [], null));
        var deniedApply = await client.SendAsync(apply);
        // Assert
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden, await denied.Content.ReadAsStringAsync());
        deniedApply.StatusCode.Should().Be(HttpStatusCode.Forbidden, await deniedApply.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ForeignSystemIsNotFound_AndMutationRequiresStableReplayHeader()
    {
        // Arrange
        var (systemId, registrationId) = await SeedAsync(OrganizationRole.SystemOwner);
        using var client = factory.CreateClient();
        var root = $"/api/dashboard/systems/{systemId}/environments";
        // Act
        var missingKey = await client.PostAsJsonAsync(root + "/apply", new ApplySystemEnvironmentRequest(0,
            new("OrganizationOwned", registrationId, null, null), Guid.NewGuid().ToString(), [], [], [], null));
        factory.GetActiveContext().TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantBId;
        var foreign = await client.GetAsync(root);
        // Assert
        missingKey.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OrganizationAdministrator_RegistrationListUsesSpinOwnerNotAzureDirectoryClaim()
    {
        // Arrange
        var (systemId, registrationId) = await SeedAsync(OrganizationRole.SystemOwner);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.OrganizationRoleAssignments.Add(new() { TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId,
                PersonId = factory.GetActiveContext().PersonId!.Value, Role = OrganizationRole.Administrator });
            await db.SaveChangesAsync();
        }
        using var client = factory.CreateClient();
        // Act
        var choices = await client.GetFromJsonAsync<EnvironmentChoicesResponse>($"/api/dashboard/systems/{systemId}/environments/choices");
        var response = await client.GetAsync("/api/onboarding/azure/subscriptions/registrations");
        // Assert
        choices!.Permissions.CanRegisterSubscriptions.Should().BeTrue();
        choices.RegistrationHref.Should().Be("/settings/azure-subscriptions");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).Should().Contain(registrationId.ToString());
    }

    [Fact]
    public async Task ProviderRegistrationAuthority_UsesExistingAdminFlowAndOnlyProviderOwnedRegistry()
    {
        // Arrange
        factory.ResetLegacyTenantContext(Guid.Empty, isCspAdmin: true);
        factory.GetActiveContext().IsWorkspaceRequest = true;
        Guid registrationId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var registration = new AzureSubscriptionRegistration { TenantId = Guid.Empty, SubscriptionId = Guid.NewGuid(),
                ParentTenantId = Guid.NewGuid(), DisplayName = "Synthetic provider-owned registration" };
            db.AzureSubscriptionRegistrations.Add(registration);
            db.AzureSubscriptionRegistrations.Add(new() { TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId,
                SubscriptionId = Guid.NewGuid(), ParentTenantId = Guid.NewGuid(), DisplayName = "Other organization private registration" });
            await db.SaveChangesAsync();
            registrationId = registration.Id;
        }
        using var client = factory.CreateClient();
        // Act
        var response = await client.GetAsync("/api/onboarding/azure/subscriptions/registrations");
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).Should().Contain(registrationId.ToString());
        (await response.Content.ReadAsStringAsync()).Should().NotContain("Other organization private registration");
        factory.GetActiveContext().ImpersonatedTenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        var support = await client.GetAsync("/api/onboarding/azure/subscriptions/registrations");
        support.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task LegacyAssessmentConfiguration_SharedSourceConflictPointsToCanonicalEndpoint()
    {
        // Arrange
        var (systemId, _) = await SeedAsync(OrganizationRole.Issm);
        var service = new Mock<IAssessmentEnvironmentService>();
        var conflict = new AssessmentEnvironmentException("ASSESSMENT_SHARED_ENVIRONMENT_REQUIRED",
            "Manage this system through its shared environment.", "Open Connected environments.");
        service.Setup(x => x.GetConfigurationAsync(systemId, It.IsAny<CancellationToken>())).ThrowsAsync(conflict);
        service.Setup(x => x.ConfigureAsync(systemId, It.IsAny<UpdateAssessmentEnvironmentRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(conflict);
        service.Setup(x => x.DetachAsync(systemId, It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(conflict);
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAssessmentEnvironmentService>();
            services.AddScoped(_ => service.Object);
            services.AddTransient<IClaimsTransformation, AssessmentWriterClaims>();
        }));
        using var client = host.CreateClient();
        var root = $"/api/dashboard/systems/{systemId}/assessment-environment";
        // Act
        var responses = new[] {
            await client.GetAsync(root),
            await client.PutAsJsonAsync(root, new UpdateAssessmentEnvironmentRequest { CloudEnvironment = "AzureCloud", SubscriptionIds = [] }),
            await client.DeleteAsync(root)
        };
        // Assert
        foreach (var response in responses)
        {
            response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
            response.Headers.Location!.ToString().Should().Be($"/api/dashboard/systems/{systemId}/environments");
            (await response.Content.ReadAsStringAsync()).Should().Contain("ASSESSMENT_SHARED_ENVIRONMENT_REQUIRED");
        }

    }

    private sealed class AssessmentWriterClaims : IClaimsTransformation
    {
        public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            principal.AddIdentity(new ClaimsIdentity([new Claim(ClaimTypes.Role, Ato.Copilot.Core.Constants.ComplianceRoles.Analyst)]));
            return Task.FromResult(principal);
        }
    }

    [Fact]
    public async Task PendingScope_CanBeExplicitlyReviewedThroughExistingPreviewAndCommitRoutes()
    {
        // Arrange
        var (systemId, registrationId) = await SeedAsync(OrganizationRole.SystemOwner);
        string resourceId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var subscription = await db.AzureSubscriptionRegistrations.SingleAsync(x => x.Id == registrationId);
            resourceId = $"/subscriptions/{subscription.SubscriptionId}/resourcegroups/rg/providers/microsoft.compute/virtualmachines/current";
            var boundary = new AuthorizationBoundaryDefinition { TenantId = subscription.TenantId,
                RegisteredSystemId = systemId, Name = "Existing recorded boundary" };
            var component = new SystemComponent { TenantId = subscription.TenantId, RegisteredSystemId = systemId,
                Name = "Previously recorded component" };
            db.AuthorizationBoundaryDefinitions.Add(boundary);
            db.SystemComponents.Add(component);
            db.BoundaryComponentAssignments.Add(new() { TenantId = subscription.TenantId,
                AuthorizationBoundaryDefinitionId = boundary.Id, SystemComponentId = component.Id, CreatedBy = "previous-reviewer" });
            await db.SaveChangesAsync();
        }
        var azure = new Mock<ISystemEnvironmentAzureSource>();
        azure.Setup(x => x.DiscoverAsync(It.IsAny<EnvironmentRegistration>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new EnvironmentResource(resourceId, "current", "microsoft.compute/virtualmachines", "rg", null)]);
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ISystemEnvironmentAzureSource>();
            services.AddScoped(_ => azure.Object);
        }));
        using var client = host.CreateClient();
        var root = $"/api/dashboard/systems/{systemId}/environments";
        var selection = new EnvironmentSourceSelection("OrganizationOwned", registrationId, null, null);
        var discovered = await client.PostAsJsonAsync(root + "/discover", new DiscoverEnvironmentResourcesRequest(0, selection));
        discovered.EnsureSuccessStatusCode();
        var initial = (await discovered.Content.ReadFromJsonAsync<EnvironmentDiscoveryResponse>())!;
        var applied = await WriteAsync(root + "/apply", new ApplySystemEnvironmentRequest(0, selection,
            initial.DiscoveryToken, [resourceId], [], [], null), "initial-attachment");
        var attachment = applied.Attachments.Single();
        attachment.Scope.ReviewState.Should().Be("PendingReview");
        var rediscovered = await client.PostAsJsonAsync(root + "/discover", new DiscoverEnvironmentResourcesRequest(applied.Version, selection));
        rediscovered.EnsureSuccessStatusCode();
        var current = (await rediscovered.Content.ReadFromJsonAsync<EnvironmentDiscoveryResponse>())!;
        var review = new EnvironmentScopeChangeRequest(applied.Version, attachment.Version, current.DiscoveryToken,
            attachment.Scope.ResourceIds, attachment.Scope.Exclusions, attachment.Scope.SharedDependencyResourceIds,
            "Accept the exact environment scope; keep recorded boundary unchanged.") { ReviewPendingScope = true };
        // Act
        var previewResponse = await client.PostAsJsonAsync($"{root}/{attachment.AttachmentId}/scope-preview", review);
        previewResponse.EnsureSuccessStatusCode();
        var preview = (await previewResponse.Content.ReadFromJsonAsync<EnvironmentImpactPreview>())!;
        var accepted = await WriteAsync($"{root}/{attachment.AttachmentId}/scope-commit",
            new CommitEnvironmentChangeRequest(applied.Version, preview.PreviewId, review.Rationale, true), "accept-scope");
        // Assert
        accepted.Attachments.Single().Scope.ReviewState.Should().Be("Reviewed");
        accepted.Attachments.Single().Scope.ReviewedAt.Should().NotBeNull();
        accepted.Attachments.Single().Readiness.State.Should().Be("Blocked");
        await using var verification = host.Services.CreateAsyncScope();
        var resolver = verification.ServiceProvider.GetRequiredService<ISystemEnvironmentScopeResolver>();
        (await resolver.ResolveAsync(systemId, EnvironmentScopePurpose.Assessment)).Sources.Single().Eligible.Should().BeTrue();
        var dbVerify = verification.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await dbVerify.BoundaryComponentAssignments.SingleAsync(x => x.AuthorizationBoundaryDefinition.RegisteredSystemId == systemId))
            .CreatedBy.Should().Be("previous-reviewer");

        async Task<SystemEnvironmentsResponse> WriteAsync(string url, object body, string key)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
            request.Headers.Add("Idempotency-Key", key);
            using var response = await client.SendAsync(request);
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<SystemEnvironmentsResponse>())!;
        }
    }

    private async Task<(string SystemId, Guid RegistrationId)> SeedAsync(OrganizationRole role)
    {
        var tenant = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        var person = Guid.NewGuid();
        factory.ResetLegacyTenantContext(tenant);
        factory.GetActiveContext().PersonId = person;
        factory.GetActiveContext().IsWorkspaceRequest = true;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        db.Persons.Add(new() { Id = person, TenantId = tenant, DisplayName = "Synthetic environment role", Email = $"{person}@example.invalid" });
        db.OrganizationMemberships.Add(new() { TenantId = tenant, PersonId = person, DirectoryTenantId = Guid.NewGuid(), ObjectId = Guid.NewGuid(), GrantedBy = "test" });
        var system = new RegisteredSystem { TenantId = tenant, Name = "Synthetic environment HTTP", CreatedBy = "test" };
        db.RegisteredSystems.Add(system);
        db.SystemRoleAssignments.Add(new() { TenantId = tenant, PersonId = person, RegisteredSystemId = system.Id, Role = role });
        var registration = new AzureSubscriptionRegistration { TenantId = tenant, SubscriptionId = Guid.NewGuid(),
            ParentTenantId = Guid.NewGuid(), DisplayName = "Synthetic canonical subscription" };
        db.AzureSubscriptionRegistrations.Add(registration);
        await db.SaveChangesAsync();
        return (system.Id, registration.Id);
    }

    [Fact]
    public async Task IndependentProviderScopeAndSubscription_HttpLinksAreOptionalAndRemovalPreservesBoth()
    {
        // Arrange
        var (systemId, registrationId) = await SeedAsync(OrganizationRole.SystemOwner);
        Guid offeringId;
        Guid hostingId;
        string resource;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var provider = await db.CspProfiles.FirstAsync();
            var offering = new ProviderOffering { ProviderId = provider.Id, Name = "Published independent service", Lifecycle = "Active" };
            offering.OfferingId = offering.Id;
            var hosting = new ProviderHostingScopeRevision { ProviderId = provider.Id, OfferingId = offering.Id,
                SnapshotJson = ProviderAuthorizationStore.Json(new CreateProviderHostingScopeRequest(1, null, "Subscription-free service",
                    [new ProviderServiceScope("ManualService", "service", "Synthetic service", null)], [], [])) };
            hosting.SnapshotHash = ProviderAuthorizationStore.Hash(hosting.SnapshotJson);
            offering.CurrentHostingScopeRevisionId = hosting.Id;
            var component = new CspInheritedComponent { CspProfileId = provider.Id, Name = "Published component", Status = CspInheritedComponentStatus.Published };
            var capability = new CspInheritedCapability { CspInheritedComponentId = component.Id, Name = "Published capability", Status = CspInheritedCapabilityStatus.Mapped };
            var release = new ProviderCapabilityRelease { CapabilityId = capability.Id, Revision = 1,
                SnapshotHash = "synthetic-release-hash",
                SnapshotJson = """{"DutiesJson":"{\"AC-2\":\"Customer\",\"AU-2\":\"Shared\"}","Capability":{"Name":"Released independent capability","Description":"Source-stated service duties."}}""" };
            var material = new ProviderPublicationContextMaterial(offering.Id, 1, Guid.NewGuid(), "boundary-hash",
                hosting.Id, hosting.SnapshotHash, [], [], []);
            var json = ProviderAuthorizationStore.Json(material);
            var review = new ProviderAuthorizationImpactReview { ProviderId = provider.Id, OfferingId = offering.Id,
                Disposition = "AcceptForPublication", ReviewedBy = "provider", ReviewedAt = DateTimeOffset.UtcNow,
                ContextSnapshotHash = ProviderAuthorizationStore.Hash(json) };
            db.AddRange(offering, hosting, component, capability, release, review,
                new ProviderCatalogContextSnapshot { ProviderId = provider.Id, OfferingId = offering.Id, CapabilityId = capability.Id,
                    ReleaseId = release.Id, ImpactReviewId = review.Id, SnapshotJson = json, SnapshotHash = ProviderAuthorizationStore.Hash(json) });
            await db.SaveChangesAsync();
            offeringId = offering.Id; hostingId = hosting.Id;
            var subscription = await db.AzureSubscriptionRegistrations.SingleAsync(x => x.Id == registrationId);
            resource = $"/subscriptions/{subscription.SubscriptionId}/resourcegroups/rg/providers/microsoft.compute/virtualmachines/current";
        }
        var azure = new Mock<ISystemEnvironmentAzureSource>();
        azure.Setup(x => x.DiscoverAsync(It.IsAny<EnvironmentRegistration>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new EnvironmentResource(resource, "current", "vm", "rg", null)]);
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ISystemEnvironmentAzureSource>();
            services.AddScoped(_ => azure.Object);
        }));
        using var client = host.CreateClient();
        var root = $"/api/dashboard/systems/{systemId}/environments";
        // Act
        var choices = await client.GetFromJsonAsync<SystemProviderScopeChoicesResponse>(root + "/provider-scope-choices");
        choices!.Choices.Should().Contain(x => x.OfferingId == offeringId);
        var selectedChoice = choices.Choices.Single(x => x.OfferingId == offeringId);
        selectedChoice.PublishedDuties.State.Should().Be("Available");
        selectedChoice.PublishedDuties.Capabilities.Single().CustomerControlIds.Should().Equal("AC-2");
        selectedChoice.PublishedDuties.Capabilities.Single().SharedControlIds.Should().Equal("AU-2");
        selectedChoice.PublishedDuties.Capabilities.Single().CapabilityName.Should().Be("Released independent capability");
        var workspace = await Write(root + "/provider-scopes", new AddSystemProviderScopeRequest(choices.Version, offeringId, 1, hostingId), "provider-only");
        // Assert
        workspace.Attachments.Should().BeEmpty();
        workspace.LegacyReferences.Should().BeEmpty();
        workspace.ProviderScopes.Should().ContainSingle();
        workspace.ProviderScopes.Single().ResponsibilityReview.State.Should().Be("NotAdopted");
        workspace.ProviderScopes.Single().ResponsibilityReview.CanConfirm.Should().BeFalse();
        var assignment = workspace.ProviderScopes.Single();
        var selection = new EnvironmentSourceSelection("OrganizationOwned", registrationId, null, null);
        using var discovered = await client.PostAsJsonAsync(root + "/discover", new DiscoverEnvironmentResourcesRequest(workspace.Version, selection));
        discovered.EnsureSuccessStatusCode();
        var discovery = (await discovered.Content.ReadFromJsonAsync<EnvironmentDiscoveryResponse>())!;
        // Act
        workspace = await Write(root + "/apply-batch", new ApplySystemEnvironmentsRequest(workspace.Version,
            [new(workspace.Version, selection, discovery.DiscoveryToken, [resource], [], [], assignment.AssignmentId)]), "subscription-explicit-link");
        var attachment = workspace.Attachments.Single();
        // Assert
        attachment.HostingAssignmentId.Should().Be(assignment.AssignmentId);
        workspace.HostingLinks.Single().State.Should().Be("Linked");
        workspace.ProviderScopes.Should().ContainSingle();
        // Act
        using var previewResponse = await client.PostAsJsonAsync(root + "/hosting-links/preview",
            new PreviewEnvironmentHostingLinkRequest(workspace.Version, attachment.AttachmentId, attachment.Version,
                assignment.AssignmentId, assignment.AssignmentVersion, "Link", "Optional association"));
        previewResponse.EnsureSuccessStatusCode();
        var preview = (await previewResponse.Content.ReadFromJsonAsync<EnvironmentImpactPreview>())!;
        workspace = await Write(root + "/hosting-links/commit",
            new CommitEnvironmentChangeRequest(workspace.Version, preview.PreviewId, "Optional association", true), "optional-link");
        workspace.HostingLinks.Single().State.Should().Be("Linked");
        using var removalResponse = await client.PostAsJsonAsync($"{root}/provider-scopes/{assignment.AssignmentId}/remove-preview",
            new PreviewProviderScopeRemovalRequest(workspace.Version, assignment.AssignmentVersion, assignment.SelectionVersion, "Retire the relationship only"));
        removalResponse.EnsureSuccessStatusCode();
        preview = (await removalResponse.Content.ReadFromJsonAsync<EnvironmentImpactPreview>())!;
        workspace = await Write($"{root}/provider-scopes/{assignment.AssignmentId}/remove",
            new CommitEnvironmentChangeRequest(workspace.Version, preview.PreviewId, "Retire the relationship only", true), "remove-relationship");
        // Assert
        workspace.Attachments.Single().AttachmentState.Should().Be("Attached");
        workspace.ProviderScopes.Single().State.Should().Be("Removed");
        workspace.HostingLinks.Single().State.Should().Be("Unlinked");
        using var verifyScope = host.Services.CreateScope();
        (await verifyScope.ServiceProvider.GetRequiredService<ISystemEnvironmentScopeResolver>()
            .ResolveAsync(systemId, EnvironmentScopePurpose.Assessment)).Sources.Single().Eligible.Should().BeTrue();
        factory.GetActiveContext().TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantBId;
        (await client.GetAsync(root + "/provider-scope-choices")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        async Task<SystemEnvironmentsResponse> Write(string url, object body, string key)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
            request.Headers.Add("Idempotency-Key", key);
            using var response = await client.SendAsync(request);
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<SystemEnvironmentsResponse>())!;
        }
    }
}
