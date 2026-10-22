using System.Net;
using System.Net.Http.Json;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Onboarding;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Integration;

public sealed partial class CapabilityResponsibilityTests
{
    [Fact]
    public async Task DraftGenerationWithoutControlEvidenceDoesNotInventCustomerAllocation()
    {
        // Arrange
        await AddBaselineAsync();
        _draftGenerator.Setup(x => x.GenerateAsync("AU-6",
                It.IsAny<IReadOnlyList<ResponsibilityDraftSource>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResponsibilityDraftSuggestion(new()
            {
                ["allocation"] = new("Customer", "AI proposed", ["system"]),
                ["customer"] = new("AI-generated duty without supporting evidence.", "AI proposed", ["system"]),
                ["basis"] = new("AI-generated basis without supporting evidence.", "AI proposed", ["system"])
            }, [], []));

        // Act
        var response = await _client.PostAsJsonAsync($"{Route}/drafts/AU-6/prepare",
            new PrepareResponsibilityDraftRequest(null, 0, Generate: true));
        var context = (await response.Content.ReadFromJsonAsync<ResponsibilityDraftContext>())!;

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        context.Draft.Should().NotBeNull();
        context.Draft!.Values["allocation"].Value.Should().Be("NeedsConfirmation");
        context.Draft.Values["allocation"].Origin.Should().Be("From system records");
        context.SourceValues["allocation"].Value.Should().Be("NeedsConfirmation");
    }

    [Fact]
    public async Task DraftRefreshPreservesHumanEditsAndRequiresExplicitComparison()
    {
        // Arrange
        await AddBaselineAsync();
        _draftGenerator.Setup(x => x.GenerateAsync("AU-6",
                It.IsAny<IReadOnlyList<ResponsibilityDraftSource>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResponsibilityDraftSuggestion(new()
            {
                ["customer"] = new("Initial synthetic proposal.", "AI proposed", ["system"]),
                ["basis"] = new("Initial synthetic basis.", "AI proposed", ["system"])
            }, [], []));
        var firstResponse = await _client.PostAsJsonAsync($"{Route}/drafts/AU-6/prepare",
            new PrepareResponsibilityDraftRequest(null, 0));
        var first = (await firstResponse.Content.ReadFromJsonAsync<ResponsibilityDraftContext>())!;
        var values = first.Draft!.Values.ToDictionary(x => x.Key, x => x.Value.Value);
        values["allocation"] = "Customer";
        values["customer"] = "Human-reviewed synthetic duty.";
        values["basis"] = "Human-reviewed synthetic basis.";
        var save = await _client.PutAsJsonAsync($"{Route}/drafts/record/{first.Draft.Id}",
            new SaveResponsibilityDraftRequest(first.Draft.Revision, values));
        var edited = (await save.Content.ReadFromJsonAsync<ResponsibilityDraftResponse>())!;
        _draftGenerator.Setup(x => x.GenerateAsync("AU-6",
                It.IsAny<IReadOnlyList<ResponsibilityDraftSource>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResponsibilityDraftSuggestion(new()
            {
                ["customer"] = new("Refreshed synthetic proposal.", "AI proposed", ["system"]),
                ["basis"] = new("Refreshed synthetic basis.", "AI proposed", ["system"])
            }, [], []));

        // Act
        var refresh = await _client.PostAsJsonAsync($"{Route}/drafts/AU-6/prepare",
            new PrepareResponsibilityDraftRequest(null, edited.Revision));
        var refreshed = (await refresh.Content.ReadFromJsonAsync<ResponsibilityDraftContext>())!;
        var confirmation = await _client.PostAsJsonAsync($"{Route}/drafts/record/{edited.Id}/confirm",
            new ConfirmResponsibilityDraftRequest(refreshed.Draft!.Revision, refreshed.SourceHash, true, true,
                "Reviewed refreshed source and proposal before acceptance."));

        // Assert
        refresh.StatusCode.Should().Be(HttpStatusCode.OK);
        refreshed.Draft.Values["customer"].Value.Should().Be("Human-reviewed synthetic duty.");
        refreshed.Draft.Values["customer"].UserEdited.Should().BeTrue();
        refreshed.Draft.Suggestion.Values["customer"].Value.Should().Be("Refreshed synthetic proposal.");
        refreshed.Draft.Status.Should().Be("ComparisonRequired");
        confirmation.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task DraftConfirmationRequiresApplicableProviderDutyFields()
    {
        // Arrange
        await AddBaselineAsync();
        var prepared = await _client.PostAsJsonAsync($"{Route}/drafts/AU-6/prepare",
            new PrepareResponsibilityDraftRequest(null, 0, Generate: false));
        var context = (await prepared.Content.ReadFromJsonAsync<ResponsibilityDraftContext>())!;
        var values = context.Draft!.Values.ToDictionary(x => x.Key, x => x.Value.Value);
        values["allocation"] = "Inherited";
        values["provider"] = "Synthetic provider from a human-reviewed record.";
        values["basis"] = "Human-reviewed provider responsibility basis.";
        var savedResponse = await _client.PutAsJsonAsync($"{Route}/drafts/record/{context.Draft.Id}",
            new SaveResponsibilityDraftRequest(context.Draft.Revision, values));
        var saved = (await savedResponse.Content.ReadFromJsonAsync<ResponsibilityDraftResponse>())!;

        // Act
        var response = await _client.PostAsJsonAsync($"{Route}/drafts/record/{saved.Id}/confirm",
            new ConfirmResponsibilityDraftRequest(saved.Revision, saved.SourceHash, true, true,
                "Acknowledged without complete provider duty fields."));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await using var verify = new AtoCopilotContext(_options);
        (await verify.ControlInheritances.CountAsync()).Should().Be(0);
        (await verify.Set<ResponsibilityDraft>().SingleAsync()).Status.Should().Be("Proposed");
    }

    [Fact]
    public async Task DraftRoutesHideForeignRecordsAndDenyReadOnlyPreparation()
    {
        // Arrange
        await AddBaselineAsync();
        var foreign = new ResponsibilityDraft
        {
            TenantId = Guid.NewGuid(),
            RegisteredSystemId = _system,
            ControlId = "AU-6",
            PreparedBy = "another tenant"
        };
        await using (var db = new AtoCopilotContext(_options))
        {
            db.Add(foreign);
            var assignment = await db.SystemRoleAssignments.SingleAsync();
            assignment.Role = OrganizationRole.SystemOwner;
            await db.SaveChangesAsync();
        }

        // Act
        var context = await _client.GetFromJsonAsync<ResponsibilityDraftContext>($"{Route}/drafts/AU-6");
        var history = await _client.GetAsync($"{Route}/drafts/record/{foreign.Id}/history");
        var prepare = await _client.PostAsJsonAsync($"{Route}/drafts/AU-6/prepare",
            new PrepareResponsibilityDraftRequest(null, 0));
        var confirm = await _client.PostAsJsonAsync($"{Route}/drafts/record/{foreign.Id}/confirm",
            new ConfirmResponsibilityDraftRequest(1, "", true, true, "Unauthorized confirmation."));

        // Assert
        context.Should().NotBeNull();
        context!.Draft.Should().BeNull();
        history.StatusCode.Should().Be(HttpStatusCode.NotFound);
        prepare.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        confirm.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _draftGenerator.Verify(x => x.GenerateAsync(It.IsAny<string>(),
            It.IsAny<IReadOnlyList<ResponsibilityDraftSource>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DraftConfirmationRollsBackWhenSourcesChangeDuringTheWrite()
    {
        // Arrange
        await AddBaselineAsync();
        var prepared = await _client.PostAsJsonAsync($"{Route}/drafts/AU-6/prepare",
            new PrepareResponsibilityDraftRequest(null, 0, Generate: false));
        var context = (await prepared.Content.ReadFromJsonAsync<ResponsibilityDraftContext>())!;
        var values = context.Draft!.Values.ToDictionary(x => x.Key, x => x.Value.Value);
        values["allocation"] = "Customer";
        values["customer"] = "Human-reviewed synthetic customer duty.";
        values["basis"] = "Human-reviewed responsibility basis.";
        var save = await _client.PutAsJsonAsync($"{Route}/drafts/record/{context.Draft.Id}",
            new SaveResponsibilityDraftRequest(context.Draft.Revision, values));
        var edited = (await save.Content.ReadFromJsonAsync<ResponsibilityDraftResponse>())!;
        await using (var db = new AtoCopilotContext(_options))
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER ChangeSystemDuringResponsibility AFTER INSERT ON ControlInheritances
                BEGIN
                  UPDATE RegisteredSystems SET Description='Changed during responsibility confirmation'
                  WHERE Id=(SELECT RegisteredSystemId FROM ControlBaselines WHERE Id=NEW.ControlBaselineId);
                END;
                """);

        // Act
        var response = await _client.PostAsJsonAsync($"{Route}/drafts/record/{edited.Id}/confirm",
            new ConfirmResponsibilityDraftRequest(edited.Revision, edited.SourceHash, true, true,
                "Reviewed sources before the write."));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await using var verify = new AtoCopilotContext(_options);
        (await verify.ControlInheritances.CountAsync()).Should().Be(0);
        (await verify.Set<ResponsibilityDraft>().SingleAsync()).Status.Should().Be("Proposed");
    }

    [Fact]
    public async Task DraftGeneration_CannotOverridePublishedProviderResponsibilitySplit()
    {
        // Arrange
        await AddBaselineAsync();
        var assignmentId = Guid.NewGuid();
        var offeringId = Guid.NewGuid();
        var hostingId = Guid.NewGuid();
        var releaseId = Guid.NewGuid();
        var duties = new ProviderScopeCapabilityDuties(_capability, "Synthetic audit logging",
            "Published control split", releaseId, 1, "synthetic-release", "synthetic-content",
            Guid.NewGuid(), [], ["AU-6"], []);
        var providerScope = new SystemProviderScope(assignmentId, 1, null, offeringId,
            "Synthetic responsibility offering", "Synthetic CSP", hostingId, "Reviewed synthetic scope",
            "Active", "Reviewed", false, [], 1)
        {
            PublishedDuties = new("Available", [duties], null)
        };
        _draftEnvironments.Setup(x => x.ListAsync(_system, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemEnvironmentsResponse(_system, 1,
                new(false, false, false, false, false), [], []) { ProviderScopes = [providerScope] });
        _draftGenerator.Setup(x => x.GenerateAsync("AU-6",
                It.IsAny<IReadOnlyList<ResponsibilityDraftSource>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResponsibilityDraftSuggestion(new()
            {
                ["allocation"] = new("Customer", "AI proposed", ["system"]),
                ["customer"] = new("AI customer suggestion", "AI proposed", ["system"]),
                ["basis"] = new("AI rationale", "AI proposed", ["system"])
            }, [], []));

        // Act
        var response = await _client.PostAsJsonAsync($"{Route}/drafts/AU-6/prepare",
            new PrepareResponsibilityDraftRequest(assignmentId, 0, Generate: true));
        var context = (await response.Content.ReadFromJsonAsync<ResponsibilityDraftContext>())!;

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        context.Draft.Should().NotBeNull();
        context.Draft!.Values["allocation"].Value.Should().Be("Shared");
        context.Draft.Values["allocation"].Origin.Should().Be("From provider source");
        context.Draft.Values["customer"].Value.Should().Be("AI customer suggestion");
        context.Draft.GenerationState.Should().Be("Prepared");
    }

    [Fact]
    public async Task DraftGenerationFailureReturnsServiceUnavailableAndPreservesSavedHumanValues()
    {
        // Arrange
        await AddBaselineAsync();
        var route = $"{Route}/drafts/AU-6";
        var prepare = await _client.PostAsJsonAsync(route + "/prepare",
            new PrepareResponsibilityDraftRequest(null, 0, Generate: false));
        var initial = (await prepare.Content.ReadFromJsonAsync<ResponsibilityDraftContext>())!;
        var values = initial.Draft!.Values.ToDictionary(x => x.Key, x => x.Value.Value);
        values["allocation"] = "Customer";
        values["customer"] = "Human-reviewed synthetic customer duty.";
        values["basis"] = "Human-reviewed responsibility basis.";
        var save = await _client.PutAsJsonAsync($"{Route}/drafts/record/{initial.Draft.Id}",
            new SaveResponsibilityDraftRequest(initial.Draft.Revision, values));
        var saved = (await save.Content.ReadFromJsonAsync<ResponsibilityDraftResponse>())!;
        _draftGenerator.Setup(x => x.GenerateAsync("AU-6",
                It.IsAny<IReadOnlyList<ResponsibilityDraftSource>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ResponsibilityGenerationException("Synthetic model unavailable."));

        // Act
        var failed = await _client.PostAsJsonAsync(route + "/prepare",
            new PrepareResponsibilityDraftRequest(null, saved.Revision, Generate: true));
        var refreshed = await _client.GetFromJsonAsync<ResponsibilityDraftContext>(route);

        // Assert
        failed.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        refreshed.Should().NotBeNull();
        refreshed!.Draft.Should().NotBeNull();
        refreshed.Draft!.GenerationState.Should().Be("Failed");
        refreshed.Draft.GenerationError.Should().Be("Synthetic model unavailable.");
        refreshed.Draft.Values["customer"].Value.Should().Be("Human-reviewed synthetic customer duty.");
        refreshed.Draft.Values["customer"].UserEdited.Should().BeTrue();
        refreshed.Draft.Status.Should().Be("Proposed");
    }

    [Fact]
    public async Task DraftConfirmation_ProviderScopeUsesCurrentAdoptionAndPersistsCanonicalSubscriptionSource()
    {
        // Arrange
        await AddBaselineAsync();
        (await _client.PostAsJsonAsync(Route, new { capabilityId = _capability })).EnsureSuccessStatusCode();
        Guid assignmentId;
        Guid offeringId;
        Guid hostingId;
        Guid releaseId;
        await using (var db = new AtoCopilotContext(_options))
        {
            var subscription = await db.CapabilitySubscriptions.SingleAsync();
            var providerId = await db.CspProfiles.Select(x => x.Id).SingleAsync();
            var offering = new ProviderOffering
            {
                ProviderId = providerId,
                Name = "Synthetic responsibility offering",
                CreatedBy = "test"
            };
            offering.OfferingId = offering.Id;
            var hosting = new ProviderHostingScopeRevision
            {
                ProviderId = providerId,
                OfferingId = offering.Id,
                CreatedBy = "test"
            };
            var assignment = new ProviderHostingAssignment
            {
                ProviderId = providerId,
                OfferingId = offering.Id,
                HostingScopeRevisionId = hosting.Id,
                TargetTenantId = _tenant,
                SystemId = _system,
                CreatedBy = "test"
            };
            var review = new ProviderAuthorizationImpactReview
            {
                ProviderId = providerId,
                OfferingId = offering.Id,
                CreatedBy = "test"
            };
            var sourceContext = new ProviderCatalogContextSnapshot
            {
                ProviderId = providerId,
                OfferingId = offering.Id,
                ImpactReviewId = review.Id,
                CreatedBy = "test"
            };
            releaseId = Guid.NewGuid();
            var adoption = new CapabilityAdoptionSnapshot
            {
                TenantId = _tenant,
                SystemId = _system,
                SubscriptionId = subscription.Id,
                AssignmentId = assignment.Id,
                AssignmentRevision = assignment.Revision,
                ProviderId = providerId,
                OfferingId = offering.Id,
                CapabilityId = _capability,
                ReleaseId = releaseId,
                ContextSnapshotId = sourceContext.Id,
                CreatedBy = "test"
            };
            db.AddRange(offering, hosting, assignment, review, sourceContext, adoption);
            await db.SaveChangesAsync();
            subscription.CurrentAdoptionSnapshotId = adoption.Id;
            subscription.AdoptionSelectionRevision = 1;
            await db.SaveChangesAsync();
            assignmentId = assignment.Id;
            offeringId = offering.Id;
            hostingId = hosting.Id;
        }

        var duties = new ProviderScopeCapabilityDuties(_capability, "Synthetic audit logging",
            "Published control split", releaseId, 1, "synthetic-release", "synthetic-content",
            Guid.NewGuid(), [], ["AU-6"], []);
        var providerScope = new SystemProviderScope(assignmentId, 1, null, offeringId,
            "Synthetic responsibility offering", "Synthetic CSP", hostingId, "Reviewed synthetic scope",
            "Active", "Reviewed", false, [], 1)
        {
            PublishedDuties = new("Available", [duties], null)
        };
        _draftEnvironments.Setup(x => x.ListAsync(_system, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemEnvironmentsResponse(_system, 1,
                new(false, false, false, false, false), [], []) { ProviderScopes = [providerScope] });

        var prepareResponse = await _client.PostAsJsonAsync($"{Route}/drafts/AU-6/prepare",
            new PrepareResponsibilityDraftRequest(assignmentId, 0, Generate: false));
        prepareResponse.StatusCode.Should().Be(HttpStatusCode.OK, await prepareResponse.Content.ReadAsStringAsync());
        var context = (await prepareResponse.Content.ReadFromJsonAsync<ResponsibilityDraftContext>())!;
        var values = context.Draft!.Values.ToDictionary(x => x.Key, x => x.Value.Value);
        values["allocation"] = "Shared";
        values["customer"] = "Customer reviews synthetic audit events.";
        values["basis"] = "Reviewed system-specific provider and customer responsibilities.";
        values["providerDuties"] = "Provider records synthetic audit events.";
        var saveResponse = await _client.PutAsJsonAsync($"{Route}/drafts/record/{context.Draft.Id}",
            new SaveResponsibilityDraftRequest(context.Draft.Revision, values));
        saveResponse.StatusCode.Should().Be(HttpStatusCode.OK, await saveResponse.Content.ReadAsStringAsync());
        var draft = (await saveResponse.Content.ReadFromJsonAsync<ResponsibilityDraftResponse>())!;

        // Act
        var response = await _client.PostAsJsonAsync($"{Route}/drafts/record/{draft.Id}/confirm",
            new ConfirmResponsibilityDraftRequest(draft.Revision, draft.SourceHash, true, true,
                "Reviewed the current adopted provider scope, coverage and customer duties."));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var accepted = (await response.Content.ReadFromJsonAsync<ResponsibilityDraftResponse>())!;
        accepted.Status.Should().Be("Accepted");
        await using var verify = new AtoCopilotContext(_options);
        var inheritance = await verify.ControlInheritances.SingleAsync(x => x.ControlId == "AU-6");
        inheritance.InheritanceType.Should().Be(InheritanceType.Shared);
        inheritance.DesignationSource.Should().Be("CspSubscription");
        inheritance.CustomerResponsibility.Should().Be(values["customer"]);
        var subscriptionAfter = await verify.CapabilitySubscriptions.SingleAsync();
        subscriptionAfter.CurrentAdoptionSnapshotId.Should().NotBeNull();
        (await verify.Set<CapabilityResponsibilityConfirmation>()
            .AnyAsync(x => x.ControlId == "AU-6" && x.IsCurrent)).Should().BeTrue();
    }
}
