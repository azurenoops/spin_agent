using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Microsoft.EntityFrameworkCore;
using Moq;
using Ato.Copilot.Agents.Compliance.Services;
using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Integration;

public sealed partial class CapabilityResponsibilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnvironmentContext_UsesRecordedControlScope_NotListOrder(bool removed)
    {
        // Arrange
        await AddBaselineAsync();
        var correct = Guid.NewGuid();
        ProviderScopeCapabilityDuties Duty(string control) => new(_capability, "Recorded capability", "Recorded statement",
            Guid.NewGuid(), 1, "release", "content", Guid.NewGuid(), [], [control], []);
        SystemProviderScope Scope(Guid id, string control, string state = "Active") => new(id, 1, null,
            Guid.NewGuid(), "Recorded offering", "Recorded provider", Guid.NewGuid(), "Recorded scope",
            state, "Undetermined", false, [], 1) { PublishedDuties = new("Available", [Duty(control)], null) };
        _draftEnvironments.Setup(x => x.ListAsync(_system, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemEnvironmentsResponse(_system, 1, new(false, false, false, false, false), [], [])
                { ProviderScopes = [Scope(Guid.NewGuid(), "AC-2"), Scope(correct, "AU-6", removed ? "Removed" : "Active")] });
        _draftEnvironments.Setup(x => x.ProviderScopeChoicesAsync(_system, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemProviderScopeChoicesResponse(_system, 1, false, []));
        // Act
        var response = await _client.GetAsync($"{Route}/drafts/AU-6?useEnvironment=true");
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<ResponsibilityDraftContext>())!;
        result.ScopeId.Should().Be(removed ? null : correct);
        if (!removed) result.SourceValues["allocation"].Value.Should().Be("Shared");
    }

    private void DraftAi(string allocation = "Customer", string customer = "Review local audit retention.")
    {
        _draftGenerator.Setup(x => x.GenerateAsync(It.IsAny<string>(),
                It.IsAny<IReadOnlyList<ResponsibilityDraftSource>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResponsibilityDraftSuggestion(new()
            {
                ["allocation"] = new(allocation, "AI proposed", ["system"], "Synthetic grounded proposal."),
                ["customer"] = new(customer, "AI proposed", ["technical"]),
                ["basis"] = new("Review scoped source evidence.", "AI proposed", ["policy"]),
            }, [], []));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnvironmentContext_RequiresUniqueRecordedApplicability_AndHonorsCapability(bool identifyCapability)
    {
        // Arrange
        await AddBaselineAsync();
        SystemProviderScope Scope(Guid capability) => new(Guid.NewGuid(), 1, null, Guid.NewGuid(),
            "Offering", "Provider", Guid.NewGuid(), "Recorded scope", "Active", "Undetermined", false, [], 1)
            { PublishedDuties = new("Available", [new(capability, "Capability", "Published statement", Guid.NewGuid(),
                1, "release", "content", Guid.NewGuid(), [], ["AU-6"], [])], null) };
        var intended = Scope(_capability);
        _draftEnvironments.Setup(x => x.ListAsync(_system, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemEnvironmentsResponse(_system, 1, new(false, false, false, false, false), [], [])
                { ProviderScopes = [Scope(Guid.NewGuid()), intended] });
        _draftEnvironments.Setup(x => x.ProviderScopeChoicesAsync(_system, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemProviderScopeChoicesResponse(_system, 1, false, []));
        // Act
        var result = await _client.GetFromJsonAsync<ResponsibilityDraftContext>(
            $"{Route}/drafts/AU-6?useEnvironment=true{(identifyCapability ? $"&capabilityId={_capability}" : "")}");
        // Assert
        result!.ScopeId.Should().Be(identifyCapability ? intended.AssignmentId : null);
        if (identifyCapability) result.EnvironmentScopeIssue.Should().BeNull();
        else result.EnvironmentScopeIssue.Should().Contain("Several");
        _draftGenerator.Verify(x => x.GenerateAsync(It.IsAny<string>(),
            It.IsAny<IReadOnlyList<ResponsibilityDraftSource>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WholeControlFirstPass_WithRecordedProviderContributions_DoesNotReplaceTheirSplitWithAiCustomerOwnership()
    {
        // Arrange
        await AddBaselineAsync();
        DraftAi("Customer");
        await using (var db = new AtoCopilotContext(_options))
        {
            db.ControlImplementations.Add(new() { TenantId = _tenant, RegisteredSystemId = _system,
                ControlId = "AU-6", Narrative = "Recorded local audit operating duties." });
            await db.SaveChangesAsync();
        }
        var scope = new SystemProviderScope(Guid.NewGuid(), 1, null, Guid.NewGuid(), "Offering", "Provider",
            Guid.NewGuid(), "Recorded scope", "Active", "Undetermined", false, [], 1)
            { PublishedDuties = new("Available", [new(_capability, "Capability", "Published source", Guid.NewGuid(),
                1, "release", "content", Guid.NewGuid(), [], ["AU-6"], [])], null) };
        _draftEnvironments.Setup(x => x.ListAsync(_system, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemEnvironmentsResponse(_system, 1, new(false, false, false, false, false), [], [])
                { ProviderScopes = [scope, scope with { AssignmentId = Guid.NewGuid() }] });
        // Act
        var result = await PrepareDraft();
        // Assert
        result.Draft!.Values["allocation"].Value.Should().Be("NeedsConfirmation");
        result.Draft.Values["provider"].Value.Should().BeEmpty();
        result.Draft.Values["customer"].Value.Should().Be("Review local audit retention.");
    }
    private async Task<ResponsibilityDraftContext> PrepareDraft(Guid? scope = null, long revision = 0)
    {
        var response = await _client.PostAsJsonAsync($"{Route}/drafts/AU-6/prepare",
            new PrepareResponsibilityDraftRequest(scope, revision));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ResponsibilityDraftContext>())!;
    }
    private async Task<ResponsibilityDraftResponse> EditDraft(ResponsibilityDraftContext context, string customer, string allocation = "Customer")
    {
        var values = context.Draft!.Values.ToDictionary(x => x.Key, x => x.Value.Value);
        values["allocation"] = allocation; values["customer"] = customer; values["basis"] = "Human reviewed local responsibilities.";
        if (allocation != "Customer") values["provider"] = "Human verified provider";
        if (allocation != "Customer") values["providerDuties"] = "Human reviewed provider duties.";
        if (allocation == "Inherited")
        {
            values["scope"] = "Reviewed synthetic scope";
            values["exclusions"] = "No exclusions in the reviewed synthetic source";
            values["source"] = "Synthetic source revision 1";
        }
        var response = await _client.PutAsJsonAsync($"{Route}/drafts/record/{context.Draft.Id}",
            new SaveResponsibilityDraftRequest(context.Draft.Revision, values));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ResponsibilityDraftResponse>())!;
    }

    [Fact]
    public async Task DraftContext_WithoutProvider_DoesNotInventAnAllocation()
    {
        // Arrange
        await AddBaselineAsync();

        // Act
        var response = await _client.GetAsync($"{Route}/drafts/AU-6");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("controlId").GetString().Should().Be("AU-6");
        body.GetProperty("draft").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("sourceValues").GetProperty("allocation").GetProperty("value").GetString()
            .Should().Be("NeedsConfirmation");
        body.GetProperty("sourceValues").GetProperty("provider").GetProperty("value").GetString()
            .Should().BeEmpty();
    }

    [Fact]
    public async Task DraftGeneration_InsufficientSystemData_DoesNotAssumeCustomer()
    {
        // Arrange
        await AddBaselineAsync();
        DraftAi();
        // Act
        var context = await PrepareDraft();
        // Assert
        context.Draft!.Values["allocation"].Value.Should().Be("NeedsConfirmation");
        context.Draft.Values["provider"].Value.Should().BeEmpty();
        await using var db = new AtoCopilotContext(_options);
        (await db.ControlInheritances.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData("Provider", "Inherited")]
    [InlineData("Shared", "Shared")]
    [InlineData("Customer", "Customer")]
    [InlineData("Conflict", "NeedsConfirmation")]
    public async Task DraftGeneration_PublishedSplitIsNotOverriddenByAi(string published, string expected)
    {
        // Arrange
        await AddBaselineAsync();
        DraftAi("Inherited");
        var assignment = Guid.NewGuid();
        var hosting = Guid.NewGuid();
        var offering = Guid.NewGuid();
        var duty = new ProviderScopeCapabilityDuties(_capability, "Published synthetic capability", "Scope description",
            Guid.NewGuid(), 7, "release-hash", "content-hash", Guid.NewGuid(),
            published is "Provider" or "Conflict" ? ["AU-6"] : [], published is "Shared" or "Conflict" ? ["AU-6"] : [], published == "Customer" ? ["AU-6"] : []);
        var scope = new SystemProviderScope(assignment, 1, null, offering, "Offering", "Named provider", hosting,
            "Reviewed scope", "Active", "Undetermined", true, [], 1)
            { PublishedDuties = new("Available", [duty], null) };
        _draftEnvironments.Setup(x => x.ListAsync(_system, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemEnvironmentsResponse(_system, 1, new(false, false, false, false, false), [], [])
                { ProviderScopes = [scope] });
        _draftEnvironments.Setup(x => x.ProviderScopeChoicesAsync(_system, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemProviderScopeChoicesResponse(_system, 1, false, []));
        // Act
        var context = await PrepareDraft(assignment);
        // Assert
        context.Draft!.Values["allocation"].Value.Should().Be(expected);
        context.Draft.Values["allocation"].Origin.Should().Be("From provider source");
        context.Draft.Status.Should().Be("Proposed");
        context.Draft.Suggestion.Questions.Should().Contain(x => x.Contains("applicability"));
    }

    [Fact]
    public async Task DraftRefresh_PreservesEdits_AndRequiresExplicitComparison()
    {
        // Arrange
        await AddBaselineAsync();
        DraftAi();
        var original = await PrepareDraft();
        var edited = await EditDraft(original, "Human corrected duties.");
        DraftAi(customer: "New AI suggestion.");
        // Act
        var refreshed = await PrepareDraft(revision: edited.Revision);
        // Assert
        refreshed.Draft!.Values["customer"].Value.Should().Be("Human corrected duties.");
        refreshed.Draft.Values["customer"].UserEdited.Should().BeTrue();
        refreshed.Draft.Suggestion.Values["customer"].Value.Should().Be("New AI suggestion.");
        refreshed.Draft.Status.Should().Be("ComparisonRequired");
        var confirm = await _client.PostAsJsonAsync($"{Route}/drafts/record/{edited.Id}/confirm",
            new ConfirmResponsibilityDraftRequest(refreshed.Draft.Revision, refreshed.SourceHash, true, true, "Reviewed."));
        confirm.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task DraftFailure_Returns503AndPreservesPreviouslySavedContent()
    {
        // Arrange
        await AddBaselineAsync(); DraftAi();
        var first = await PrepareDraft();
        var edited = await EditDraft(first, "Retain this human correction.");
        _draftGenerator.Setup(x => x.GenerateAsync(It.IsAny<string>(),
                It.IsAny<IReadOnlyList<ResponsibilityDraftSource>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ResponsibilityGenerationException("Synthetic generation failure."));
        // Act
        var failed = await _client.PostAsJsonAsync($"{Route}/drafts/AU-6/prepare",
            new PrepareResponsibilityDraftRequest(null, edited.Revision));
        var reread = (await _client.GetFromJsonAsync<ResponsibilityDraftContext>($"{Route}/drafts/AU-6"))!;
        // Assert
        failed.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        reread.Draft!.Values["customer"].Value.Should().Be("Retain this human correction.");
        reread.Draft.GenerationState.Should().Be("Failed");
    }

    [Theory]
    [InlineData("Customer")]
    [InlineData("Shared")]
    [InlineData("Inherited")]
    public async Task DraftConfirmation_SystemOnly_PreservesImplementationStatusAndReviewHistory(string allocation)
    {
        // Arrange
        await AddBaselineAsync(); DraftAi();
        await using (var db = new AtoCopilotContext(_options))
        {
            var seededImplementation = new ControlImplementation { TenantId = _tenant, RegisteredSystemId = _system, ControlId = "AU-6",
                Narrative = "Human approved text", TechnicalNarrative = "Human approved text", ApprovalStatus = SspSectionStatus.Approved,
                ImplementationStatus = ImplementationStatus.Planned };
            var approved = new NarrativeVersion { TenantId = _tenant, ControlImplementationId = seededImplementation.Id,
                Content = "Human approved text", Status = SspSectionStatus.Approved, AuthoredBy = "fixture-author" };
            db.AddRange(seededImplementation, approved);
            await db.SaveChangesAsync();
            seededImplementation.ApprovedVersionId = approved.Id;
            await db.SaveChangesAsync();
        }
        var first = await PrepareDraft();
        var edited = await EditDraft(first, "Human confirmed duties.", allocation);
        // Act
        var response = await _client.PostAsJsonAsync($"{Route}/drafts/record/{edited.Id}/confirm",
            new ConfirmResponsibilityDraftRequest(edited.Revision, first.SourceHash, true, true, "Human reviewed source."));
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        await using var verify = new AtoCopilotContext(_options);
        var implementation = await verify.ControlImplementations.SingleAsync();
        implementation.Narrative.Should().Be("Human approved text");
        implementation.ImplementationStatus.Should().Be(ImplementationStatus.Planned);
        implementation.ApprovalStatus.Should().Be(SspSectionStatus.Approved);
        (await verify.ControlInheritances.SingleAsync()).CustomerResponsibility.Should().Be("Human confirmed duties.");
        (await verify.Set<ResponsibilityDraftHistory>().CountAsync()).Should().Be(3);
        var scopes = _app.Services.GetRequiredService<IServiceScopeFactory>();
        var crm = await new BaselineService(scopes, Mock.Of<IReferenceDataService>(),
            NullLogger<BaselineService>.Instance, Mock.Of<IOrgInheritanceService>()).GenerateCrmAsync(_system);
        crm.FamilyGroups.SelectMany(x => x.Controls).Single(x => x.ControlId == "AU-6")
            .CustomerResponsibility.Should().Be("Human confirmed duties.");
        var ssp = await new SspService(scopes, NullLogger<SspService>.Instance).GenerateSspAsync(_system, sections: ["controls"]);
        ssp.Content.Should().Contain($"**Responsibility**: {allocation}").And.Contain("Human approved text");
        var exporter = new EmassExportService(scopes, NullLogger<EmassExportService>.Instance, Mock.Of<IOscalSspExportService>());
        using var workbook = new XLWorkbook(new MemoryStream(await exporter.ExportControlsAsync(_system)));
        var exportRow = workbook.Worksheet("Controls").RowsUsed().Single(x => x.Cell(5).GetString() == "AU-6");
        exportRow.Cell(11).GetString().Should().Be(allocation == "Customer" ? "System-Specific" : allocation);
        exportRow.Cell(12).GetString().Should().Be("Not Assessed");
        var reread = (await _client.GetFromJsonAsync<ResponsibilityDraftContext>($"{Route}/drafts/AU-6"))!;
        reread.Draft!.IsStale.Should().BeFalse();
    }

    [Fact]
    public async Task DraftConcurrencyAndSourceChanges_AreRejectedWithoutLosingEdits()
    {
        // Arrange
        await AddBaselineAsync(); DraftAi();
        var first = await PrepareDraft();
        var edited = await EditDraft(first, "Preserved duties.");
        // Act
        var duplicate = await _client.PutAsJsonAsync($"{Route}/drafts/record/{edited.Id}",
            new SaveResponsibilityDraftRequest(first.Draft!.Revision, first.Draft.Values.ToDictionary(x => x.Key, x => x.Value.Value)));
        await using (var db = new AtoCopilotContext(_options))
        {
            (await db.RegisteredSystems.SingleAsync()).Description = "Changed system description";
            await db.SaveChangesAsync();
        }
        var response = await _client.PostAsJsonAsync($"{Route}/drafts/record/{edited.Id}/confirm",
            new ConfirmResponsibilityDraftRequest(edited.Revision, first.SourceHash, true, true, "Reviewed."));
        // Assert
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var reread = (await _client.GetFromJsonAsync<ResponsibilityDraftContext>($"{Route}/drafts/AU-6"))!;
        reread.Draft!.IsStale.Should().BeTrue();
        reread.Draft.Values["customer"].Value.Should().Be("Preserved duties.");
    }

    [Fact]
    public async Task DraftRoutes_DenyForeignDraftsAndReaderMutations()
    {
        // Arrange
        await AddBaselineAsync();
        var foreign = new ResponsibilityDraft { TenantId = Guid.NewGuid(), RegisteredSystemId = _system,
            ControlId = "AU-6", PreparedBy = "other-tenant" };
        await using (var db = new AtoCopilotContext(_options))
        {
            db.Add(foreign);
            var assignment = await db.SystemRoleAssignments.SingleAsync();
            assignment.Role = OrganizationRole.SystemOwner;
            await db.SaveChangesAsync();
        }
        // Act
        var read = await _client.GetAsync($"{Route}/drafts/AU-6");
        var history = await _client.GetAsync($"{Route}/drafts/record/{foreign.Id}/history");
        var prepare = await _client.PostAsJsonAsync($"{Route}/drafts/AU-6/prepare", new PrepareResponsibilityDraftRequest(null, 0));
        var confirm = await _client.PostAsJsonAsync($"{Route}/drafts/record/{foreign.Id}/confirm",
            new ConfirmResponsibilityDraftRequest(1, "", true, true, "Unauthorized"));
        // Assert
        read.StatusCode.Should().Be(HttpStatusCode.OK);
        history.StatusCode.Should().Be(HttpStatusCode.NotFound);
        prepare.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        confirm.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _draftGenerator.Verify(x => x.GenerateAsync(It.IsAny<string>(),
            It.IsAny<IReadOnlyList<ResponsibilityDraftSource>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DraftGeneration_DoesNotOverwriteAnEditCommittedDuringModelCall()
    {
        // Arrange
        await AddBaselineAsync(); DraftAi();
        var first = await PrepareDraft();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<ResponsibilityDraftSuggestion>(TaskCreationOptions.RunContinuationsAsynchronously);
        _draftGenerator.Setup(x => x.GenerateAsync(It.IsAny<string>(),
                It.IsAny<IReadOnlyList<ResponsibilityDraftSource>>(), It.IsAny<CancellationToken>()))
            .Returns(() => { started.SetResult(); return release.Task; });
        // Act
        var refresh = _client.PostAsJsonAsync($"{Route}/drafts/AU-6/prepare", new PrepareResponsibilityDraftRequest(null, 1));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await EditDraft(first, "Concurrent human correction.");
        release.SetResult(new(new(), [], []));
        var response = await refresh;
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var current = (await _client.GetFromJsonAsync<ResponsibilityDraftContext>($"{Route}/drafts/AU-6"))!;
        current.Draft!.Values["customer"].Value.Should().Be("Concurrent human correction.");
    }

    [Fact]
    public async Task DraftConfirmation_RequiresApplicableDuties_NotJustAcknowledgements()
    {
        // Arrange
        await AddBaselineAsync(); DraftAi();
        var first = await PrepareDraft();
        var values = first.Draft!.Values.ToDictionary(x => x.Key, x => x.Value.Value);
        values["allocation"] = "Inherited"; values["provider"] = "Human selected provider";
        var saved = await _client.PutAsJsonAsync($"{Route}/drafts/record/{first.Draft.Id}", new SaveResponsibilityDraftRequest(1, values));
        saved.EnsureSuccessStatusCode();
        // Act
        var response = await _client.PostAsJsonAsync($"{Route}/drafts/record/{first.Draft.Id}/confirm",
            new ConfirmResponsibilityDraftRequest(2, first.SourceHash, true, true, "Acknowledged without complete duty fields"));
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DraftConfirmation_DoesNotBlessUnreviewedSourceChangesDuringTheWrite()
    {
        // Arrange
        await AddBaselineAsync(); DraftAi();
        var first = await PrepareDraft();
        var edited = await EditDraft(first, "Reviewed duties.");
        await using (var db = new AtoCopilotContext(_options))
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER ChangeSystemDuringResponsibility AFTER INSERT ON ControlInheritances
                BEGIN
                  UPDATE RegisteredSystems SET Description='Changed during confirmation'
                  WHERE Id=(SELECT RegisteredSystemId FROM ControlBaselines WHERE Id=NEW.ControlBaselineId);
                END;
                """);
        // Act
        var response = await _client.PostAsJsonAsync($"{Route}/drafts/record/{edited.Id}/confirm",
            new ConfirmResponsibilityDraftRequest(edited.Revision, first.SourceHash, true, true, "Reviewed before change"));
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await using var verify = new AtoCopilotContext(_options);
        (await verify.ControlInheritances.CountAsync()).Should().Be(0);
        (await verify.Set<ResponsibilityDraft>().SingleAsync()).Status.Should().Be("Proposed");
    }

    [Fact]
    public async Task DraftConfirmation_ProviderScope_UsesCanonicalSubscriptionReconciliation()
    {
        // Arrange
        await AddBaselineAsync(); DraftAi("Shared");
        (await _client.PostAsJsonAsync(Route, new { capabilityId = _capability })).EnsureSuccessStatusCode();
        var assignment = Guid.NewGuid();
        var offering = Guid.NewGuid();
        var hosting = Guid.NewGuid();
        var release = Guid.NewGuid();
        await using (var db = new AtoCopilotContext(_options))
        {
            var subscription = await db.CapabilitySubscriptions.SingleAsync();
            var provider = await db.CspProfiles.Select(x => x.Id).SingleAsync();
            var offeringRecord = new Ato.Copilot.Core.Models.ProviderAuthorizations.ProviderOffering {
                Id = offering, ProviderId = provider, OfferingId = offering, Name = "Synthetic offering", CreatedBy = "fixture" };
            var hostingRecord = new Ato.Copilot.Core.Models.ProviderAuthorizations.ProviderHostingScopeRevision {
                Id = hosting, ProviderId = provider, OfferingId = offering, CreatedBy = "fixture" };
            var assignmentRecord = new Ato.Copilot.Core.Models.ProviderAuthorizations.ProviderHostingAssignment {
                Id = assignment, ProviderId = provider, OfferingId = offering, HostingScopeRevisionId = hosting,
                TargetTenantId = _tenant, SystemId = _system, CreatedBy = "fixture" };
            var review = new Ato.Copilot.Core.Models.ProviderAuthorizations.ProviderAuthorizationImpactReview {
                ProviderId = provider, OfferingId = offering, CreatedBy = "fixture" };
            var sourceContext = new Ato.Copilot.Core.Models.ProviderAuthorizations.ProviderCatalogContextSnapshot {
                ProviderId = provider, OfferingId = offering, ImpactReviewId = review.Id, CreatedBy = "fixture" };
            db.AddRange(offeringRecord, hostingRecord, assignmentRecord, review, sourceContext);
            var adoption = new Ato.Copilot.Core.Models.ProviderAuthorizations.CapabilityAdoptionSnapshot {
                TenantId = _tenant, SystemId = _system, SubscriptionId = subscription.Id, AssignmentId = assignment,
                ProviderId = provider, OfferingId = offering, CapabilityId = _capability, ReleaseId = release,
                ContextSnapshotId = sourceContext.Id, CreatedBy = "fixture" };
            db.Add(adoption);
            await db.SaveChangesAsync();
            subscription.CurrentAdoptionSnapshotId = adoption.Id;
            await db.SaveChangesAsync();
        }
        var duties = new ProviderScopeCapabilityDuties(_capability, "Synthetic scoped service", "Published scope",
            release, 1, "release", "content", Guid.NewGuid(), [], ["AU-6"], []);
        var scope = new SystemProviderScope(assignment, 1, null, offering, "Offering", "Source provider", hosting,
            "Reviewed scope", "Active", "Reviewed", false, [], 1) { PublishedDuties = new("Available", [duties], null) };
        _draftEnvironments.Setup(x => x.ListAsync(_system, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemEnvironmentsResponse(_system, 1, new(false, false, false, false, false), [], []) { ProviderScopes = [scope] });
        _draftEnvironments.Setup(x => x.ProviderScopeChoicesAsync(_system, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemProviderScopeChoicesResponse(_system, 1, false, []));
        var first = await PrepareDraft(assignment);
        var edited = await EditDraft(first, "Reviewed local duty.", "Shared");
        // Act
        var response = await _client.PostAsJsonAsync($"{Route}/drafts/record/{edited.Id}/confirm",
            new ConfirmResponsibilityDraftRequest(edited.Revision, first.SourceHash, true, true, "Scope and duties reviewed"));
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        await using var verify = new AtoCopilotContext(_options);
        (await verify.ControlInheritances.SingleAsync()).DesignationSource.Should().Be("CspSubscription");
        (await verify.Set<CapabilityResponsibilityConfirmation>().SingleAsync()).ReviewedBaselineId.Should().Be(first.BaselineId);
        (await verify.Set<ResponsibilityDraft>().SingleAsync()).Status.Should().Be("Accepted");
    }
}
