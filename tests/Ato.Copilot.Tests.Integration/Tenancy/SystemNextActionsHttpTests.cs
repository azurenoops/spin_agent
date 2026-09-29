using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

[Collection("Tenancy")]
public sealed class SystemNextActionsHttpTests(MultiTenantWebApplicationFactory<McpProgram> factory)
{
    private static readonly Guid TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
    [Fact]
    public async Task MissionOwner_SavedProgressReplacesAuthoringWithSubmissionThenWaitingAndRemovesApproved()
    {
        // Arrange
        var (system, _) = await SeedAsync(OrganizationRole.MissionOwner);
        using var client = factory.CreateClient();
        var initial = await ReadAsync(client, system);
        await SaveSectionAsync(system, ProfileSectionType.MissionAndPurpose, SspSectionStatus.Draft);

        // Act
        var draft = await ReadAsync(client, system);
        await SaveSectionAsync(system, ProfileSectionType.MissionAndPurpose, SspSectionStatus.UnderReview);
        var submitted = await ReadAsync(client, system);
        await SaveSectionAsync(system, ProfileSectionType.MissionAndPurpose, SspSectionStatus.Approved);
        var approved = await ReadAsync(client, system);
        await SaveSectionAsync(system, ProfileSectionType.MissionAndPurpose, SspSectionStatus.NeedsRevision);
        var revision = await ReadAsync(client, system);

        // Assert
        Ids(initial).Should().Contain("profile-MissionAndPurpose-start").And.HaveCountGreaterThan(3);
        Ids(draft).Should().Contain("profile-MissionAndPurpose-submit").And.NotContain("profile-MissionAndPurpose-start");
        Ids(submitted).Should().NotContain(x => x.StartsWith("profile-MissionAndPurpose-"));
        submitted.GetProperty("waitingOnOtherRoles")[0].GetProperty("role").GetString().Should().Be("Issm");
        submitted.GetProperty("waitingOnOtherRoles")[0].GetProperty("count").GetInt32().Should().Be(1);
        Ids(approved).Should().NotContain(x => x.StartsWith("profile-MissionAndPurpose-"));
        approved.GetProperty("waitingOnOtherRoles").GetArrayLength().Should().Be(0);
        Ids(revision).Should().Contain("profile-MissionAndPurpose-revise");
        Items(revision).Should().OnlyContain(x => x.GetProperty("responsibleRole").GetString() == "MissionOwner");
        Ids(initial).Should().NotContain(x => x.Contains("LeveragedAuthorizations"));
    }

