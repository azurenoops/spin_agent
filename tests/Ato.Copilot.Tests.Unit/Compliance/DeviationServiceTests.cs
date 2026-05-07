using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FluentAssertions;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services;

namespace Ato.Copilot.Tests.Unit.Compliance;

/// <summary>
/// Unit tests for DeviationService covering lifecycle transitions, status cascade,
/// duplicate checks, CAT I two-step ISSM flow, max review cycle, and orphan handling.
/// </summary>
public class DeviationServiceTests : IDisposable
{
    private readonly AtoCopilotContext _db;
    private readonly DeviationService _service;
    private const string SystemId = "sys-1";

    public DeviationServiceTests()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"DeviationTests_{Guid.NewGuid()}")
            .Options;
        var factory = new TestDbContextFactory(options);
        _db = factory.Context;

        // Seed a RegisteredSystem
        _db.RegisteredSystems.Add(new RegisteredSystem
        {
            Id = SystemId,
            Name = "Test System",
        });
        _db.SaveChanges();

        _service = new DeviationService(factory, Mock.Of<ILogger<DeviationService>>());
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private CreateDeviationRequest MakeRequest(
        string controlId = "AC-2",
        string type = "RiskAcceptance",
        string severity = "CatII",
        int expiresInDays = 180) => new()
    {
        ControlId = controlId,
        DeviationType = type,
        CatSeverity = severity,
        Justification = "Test justification",
        CompensatingControls = "Compensating control",
        ReviewCycle = "180d",
        ExpirationDate = DateTime.UtcNow.AddDays(expiresInDays),
    };

    private async Task<Deviation> CreateApprovedDeviation(
        string? findingId = null,
        string? poamId = null,
        string type = "RiskAcceptance",
        string severity = "CatII")
    {
        var request = MakeRequest(type: type, severity: severity);
        request.FindingId = findingId;
        request.PoamEntryId = poamId;
        var deviation = await _service.CreateDeviationAsync(SystemId, request);

        var review = new ReviewDeviationRequest { Decision = "Approve", Comments = "Approved" };
        return await _service.ReviewDeviationAsync(deviation.Id, review, "reviewer", "ISSM");
    }

    // ─── Create ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateDeviation_ValidRequest_CreatesPendingDeviation()
    {
        var result = await _service.CreateDeviationAsync(SystemId, MakeRequest());

        result.Status.Should().Be(DeviationStatus.Pending);
        result.DeviationType.Should().Be(DeviationType.RiskAcceptance);
        result.ControlId.Should().Be("AC-2");

        // Audit trail created
        var activity = await _db.DashboardActivities.FirstOrDefaultAsync(a => a.EventType == "DeviationCreated");
        activity.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateDeviation_InvalidType_Throws()
    {
        var request = MakeRequest();
        request.DeviationType = "Invalid";

        var act = () => _service.CreateDeviationAsync(SystemId, request);
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*DeviationType*");
    }

    [Fact]
    public async Task CreateDeviation_InvalidReviewCycle_Throws()
    {
        var request = MakeRequest();
        request.ReviewCycle = "999d";

        var act = () => _service.CreateDeviationAsync(SystemId, request);
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*ReviewCycle*");
    }

    [Fact]
    public async Task CreateDeviation_ExpirationPast_Throws()
    {
        var request = MakeRequest(expiresInDays: -1);

        var act = () => _service.CreateDeviationAsync(SystemId, request);
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*future*");
    }

    [Fact]
    public async Task CreateDeviation_ExpirationExceedsMaxCycle_Throws()
    {
        var request = MakeRequest(expiresInDays: DeviationConstants.MaxReviewCycleDays + 1);

        var act = () => _service.CreateDeviationAsync(SystemId, request);
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*365*");
    }

    [Fact]
    public async Task CreateDeviation_DuplicateActiveFinding_ThrowsConflict()
    {
        var finding = new ComplianceFinding
        {
            ControlId = "AC-2",
            Status = FindingStatus.Open,
        };
        _db.Findings.Add(finding);
        await _db.SaveChangesAsync();

        var request = MakeRequest();
        request.FindingId = finding.Id;
        await _service.CreateDeviationAsync(SystemId, request);

        // Second create with same finding
        var request2 = MakeRequest();
        request2.FindingId = finding.Id;

        var act = () => _service.CreateDeviationAsync(SystemId, request2);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*DUPLICATE_DEVIATION*");
    }

    [Fact]
    public async Task CreateDeviation_SystemNotFound_Throws()
    {
        var act = () => _service.CreateDeviationAsync("nonexistent", MakeRequest());
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found*");
    }

    // ─── Review: Approve / Deny ──────────────────────────────────────────────

    [Fact]
    public async Task ReviewDeviation_Approve_SetsApproved()
    {
        var deviation = await _service.CreateDeviationAsync(SystemId, MakeRequest());
        var review = new ReviewDeviationRequest { Decision = "Approve", Comments = "Looks good" };

        var result = await _service.ReviewDeviationAsync(deviation.Id, review, "reviewer", "ISSM");

        result.Status.Should().Be(DeviationStatus.Approved);
        result.ReviewedBy.Should().Be("reviewer");
        result.ReviewerRole.Should().Be("ISSM");
    }

    [Fact]
    public async Task ReviewDeviation_Deny_SetsDenied()
    {
        var deviation = await _service.CreateDeviationAsync(SystemId, MakeRequest());
        var review = new ReviewDeviationRequest { Decision = "Deny", Comments = "Insufficient justification" };

        var result = await _service.ReviewDeviationAsync(deviation.Id, review, "reviewer", "ISSM");

        result.Status.Should().Be(DeviationStatus.Denied);
    }

    [Fact]
    public async Task ReviewDeviation_NotPending_Throws()
    {
        var deviation = await CreateApprovedDeviation();
        var review = new ReviewDeviationRequest { Decision = "Deny" };

        var act = () => _service.ReviewDeviationAsync(deviation.Id, review);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*NOT_PENDING*");
    }

    [Fact]
    public async Task ReviewDeviation_InvalidDecision_Throws()
    {
        var deviation = await _service.CreateDeviationAsync(SystemId, MakeRequest());
        var review = new ReviewDeviationRequest { Decision = "Maybe" };

        var act = () => _service.ReviewDeviationAsync(deviation.Id, review);
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*INVALID_DECISION*");
    }

    // ─── Finding / POA&M Status Cascade ──────────────────────────────────────

    [Fact]
    public async Task ReviewDeviation_ApproveFalsePositive_FindingSetToFalsePositive()
    {
        var finding = new ComplianceFinding
        {
            ControlId = "AC-2",
            Status = FindingStatus.Open,
        };
        _db.Findings.Add(finding);
        await _db.SaveChangesAsync();

        var request = MakeRequest(type: "FalsePositive");
        request.FindingId = finding.Id;
        var deviation = await _service.CreateDeviationAsync(SystemId, request);

        var review = new ReviewDeviationRequest { Decision = "Approve" };
        await _service.ReviewDeviationAsync(deviation.Id, review, "reviewer", "ISSM");

        var updatedFinding = await _db.Findings.FindAsync(finding.Id);
        updatedFinding!.Status.Should().Be(FindingStatus.FalsePositive);
    }

    [Fact]
    public async Task ReviewDeviation_ApproveRiskAcceptance_FindingSetToAccepted()
    {
        var finding = new ComplianceFinding
        {
            ControlId = "AC-2",
            Status = FindingStatus.Open,
        };
        _db.Findings.Add(finding);
        await _db.SaveChangesAsync();

        var request = MakeRequest(type: "RiskAcceptance");
        request.FindingId = finding.Id;
        var deviation = await _service.CreateDeviationAsync(SystemId, request);

        var review = new ReviewDeviationRequest { Decision = "Approve" };
        await _service.ReviewDeviationAsync(deviation.Id, review, "reviewer", "ISSM");

        var updatedFinding = await _db.Findings.FindAsync(finding.Id);
        updatedFinding!.Status.Should().Be(FindingStatus.Accepted);
    }

    [Fact]
    public async Task ReviewDeviation_Approve_PoamSetToRiskAccepted()
    {
        var poam = new PoamItem
        {
            RegisteredSystemId = SystemId,
            Weakness = "Test weakness",
            Status = PoamStatus.Ongoing,
        };
        _db.PoamItems.Add(poam);
        await _db.SaveChangesAsync();

        var request = MakeRequest();
        request.PoamEntryId = poam.Id;
        var deviation = await _service.CreateDeviationAsync(SystemId, request);

        var review = new ReviewDeviationRequest { Decision = "Approve" };
        await _service.ReviewDeviationAsync(deviation.Id, review, "reviewer", "ISSM");

        var updatedPoam = await _db.PoamItems.FindAsync(poam.Id);
        updatedPoam!.Status.Should().Be(PoamStatus.RiskAccepted);
    }

    // ─── CAT I Two-Step ISSM Recommendation Flow ────────────────────────────

    [Fact]
    public async Task ReviewDeviation_CatI_ISSM_RecordsRecommendation_StaysPending()
    {
        var request = MakeRequest(severity: "CatI");
        var deviation = await _service.CreateDeviationAsync(SystemId, request);

        var review = new ReviewDeviationRequest { Decision = "Approve", Comments = "ISSM recommends" };
        var result = await _service.ReviewDeviationAsync(deviation.Id, review, "issm-user", "ISSM");

        result.Status.Should().Be(DeviationStatus.Pending);
        result.ISSMRecommendation.Should().Be("Approve");
        result.ISSMRecommendedBy.Should().Be("issm-user");
        result.ISSMRecommendedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ReviewDeviation_CatI_AO_RendersApproval()
    {
        var request = MakeRequest(severity: "CatI");
        var deviation = await _service.CreateDeviationAsync(SystemId, request);

        // ISSM recommends first
        var issmReview = new ReviewDeviationRequest { Decision = "Approve", Comments = "ISSM recommends" };
        await _service.ReviewDeviationAsync(deviation.Id, issmReview, "issm-user", "ISSM");

        // AO renders final
        var aoReview = new ReviewDeviationRequest { Decision = "Approve", Comments = "AO approves" };
        var result = await _service.ReviewDeviationAsync(deviation.Id, aoReview, "ao-user", "AO");

        result.Status.Should().Be(DeviationStatus.Approved);
        result.ReviewedBy.Should().Be("ao-user");
        result.ReviewerRole.Should().Be("AO");
    }

    // ─── Revoke ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task RevokeDeviation_Approved_SetsRevoked()
    {
        var deviation = await CreateApprovedDeviation();
        var revoke = new RevokeDeviationRequest { Reason = "Policy change" };

        var result = await _service.RevokeDeviationAsync(deviation.Id, revoke, "revoker");

        result.Status.Should().Be(DeviationStatus.Revoked);
        result.RevokedBy.Should().Be("revoker");
        result.RevocationReason.Should().Be("Policy change");
    }

    [Fact]
    public async Task RevokeDeviation_NotApproved_Throws()
    {
        var deviation = await _service.CreateDeviationAsync(SystemId, MakeRequest());
        var revoke = new RevokeDeviationRequest { Reason = "Test" };

        var act = () => _service.RevokeDeviationAsync(deviation.Id, revoke);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*NOT_APPROVED*");
    }

    [Fact]
    public async Task RevokeDeviation_RevertsFindingAndPoam()
    {
        var finding = new ComplianceFinding
        {
            ControlId = "AC-2",
            Status = FindingStatus.Open,
        };
        var poam = new PoamItem
        {
            RegisteredSystemId = SystemId,
            Weakness = "Test",
            Status = PoamStatus.Ongoing,
        };
        _db.Findings.Add(finding);
        _db.PoamItems.Add(poam);
        await _db.SaveChangesAsync();

        var deviation = await CreateApprovedDeviation(findingId: finding.Id, poamId: poam.Id);
        await _service.RevokeDeviationAsync(deviation.Id, new RevokeDeviationRequest { Reason = "Test" });

        (await _db.Findings.FindAsync(finding.Id))!.Status.Should().Be(FindingStatus.Open);
        (await _db.PoamItems.FindAsync(poam.Id))!.Status.Should().Be(PoamStatus.Ongoing);
    }

    // ─── Extend ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExtendDeviation_Approved_UpdatesExpiration()
    {
        var deviation = await CreateApprovedDeviation();
        var newDate = DateTime.UtcNow.AddDays(90);
        var extend = new ExtendDeviationRequest { NewExpirationDate = newDate, Justification = "Renewal" };

        var result = await _service.ExtendDeviationAsync(deviation.Id, extend);

        result.ExpirationDate.Should().BeCloseTo(newDate, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ExtendDeviation_ExceedsMaxCycle_Throws()
    {
        var deviation = await CreateApprovedDeviation();
        var extend = new ExtendDeviationRequest
        {
            NewExpirationDate = DateTime.UtcNow.AddDays(DeviationConstants.MaxReviewCycleDays + 1),
        };

        var act = () => _service.ExtendDeviationAsync(deviation.Id, extend);
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*365*");
    }

    [Fact]
    public async Task ExtendDeviation_NotApproved_Throws()
    {
        var deviation = await _service.CreateDeviationAsync(SystemId, MakeRequest());
        var extend = new ExtendDeviationRequest { NewExpirationDate = DateTime.UtcNow.AddDays(30) };

        var act = () => _service.ExtendDeviationAsync(deviation.Id, extend);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*NOT_APPROVED*");
    }

    // ─── Expire ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExpireDeviations_ExpiredApproved_SetsExpiredAndReverts()
    {
        // Create and approve a deviation, then backdate its expiration
        var deviation = await CreateApprovedDeviation();
        deviation.ExpirationDate = DateTime.UtcNow.AddDays(-1);
        await _db.SaveChangesAsync();

        var count = await _service.ExpireDeviationsAsync();

        count.Should().Be(1);
        var updated = await _db.Deviations.FindAsync(deviation.Id);
        updated!.Status.Should().Be(DeviationStatus.Expired);
    }

    [Fact]
    public async Task ExpireDeviations_NotExpired_NoChanges()
    {
        await CreateApprovedDeviation();

        var count = await _service.ExpireDeviationsAsync();

        count.Should().Be(0);
    }

    // ─── Orphan Handling ─────────────────────────────────────────────────────

    [Fact]
    public async Task HandleOrphanedDeviations_ActiveDeviation_SetsRevoked()
    {
        var finding = new ComplianceFinding
        {
            ControlId = "AC-2",
            Status = FindingStatus.Open,
        };
        _db.Findings.Add(finding);
        await _db.SaveChangesAsync();

        var request = MakeRequest();
        request.FindingId = finding.Id;
        var deviation = await _service.CreateDeviationAsync(SystemId, request);

        var count = await _service.HandleOrphanedDeviationsAsync(finding.Id);

        count.Should().Be(1);
        var updated = await _db.Deviations.FindAsync(deviation.Id);
        updated!.Status.Should().Be(DeviationStatus.Revoked);
        updated.RevocationReason.Should().Contain("deleted");
    }

    // ─── List & Summary ──────────────────────────────────────────────────────

    [Fact]
    public async Task ListDeviations_FiltersByType()
    {
        await _service.CreateDeviationAsync(SystemId, MakeRequest(type: "RiskAcceptance"));
        await _service.CreateDeviationAsync(SystemId, MakeRequest(type: "FalsePositive", controlId: "AC-3"));

        var result = await _service.ListDeviationsAsync(SystemId, typeFilter: "FalsePositive");

        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle(d => d.DeviationType == "FalsePositive");
    }

    [Fact]
    public async Task ListDeviations_PaginatesCorrectly()
    {
        for (int i = 0; i < 5; i++)
            await _service.CreateDeviationAsync(SystemId, MakeRequest(controlId: $"AC-{i}"));

        var page1 = await _service.ListDeviationsAsync(SystemId, page: 1, pageSize: 2);
        var page2 = await _service.ListDeviationsAsync(SystemId, page: 2, pageSize: 2);

        page1.Items.Should().HaveCount(2);
        page2.Items.Should().HaveCount(2);
        page1.TotalCount.Should().Be(5);
    }

    [Fact]
    public async Task GetDeviationSummary_ReturnsCorrectCounts()
    {
        await _service.CreateDeviationAsync(SystemId, MakeRequest(controlId: "AC-2"));
        await CreateApprovedDeviation();

        var summary = await _service.GetDeviationSummaryAsync(SystemId);

        summary.Total.Should().Be(2);
        summary.Pending.Should().Be(1);
        summary.Approved.Should().Be(1);
    }

    // ─── Get / Detail ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetDeviation_Exists_ReturnsDeviation()
    {
        var deviation = await _service.CreateDeviationAsync(SystemId, MakeRequest());

        var result = await _service.GetDeviationAsync(deviation.Id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(deviation.Id);
    }

    [Fact]
    public async Task GetDeviation_NotFound_ReturnsNull()
    {
        var result = await _service.GetDeviationAsync("nonexistent");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetDeviationDetail_ReturnsAuditTrail()
    {
        var deviation = await _service.CreateDeviationAsync(SystemId, MakeRequest());

        var detail = await _service.GetDeviationDetailAsync(deviation.Id);

        detail.Should().NotBeNull();
        detail!.AuditTrail.Should().HaveCountGreaterThan(0);
        detail.AuditTrail[0].EventType.Should().Be("DeviationCreated");
    }

    // ─── Boundary Waiver Queries ─────────────────────────────────────────────

    [Fact]
    public async Task GetWaivedControls_ReturnsOnlyApprovedWaivers()
    {
        var boundary = new AuthorizationBoundaryDefinition
        {
            RegisteredSystemId = SystemId,
            Name = "Primary",
            BoundaryType = BoundaryDefinitionType.Logical,
            CreatedBy = "test",
        };
        _db.AuthorizationBoundaryDefinitions.Add(boundary);
        await _db.SaveChangesAsync();

        var request = MakeRequest(type: "Waiver");
        request.BoundaryDefinitionId = boundary.Id;
        var deviation = await _service.CreateDeviationAsync(SystemId, request);

        // Approve the waiver
        var review = new ReviewDeviationRequest { Decision = "Approve" };
        await _service.ReviewDeviationAsync(deviation.Id, review, "reviewer", "ISSM");

        var waived = await _service.GetWaivedControlsForBoundaryAsync(SystemId, boundary.Id);

        waived.Should().Contain("AC-2");
    }

    [Fact]
    public async Task GetActiveDeviationCount_ReturnsApprovedCount()
    {
        await _service.CreateDeviationAsync(SystemId, MakeRequest(controlId: "AC-2"));
        await CreateApprovedDeviation();

        var count = await _service.GetActiveDeviationCountAsync(SystemId);

        count.Should().Be(1);
    }
}
