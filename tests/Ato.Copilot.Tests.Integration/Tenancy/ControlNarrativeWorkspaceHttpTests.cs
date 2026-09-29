using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

public sealed class ControlNarrativeWorkspaceHttpTests : IClassFixture<WorkspaceMembershipFactory>
{
    private static readonly Guid DirectoryId = Guid.Parse("079ca000-0000-0000-0000-000000000001");
    private static readonly Guid Tenant = WorkspaceMembershipFactory.TenantAId;
    private static readonly Guid OtherTenant = WorkspaceMembershipFactory.TenantBId;
    private readonly WebApplicationFactory<McpProgram> _factory;

    public ControlNarrativeWorkspaceHttpTests(WorkspaceMembershipFactory factory)
    {
        Environment.GetEnvironmentVariable("ATO_TEST_SQLSERVER_CONNSTRING").Should().BeNullOrEmpty(
            "this focused HTTP regression uses local SQLite, not an external database or Docker");
        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            foreach (var descriptor in services.Where(service => service.ServiceType == typeof(IHostedService)
                && service.ImplementationType != typeof(TenancySeedHostedService)).ToArray())
                services.Remove(descriptor);
        }));
    }

    [Fact]
    public async Task WorkspaceAndDetail_ProjectScopedNarrativeStateCountsPermissionsAndHistory()
    {
        // Arrange
        var fixture = await SeedAsync(OrganizationRole.Issm);
        using var client = Client(fixture.Actor);

        // Act
        var listResponse = await client.GetAsync(
            $"/api/dashboard/systems/{fixture.SystemId}/narrative-workspace?view=needs-attention&page=1&pageSize=20");
        var detailResponse = await client.GetAsync(
            $"/api/dashboard/systems/{fixture.SystemId}/narrative-workspace/{fixture.AttentionControlId}");

        // Assert
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK, await listResponse.Content.ReadAsStringAsync());
        var list = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        list.GetProperty("systemId").GetString().Should().Be(fixture.SystemId);
        list.GetProperty("counts").GetProperty("allControls").GetInt32().Should().Be(3);
        list.GetProperty("counts").GetProperty("needsAttention").GetInt32().Should().Be(2);
        list.GetProperty("counts").GetProperty("approvedStatements").GetInt32().Should().Be(1);
        list.GetProperty("counts").GetProperty("proposedUpdates").GetInt32().Should().Be(1);
        list.GetProperty("permissions").GetProperty("canAuthor").GetBoolean().Should().BeTrue();
        list.GetProperty("permissions").GetProperty("canReview").GetBoolean().Should().BeTrue();
        list.GetProperty("permissions").GetProperty("canManageEvidence").GetBoolean().Should().BeTrue();
        var attention = list.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("controlId").GetString() == fixture.AttentionControlId);
        attention.GetProperty("id").GetString().Should().NotBeNullOrWhiteSpace();
        attention.GetProperty("controlTitle").GetString().Should().Be("Account Management");
        attention.GetProperty("policy").GetProperty("state").GetString().Should().Be("Draft");
        attention.GetProperty("policy").GetProperty("hasContent").GetBoolean().Should().BeTrue();
        attention.GetProperty("policy").GetProperty("hasApprovedContent").GetBoolean().Should().BeFalse();
        attention.GetProperty("policy").GetProperty("proposalId").GetGuid().Should().NotBeEmpty();
        attention.GetProperty("policy").GetProperty("proposalStatus").GetString().Should().Be("Draft");
        attention.GetProperty("policy").GetProperty("isStale").GetBoolean().Should().BeFalse();
        attention.GetProperty("technical").GetProperty("state").GetString().Should().Be("Missing");
        attention.GetProperty("technical").GetProperty("proposalId").ValueKind.Should().Be(JsonValueKind.Null);
        attention.GetProperty("implementationStatus").GetString().Should().Be("Implemented");
        attention.GetProperty("nextAction").GetString().Should().Be("review-proposal");
        attention.GetProperty("nextActionLabel").GetString().Should().Be("Review proposed update");
        attention.GetProperty("nextActionReason").ValueKind.Should().Be(JsonValueKind.Null);

        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK, await detailResponse.Content.ReadAsStringAsync());
        var detail = await detailResponse.Content.ReadFromJsonAsync<JsonElement>();
        detail.GetProperty("systemId").GetString().Should().Be(fixture.SystemId);
        detail.GetProperty("controlId").GetString().Should().Be(fixture.AttentionControlId);
        detail.GetProperty("statements").GetProperty("policy").GetProperty("currentContent").GetString()
            .Should().Be("Current policy");
        detail.GetProperty("statements").GetProperty("technical").GetProperty("currentContent").GetString()
            .Should().BeEmpty();
        detail.GetProperty("proposals")[0].GetProperty("beforeContent").GetString().Should().Be("Current policy");
        detail.GetProperty("proposals")[0].GetProperty("proposedContent").GetString().Should().Be("Proposed policy");
        detail.GetProperty("proposals")[0].GetProperty("cause").GetString().Should().Be("ReferencePublication");
        detail.GetProperty("proposals")[0].GetProperty("provenance").GetProperty("sourceRevision").GetString()
            .Should().Be("rev-7");
        detail.GetProperty("proposals")[0].GetProperty("conflicts")[0].GetString().Should().Be("Conflicting source");
        detail.GetProperty("proposals")[0].GetProperty("missingEvidence")[0].GetString().Should().Be("Missing diagram");
        detail.GetProperty("evidence")[0].GetProperty("target").GetString().Should().Be("evidence://artifact-1");
        detail.GetProperty("responsibilities")[0].GetProperty("customerResponsibility").GetString()
            .Should().Be("Customer configures account lifecycle.");
        detail.GetProperty("history").GetArrayLength().Should().Be(2);
        detail.GetProperty("history")[0].GetProperty("reviews")[0].GetProperty("decision").GetString()
            .Should().Be("RequestRevision");
    }

    [Fact]
    public async Task Detail_PreservesAuthoritativeApprovedSnapshotAlongsideNewerDraft()
    {
        // Arrange
        var fixture = await SeedAsync(OrganizationRole.Issm);
        using var client = Client(fixture.Actor);

        // Act
        var response = await client.GetAsync(
            $"/api/dashboard/systems/{fixture.SystemId}/narrative-workspace/{fixture.ApprovedControlId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var detail = await response.Content.ReadFromJsonAsync<JsonElement>();
        detail.GetProperty("statements").GetProperty("policy").GetProperty("currentContent").GetString()
            .Should().Be("New draft policy");
        detail.GetProperty("statements").GetProperty("policy").GetProperty("approvedContent").GetString()
            .Should().Be("Approved policy");
        detail.GetProperty("statements").GetProperty("technical").GetProperty("approvedContent").GetString()
            .Should().Be("Approved technical");
        detail.GetProperty("approvedSnapshot").GetProperty("versionNumber").GetInt32().Should().Be(1);
        detail.GetProperty("currentVersion").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Workspace_AppliesFiltersBeforeCountsAndPaging()
    {
        // Arrange
        var fixture = await SeedAsync(OrganizationRole.Issm);
        using var client = Client(fixture.Actor);

        // Act
        var response = await client.GetAsync(
            $"/api/dashboard/systems/{fixture.SystemId}/narrative-workspace?view=all-controls&page=1&pageSize=1");
        var statusResponse = await client.GetAsync(
            $"/api/dashboard/systems/{fixture.SystemId}/narrative-workspace?view=all-controls&status=Implemented&page=1&pageSize=20");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("counts").GetProperty("allControls").GetInt32().Should().Be(3);
        result.GetProperty("items").GetArrayLength().Should().Be(1);
        statusResponse.StatusCode.Should().Be(HttpStatusCode.OK, await statusResponse.Content.ReadAsStringAsync());
        var filtered = await statusResponse.Content.ReadFromJsonAsync<JsonElement>();
        filtered.GetProperty("counts").GetProperty("allControls").GetInt32().Should().Be(1);
        filtered.GetProperty("items").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Workspace_ResolvesReadableEnhancementTitlesFromTheCatalog()
    {
        // Arrange
        var fixture = await SeedAsync(OrganizationRole.Issm);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.ControlImplementations.Add(new ControlImplementation
            {
                TenantId = Tenant,
                RegisteredSystemId = fixture.SystemId,
                ControlId = "AC-11(1)",
                PolicyNarrative = "",
                TechnicalNarrative = "",
                Narrative = "",
                ApprovalStatus = SspSectionStatus.NotStarted,
                CurrentVersion = 1,
                ImplementationStatus = ImplementationStatus.Planned,
                AuthoredBy = "fixture",
            });
            await db.SaveChangesAsync();
        }
        using var client = Client(fixture.Actor);

        // Act
        var response = await client.GetAsync(
            $"/api/dashboard/systems/{fixture.SystemId}/narrative-workspace?view=all-controls&search=AC-11%281%29");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("items")[0].GetProperty("controlTitle").GetString()
            .Should().Be("Pattern-hiding Displays");
    }

    [Fact]
    public async Task ViewOnlyUser_ReceivesServerDerivedBlockedActionReasons()
    {
        // Arrange
        var fixture = await SeedAsync(OrganizationRole.Assessor);
        using var client = Client(fixture.Actor);

        // Act
        var response = await client.GetAsync(
            $"/api/dashboard/systems/{fixture.SystemId}/narrative-workspace/{fixture.AttentionControlId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var detail = await response.Content.ReadFromJsonAsync<JsonElement>();
        detail.GetProperty("permissions").GetProperty("canAuthor").GetBoolean().Should().BeFalse();
        detail.GetProperty("permissions").GetProperty("authorReason").GetString().Should().NotBeNullOrWhiteSpace();
        detail.GetProperty("permissions").GetProperty("canReview").GetBoolean().Should().BeFalse();
        detail.GetProperty("permissions").GetProperty("reviewReason").GetString().Should().NotBeNullOrWhiteSpace();
        detail.GetProperty("proposals")[0].GetProperty("canReview").GetBoolean().Should().BeFalse();
        detail.GetProperty("proposals")[0].GetProperty("reviewReason").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ForeignTenantSystemAndForeignSystemControl_ReturnNotFound()
    {
        // Arrange
        var fixture = await SeedAsync(OrganizationRole.Issm);
        using var client = Client(fixture.Actor);

        // Act
        var foreignTenant = await client.GetAsync(
            $"/api/dashboard/systems/{fixture.ForeignTenantSystemId}/narrative-workspace");
        var foreignControl = await client.GetAsync(
            $"/api/dashboard/systems/{fixture.SystemId}/narrative-workspace/{fixture.OtherSystemControlId}");

        // Assert
        foreignTenant.StatusCode.Should().Be(HttpStatusCode.NotFound);
        foreignControl.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ProjectionFailure_IsServerErrorRatherThanSuccessShapedZeroCounts()
    {
        // Arrange
        var fixture = await SeedAsync(OrganizationRole.Issm);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var proposal = await db.NarrativeProposals.SingleAsync(item =>
                item.RegisteredSystemId == fixture.SystemId && item.ControlId == fixture.AttentionControlId);
            proposal.ConflictsJson = """{"invalid":"not-an-array"}""";
            await db.SaveChangesAsync();
        }
        using var client = Client(fixture.Actor);

        // Act
        Func<Task> act = () => client.GetAsync($"/api/dashboard/systems/{fixture.SystemId}/narrative-workspace");

        // Assert
        await act.Should().ThrowAsync<JsonException>(
            "invalid persisted projection data must fail the request instead of returning zero counts");
    }

    private HttpClient Client(Guid actor)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Tid", DirectoryId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Oid", actor.ToString());
        client.DefaultRequestHeaders.Add("X-Workspace-Kind", "organization");
        client.DefaultRequestHeaders.Add("X-Workspace-Mode", "ordinary");
        client.DefaultRequestHeaders.Add("X-Workspace-Tenant-Id", Tenant.ToString());
        return client;
    }

    private async Task<WorkspaceFixture> SeedAsync(OrganizationRole role)
    {
        using var initialized = _factory.CreateClient();
        var actor = Guid.NewGuid();
        var suffix = Random.Shared.Next(100000, 999999).ToString();
        var attentionControlId = $"AC-{suffix}";
        var approvedControlId = $"AU-{suffix}";
        var staleControlId = $"CM-{suffix}";
        var otherSystemControlId = $"IA-{suffix}";
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var person = new Person { TenantId = Tenant, DisplayName = "Narrative reader", Email = $"{actor:N}@example.invalid" };
        var system = new RegisteredSystem { TenantId = Tenant, Name = $"Narrative system {actor:N}",
            CurrentRmfStep = RmfPhase.Implement, HostingEnvironment = "Local test", CreatedBy = "fixture", IsActive = true };
        var otherSystem = new RegisteredSystem { TenantId = Tenant, Name = $"Other system {actor:N}",
            CurrentRmfStep = RmfPhase.Implement, HostingEnvironment = "Local test", CreatedBy = "fixture", IsActive = true };
        var foreignTenantSystem = new RegisteredSystem { TenantId = OtherTenant, Name = $"Foreign system {actor:N}",
            CurrentRmfStep = RmfPhase.Implement, HostingEnvironment = "Local test", CreatedBy = "fixture", IsActive = true };
        db.AddRange(person, system, otherSystem, foreignTenantSystem);
        db.NistControls.AddRange(
            new NistControl { Id = attentionControlId, Family = "AC", Title = "Account Management" },
            new NistControl { Id = approvedControlId, Family = "AU", Title = "Audit Record Generation" },
            new NistControl { Id = staleControlId, Family = "CM", Title = "Configuration Settings" });
        await db.SaveChangesAsync();

        db.OrganizationMemberships.Add(new() { TenantId = Tenant, DirectoryTenantId = DirectoryId,
            ObjectId = actor, PersonId = person.Id, GrantedBy = "fixture" });
        db.SystemRoleAssignments.Add(new() { TenantId = Tenant, PersonId = person.Id,
            RegisteredSystemId = system.Id, Role = role, IsInherited = false });

        var attention = new ControlImplementation { TenantId = Tenant, RegisteredSystemId = system.Id,
            ControlId = attentionControlId, PolicyNarrative = "Current policy", TechnicalNarrative = "",
            Narrative = "", ApprovalStatus = SspSectionStatus.Draft, CurrentVersion = 2,
            ImplementationStatus = ImplementationStatus.Implemented, AuthoredBy = "author" };
        var approved = new ControlImplementation { TenantId = Tenant, RegisteredSystemId = system.Id,
            ControlId = approvedControlId, PolicyNarrative = "New draft policy", TechnicalNarrative = "New draft technical",
            Narrative = "", ApprovalStatus = SspSectionStatus.Draft, CurrentVersion = 2,
            ImplementationStatus = ImplementationStatus.PartiallyImplemented, AuthoredBy = "author" };
        var stale = new ControlImplementation { TenantId = Tenant, RegisteredSystemId = system.Id,
            ControlId = staleControlId, PolicyNarrative = "Policy", TechnicalNarrative = "Technical",
            Narrative = "", ApprovalStatus = SspSectionStatus.Approved, CurrentVersion = 3,
            ImplementationStatus = ImplementationStatus.Planned, AuthoredBy = "author" };
        var otherControl = new ControlImplementation { TenantId = Tenant, RegisteredSystemId = otherSystem.Id,
            ControlId = otherSystemControlId, PolicyNarrative = "Other", TechnicalNarrative = "Other",
            Narrative = "", ApprovalStatus = SspSectionStatus.Approved, CurrentVersion = 1,
            ImplementationStatus = ImplementationStatus.Implemented, AuthoredBy = "author" };
        db.ControlImplementations.AddRange(attention, approved, stale, otherControl);
        await db.SaveChangesAsync();

        var attentionV1 = Version(attention, 1, "Old policy", "Old technical", SspSectionStatus.Approved);
        var attentionV2 = Version(attention, 2, "Current policy", "", SspSectionStatus.NeedsRevision);
        var approvedV1 = Version(approved, 1, "Approved policy", "Approved technical", SspSectionStatus.Approved);
        db.NarrativeVersions.AddRange(attentionV1, attentionV2, approvedV1,
            Version(approved, 2, "New draft policy", "New draft technical", SspSectionStatus.Draft),
            Version(stale, 3, "Policy", "Technical", SspSectionStatus.Approved));
        await db.SaveChangesAsync();
        approved.ApprovedVersionId = approvedV1.Id;
        db.NarrativeReviews.Add(new NarrativeReview { TenantId = Tenant, NarrativeVersionId = attentionV2.Id,
            ReviewedBy = "reviewer", Decision = ReviewDecision.RequestRevision,
            ReviewerComments = "Add implementation detail", ReviewedAt = DateTime.UtcNow });
        db.NarrativeProposals.Add(new NarrativeProposal { TenantId = Tenant, RegisteredSystemId = system.Id,
            ControlId = attentionControlId, NarrativeType = "Policy", BaseVersion = 2,
            BeforeContent = "Current policy", ProposedContent = "Proposed policy", StateHash = "retained-hash",
            DeduplicationKey = Guid.NewGuid().ToString("N"), Status = "Draft", Revision = 4,
            ChangeSourceKind = "ReferencePublication", ChangeSourceId = "reference-1", CreatedBy = "different-author",
            ProvenanceJson = """{"sourceRevision":"rev-7","sources":[{"id":"reference-1"}]}""",
            ConflictsJson = """["Conflicting source"]""", MissingEvidenceJson = """["Missing diagram"]""" });
        db.ControlValidationLinks.Add(new ControlValidationLink { TenantId = Tenant,
            ControlImplementationId = attention.Id, LinkType = ControlValidationLinkType.EvidenceArtifact,
            LinkTarget = "evidence://artifact-1", Description = "Architecture evidence", AddedBy = "author" });
        var baseline = new ControlBaseline { TenantId = Tenant, RegisteredSystemId = system.Id,
            BaselineLevel = "Moderate", ControlIds = [attentionControlId, approvedControlId, staleControlId],
            TotalControls = 3, CreatedBy = "fixture" };
        db.ControlBaselines.Add(baseline);
        await db.SaveChangesAsync();
        db.ControlInheritances.Add(new ControlInheritance { TenantId = Tenant, ControlBaselineId = baseline.Id,
            ControlId = attentionControlId, InheritanceType = InheritanceType.Shared, Provider = "Cloud provider",
            CustomerResponsibility = "Customer configures account lifecycle.", SetBy = "fixture" });
        await db.SaveChangesAsync();

        return new(actor, system.Id, attentionControlId, approvedControlId, otherControl.ControlId,
            foreignTenantSystem.Id);
    }

    private static NarrativeVersion Version(ControlImplementation implementation, int versionNumber,
        string policy, string technical, SspSectionStatus status)
    {
        var snapshotSource = new ControlImplementation
        {
            PolicyNarrative = policy, TechnicalNarrative = technical, Narrative = technical, AuthoredBy = "fixture"
        };
        return new NarrativeVersion { TenantId = implementation.TenantId,
            ControlImplementationId = implementation.Id, VersionNumber = versionNumber,
            Content = technical, SnapshotJson = NarrativeContentSnapshot.Capture(snapshotSource),
            Status = status, AuthoredBy = "author", ChangeReason = $"Version {versionNumber}" };
    }

    private sealed record WorkspaceFixture(Guid Actor, string SystemId, string AttentionControlId,
        string ApprovedControlId, string OtherSystemControlId, string ForeignTenantSystemId);
}
