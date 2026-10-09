using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Fixture = Ato.Copilot.Tests.Unit.ProviderAuthorizations.OfferingBoundaryOverviewTests.Fixture;

namespace Ato.Copilot.Tests.Unit.ProviderAuthorizations;

public sealed class DeclaredUseCorrectionTests
{
    [Fact]
    public void RequestCorrection_LeavesAccessApproved()
    {
        // Arrange
        var row = ApprovedRequest();

        // Act
        OfferingAccessFacts.ApplyCorrectionRequest(row, "Cite the adopted CRM, not the SSP body.");

        // Assert
        row.Status.Should().Be("Approved");
        row.CorrectionStatus.Should().Be(OfferingAccessFacts.CorrectionRequested);
        row.EstimateResult.Should().Be(ScopeFitFacts.InScope);
        row.CorrectionMessage.Should().Contain("CRM");
    }

    [Fact]
    public void Resubmit_UpdatesDeclaredUse_DoesNotRevokeAccess()
    {
        // Arrange
        var row = ApprovedRequest();
        OfferingAccessFacts.ApplyCorrectionRequest(row, "Need the declared environment.");

        // Act
        OfferingAccessFacts.ApplyResubmit(row, new RequestOfferingAccessRequest(
            row.OfferingId, "IATT", "Test", "IL4", "CUI as published", "None"));

        // Assert
        row.Status.Should().Be("Approved");
        row.CorrectionStatus.Should().Be(OfferingAccessFacts.CorrectionResubmitted);
        row.AuthPath.Should().Be("IATT");
        row.EnvironmentLane.Should().Be("Test");
        row.EstimateResult.Should().Be(ScopeFitFacts.Exception);
    }

    [Fact]
    public async Task RequestCorrection_DoesNotChangeScopeFitResult()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var assignment = await fixture.AddAssignmentAsync();
        await using (var db = fixture.Db())
        {
            db.Add(new ProviderOfferingAccessRequest
            {
                ProviderId = fixture.Provider, OfferingId = fixture.Offering,
                TargetTenantId = fixture.Customer, SystemId = assignment.SystemId,
                Status = "Approved", AuthPath = "IATT", EnvironmentLane = "Dev", ImpactLevel = "IL4",
                DataTypes = "CUI as published", Interconnect = "None",
                EstimateResult = ScopeFitFacts.InScope, EstimateRationale = "In scope"
            });
            db.Add(new ProviderScopeFitRecord
            {
                ProviderId = fixture.Provider, OfferingId = fixture.Offering, AssignmentId = assignment.Id,
                Result = ScopeFitFacts.InScope, DeclaredUse = "IATT · Dev · IL4",
                Evaluator = "tester", EvaluatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // Act
        await using (var db = fixture.Db())
        {
            var access = await db.Set<ProviderOfferingAccessRequest>().SingleAsync();
            OfferingAccessFacts.ApplyCorrectionRequest(access, "Resubmit declared use against the cited CRM.");
            await db.SaveChangesAsync();
        }
        var missions = await fixture.Service.BoundaryOverviewAsync(fixture.Offering, 1, 1, 10, default);
        await using var after = fixture.Db();
        var stored = await after.Set<ProviderOfferingAccessRequest>().SingleAsync();

        // Assert
        missions.MissionSystems.Items.Single(x => x.AssignmentId == assignment.Id).ScopeFit.Result
            .Should().Be(ScopeFitFacts.InScope);
        stored.Status.Should().Be("Approved");
        stored.CorrectionStatus.Should().Be(OfferingAccessFacts.CorrectionRequested);
    }

    private static ProviderOfferingAccessRequest ApprovedRequest() => new()
    {
        OfferingId = Guid.NewGuid(), Status = "Approved", AuthPath = "IATT", EnvironmentLane = "Dev",
        ImpactLevel = "IL4", DataTypes = "CUI as published", Interconnect = "None",
        EstimateResult = ScopeFitFacts.InScope, EstimateRationale = "In scope"
    };
}