    [Fact]
    public async Task Issm_ReviewsOnlySubmittedProfileAndCategoryRecords_NotMissionOwnerDraftChecklist()
    {
        // Arrange
        var (system, _) = await SeedAsync(OrganizationRole.Issm);
        await SaveSectionAsync(system, ProfileSectionType.MissionAndPurpose, SspSectionStatus.Draft);
        var usersId = await SaveSectionAsync(system, ProfileSectionType.UsersAndAccess, SspSectionStatus.UnderReview);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        db.UserCategories.AddRange(
            new() { TenantId = TenantId, SystemProfileSectionId = usersId, CategoryName = "Submitted category", GovernanceStatus = SspSectionStatus.UnderReview },
            new() { TenantId = TenantId, SystemProfileSectionId = usersId, CategoryName = "Draft category", GovernanceStatus = SspSectionStatus.Draft },
            new() { TenantId = TenantId, SystemProfileSectionId = usersId, CategoryName = "Approved deletion", PendingDeletion = true, GovernanceStatus = SspSectionStatus.Approved });
        await db.SaveChangesAsync();
        using var client = factory.CreateClient();

        // Act
        var result = await ReadAsync(client, system);

        // Assert
        Ids(result).Should().Contain("profile-UsersAndAccess-review");
        Items(result).Count(x => x.GetProperty("id").GetString()!.StartsWith("user-category-")).Should().Be(1);
        Ids(result).Should().NotContain(x => x.StartsWith("profile-MissionAndPurpose-"));
        Items(result).Should().OnlyContain(x => x.GetProperty("responsibleRole").GetString() == "Issm");
        result.GetProperty("waitingOnOtherRoles").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task UserAccessContextApproval_DoesNotHideIndependentCategoryAuthoringOrReview()
    {
        // Arrange
        var (system, _) = await SeedAsync(OrganizationRole.MissionOwner);
        var usersId = await SaveSectionAsync(system, ProfileSectionType.UsersAndAccess, SspSectionStatus.Approved);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var draft = new UserCategory { TenantId = TenantId, SystemProfileSectionId = usersId, CategoryName = "Operator", GovernanceStatus = SspSectionStatus.Draft };
        var review = new UserCategory { TenantId = TenantId, SystemProfileSectionId = usersId, CategoryName = "Reviewer", GovernanceStatus = SspSectionStatus.UnderReview };
        db.UserCategories.AddRange(draft, review);
        await db.SaveChangesAsync();
        using var client = factory.CreateClient();

        // Act
        var result = await ReadAsync(client, system);

        // Assert
        Ids(result).Should().Contain($"user-category-{draft.Id}-submit");
        Ids(result).Should().NotContain(x => x.StartsWith("profile-UsersAndAccess-") || x.StartsWith($"user-category-{review.Id}"));
        result.GetProperty("waitingOnOtherRoles")[0].GetProperty("count").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task ModernBoundaryProgress_ReplacesDefinitionTaskAndRemovesCompletedScopeTask()
    {
        // Arrange
        var (system, _) = await SeedAsync(OrganizationRole.Issm);
        using var client = factory.CreateClient();
        var missing = await ReadAsync(client, system);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var boundary = new AuthorizationBoundaryDefinition { TenantId = TenantId, RegisteredSystemId = system, Name = "Modern boundary", CreatedBy = "test" };
        db.AuthorizationBoundaryDefinitions.Add(boundary);
        await db.SaveChangesAsync();

        // Act
        var defined = await ReadAsync(client, system);
        var component = new SystemComponent { TenantId = TenantId, Name = "Synthetic component" };
        db.SystemComponents.Add(component);
        db.BoundaryComponentAssignments.Add(new()
        {
            TenantId = TenantId, AuthorizationBoundaryDefinitionId = boundary.Id, SystemComponentId = component.Id, IsInScope = true, CreatedBy = "test"
        });
        await db.SaveChangesAsync();
        var scoped = await ReadAsync(client, system);

        // Assert
        Ids(missing).Should().Contain("boundary-define");
        Ids(defined).Should().NotContain("boundary-define").And.Contain("boundary-scope");
        Ids(scoped).Should().NotContain(x => x.StartsWith("boundary-"));
    }

    [Fact]
    public async Task PersistedRoles_UnionAndOverrideInheritance_IgnoreBrowserPersona()
    {
        // Arrange
        var (system, _) = await SeedAsync(OrganizationRole.MissionOwner, OrganizationRole.Issm);
        await SaveSectionAsync(system, ProfileSectionType.DataTypes, SspSectionStatus.UnderReview);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Simulated-Role", "AuthorizingOfficial");
        var union = await ReadAsync(client, system);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var inherited = await db.SystemRoleAssignments.SingleAsync(x => x.RegisteredSystemId == system && x.Role == OrganizationRole.Issm);
        inherited.IsInherited = true;
        await db.SaveChangesAsync();
        var inheritedResult = await ReadAsync(client, system);
        var other = new Person { TenantId = TenantId, DisplayName = "Override", Email = $"{Guid.NewGuid()}@example.invalid" };
        db.Persons.Add(other);
        db.SystemRoleAssignments.Add(new() { TenantId = TenantId, RegisteredSystemId = system, PersonId = other.Id, Role = OrganizationRole.Issm });
        await db.SaveChangesAsync();

        // Act
        var overridden = await ReadAsync(client, system);

        // Assert
        Ids(union).Should().Contain("profile-DataTypes-review").And.Contain("profile-MissionAndPurpose-start");
        Ids(inheritedResult).Should().Contain("profile-DataTypes-review");
        Ids(overridden).Should().NotContain("profile-DataTypes-review");
        overridden.GetProperty("effectiveRoles").EnumerateArray().Select(x => x.GetString()).Should().Equal("MissionOwner");
        Items(overridden).Should().OnlyContain(x => x.GetProperty("responsibleRole").GetString() == "MissionOwner");
    }

    [Fact]
    public async Task AdministratorWithoutRmfRole_HasNoSpecialistActions()
    {
        // Arrange
        var (system, person) = await SeedAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        db.OrganizationRoleAssignments.Add(new() { TenantId = TenantId, PersonId = person, Role = OrganizationRole.Administrator });
        await db.SaveChangesAsync();
        using var client = factory.CreateClient();

        // Act
        var result = await ReadAsync(client, system);

        // Assert
        Items(result).Should().BeEmpty();
        result.GetProperty("effectiveRoles").EnumerateArray().Select(x => x.GetString()).Should().Contain("Administrator");
    }

    [Fact]
    public async Task Assessor_UsesActualSapAndBaselineState_NotGenericRunAssessmentTask()
    {
        // Arrange
        var (system, _) = await SeedAsync(OrganizationRole.Assessor);
        using var client = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var systemRow = await db.RegisteredSystems.SingleAsync(x => x.Id == system);
        systemRow.CurrentRmfStep = RmfPhase.Assess;
        await db.SaveChangesAsync();
        var noBaseline = await ReadAsync(client, system);
        db.ControlBaselines.Add(new() { TenantId = TenantId, RegisteredSystemId = system, BaselineLevel = "Low", ControlIds = ["AC-1"], TotalControls = 1, CreatedBy = "test" });
        await db.SaveChangesAsync();

        // Act
        var missingSap = await ReadAsync(client, system);
        var sap = new SecurityAssessmentPlan { TenantId = TenantId, RegisteredSystemId = system, Title = "Synthetic SAP", Status = SapStatus.Draft, BaselineLevel = "Low" };
        db.SecurityAssessmentPlans.Add(sap);
        await db.SaveChangesAsync();
        var draft = await ReadAsync(client, system);
        sap.Status = SapStatus.Finalized;
        await db.SaveChangesAsync();
        var finalized = await ReadAsync(client, system);

        // Assert
        Items(noBaseline).Should().BeEmpty();
        Ids(missingSap).Should().Equal("sap-prepare");
        Ids(draft).Should().Equal("sap-complete");
        Items(finalized).Should().BeEmpty();
        Items(draft).Should().OnlyContain(x => x.GetProperty("responsibleRole").GetString() == "Sca"
            && x.GetProperty("path").GetString() == "assessments?tab=plan");
    }

    [Theory]
    [InlineData(RmfPhase.Prepare, false)]
    [InlineData(RmfPhase.Authorize, true)]
    [InlineData(RmfPhase.Monitor, false)]
    public async Task AuthorizingOfficial_OnlyGetsDecisionReviewAtAuthorizationStage(RmfPhase phase, bool expected)
    {
        // Arrange
        var (system, _) = await SeedAsync(OrganizationRole.AuthorizingOfficial);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.RegisteredSystems.SingleAsync(x => x.Id == system)).CurrentRmfStep = phase;
        await db.SaveChangesAsync();
        using var client = factory.CreateClient();

        // Act
        var result = await ReadAsync(client, system);

        // Assert
        Ids(result).Contains("authorization-review").Should().Be(expected);
        if (expected)
            Items(result).Should().OnlyContain(x => x.GetProperty("responsibleRole").GetString() == "AuthorizingOfficial");
        else Items(result).Should().BeEmpty();
    }

    [Fact]
    public async Task Assessor_ReportPreparationRequiresCanonicalDeterminations_AndStopsAfterReportExists()
    {
        // Arrange
        var (system, _) = await SeedAsync(OrganizationRole.Assessor);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.RegisteredSystems.SingleAsync(x => x.Id == system)).CurrentRmfStep = RmfPhase.Assess;
        var assessment = new ComplianceAssessment { TenantId = TenantId, Status = AssessmentStatus.Completed };
        db.Assessments.Add(assessment);
        await db.SaveChangesAsync();
        using var client = factory.CreateClient();
        var scanOnly = await ReadAsync(client, system);
        db.ControlEffectivenessRecords.Add(new()
        {
            TenantId = TenantId, RegisteredSystemId = system, AssessmentId = assessment.Id,
            ControlId = "AC-1", AssessorId = "synthetic"
        });
        await db.SaveChangesAsync();

        // Act
        var determinations = await ReadAsync(client, system);
        db.SecurityAssessmentReports.Add(new()
        {
            TenantId = TenantId, RegisteredSystemId = system, Title = "Synthetic report", Status = SarStatus.Draft, CreatedBy = "test"
        });
        await db.SaveChangesAsync();
        var reportExists = await ReadAsync(client, system);

        // Assert
        Ids(scanOnly).Should().NotContain("sar-prepare");
        Ids(determinations).Should().Contain("sar-prepare");
        Ids(reportExists).Should().NotContain("sar-prepare");
    }

