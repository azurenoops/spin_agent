using System.Net;
using System.Net.Http.Json;
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
public sealed class ResponsibilityDraftHttpTests(MultiTenantWebApplicationFactory<McpProgram> factory)
{
    [Fact]
    public async Task DraftContextWithoutProviderDoesNotInventResponsibilityOrPersistADraft()
    {
        // Arrange
        var systemId = await SeedAsync();
        using var client = factory.CreateClient();
        var route = $"/api/dashboard/systems/{systemId}/capability-subscriptions";
        var endpoint = route + "/drafts/AU-6";

        // Act
        var response = await client.GetAsync(endpoint);
        var context = await response.Content.ReadFromJsonAsync<ResponsibilityDraftContext>();

        // Assert
        response.EnsureSuccessStatusCode();
        context.Should().NotBeNull();
        context!.ControlId.Should().Be("AU-6");
        context.Draft.Should().BeNull();
        context.SourceValues["allocation"].Value.Should().Be("NeedsConfirmation");
        context.SourceValues["provider"].Value.Should().BeEmpty();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.Set<ResponsibilityDraft>().CountAsync(x => x.RegisteredSystemId == systemId)).Should().Be(0);
    }

    [Fact]
    public async Task ExplicitNoGenerationPreparationPersistsUnconfirmedDraftAndSourceHistory()
    {
        // Arrange
        var systemId = await SeedAsync();
        using var client = factory.CreateClient();
        var route = $"/api/dashboard/systems/{systemId}/capability-subscriptions";
        var endpoint = route + "/drafts/AU-6";

        // Act
        var response = await client.PostAsJsonAsync(endpoint + "/prepare",
            new PrepareResponsibilityDraftRequest(null, 0, Generate: false));
        var context = await response.Content.ReadFromJsonAsync<ResponsibilityDraftContext>();

        // Assert
        response.EnsureSuccessStatusCode();
        context.Should().NotBeNull();
        context!.Draft.Should().NotBeNull();
        context.Draft!.GenerationState.Should().Be("NotRequested");
        context.Draft.Values["allocation"].Value.Should().Be("NeedsConfirmation");

        // Act
        var values = context.Draft.Values.ToDictionary(x => x.Key, x => x.Value.Value);
        values["allocation"] = "Customer";
        values["customer"] = "System team reviews the synthetic audit evidence.";
        values["basis"] = "Reviewed system-specific responsibility.";
        var savedResponse = await client.PutAsJsonAsync(
            route + $"/drafts/record/{context.Draft.Id}",
            new SaveResponsibilityDraftRequest(context.Draft.Revision, values));
        savedResponse.EnsureSuccessStatusCode();
        var saved = await savedResponse.Content.ReadFromJsonAsync<ResponsibilityDraftResponse>();

        // Assert
        saved.Should().NotBeNull();
        saved!.Revision.Should().Be(context.Draft.Revision + 1);
        saved.Values["customer"].Value.Should().Be(values["customer"]);
        saved.Values["customer"].UserEdited.Should().BeTrue();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var savedDraft = await db.Set<ResponsibilityDraft>().SingleAsync(x => x.RegisteredSystemId == systemId);
        savedDraft.Status.Should().Be("Proposed");
        savedDraft.Revision.Should().Be(saved!.Revision);
        var actions = await db.Set<ResponsibilityDraftHistory>().Where(x => x.DraftId == savedDraft.Id)
            .Select(x => x.Action).ToListAsync();
        actions.Should().Contain("SuggestionPrepared").And.Contain("DraftEdited");
        var baselineId = await db.ControlBaselines.Where(x => x.RegisteredSystemId == systemId)
            .Select(x => x.Id).SingleAsync();
        (await db.ControlInheritances.CountAsync(x => x.ControlBaselineId == baselineId)).Should().Be(0);
    }

    [Fact]
    public async Task DraftPreparationDeniesAssessorMutation()
    {
        // Arrange
        var systemId = await SeedAsync(OrganizationRole.Assessor);
        using var client = factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync(
            $"/api/dashboard/systems/{systemId}/capability-subscriptions/drafts/AU-6/prepare",
            new PrepareResponsibilityDraftRequest(null, 0, Generate: false));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DraftConfirmationRequiresSavedHumanReviewBeforeCreatingCustomerAllocation()
    {
        // Arrange
        var systemId = await SeedAsync();
        using var client = factory.CreateClient();
        var route = $"/api/dashboard/systems/{systemId}/capability-subscriptions";
        var prepareResponse = await client.PostAsJsonAsync(route + "/drafts/AU-6/prepare",
            new PrepareResponsibilityDraftRequest(null, 0, Generate: false));
        prepareResponse.EnsureSuccessStatusCode();
        var context = (await prepareResponse.Content.ReadFromJsonAsync<ResponsibilityDraftContext>())!;
        var values = context.Draft!.Values.ToDictionary(x => x.Key, x => x.Value.Value);
        values["allocation"] = "Customer";
        values["customer"] = "System team reviews the synthetic audit evidence.";
        values["basis"] = "Reviewed system-specific responsibility.";
        var saveResponse = await client.PutAsJsonAsync(route + $"/drafts/record/{context.Draft.Id}",
            new SaveResponsibilityDraftRequest(context.Draft.Revision, values));
        saveResponse.EnsureSuccessStatusCode();
        var saved = (await saveResponse.Content.ReadFromJsonAsync<ResponsibilityDraftResponse>())!;

        // Act
        var staleSave = await client.PutAsJsonAsync(route + $"/drafts/record/{saved.Id}",
            new SaveResponsibilityDraftRequest(context.Draft.Revision, values));
        var unreviewed = await client.PostAsJsonAsync(route + $"/drafts/record/{saved.Id}/confirm",
            new ConfirmResponsibilityDraftRequest(saved.Revision, saved.SourceHash, false, true,
                "Coverage was not confirmed."));
        var confirmation = await client.PostAsJsonAsync(route + $"/drafts/record/{saved.Id}/confirm",
            new ConfirmResponsibilityDraftRequest(saved.Revision, saved.SourceHash, true, true,
                "Reviewed current source records and the customer responsibility."));
        var accepted = await confirmation.Content.ReadFromJsonAsync<ResponsibilityDraftResponse>();

        // Assert
        staleSave.StatusCode.Should().Be(HttpStatusCode.Conflict);
        unreviewed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        confirmation.EnsureSuccessStatusCode();
        accepted.Should().NotBeNull();
        accepted!.Status.Should().Be("Accepted");
        accepted.ReviewedBy.Should().NotBeNullOrWhiteSpace();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var baselineId = await db.ControlBaselines.Where(x => x.RegisteredSystemId == systemId)
            .Select(x => x.Id).SingleAsync();
        var allocation = await db.ControlInheritances.SingleAsync(x =>
            x.ControlBaselineId == baselineId && x.ControlId == "AU-6");
        allocation.InheritanceType.Should().Be(InheritanceType.Customer);
        allocation.CustomerResponsibility.Should().Be(values["customer"]);
    }

    [Fact]
    public async Task DraftConfirmationRejectsChangedSystemSourcesWithoutLosingTheSavedDraft()
    {
        // Arrange
        var systemId = await SeedAsync();
        using var client = factory.CreateClient();
        var route = $"/api/dashboard/systems/{systemId}/capability-subscriptions";
        var prepare = await client.PostAsJsonAsync(route + "/drafts/AU-6/prepare",
            new PrepareResponsibilityDraftRequest(null, 0, Generate: false));
        prepare.EnsureSuccessStatusCode();
        var context = (await prepare.Content.ReadFromJsonAsync<ResponsibilityDraftContext>())!;
        var values = context.Draft!.Values.ToDictionary(x => x.Key, x => x.Value.Value);
        values["allocation"] = "Customer";
        values["customer"] = "System team reviews the synthetic audit evidence.";
        values["basis"] = "Reviewed system-specific responsibility.";
        var save = await client.PutAsJsonAsync(route + $"/drafts/record/{context.Draft.Id}",
            new SaveResponsibilityDraftRequest(context.Draft.Revision, values));
        save.EnsureSuccessStatusCode();
        var draft = (await save.Content.ReadFromJsonAsync<ResponsibilityDraftResponse>())!;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var system = await db.RegisteredSystems.SingleAsync(x => x.Id == systemId);
            system.Description = "Updated source after the responsibility draft was reviewed.";
            await db.SaveChangesAsync();
        }

        // Act
        var confirmation = await client.PostAsJsonAsync(route + $"/drafts/record/{draft.Id}/confirm",
            new ConfirmResponsibilityDraftRequest(draft.Revision, draft.SourceHash, true, true,
                "Reviewed current source records and the customer responsibility."));

        // Assert
        confirmation.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await using var verify = factory.Services.CreateAsyncScope();
        var verificationDb = verify.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var retained = await verificationDb.Set<ResponsibilityDraft>().SingleAsync(x => x.Id == draft.Id);
        retained.Status.Should().Be("Proposed");
        retained.Revision.Should().Be(draft.Revision);
        var baselineId = await verificationDb.ControlBaselines.Where(x => x.RegisteredSystemId == systemId)
            .Select(x => x.Id).SingleAsync();
        (await verificationDb.ControlInheritances.CountAsync(x =>
            x.ControlBaselineId == baselineId && x.ControlId == "AU-6")).Should().Be(0);
    }

    private async Task<string> SeedAsync(OrganizationRole role = OrganizationRole.Isso)
    {
        var tenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        var personId = Guid.NewGuid();
        factory.ResetLegacyTenantContext(tenantId);
        factory.GetActiveContext().PersonId = personId;
        factory.GetActiveContext().IsWorkspaceRequest = true;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        db.Persons.Add(new()
        {
            Id = personId,
            TenantId = tenantId,
            DisplayName = "Synthetic responsibility reviewer",
            Email = $"{personId}@example.invalid"
        });
        db.OrganizationMemberships.Add(new()
        {
            TenantId = tenantId,
            PersonId = personId,
            DirectoryTenantId = Guid.NewGuid(),
            ObjectId = Guid.NewGuid(),
            GrantedBy = "test"
        });
        var system = new RegisteredSystem { TenantId = tenantId, Name = "Synthetic responsibility system", CreatedBy = "test" };
        db.RegisteredSystems.Add(system);
        db.SystemRoleAssignments.Add(new()
        {
            TenantId = tenantId,
            PersonId = personId,
            RegisteredSystemId = system.Id,
            Role = role
        });
        db.ControlBaselines.Add(new()
        {
            TenantId = tenantId,
            RegisteredSystemId = system.Id,
            BaselineLevel = "Moderate",
            TotalControls = 1,
            ControlIds = ["AU-6"],
            CreatedBy = "test"
        });
        await db.SaveChangesAsync();
        return system.Id;
    }
}
