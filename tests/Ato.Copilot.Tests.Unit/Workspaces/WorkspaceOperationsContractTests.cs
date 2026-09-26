using Ato.Copilot.Core.Services.Workspaces;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Workspaces;

public sealed class WorkspaceOperationsContractTests
{
    [Fact]
    public void T010_CatalogQuery_ClampsPagingAndUsesStableSort()
    {
        // Arrange
        var query = new WorkspaceCatalogQuery(0, 500, "  key  ", "component", "name", "desc");

        // Act
        var normalized = query.Normalize();

        // Assert
        normalized.Page.Should().Be(1);
        normalized.PageSize.Should().Be(200);
        normalized.Search.Should().Be("key");
        normalized.Sort.Should().Be("name");
        normalized.Direction.Should().Be("desc");
    }

    [Fact]
    public void T011_WorkingRevision_RejectsDuplicateContributorsAndInvalidDuties()
    {
        // Arrange
        var request = new SaveWorkingRevisionRequest(
            1, "security", "platform", ["a", " a "], new Dictionary<string, string> { [""] = "Provider" });

        // Act
        var errors = WorkspaceContractValidator.Validate(request);

        // Assert
        errors.Should().Contain(["contributors must be unique", "control duty IDs are required"]);
    }

    [Fact]
    public void T012_PublishRequest_RequiresExactApprovedRevisionAndPreview()
    {
        // Arrange
        var request = new PublishWorkingRevisionRequest(4, 3, Guid.NewGuid(), "hash", "key");

        // Act
        var errors = WorkspaceContractValidator.Validate(request);

        // Assert
        errors.Should().Contain("approved revision must match the requested revision");
    }

    [Fact]
    public void T013_ImpactState_DoesNotConflateDeliveryAndCustomerReview()
    {
        // Arrange
        var impact = new ProviderReleaseImpactState("Delivered", "Pending", "Pending");

        // Act
        var completed = impact.IsCustomerComplete;

        // Assert
        completed.Should().BeFalse();
    }

    [Fact]
    public void T014_OrganizationQuery_ClampsPagingAndKeepsIndependentFilters()
    {
        // Arrange
        var query = new OrganizationCatalogQuery(2, 1000, "alpha", "Active", "Pending", "Overdue");

        // Act
        var normalized = query.Normalize();

        // Assert
        normalized.Page.Should().Be(2);
        normalized.PageSize.Should().Be(200);
        normalized.Lifecycle.Should().Be("Active");
        normalized.Onboarding.Should().Be("Pending");
        normalized.Review.Should().Be("Overdue");
    }

    [Theory]
    [InlineData("", null, false)]
    [InlineData("support", null, true)]
    [InlineData("support", "INC-42", false)]
    public void T017_SupportPurpose_RequiresBoundedReasonAndAcknowledgement(
        string reason, string? reference, bool acknowledged)
    {
        // Arrange
        var request = new StartSupportAccessRequest(reason, reference, acknowledged);

        // Act
        var errors = WorkspaceContractValidator.Validate(request);

        // Assert
        if (acknowledged && reason == "support")
            errors.Should().BeEmpty();
        else
            errors.Should().NotBeEmpty();
    }

    [Fact]
    public void T018_NormalizedIdentity_KeepsSourceAndRecordSeparate()
    {
        // Arrange
        var provider = new WorkspaceRecordIdentity("provider", Guid.NewGuid().ToString());
        var local = new WorkspaceRecordIdentity("local", provider.RecordId);

        // Act
        var equal = provider == local;

        // Assert
        equal.Should().BeFalse();
    }

    [Fact]
    public void T019_ResponsibilityDetail_DoesNotInferMissingConfirmation()
    {
        // Arrange
        var detail = new ResponsibilityDetailState(null, null, null);

        // Act
        var designation = detail.Designation;

        // Assert
        designation.Should().Be("Undesignated");
    }

    [Fact]
    public void T020_SetupPlan_IsIdempotentAndNeverDeletesSharedComponents()
    {
        // Arrange
        var plan = CapabilitySetupPlan.Create("request-1", ["one", "one", "two"], subscribe: true);

        // Act
        var writes = plan.Writes;

        // Assert
        writes.Should().OnlyHaveUniqueItems();
        plan.CompensationDeletesSharedComponents.Should().BeFalse();
    }
}