    [Fact]
    public async Task NarrativeReviewRequiresSavedCurrentVersion_AndTracksApprovalWithoutWrites()
    {
        // Arrange
        var (system, person) = await SeedAsync(OrganizationRole.Issm);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var control = new ControlImplementation
        {
            TenantId = TenantId, RegisteredSystemId = system, ControlId = "AC-1",
            ApprovalStatus = SspSectionStatus.UnderReview, CurrentVersion = 2
        };
        db.ControlImplementations.Add(control);
        db.NarrativeVersions.Add(new()
        {
            TenantId = TenantId, ControlImplementationId = control.Id, VersionNumber = 1, AuthoredBy = "test"
        });
        await db.SaveChangesAsync();
        using var client = factory.CreateClient();
        var missingCurrent = await ReadAsync(client, system);
        db.NarrativeVersions.Add(new()
        {
            TenantId = TenantId, ControlImplementationId = control.Id, VersionNumber = 2, AuthoredBy = "test"
        });
        await db.SaveChangesAsync();

        // Act
        var submitted = await ReadAsync(client, system);
        var assignment = await db.SystemRoleAssignments.SingleAsync(x => x.RegisteredSystemId == system && x.PersonId == person);
        assignment.Role = OrganizationRole.Isso;
        await db.SaveChangesAsync();
        var waiting = await ReadAsync(client, system);
        control.ApprovalStatus = SspSectionStatus.Approved;
        await db.SaveChangesAsync();
        var approved = await ReadAsync(client, system);

        // Assert
        Ids(missingCurrent).Should().NotContain("narratives-review");
        Ids(submitted).Should().Contain("narratives-review");
        Ids(waiting).Should().NotContain("narratives-author").And.NotContain("narratives-review");
        waiting.GetProperty("waitingOnOtherRoles")[0].GetProperty("count").GetInt32().Should().Be(1);
        approved.GetProperty("waitingOnOtherRoles").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Isso_NarrativeAndEvidenceWorkDisappearsAfterSavedApprovalAndClassification()
    {
        // Arrange
        var (system, _) = await SeedAsync(OrganizationRole.Isso);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var control = new ControlImplementation { TenantId = TenantId, RegisteredSystemId = system, ControlId = "AC-1", ApprovalStatus = SspSectionStatus.Draft };
        var evidence = new EvidenceArtifact { TenantId = TenantId, RegisteredSystemId = system, FileName = "synthetic.txt", ContentType = "text/plain", StoragePath = "synthetic", UploadedBy = "test" };
        db.ControlImplementations.Add(control);
        db.EvidenceArtifacts.Add(evidence);
        await db.SaveChangesAsync();
        using var client = factory.CreateClient();
        var initial = await ReadAsync(client, system);

        // Act
        control.ApprovalStatus = SspSectionStatus.Approved;
        evidence.NarrativeType = EvidenceNarrativeType.Technical;
        await db.SaveChangesAsync();
        var completed = await ReadAsync(client, system);

        // Assert
        Ids(initial).Should().Contain("narratives-author").And.Contain("evidence-classify");
        Items(completed).Should().BeEmpty();
        Items(initial).Should().OnlyContain(x => x.GetProperty("responsibleRole").GetString() == "Isso");
    }

    [Fact]
    public async Task UnknownCrossSystemCrossTenantAndAnonymousRequests_AreDenied_ProjectionDoesNotWrite()
    {
        // Arrange
        var (system, _) = await SeedAsync(OrganizationRole.MissionOwner);
        using var client = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var other = new RegisteredSystem { TenantId = TenantId, Name = "Inaccessible", CreatedBy = "test" };
        db.RegisteredSystems.Add(other);
        await db.SaveChangesAsync();

        // Act
        var first = await ReadAsync(client, system);
        var second = await ReadAsync(client, system);
        var unknown = await client.GetAsync(Url(Guid.NewGuid().ToString()));
        var forbiddenSystem = await client.GetAsync(Url(other.Id));
        factory.GetActiveContext().TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantBId;
        var forbiddenTenant = await client.GetAsync(Url(system));
        client.DefaultRequestHeaders.Add("X-Test-Anonymous", "true");
        var anonymous = await client.GetAsync(Url(system));
        factory.GetActiveContext().TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;

        // Assert
        Ids(second).Should().Equal(Ids(first));
        Items(first).Should().OnlyContain(x => x.GetProperty("actionLabel").GetString() == "Open");
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        forbiddenSystem.StatusCode.Should().Be(HttpStatusCode.NotFound);
        forbiddenTenant.StatusCode.Should().Be(HttpStatusCode.NotFound);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await db.SystemProfileSections.CountAsync(x => x.RegisteredSystemId == system)).Should().Be(0);
        (await db.DashboardActivities.CountAsync(x => x.RegisteredSystemId == system)).Should().Be(0);
    }

