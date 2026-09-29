using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Services.ProviderAuthorizations;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.ProviderAuthorizations;

public sealed partial class ProviderMissionServiceTests
{
    [Theory]
    [InlineData(OrganizationRole.Assessor, true, false, false, false)]
    [InlineData(OrganizationRole.Issm, true, false, true, false)]
    [InlineData(OrganizationRole.Isso, true, false, true, false)]
    [InlineData(OrganizationRole.MissionOwner, true, false, true, false)]
    [InlineData(OrganizationRole.SystemOwner, true, false, true, false)]
    [InlineData(OrganizationRole.Issm, true, true, false, false)]
    [InlineData(OrganizationRole.MissionOwner, true, true, false, false)]
    [InlineData(OrganizationRole.AuthorizingOfficial, true, true, true, true)]
    [InlineData(OrganizationRole.AuthorizingOfficial, true, false, false, true)]
    [InlineData(OrganizationRole.Issm, false, false, false, false)]
    [InlineData(OrganizationRole.AuthorizingOfficial, false, false, false, false)]
    public async Task RelationshipProjection_ExposesExactAssignedReviewPermissions(
        OrganizationRole role, bool associated, bool covered, bool ordinaryReview, bool coveredReview)
    {
        // Arrange
        await using (var seed = new AtoCopilotContext(_options))
        {
            (await seed.SystemRoleAssignments.SingleAsync()).Role = role;
            if (associated)
                seed.Add(new MissionProviderRelationshipReview
                {
                    ProviderId = _provider, OfferingId = _offering.Id, TenantId = _tenant, SystemId = _system,
                    AssignmentId = _assignment.Id, AssignmentRevision = _assignment.Revision,
                    State = covered ? "ExplicitlyCoveredByRecordedScope" : "Undetermined"
                });
            await seed.SaveChangesAsync();
        }
        var tenant = new TenantContext(_tenant) { PersonId = _person, IsWorkspaceRequest = true };
        using var scope = _accessor.Push(tenant);
        await using var db = new AtoCopilotContext(_options, _accessor);

        // Act
        var row = (await Service(db, tenant).RelationshipsAsync(_system, 1, 10, default)).Items.Single();
        var json = JsonSerializer.SerializeToElement(row, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        // Assert
        json.GetProperty("canReviewRelationship").GetBoolean().Should().Be(ordinaryReview);
        json.GetProperty("canReviewCoveredScope").GetBoolean().Should().Be(coveredReview);
        if (associated) row.CanAssociate.Should().BeFalse();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task RelationshipProjection_IneligibleAllocationDisablesEveryReview(bool retired, bool covered)
    {
        // Arrange
        await using (var seed = new AtoCopilotContext(_options))
        {
            seed.SystemRoleAssignments.Add(new()
            {
                TenantId = _tenant, RegisteredSystemId = _system, PersonId = _person,
                Role = OrganizationRole.AuthorizingOfficial
            });
            var offering = await seed.Set<ProviderOffering>().SingleAsync();
            if (retired) offering.Lifecycle = "Retired";
            else offering.CurrentHostingScopeRevisionId = null;
            seed.Add(new MissionProviderRelationshipReview
            {
                ProviderId = _provider, OfferingId = _offering.Id, TenantId = _tenant, SystemId = _system,
                AssignmentId = _assignment.Id, AssignmentRevision = _assignment.Revision,
                State = covered ? "ExplicitlyCoveredByRecordedScope" : "Undetermined"
            });
            await seed.SaveChangesAsync();
        }
        var tenant = new TenantContext(_tenant) { PersonId = _person, IsWorkspaceRequest = true };
        using var scope = _accessor.Push(tenant);
        await using var db = new AtoCopilotContext(_options, _accessor);

        // Act
        var row = (await Service(db, tenant).RelationshipsAsync(_system, 1, 10, default)).Items.Single();
        var json = JsonSerializer.SerializeToElement(row, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        // Assert
        json.GetProperty("canReviewRelationship").GetBoolean().Should().BeFalse();
        json.GetProperty("canReviewCoveredScope").GetBoolean().Should().BeFalse();
        row.ReviewRequired.Should().BeTrue();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task RelationshipProjection_CoveredReviewRequiresBothAoRoleAndDecisionPermission(
        bool aoRole, bool decisionPermission)
    {
        // Arrange
        var tenant = new TenantContext(_tenant) { PersonId = _person, IsWorkspaceRequest = true };
        using var scope = _accessor.Push(tenant);
        await using var db = new AtoCopilotContext(_options, _accessor);
        await Service(db, tenant).AssociateAsync(_system, new(_assignment.Id, 1), "manager", default);
        var relationship = await db.Set<MissionProviderRelationshipReview>().SingleAsync();
        relationship.State = "ExplicitlyCoveredByRecordedScope";
        await db.SaveChangesAsync();
        var access = new Mock<ISystemWorkspaceAccessService>();
        access.Setup(x => x.GetAccessAsync(_tenant, _person, _system, false, default)).ReturnsAsync(
            new SystemWorkspaceAccessResponse(_system, aoRole ? ["AuthorizingOfficial"] : ["Issm"],
                new(true, false, false, false, false, false, false, false, decisionPermission)));
        var service = new ProviderMissionService(
            db, tenant, access.Object, Mock.Of<ICapabilityResponsibilityService>());

        // Act
        var row = (await service.RelationshipsAsync(_system, 1, 10, default)).Items.Single();
        var json = JsonSerializer.SerializeToElement(row, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        // Assert
        json.GetProperty("canReviewRelationship").GetBoolean().Should().BeFalse();
        json.GetProperty("canReviewCoveredScope").GetBoolean().Should().BeFalse();
        await FluentActions.Awaiting(() => service.AuthorizeAsync(_system, true, true, default))
            .Should().ThrowAsync<UnauthorizedAccessException>();
    }
}
