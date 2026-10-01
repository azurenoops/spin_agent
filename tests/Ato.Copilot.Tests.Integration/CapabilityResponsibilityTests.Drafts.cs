using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Integration;

public sealed partial class CapabilityResponsibilityTests
{
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
            db.ControlImplementations.Add(new() { TenantId = _tenant, RegisteredSystemId = _system, ControlId = "AU-6",
                Narrative = "Human approved text", ApprovalStatus = SspSectionStatus.Approved,
                ImplementationStatus = ImplementationStatus.Planned });
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
}