    private static string Url(string system) => $"/api/dashboard/systems/{system}/next-actions";
    private static JsonElement[] Items(JsonElement response) => response.GetProperty("items").EnumerateArray().ToArray();
    private static string[] Ids(JsonElement response) => Items(response).Select(x => x.GetProperty("id").GetString()!).ToArray();

    private static async Task<JsonElement> ReadAsync(HttpClient client, string system)
    {
        var response = await client.GetAsync(Url(system));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<(string System, Guid Person)> SeedAsync(params OrganizationRole[] roles)
    {
        var tenant = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        var person = Guid.NewGuid();
        factory.ResetLegacyTenantContext(tenant);
        factory.GetActiveContext().PersonId = person;
        factory.GetActiveContext().IsWorkspaceRequest = true;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        db.Persons.Add(new() { TenantId = tenant, Id = person, DisplayName = "Synthetic user", Email = $"{person}@example.invalid" });
        db.OrganizationMemberships.Add(new()
        {
            TenantId = tenant, PersonId = person, DirectoryTenantId = Guid.NewGuid(), ObjectId = Guid.NewGuid(), GrantedBy = "test"
        });
        var system = new RegisteredSystem { TenantId = tenant, Name = "Synthetic next-actions system", CreatedBy = "test" };
        db.RegisteredSystems.Add(system);
        foreach (var role in roles)
            db.SystemRoleAssignments.Add(new() { TenantId = tenant, RegisteredSystemId = system.Id, PersonId = person, Role = role });
        await db.SaveChangesAsync();
        return (system.Id, person);
    }

    private async Task<string> SaveSectionAsync(string system, ProfileSectionType type, SspSectionStatus status)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var section = await db.SystemProfileSections.SingleOrDefaultAsync(x => x.RegisteredSystemId == system && x.SectionType == type);
        if (section is null)
        {
            section = new() { TenantId = TenantId, RegisteredSystemId = system, SectionType = type };
            db.SystemProfileSections.Add(section);
        }
        section.GovernanceStatus = status;
        section.DraftContent = "{\"mission\":\"Synthetic mission\"}";
        section.LastEditedBy = "synthetic";
        section.LastEditedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return section.Id;
    }
}
