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
}
