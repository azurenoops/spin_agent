using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.TestHost;
using Ato.Copilot.Core.Interfaces.Compliance;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

[Collection("Tenancy")]
public sealed class WorkspaceOperationsAuthorizationTests(
    MultiTenantWebApplicationFactory<McpProgram> factory)
{
    [Theory]
    [InlineData("component")]
    [InlineData("capability")]
    public async Task OrganizationComponentNavigation_UsesExplicitTypeAndPagedEligibleChildren(string check)
    {
        // Arrange
        var tenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        var personId = Guid.NewGuid();
        var systemId = Guid.NewGuid().ToString();
        var componentId = Guid.NewGuid();
        var unrelatedComponentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var context = factory.GetActiveContext();
        context.TenantId = tenantId;
        context.PersonId = personId;
        context.IsCspAdmin = false;
        context.ImpersonatedTenantId = null;
        context.IsWorkspaceRequest = true;
        context.Status = TenantStatus.Active;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.Persons.Add(new Person
            {
                Id = personId, TenantId = tenantId, DisplayName = "Reader",
                Email = $"{personId:N}@example.invalid"
            });
            db.RegisteredSystems.Add(new RegisteredSystem
            {
                TenantId = tenantId, Id = systemId, Name = "Read system", CreatedBy = "test"
            });
            var profileId = await db.Set<CspProfile>().Select(x => x.Id).FirstAsync();
            db.CspInheritedComponents.Add(new CspInheritedComponent
            {
                Id = componentId, CspProfileId = profileId, Name = "Component navigation",
                Description = "Platform", Status = CspInheritedComponentStatus.Published
            });
            db.CspInheritedComponents.Add(new CspInheritedComponent
            {
                Id = unrelatedComponentId, CspProfileId = profileId, Name = "Unrelated component",
                Description = "Not a child source", Status = CspInheritedComponentStatus.Published
            });
            db.CspInheritedCapabilities.AddRange(
                new CspInheritedCapability
                {
                    Id = Guid.NewGuid(), CspInheritedComponentId = unrelatedComponentId, Name = "0 unrelated",
                    Description = "Not a child", Status = CspInheritedCapabilityStatus.Mapped
                },
                new CspInheritedCapability
                {
                    Id = childId, CspInheritedComponentId = componentId, Name = "A mapped",
                    Description = "Child", Status = CspInheritedCapabilityStatus.Mapped
                },
                new CspInheritedCapability
                {
                    Id = Guid.NewGuid(), CspInheritedComponentId = componentId, Name = "B mapped",
                    Description = "Child", Status = CspInheritedCapabilityStatus.Mapped
                },
                new CspInheritedCapability
                {
                    Id = Guid.NewGuid(), CspInheritedComponentId = componentId, Name = "Hidden",
                    Description = "Child", Status = CspInheritedCapabilityStatus.NeedsReview
                });
            await db.SaveChangesAsync();
            db.CapabilitySubscriptions.Add(new CapabilitySubscription
            {
                Id = Guid.NewGuid().ToString(), RegisteredSystemId = systemId, RoutingTenantId = tenantId,
                CspInheritedCapabilityId = childId.ToString("D"), IsActive = true
            });
            db.OrganizationMemberships.Add(new OrganizationMembership
            {
                TenantId = tenantId, PersonId = personId, DirectoryTenantId = Guid.NewGuid(),
                ObjectId = Guid.NewGuid(), GrantedBy = "test"
            });
            await db.SaveChangesAsync();
        }
        await SetSystemRoleAsync(personId, systemId, OrganizationRole.Isso);
        using var client = factory.CreateClient();
        var root = $"/api/workspaces/organizations/{tenantId}/capabilities";

        // Act
        var detail = await client.GetAsync($"{root}/provider/{componentId.ToString().ToUpperInvariant()}?recordType=component");
        var children = await client.GetAsync($"{root}?grouping=capability&source=provider&componentId={componentId}&pageSize=1");
        var wrongType = await client.GetAsync($"{root}/provider/{childId}?recordType=component");
        var missing = await client.GetAsync($"{root}/provider/{Guid.NewGuid()}?recordType=component");
        var crossTenant = await client.GetAsync(
            $"/api/workspaces/organizations/{MultiTenantWebApplicationFactory<McpProgram>.TenantBId}/capabilities/provider/{componentId}?recordType=component");
        var invalidType = await client.GetAsync($"{root}/provider/{componentId}?recordType=invalid");

        // Assert
        if (check == "component")
        {
            detail.StatusCode.Should().Be(HttpStatusCode.OK);
            var data = (await detail.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
            data.GetProperty("capability").GetProperty("recordType").GetString().Should().Be("component");
            data.GetProperty("capability").GetProperty("systemCount").GetInt32().Should().Be(0);
            wrongType.StatusCode.Should().Be(HttpStatusCode.NotFound);
            missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
            crossTenant.StatusCode.Should().Be(HttpStatusCode.NotFound);
            invalidType.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        else
        {
            children.StatusCode.Should().Be(HttpStatusCode.OK);
            var data = (await children.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
            data.GetProperty("total").GetInt32().Should().Be(2);
            data.GetProperty("items").GetArrayLength().Should().Be(1);
            data.GetProperty("items")[0].GetProperty("recordId").GetGuid().Should().Be(childId);
            var second = await client.GetFromJsonAsync<JsonElement>(
                $"{root}?grouping=capability&source=provider&componentId={componentId}&pageSize=1&page=2");
            second.GetProperty("data").GetProperty("items")[0].GetProperty("name").GetString().Should().Be("B mapped");
            var scoped = await client.GetFromJsonAsync<JsonElement>(
                $"{root}?grouping=capability&source=provider&componentId={componentId}&systemId={systemId}");
            scoped.GetProperty("data").GetProperty("total").GetInt32().Should().Be(1);
            scoped.GetProperty("data").GetProperty("items")[0].GetProperty("recordId").GetGuid().Should().Be(childId);
            scoped.GetProperty("data").GetProperty("items")[0].GetProperty("isSubscribed").GetBoolean().Should().BeTrue();
            scoped.GetProperty("data").GetProperty("items")[0].GetProperty("systemCount").GetInt32().Should().Be(1);
            var components = await client.GetFromJsonAsync<JsonElement>(
                $"{root}?grouping=component&source=provider&systemId={systemId}");
            components.GetProperty("data").GetProperty("total").GetInt32().Should().Be(1);
            components.GetProperty("data").GetProperty("items")[0].GetProperty("recordId").GetGuid().Should().Be(componentId);
            components.GetProperty("data").GetProperty("items")[0].GetProperty("systemCount").GetInt32().Should().Be(1);
        }
    }

    [Fact]
    public async Task T080_WorkingRevisionAndPreview_RequireProviderAuthorization()
    {
        // Arrange
        var context = factory.GetActiveContext();
        context.TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        context.IsCspAdmin = false;
        context.ImpersonatedTenantId = null;
        context.IsWorkspaceRequest = true;
        context.Status = TenantStatus.Active;
        using var client = factory.CreateClient();
        var capabilityId = Guid.NewGuid();

        // Act
        var working = await client.GetAsync(
            $"/api/csp/catalog/capabilities/{capabilityId}/working-revision");
        var preview = await client.PostAsJsonAsync(
            $"/api/csp/catalog/capabilities/{capabilityId}/publication-previews",
            new { revision = 1 });

        // Assert
        working.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        preview.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task T082_OrganizationCreateAndProvisioningStatus_ReplayByIdempotencyKey()
    {
        // Arrange
        var context = factory.GetActiveContext();
        context.TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        context.IsCspAdmin = true;
        context.ImpersonatedTenantId = null;
        context.IsWorkspaceRequest = true;
        context.Status = TenantStatus.Active;
        using var client = factory.CreateClient();
        var key = $"create-{Guid.NewGuid():N}";
        var body = new
        {
            displayName = $"Mission {Guid.NewGuid():N}",
            legalEntityName = "Mission LLC",
            primaryPocName = "Owner",
            primaryPocEmail = "owner@example.mil"
        };
        using var firstRequest = new HttpRequestMessage(HttpMethod.Post, "/api/csp/dashboard/tenants")
        {
            Content = JsonContent.Create(body)
        };
        firstRequest.Headers.Add("Idempotency-Key", key);

        // Act
        var first = await client.SendAsync(firstRequest);
        var firstJson = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        var tenantId = firstJson.RootElement.GetProperty("data").GetProperty("tenantId").GetGuid();
        var operationId = firstJson.RootElement.GetProperty("data").GetProperty("operationId").GetGuid();
        using var retryRequest = new HttpRequestMessage(HttpMethod.Post, "/api/csp/dashboard/tenants")
        {
            Content = JsonContent.Create(body)
        };
        retryRequest.Headers.Add("Idempotency-Key", key);
        var retry = await client.SendAsync(retryRequest);
        using var conflictRequest = new HttpRequestMessage(HttpMethod.Post, "/api/csp/dashboard/tenants")
        {
            Content = JsonContent.Create(new
            {
                displayName = $"{body.displayName} changed",
                body.legalEntityName,
                body.primaryPocName,
                body.primaryPocEmail
            })
        };
        conflictRequest.Headers.Add("Idempotency-Key", key);
        var conflict = await client.SendAsync(conflictRequest);
        var status = await client.GetAsync(
            $"/api/csp/organizations/{tenantId}/provisioning?idempotencyKey={Uri.EscapeDataString(key)}");

        // Assert
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        retry.StatusCode.Should().Be(HttpStatusCode.OK);
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        status.StatusCode.Should().Be(HttpStatusCode.OK);
        var statusJson = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
        statusJson.RootElement.GetProperty("data").GetProperty("operationId").GetGuid()
            .Should().Be(operationId);
    }

    [Fact]
    public async Task T010_ProviderCatalog_FilteredQueryExecutesAgainstSqlite()
    {
        // Arrange
        var context = factory.GetActiveContext();
        context.TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        context.IsCspAdmin = true;
        context.ImpersonatedTenantId = null;
        context.IsWorkspaceRequest = true;
        context.Status = TenantStatus.Active;
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync(
            "/api/csp/catalog?grouping=capability&lifecycle=Published&review=NeedsReview");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task T014_ProviderCatalog_DeniesOrdinaryOrganizationMember()
    {
        // Arrange
        var context = factory.GetActiveContext();
        context.TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        context.IsCspAdmin = false;
        context.ImpersonatedTenantId = null;
        context.IsWorkspaceRequest = true;
        context.Status = TenantStatus.Active;
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/csp/catalog");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ProviderSubscriberSummaries_RequireCspAdministrator()
    {
        // Arrange
        var context = factory.GetActiveContext();
        context.TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        context.IsCspAdmin = false;
        context.ImpersonatedTenantId = null;
        context.IsWorkspaceRequest = true;
        context.Status = TenantStatus.Active;
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync(
            $"/api/csp/catalog/capabilities/{Guid.NewGuid()}/subscribers");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task T015_OrganizationCapability_DoesNotRevealAnotherTenant()
    {
        // Arrange
        var context = factory.GetActiveContext();
        context.TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        context.IsCspAdmin = false;
        context.ImpersonatedTenantId = null;
        context.IsWorkspaceRequest = true;
        context.Status = TenantStatus.Active;
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync(
            $"/api/workspaces/organizations/{MultiTenantWebApplicationFactory<McpProgram>.TenantBId}/capabilities");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task T014_OrganizationReviewFilterExecutesAgainstSqlite()
    {
        // Arrange
        var context = factory.GetActiveContext();
        context.TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        context.IsCspAdmin = true;
        context.ImpersonatedTenantId = null;
        context.IsWorkspaceRequest = true;
        context.Status = TenantStatus.Active;
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/csp/organizations?review=Pending");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task T018_ProviderMustEnterSupportBeforeReadingOrganizationLibrary()
    {
        // Arrange
        var context = factory.GetActiveContext();
        context.TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        context.IsCspAdmin = true;
        context.ImpersonatedTenantId = null;
        context.IsWorkspaceRequest = true;
        context.Status = TenantStatus.Active;
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync(
            $"/api/workspaces/organizations/{MultiTenantWebApplicationFactory<McpProgram>.TenantBId}/capabilities");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OrganizationLibraryAndSetup_RequirePersistedSystemPermissions()
    {
        // Arrange
        var personId = Guid.NewGuid();
        var systemId = Guid.NewGuid().ToString();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.Persons.Add(new Person
            {
                Id = personId, TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId,
                DisplayName = "Workspace member", Email = $"{personId:N}@example.invalid"
            });
            db.OrganizationMemberships.Add(new OrganizationMembership
            {
                TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId,
                PersonId = personId, DirectoryTenantId = Guid.NewGuid(), ObjectId = Guid.NewGuid(),
                GrantedBy = "test"
            });
            db.RegisteredSystems.Add(new RegisteredSystem
            {
                TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId,
                Id = systemId, Name = "Protected system", HostingEnvironment = "Azure", CreatedBy = "test"
            });
            await db.SaveChangesAsync();
        }
        var context = factory.GetActiveContext();
        context.TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        context.PersonId = personId;
        context.IsCspAdmin = false;
        context.ImpersonatedTenantId = null;
        context.IsWorkspaceRequest = true;
        context.Status = TenantStatus.Active;
        using var client = factory.CreateClient();

        // Act
        var membershipOnly = await client.GetAsync(
            $"/api/workspaces/organizations/{context.TenantId}/capabilities?systemId={systemId}");
        await SetSystemRoleAsync(personId, systemId, OrganizationRole.Isso);
        var readable = await client.GetAsync(
            $"/api/workspaces/organizations/{context.TenantId}/capabilities?systemId={systemId}");
        var setupDenied = await client.PostAsJsonAsync(
            $"/api/workspaces/organizations/{context.TenantId}/capability-setups",
            new { idempotencyKey = $"denied-{personId:N}", source = "provider",
                recordId = Guid.NewGuid().ToString(), systemId, componentIds = Array.Empty<string>(), subscribe = true });
        await SetSystemRoleAsync(personId, systemId, OrganizationRole.Issm);
        var setupAuthorized = await client.PostAsJsonAsync(
            $"/api/workspaces/organizations/{context.TenantId}/capability-setups",
            new { idempotencyKey = $"allowed-{personId:N}", source = "provider",
                recordId = Guid.NewGuid().ToString(), systemId, componentIds = Array.Empty<string>(), subscribe = true });

        // Assert
        membershipOnly.StatusCode.Should().Be(HttpStatusCode.NotFound);
        readable.StatusCode.Should().Be(HttpStatusCode.OK);
        setupDenied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        setupAuthorized.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task LocalCapabilityNarrativeReview_UsesOrganizationCapabilityProposal()
    {
        // Arrange
        var tenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        var personId = Guid.NewGuid();
        var systemId = Guid.NewGuid().ToString();
        var capabilityId = Guid.NewGuid().ToString();
        var proposalId = Guid.NewGuid();
        var context = factory.GetActiveContext();
        context.TenantId = tenantId;
        context.PersonId = personId;
        context.IsCspAdmin = false;
        context.ImpersonatedTenantId = null;
        context.IsWorkspaceRequest = true;
        context.Status = TenantStatus.Active;
        using var client = factory.CreateClient();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.Persons.Add(new Person
            {
                Id = personId, TenantId = tenantId, DisplayName = "Narrative reviewer",
                Email = $"{personId:N}@example.invalid"
            });
            db.RegisteredSystems.Add(new RegisteredSystem
            {
                TenantId = tenantId, Id = systemId, Name = "Narrative system", CreatedBy = "test"
            });
            db.SecurityCapabilities.Add(new SecurityCapability
            {
                TenantId = tenantId, Id = capabilityId, Name = "Local capability", Description = "local",
                Provider = "local", Category = "AC", Owner = "owner", CreatedBy = "test"
            });
            db.SystemCapabilityLinks.Add(new SystemCapabilityLink
            {
                TenantId = tenantId, RegisteredSystemId = systemId,
                SecurityCapabilityId = capabilityId, LinkedBy = "test"
            });
            db.ControlImplementations.Add(new ControlImplementation
            {
                TenantId = tenantId, RegisteredSystemId = systemId, ControlId = "AC-2",
                PolicyNarrative = "before", TechnicalNarrative = "technical"
            });
            db.NarrativeProposals.Add(new NarrativeProposal
            {
                Id = proposalId, TenantId = tenantId, RegisteredSystemId = systemId,
                ControlId = "AC-2", NarrativeType = "Policy", BaseVersion = 1,
                BeforeContent = "before", ProposedContent = "after", StateHash = "synthetic",
                ProvenanceJson = "{}", ConflictsJson = "[]", MissingEvidenceJson = "[]",
                Status = "Draft", Revision = 1, CreatedBy = "different-author",
                DeduplicationKey = Guid.NewGuid().ToString(),
                ChangeSourceKind = "OrganizationCapability", ChangeSourceId = capabilityId
            });
            await db.SaveChangesAsync();
            db.OrganizationMemberships.Add(new OrganizationMembership
            {
                TenantId = tenantId, PersonId = personId, DirectoryTenantId = Guid.NewGuid(),
                ObjectId = Guid.NewGuid(), GrantedBy = "test"
            });
            await db.SaveChangesAsync();
        }
        await SetSystemRoleAsync(personId, systemId, OrganizationRole.Issm);

        // Act
        var response = await client.PostAsJsonAsync(
            $"/api/workspaces/organizations/{tenantId}/capabilities/local/{capabilityId}" +
            $"/narrative-proposals/{proposalId}/review",
            new { systemId, expectedRevision = 1, decision = "RequestRevision", note = "Revise it." });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var proposal = await verify.NarrativeProposals.IgnoreQueryFilters()
            .SingleAsync(x => x.Id == proposalId);
        proposal.Status.Should().Be("NeedsRevision");
        proposal.Revision.Should().Be(2);
    }

    [Fact]
    public async Task CapabilitySetupStatus_NormalizesLegacyOutcomeStrings()
    {
        // Arrange
        var tenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        var personId = Guid.NewGuid();
        var systemId = Guid.NewGuid().ToString();
        var operationId = Guid.NewGuid();
        var capabilityId = Guid.NewGuid().ToString();
        var context = factory.GetActiveContext();
        context.TenantId = tenantId;
        context.PersonId = personId;
        context.IsCspAdmin = false;
        context.ImpersonatedTenantId = null;
        context.IsWorkspaceRequest = true;
        context.Status = TenantStatus.Active;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.Persons.Add(new Person
            {
                Id = personId, TenantId = tenantId, DisplayName = "Setup reader",
                Email = $"{personId:N}@example.invalid"
            });
            db.OrganizationMemberships.Add(new OrganizationMembership
            {
                TenantId = tenantId, PersonId = personId, DirectoryTenantId = Guid.NewGuid(),
                ObjectId = Guid.NewGuid(), GrantedBy = "test"
            });
            db.RegisteredSystems.Add(new RegisteredSystem
            {
                TenantId = tenantId, Id = systemId, Name = "Legacy setup system", CreatedBy = "test"
            });
            db.CapabilitySetupOperations.Add(new Ato.Copilot.Core.Models.Workspaces.CapabilitySetupOperation
            {
                Id = operationId, TenantId = tenantId, IdempotencyKey = $"legacy-{operationId:N}",
                SourceKind = "provider", SourceRecordId = capabilityId, RegisteredSystemId = systemId,
                RecordState = "Completed", ComponentLinksState = "Completed",
                SubscriptionState = "Completed", SubscribeRequested = true,
                OutcomesJson = """["record:completed","component:component-1:completed","subscription:completed"]"""
            });
            await db.SaveChangesAsync();
        }
        await SetSystemRoleAsync(personId, systemId, OrganizationRole.Issm);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync(
            $"/api/workspaces/organizations/{tenantId}/capability-setups/{operationId}");
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var outcomes = body.GetProperty("data").GetProperty("outcomes").EnumerateArray().ToArray();
        outcomes.Should().HaveCount(3);
        outcomes.Should().OnlyContain(x => x.ValueKind == System.Text.Json.JsonValueKind.Object);
        outcomes.Should().ContainSingle(x =>
            x.GetProperty("writeKind").GetString() == "record"
            && x.GetProperty("writeId").GetString() == capabilityId
            && x.GetProperty("state").GetString() == "Completed");
        outcomes.Should().ContainSingle(x =>
            x.GetProperty("writeKind").GetString() == "component-link"
            && x.GetProperty("writeId").GetString() == "component-1");
        outcomes.Should().ContainSingle(x =>
            x.GetProperty("writeKind").GetString() == "subscription"
            && x.GetProperty("writeId").GetString() == systemId);
    }

    [Fact]
    public async Task T093_SetupStatus_ReturnsCompleteIntentOnlyToSystemManagers()
    {
        // Arrange
        var tenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        var personId = Guid.NewGuid();
        var systemId = Guid.NewGuid().ToString("D");
        var operationId = Guid.NewGuid();
        var recordId = Guid.NewGuid().ToString("D");
        var idempotencyKey = $"setup-{Guid.NewGuid():N}";
        var context = factory.GetActiveContext();
        context.TenantId = tenantId;
        context.PersonId = personId;
        context.IsCspAdmin = false;
        context.ImpersonatedTenantId = null;
        context.IsWorkspaceRequest = true;
        context.Status = TenantStatus.Active;
        using var client = factory.CreateClient();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.Persons.Add(new Person
            {
                Id = personId, TenantId = tenantId, DisplayName = "Setup manager",
                Email = $"{personId:N}@example.invalid"
            });
            db.RegisteredSystems.Add(new RegisteredSystem
            {
                TenantId = tenantId, Id = systemId, Name = "Setup intent system", CreatedBy = "test"
            });
            db.CapabilitySetupOperations.Add(new Ato.Copilot.Core.Models.Workspaces.CapabilitySetupOperation
            {
                Id = operationId, TenantId = tenantId, IdempotencyKey = idempotencyKey,
                SourceKind = "local", SourceRecordId = recordId, RegisteredSystemId = systemId,
                RecordState = "Completed", ComponentLinksState = "Completed",
                SubscriptionState = "NotRequested", SubscribeRequested = false,
                ComponentIdsJson = """["component-a","component-b"]""",
                LocalCapabilityJson = JsonSerializer.Serialize(new InlineLocalCapabilityRequest(
                    "Boundary service", "Mission", "SOFTWARE", "Local service",
                    "Implemented", "owner@example.mil")),
                OutcomesJson = JsonSerializer.Serialize(new[]
                {
                    new SetupWriteOutcome("record-create", recordId, "Completed", null,
                        DateTimeOffset.Parse("2026-01-01T00:00:00+00:00")),
                    new SetupWriteOutcome("system-link", systemId, "Completed", null,
                        DateTimeOffset.Parse("2026-01-01T00:00:00+00:00"))
                })
            });
            await db.SaveChangesAsync();
            db.OrganizationMemberships.Add(new OrganizationMembership
            {
                TenantId = tenantId, PersonId = personId, DirectoryTenantId = Guid.NewGuid(),
                ObjectId = Guid.NewGuid(), GrantedBy = "test"
            });
            await db.SaveChangesAsync();
        }
        await SetSystemRoleAsync(personId, systemId, OrganizationRole.Issm);

        // Act
        var response = await client.GetAsync(
            $"/api/workspaces/organizations/{tenantId}/capability-setups/{operationId}");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        await SetSystemRoleAsync(personId, systemId, OrganizationRole.Isso);
        var insufficient = await client.GetAsync(
            $"/api/workspaces/organizations/{tenantId}/capability-setups/{operationId}");
        var crossTenant = await client.GetAsync(
            $"/api/workspaces/organizations/{MultiTenantWebApplicationFactory<McpProgram>.TenantBId}" +
            $"/capability-setups/{operationId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = body.GetProperty("data");
        data.GetProperty("operationId").GetGuid().Should().Be(operationId);
        data.GetProperty("idempotencyKey").GetString().Should().Be(idempotencyKey);
        data.GetProperty("tenantId").GetGuid().Should().Be(tenantId);
        data.GetProperty("systemId").GetString().Should().Be(systemId);
        data.GetProperty("source").GetString().Should().Be("local");
        data.GetProperty("recordId").GetString().Should().Be(recordId);
        data.GetProperty("componentIds").EnumerateArray().Select(x => x.GetString())
            .Should().Equal("component-a", "component-b");
        data.GetProperty("inlineLocalCapability").GetProperty("name").GetString()
            .Should().Be("Boundary service");
        data.GetProperty("outcomes").GetArrayLength().Should().Be(2);
        insufficient.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        crossTenant.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task T094_CurrentProvisioning_ReturnsLatestPersistedOperationWithoutCreating()
    {
        // Arrange
        var context = factory.GetActiveContext();
        context.TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        context.IsCspAdmin = true;
        context.ImpersonatedTenantId = null;
        context.IsWorkspaceRequest = true;
        context.Status = TenantStatus.Active;
        var tenantId = Guid.NewGuid();
        var olderId = Guid.NewGuid();
        var currentId = Guid.NewGuid();
        var olderKey = $"older-{tenantId:N}";
        var currentKey = $"current-{tenantId:N}";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.Tenants.Add(new Tenant { Id = tenantId, DisplayName = $"Current {tenantId:N}" });
            db.OrganizationProvisioningOperations.AddRange(
                new Ato.Copilot.Core.Models.Workspaces.OrganizationProvisioningOperation
                {
                    Id = olderId, TenantId = tenantId, IdempotencyKey = olderKey,
                    CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00+00:00"),
                    UpdatedAt = DateTimeOffset.Parse("2026-01-02T00:00:00+00:00")
                },
                new Ato.Copilot.Core.Models.Workspaces.OrganizationProvisioningOperation
                {
                    Id = currentId, TenantId = tenantId, IdempotencyKey = currentKey,
                    CreatedAt = DateTimeOffset.Parse("2026-01-02T00:00:00+00:00"),
                    UpdatedAt = DateTimeOffset.Parse("2026-01-03T00:00:00+00:00")
                });
            await db.SaveChangesAsync();
        }
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync(
            $"/api/csp/organizations/{tenantId}/provisioning/current");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var operationCount = await verify.OrganizationProvisioningOperations
            .CountAsync(x => x.TenantId == tenantId);
        context.IsCspAdmin = false;
        var unauthorized = await client.GetAsync(
            $"/api/csp/organizations/{tenantId}/provisioning/current");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.GetProperty("data").GetProperty("operationId").GetGuid().Should().Be(currentId);
        body.GetProperty("data").GetProperty("idempotencyKey").GetString().Should().Be(currentKey);
        operationCount.Should().Be(2);
        unauthorized.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task T097_PrepareSetup_RequiresSystemManagementAndTenantBoundary()
    {
        // Arrange
        var tenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        var personId = Guid.NewGuid();
        var systemId = Guid.NewGuid().ToString("D");
        var idempotencyKey = $"prepare-{Guid.NewGuid():N}";
        var context = factory.GetActiveContext();
        context.TenantId = tenantId;
        context.PersonId = personId;
        context.IsCspAdmin = false;
        context.ImpersonatedTenantId = null;
        context.IsWorkspaceRequest = true;
        context.Status = TenantStatus.Active;
        using var client = factory.CreateClient();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.Persons.Add(new Person
            {
                Id = personId, TenantId = tenantId, DisplayName = "Setup preparer",
                Email = $"{personId:N}@example.invalid"
            });
            db.RegisteredSystems.Add(new RegisteredSystem
            {
                TenantId = tenantId, Id = systemId, Name = "Preparation System", CreatedBy = "test"
            });
            await db.SaveChangesAsync();
            db.OrganizationMemberships.Add(new OrganizationMembership
            {
                TenantId = tenantId, PersonId = personId, DirectoryTenantId = Guid.NewGuid(),
                ObjectId = Guid.NewGuid(), GrantedBy = "test"
            });
            await db.SaveChangesAsync();
        }
        var body = new
        {
            idempotencyKey,
            source = "local",
            recordId = "",
            systemId,
            componentIds = Array.Empty<string>(),
            subscribe = false,
            inlineLocalCapability = new
            {
                name = "Prepared service",
                provider = "Mission",
                category = "IA",
                description = "Prepared only",
                implementationStatus = "Implemented",
                owner = "owner"
            }
        };
        await SetSystemRoleAsync(personId, systemId, OrganizationRole.Isso);

        // Act
        var denied = await client.PostAsJsonAsync(
            $"/api/workspaces/organizations/{tenantId}/capability-setups/prepare", body);
        await SetSystemRoleAsync(personId, systemId, OrganizationRole.Issm);
        var prepared = await client.PostAsJsonAsync(
            $"/api/workspaces/organizations/{tenantId}/capability-setups/prepare", body);
        var preparedJson = await prepared.Content.ReadFromJsonAsync<JsonElement>();
        var crossTenant = await client.PostAsJsonAsync(
            $"/api/workspaces/organizations/{MultiTenantWebApplicationFactory<McpProgram>.TenantBId}" +
            "/capability-setups/prepare", body);

        // Assert
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        prepared.StatusCode.Should().Be(HttpStatusCode.Created);
        preparedJson.GetProperty("data").GetProperty("idempotencyKey").GetString()
            .Should().Be(idempotencyKey);
        crossTenant.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await verify.SecurityCapabilities.IgnoreQueryFilters()
            .CountAsync(x => x.TenantId == tenantId && x.Name == "Prepared service")).Should().Be(0);
    }

    private async Task SetSystemRoleAsync(Guid personId, string systemId, OrganizationRole role)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var existing = await db.SystemRoleAssignments.SingleOrDefaultAsync(x =>
            x.TenantId == MultiTenantWebApplicationFactory<McpProgram>.TenantAId
            && x.PersonId == personId && x.RegisteredSystemId == systemId);
        if (existing is null)
            db.SystemRoleAssignments.Add(new SystemRoleAssignment
            {
                TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId,
                PersonId = personId, RegisteredSystemId = systemId, Role = role
            });
        else
            existing.Role = role;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task SystemSecurityCapabilities_ComponentPlacementUsesCurrentManagementAndReviewedTokens()
    {
        // Arrange
        var tenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        var personId = Guid.NewGuid();
        var systemId = Guid.NewGuid().ToString();
        var componentId = Guid.NewGuid().ToString();
        var boundaryId = Guid.NewGuid().ToString();
        var context = factory.GetActiveContext();
        context.TenantId = tenantId;
        context.PersonId = personId;
        context.IsCspAdmin = false;
        context.ImpersonatedTenantId = null;
        context.IsWorkspaceRequest = true;
        context.Status = TenantStatus.Active;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.Persons.Add(new Person { Id = personId, TenantId = tenantId, DisplayName = "Placement manager", Email = $"{personId}@example.invalid" });
            db.RegisteredSystems.Add(new RegisteredSystem { Id = systemId, TenantId = tenantId, Name = "Placement system" });
            db.SystemComponents.Add(new SystemComponent
            {
                Id = componentId, TenantId = tenantId, RegisteredSystemId = systemId,
                Name = "Direct component", ComponentType = ComponentType.Thing
            });
            db.AuthorizationBoundaryDefinitions.Add(new AuthorizationBoundaryDefinition
            {
                Id = boundaryId, TenantId = tenantId, RegisteredSystemId = systemId, Name = "Placement boundary"
            });
            db.OrganizationMemberships.Add(new OrganizationMembership
            {
                TenantId = tenantId, PersonId = personId, DirectoryTenantId = Guid.NewGuid(), ObjectId = Guid.NewGuid(), GrantedBy = "test"
            });
            await db.SaveChangesAsync();
        }
        await SetSystemRoleAsync(personId, systemId, OrganizationRole.Isso);
        using var client = factory.CreateClient();
        var root = $"/api/workspaces/organizations/{tenantId}/systems/{systemId}/security-capabilities/local/component/{componentId}/placements";
        var read = await client.GetAsync(root);
        read.StatusCode.Should().Be(HttpStatusCode.OK, await read.Content.ReadAsStringAsync());
        var envelope = await read.Content.ReadFromJsonAsync<JsonElement>();
        var options = envelope.GetProperty("data").Deserialize<SystemComponentPlacementOptions>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var request = new AssignSystemComponentPlacementRequest(boundaryId, options.SourceRevision, options.RelationshipRevision);

        // Act
        var denied = await client.PostAsJsonAsync($"{root}/assign", request);
        await SetSystemRoleAsync(personId, systemId, OrganizationRole.Issm);
        var assigned = await client.PostAsJsonAsync($"{root}/assign", request);
        assigned.StatusCode.Should().Be(HttpStatusCode.OK, await assigned.Content.ReadAsStringAsync());
        var stale = await client.PostAsJsonAsync($"{root}/assign", request);
        var currentJson = await client.GetFromJsonAsync<JsonElement>(root);
        var current = currentJson.GetProperty("data").Deserialize<SystemComponentPlacementOptions>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var placement = current.Placements.Single(x => x.CanUnassign);
        var removal = new UnassignSystemComponentPlacementRequest(current.SourceRevision, current.RelationshipRevision, placement.Revision);
        var stalePlacement = await client.PostAsJsonAsync($"{root}/{placement.Id}/unassign", removal with { PlacementRevision = "old" });
        await SetSystemRoleAsync(personId, systemId, OrganizationRole.Isso);
        var removeDenied = await client.PostAsJsonAsync($"{root}/{placement.Id}/unassign", removal);
        await SetSystemRoleAsync(personId, systemId, OrganizationRole.Issm);
        var removed = await client.PostAsJsonAsync($"{root}/{placement.Id}/unassign", removal);
        var crossTenant = await client.GetAsync(root.Replace(tenantId.ToString(), MultiTenantWebApplicationFactory<McpProgram>.TenantBId.ToString()));

        // Assert
        options.CanAssignBoundary.Should().BeFalse();
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("code").GetString().Should().Be("STALE_RELATIONSHIP");
        stalePlacement.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await stalePlacement.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("code").GetString().Should().Be("STALE_PLACEMENT");
        removeDenied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        removed.StatusCode.Should().Be(HttpStatusCode.OK);
        crossTenant.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await using var verify = factory.Services.CreateAsyncScope();
        var after = verify.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await after.SystemComponents.AnyAsync(x => x.Id == componentId)).Should().BeTrue();
        (await after.BoundaryComponentAssignments.AnyAsync(x => x.SystemComponentId == componentId)).Should().BeFalse();
    }

    [Fact]
    public async Task SystemSecurityCapabilities_RequireExactSystemManagement_NotBlanketIssoOrCspIdentity()
    {
        // Arrange
        var tenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        var personId = Guid.NewGuid();
        var systemId = Guid.NewGuid().ToString();
        var capabilityId = Guid.NewGuid().ToString();
        var context = factory.GetActiveContext();
        context.TenantId = tenantId;
        context.PersonId = personId;
        context.IsCspAdmin = false;
        context.ImpersonatedTenantId = null;
        context.IsWorkspaceRequest = true;
        context.Status = TenantStatus.Active;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.Persons.Add(new Person { Id = personId, TenantId = tenantId, DisplayName = "Manager", Email = $"{personId}@example.invalid" });
            db.RegisteredSystems.Add(new RegisteredSystem { Id = systemId, TenantId = tenantId, Name = "Selected system" });
            db.SecurityCapabilities.Add(new SecurityCapability { Id = capabilityId, TenantId = tenantId, Name = "Selected capability", Category = "AC" });
            db.OrganizationMemberships.Add(new OrganizationMembership
            {
                TenantId = tenantId, PersonId = personId, DirectoryTenantId = Guid.NewGuid(), ObjectId = Guid.NewGuid(), GrantedBy = "test"
            });
            await db.SaveChangesAsync();
        }
        await SetSystemRoleAsync(personId, systemId, OrganizationRole.Isso);
        using var client = factory.CreateClient();
        var root = $"/api/workspaces/organizations/{tenantId}/systems/{systemId}/security-capabilities";
        var list = await client.GetFromJsonAsync<JsonElement>($"{root}?scope=available&source=local&search=Selected%20capability");
        var source = list.GetProperty("data").GetProperty("items").EnumerateArray().Single(x => x.GetProperty("recordId").GetString() == capabilityId);
        var request = new PrepareSystemCapabilitySetupRequest(Guid.NewGuid().ToString(),
            [new("local", capabilityId, source.GetProperty("sourceRevision").GetString()!, [], [])]);

        // Act
        var denied = await client.PostAsJsonAsync($"{root}/setups/prepare", request);
        await SetSystemRoleAsync(personId, systemId, OrganizationRole.Issm);
        var prepared = await client.PostAsJsonAsync($"{root}/setups/prepare", request);
        var envelope = await prepared.Content.ReadFromJsonAsync<JsonElement>();
        var operation = envelope.GetProperty("data").GetProperty("operation");
        var recovered = await client.GetFromJsonAsync<JsonElement>($"{root}/setups/{operation.GetProperty("operationId").GetGuid()}");
        var complete = await client.PostAsJsonAsync($"{root}/setups/{operation.GetProperty("operationId").GetGuid()}/complete",
            new CompleteSystemCapabilitySetupRequest(operation.GetProperty("revision").GetInt64()));
        using var evidenceForm = new MultipartFormDataContent();
        var evidenceContent = new StringContent("Synthetic repository evidence");
        evidenceContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        evidenceForm.Add(evidenceContent, "file", "proof.txt");
        evidenceForm.Add(new StringContent("PolicyDocument"), "artifactCategory");
        evidenceForm.Add(new StringContent(capabilityId), "securityCapabilityId");
        var upload = await client.PostAsync($"/api/dashboard/systems/{systemId}/evidence", evidenceForm);
        upload.StatusCode.Should().Be(HttpStatusCode.OK, await upload.Content.ReadAsStringAsync());
        var uploadedId = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        var evidenceDetail = (await client.GetFromJsonAsync<JsonElement>($"{root}/local/capability/{capabilityId}")).GetProperty("data");
        var evidence = evidenceDetail.GetProperty("evidence").EnumerateArray().Single();
        var download = await client.GetAsync($"/api/dashboard/systems/{systemId}/evidence/{evidence.GetProperty("id").GetString()}/download");
        var applied = await client.GetFromJsonAsync<JsonElement>($"{root}?scope=applied");
        var crossTenant = await client.GetAsync($"/api/workspaces/organizations/{MultiTenantWebApplicationFactory<McpProgram>.TenantBId}/systems/{systemId}/security-capabilities");
        var foreignProposal = await client.PostAsJsonAsync($"{root}/local/capability/{capabilityId}/narrative-proposals/{Guid.NewGuid()}/review",
            new { expectedRevision = 0, decision = "Approve", note = "Not this source" });
        var foreignControl = await client.PostAsJsonAsync($"{root}/local/capability/{capabilityId}/narrative-proposals",
            new { controlId = "ZZ-999", narrativeType = "Policy", expectedVersion = 0, sourceRevision = source.GetProperty("sourceRevision").GetString() });
        context.IsCspAdmin = true;
        var cspDenied = await client.GetAsync(root);

        // Assert
        list.GetProperty("data").GetProperty("permissions").GetProperty("canManage").GetBoolean().Should().BeFalse();
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        prepared.StatusCode.Should().Be(HttpStatusCode.Created);
        var plannedWrite = operation.GetProperty("plannedWrites").EnumerateArray().Single();
        plannedWrite.GetProperty("displayLabel").GetString().Should().Contain("Selected capability").And.NotContain(capabilityId);
        plannedWrite.GetProperty("recordId").GetString().Should().Be(capabilityId);
        recovered.GetProperty("data").GetProperty("plannedWrites").GetRawText().Should()
            .Be(operation.GetProperty("plannedWrites").GetRawText());
        complete.StatusCode.Should().Be(HttpStatusCode.OK);
        evidence.GetProperty("id").GetString().Should().Be(uploadedId);
        evidence.GetProperty("openUrl").GetString().Should().Be($"/api/dashboard/systems/{systemId}/evidence/{uploadedId}/download");
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        (await download.Content.ReadAsStringAsync()).Should().Be("Synthetic repository evidence");
        applied.GetProperty("data").GetProperty("total").GetInt32().Should().Be(1);
        crossTenant.StatusCode.Should().Be(HttpStatusCode.NotFound);
        foreignProposal.StatusCode.Should().Be(HttpStatusCode.NotFound);
        foreignControl.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        cspDenied.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SystemSecurityCapabilities_ResponsibilityReviewRequiresChecksNotesAndIndependentPermission()
    {
        // Arrange
        var tenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        var personId = Guid.NewGuid();
        var systemId = Guid.NewGuid().ToString();
        var capabilityId = Guid.NewGuid();
        var componentId = Guid.NewGuid();
        var context = factory.GetActiveContext();
        context.TenantId = tenantId;
        context.PersonId = personId;
        context.IsCspAdmin = false;
        context.ImpersonatedTenantId = null;
        context.IsWorkspaceRequest = true;
        context.Status = TenantStatus.Active;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var hostingProfile = await db.CspProfiles.OrderBy(x => x.Id).FirstAsync();
            hostingProfile.OnboardingState.Should().Be(OnboardingState.Active);
            db.Persons.Add(new() { Id = personId, TenantId = tenantId, DisplayName = "Reviewer", Email = $"{personId}@example.invalid" });
            db.RegisteredSystems.Add(new() { Id = systemId, TenantId = tenantId, Name = "Review system" });
            db.OrganizationMemberships.Add(new()
            {
                TenantId = tenantId, PersonId = personId, DirectoryTenantId = Guid.NewGuid(), ObjectId = Guid.NewGuid(), GrantedBy = "test"
            });
            db.CspInheritedComponents.Add(new() { Id = componentId, CspProfileId = hostingProfile.Id, Name = "Monitoring", Status = CspInheritedComponentStatus.Published });
            db.CspInheritedCapabilities.Add(new()
            {
                Id = capabilityId, CspInheritedComponentId = componentId, Name = "Monitoring capability",
                Status = CspInheritedCapabilityStatus.Mapped, MappedNistControlIds = ["AC-1"]
            });
            db.CapabilitySubscriptions.Add(new()
            {
                RegisteredSystemId = systemId, CspInheritedCapabilityId = capabilityId.ToString(),
                RoutingCapabilityId = capabilityId.ToString(), RoutingTenantId = tenantId
            });
            db.ControlBaselines.Add(new()
            {
                TenantId = tenantId, RegisteredSystemId = systemId, BaselineLevel = "Moderate",
                ControlIds = ["AC-1"], TotalControls = 1, CreatedBy = "test"
            });
            await db.SaveChangesAsync();
        }
        await SetSystemRoleAsync(personId, systemId, OrganizationRole.Isso);
        using var client = factory.CreateClient();
        var root = $"/api/workspaces/organizations/{tenantId}/systems/{systemId}/security-capabilities/provider/capability/{capabilityId}";
        var detailResponse = await client.GetAsync(root);
        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK, await detailResponse.Content.ReadAsStringAsync());
        var detail = (await detailResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        var control = detail.GetProperty("controls")[0];
        var legacy = new ConfirmCapabilityResponsibilitiesRequest(detail.GetProperty("baselineId").GetString()!,
            control.GetProperty("availableSourceRevision").GetString()!, control.GetProperty("reviewRevision").GetString()!,
            [new("AC-1", "Shared", "Provider", "Review customer alerts")]);
        var reviewed = legacy with { ProviderCoverageVerified = true, CustomerDutiesReviewed = true, ReviewNotes = "Reviewed current coverage." };

        // Act
        var missing = await client.PostAsJsonAsync($"{root}/responsibilities/confirm", legacy);
        var confirmed = await client.PostAsJsonAsync($"{root}/responsibilities/confirm", reviewed);
        var stale = await client.PostAsJsonAsync($"{root}/responsibilities/confirm", reviewed);
        var after = (await client.GetFromJsonAsync<JsonElement>(root)).GetProperty("data");
        await SetSystemRoleAsync(personId, systemId, OrganizationRole.SystemOwner);
        var denied = await client.PostAsJsonAsync($"{root}/responsibilities/confirm", reviewed);

        // Assert
        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        confirmed.StatusCode.Should().Be(HttpStatusCode.OK, await confirmed.Content.ReadAsStringAsync());
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("code")
            .GetString().Should().Be("STALE_RESPONSIBILITY_REVIEW");
        after.GetProperty("controls")[0].GetProperty("reviewNotes").GetString().Should().Be("Reviewed current coverage.");
        after.GetProperty("controls")[0].GetProperty("providerCoverageVerified").GetBoolean().Should().BeTrue();
        after.GetProperty("controls")[0].GetProperty("customerDutiesReviewed").GetBoolean().Should().BeTrue();
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await using var verify = factory.Services.CreateAsyncScope();
        var persisted = await verify.ServiceProvider.GetRequiredService<AtoCopilotContext>()
            .Set<CapabilityResponsibilityConfirmation>().SingleAsync(x => x.RegisteredSystemId == systemId);
        persisted.ReviewNotes.Should().Be(reviewed.ReviewNotes);
        persisted.ProviderCoverageVerified.Should().BeTrue();
        persisted.CustomerDutiesReviewed.Should().BeTrue();
        persisted.SourceRevision.Should().Be(reviewed.SourceRevision);
        persisted.ReviewedBaselineId.Should().Be(reviewed.BaselineId);
    }

    [Fact]
    public async Task SystemSecurityCapabilities_GenerationOriginReviewAndRemoval_PreserveApprovedHistory()
    {
        // Arrange
        var generator = new Mock<IControlNarrativeService>(MockBehavior.Strict);
        generator.Setup(x => x.GenerateGroundedDraftAsync("Policy", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GroundedNarrativeDraft("Synthetic policy draft", [], []));
        using var owner = new MultiTenantWebApplicationFactory<McpProgram>();
        using var isolated = owner.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IControlNarrativeService>();
            services.AddSingleton(generator.Object);
        }));
        var tenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        var authorId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        var systemId = Guid.NewGuid().ToString();
        var capabilityId = Guid.NewGuid().ToString();
        var implementationId = Guid.NewGuid().ToString();
        var context = owner.GetActiveContext();
        context.TenantId = tenantId;
        context.PersonId = authorId;
        context.IsWorkspaceRequest = true;
        context.Status = TenantStatus.Active;
        await using (var scope = isolated.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.RegisteredSystems.Add(new() { Id = systemId, TenantId = tenantId, Name = "Narrative test" });
            db.SecurityCapabilities.Add(new() { Id = capabilityId, TenantId = tenantId, Name = "Source", Category = "AC" });
            db.CapabilityControlMappings.Add(new() { TenantId = tenantId, SecurityCapabilityId = capabilityId, ControlId = "AC-1" });
            db.SystemCapabilityLinks.Add(new() { TenantId = tenantId, RegisteredSystemId = systemId, SecurityCapabilityId = capabilityId });
            var approvedImplementation = new ControlImplementation
            {
                Id = implementationId, TenantId = tenantId, RegisteredSystemId = systemId, ControlId = "AC-1",
                CurrentVersion = 1, PolicyNarrative = "Prior policy", TechnicalNarrative = "Independent technical",
                ApprovalStatus = SspSectionStatus.Approved
            };
            db.ControlImplementations.Add(approvedImplementation);
            var approvedVersionId = Guid.NewGuid().ToString();
            db.NarrativeVersions.Add(new()
            {
                Id = approvedVersionId, TenantId = tenantId, ControlImplementationId = implementationId,
                VersionNumber = 1, Status = SspSectionStatus.Approved, Content = "Independent technical",
                SnapshotJson = NarrativeContentSnapshot.Capture(approvedImplementation)
            });
            foreach (var person in new[] { authorId, reviewerId })
            {
                db.Persons.Add(new() { Id = person, TenantId = tenantId, DisplayName = "Synthetic reviewer", Email = $"{person}@example.invalid" });
                db.OrganizationMemberships.Add(new() { TenantId = tenantId, PersonId = person, DirectoryTenantId = Guid.NewGuid(), ObjectId = Guid.NewGuid(), GrantedBy = "test" });
                db.SystemRoleAssignments.Add(new() { TenantId = tenantId, PersonId = person, RegisteredSystemId = systemId, Role = OrganizationRole.Issm });
            }
            await db.SaveChangesAsync();
            approvedImplementation.ApprovedVersionId = approvedVersionId;
            await db.SaveChangesAsync();
        }
        using var client = isolated.CreateClient();
        var root = $"/api/workspaces/organizations/{tenantId}/systems/{systemId}/security-capabilities";
        var detail = await client.GetFromJsonAsync<JsonElement>($"{root}/local/capability/{capabilityId}");
        var revision = detail.GetProperty("data").GetProperty("item").GetProperty("sourceRevision").GetString();

        // Act
        var generatedResponse = await client.PostAsJsonAsync($"{root}/local/capability/{capabilityId}/narrative-proposals",
            new { controlId = "AC-1", narrativeType = "Policy", expectedVersion = 1, sourceRevision = revision });
        generatedResponse.StatusCode.Should().Be(HttpStatusCode.OK, await generatedResponse.Content.ReadAsStringAsync());
        var generated = (await generatedResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        var proposalId = generated.GetProperty("id").GetGuid();
        var selfReview = await client.PostAsJsonAsync($"{root}/local/capability/{capabilityId}/narrative-proposals/{proposalId}/review",
            new { expectedRevision = generated.GetProperty("revision").GetInt32(), decision = "Approve" });
        context.PersonId = reviewerId;
        var beforeReview = await client.GetFromJsonAsync<JsonElement>($"{root}/local/capability/{capabilityId}");
        var reviewed = await client.PostAsJsonAsync($"{root}/local/capability/{capabilityId}/narrative-proposals/{proposalId}/review",
            new { expectedRevision = generated.GetProperty("revision").GetInt32(), decision = "Approve" });
        reviewed.StatusCode.Should().Be(HttpStatusCode.OK, await reviewed.Content.ReadAsStringAsync());
        detail = await client.GetFromJsonAsync<JsonElement>($"{root}/local/capability/{capabilityId}");
        var request = new PrepareSystemCapabilityRemovalRequest(Guid.NewGuid().ToString(),
            revision!, detail.GetProperty("data").GetProperty("relationshipRevision").GetString()!);
        var preparation = await client.PostAsJsonAsync($"{root}/local/capability/{capabilityId}/removals/prepare", request);
        preparation.StatusCode.Should().Be(HttpStatusCode.Created, await preparation.Content.ReadAsStringAsync());
        var operation = (await preparation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("operation");
        var replay = await client.PostAsJsonAsync($"{root}/local/capability/{capabilityId}/removals/prepare", request);
        var operationId = operation.GetProperty("operationId").GetGuid();
        var recovery = await client.GetAsync($"{root}/setups/{operationId}");
        var removed = await client.PostAsJsonAsync($"{root}/setups/{operationId}/complete",
            new CompleteSystemCapabilitySetupRequest(operation.GetProperty("revision").GetInt64()));

        // Assert
        selfReview.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        beforeReview.GetProperty("data").GetProperty("narratives").EnumerateArray().Single(x => x.GetProperty("narrativeType").GetString() == "Policy")
            .GetProperty("proposals").EnumerateArray().Single().GetProperty("canReview").GetBoolean().Should().BeTrue();
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        recovery.StatusCode.Should().Be(HttpStatusCode.OK);
        removed.StatusCode.Should().Be(HttpStatusCode.OK, await removed.Content.ReadAsStringAsync());
        await using var verifyScope = isolated.Services.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var implementation = await verify.ControlImplementations.SingleAsync(x => x.Id == implementationId);
        implementation.PolicyNarrative.Should().Be("Synthetic policy draft");
        implementation.TechnicalNarrative.Should().Be("Independent technical");
        implementation.ApprovedVersionId.Should().NotBeNull();
        (await verify.NarrativeVersions.CountAsync(x => x.ControlImplementationId == implementationId)).Should().BeGreaterThan(0);
        (await verify.SystemCapabilityLinks.CountAsync(x => x.RegisteredSystemId == systemId)).Should().Be(0);
        generator.Verify(x => x.GenerateGroundedDraftAsync("Policy", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
