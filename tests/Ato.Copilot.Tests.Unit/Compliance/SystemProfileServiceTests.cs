using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Compliance;

/// <summary>
/// Unit tests for SystemProfileService (Feature 046).
/// T018: Save draft, submit, withdraw, review, batch approve,
///        completeness calculation, NotStarted synthesis, ApprovedContent preservation,
///        concurrency, business context.
/// </summary>
public class SystemProfileServiceTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly AtoCopilotContext _db;
    private readonly SystemProfileService _service;

    private const string MoUserId = "mo-user";
    private const string IssmUserId = "issm-user";
    private const string UnauthorizedUserId = "random-user";

    public SystemProfileServiceTests()
    {
        var dbName = $"SystemProfileSvc_{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddDbContext<AtoCopilotContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();
        _db = _serviceProvider.GetRequiredService<AtoCopilotContext>();

        _service = new SystemProfileService(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            Mock.Of<ILogger<SystemProfileService>>());
    }

    public void Dispose() => _serviceProvider.Dispose();

    // ─── Seed Helpers ────────────────────────────────────────────────────────

    private async Task<RegisteredSystem> SeedSystemAsync(string? name = null)
    {
        var system = new RegisteredSystem
        {
            Id = Guid.NewGuid().ToString(),
            Name = name ?? "Test System",
            Acronym = "TST",
            Description = "Test system for profile tests",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure Government",
            CreatedBy = "test-user"
        };
        _db.RegisteredSystems.Add(system);
        await _db.SaveChangesAsync();
        return system;
    }

    private async Task AssignRoleAsync(string systemId, string userId, RmfRole role)
    {
        _db.RmfRoleAssignments.Add(new RmfRoleAssignment
        {
            RegisteredSystemId = systemId,
            UserId = userId,
            RmfRole = role,
            AssignedBy = "test-setup",
            IsActive = true
        });
        await _db.SaveChangesAsync();
    }

    private async Task<RegisteredSystem> SeedSystemWithRolesAsync()
    {
        var system = await SeedSystemAsync();
        await AssignRoleAsync(system.Id, MoUserId, RmfRole.MissionOwner);
        await AssignRoleAsync(system.Id, IssmUserId, RmfRole.Issm);
        return system;
    }

    private async Task<SystemProfileSection> SeedSectionAsync(
        string systemId,
        ProfileSectionType type = ProfileSectionType.MissionAndPurpose,
        SspSectionStatus status = SspSectionStatus.Draft,
        string? draftContent = "{\"mission\":\"test\"}")
    {
        var section = new SystemProfileSection
        {
            RegisteredSystemId = systemId,
            SectionType = type,
            GovernanceStatus = status,
            DraftContent = draftContent,
            LastEditedBy = MoUserId,
            LastEditedAt = DateTime.UtcNow
        };
        _db.SystemProfileSections.Add(section);
        await _db.SaveChangesAsync();
        return section;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SaveDraftAsync
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SaveDraft_HappyPath_CreatesSection()
    {
        var system = await SeedSystemWithRolesAsync();

        var result = await _service.SaveDraftAsync(
            system.Id, ProfileSectionType.MissionAndPurpose,
            "{\"mission\":\"Protect data\"}", MoUserId);

        result.Should().NotBeNull();
        result.SectionType.Should().Be(ProfileSectionType.MissionAndPurpose);
        result.GovernanceStatus.Should().Be(SspSectionStatus.Draft);
        result.DraftContent.Should().Be("{\"mission\":\"Protect data\"}");
        result.LastEditedBy.Should().Be(MoUserId);
    }

    [Fact]
    public async Task SaveDraft_ExistingDraft_UpdatesContent()
    {
        var system = await SeedSystemWithRolesAsync();
        await SeedSectionAsync(system.Id);

        var result = await _service.SaveDraftAsync(
            system.Id, ProfileSectionType.MissionAndPurpose,
            "{\"mission\":\"Updated\"}", MoUserId);

        result.DraftContent.Should().Be("{\"mission\":\"Updated\"}");
        result.GovernanceStatus.Should().Be(SspSectionStatus.Draft);
    }

    [Fact]
    public async Task SaveDraft_Unauthorized_Throws()
    {
        var system = await SeedSystemAsync();
        // No role assignment for UnauthorizedUserId

        var act = () => _service.SaveDraftAsync(
            system.Id, ProfileSectionType.MissionAndPurpose,
            "{\"data\":\"test\"}", UnauthorizedUserId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*UNAUTHORIZED*");
    }

    [Fact]
    public async Task SaveDraft_InactiveSystem_Throws()
    {
        var system = await SeedSystemAsync();
        system.IsActive = false;
        await _db.SaveChangesAsync();

        var act = () => _service.SaveDraftAsync(
            system.Id, ProfileSectionType.MissionAndPurpose,
            "{}", MoUserId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*SYSTEM_NOT_FOUND*");
    }

    [Fact]
    public async Task SaveDraft_SectionUnderReview_Throws()
    {
        var system = await SeedSystemWithRolesAsync();
        await SeedSectionAsync(system.Id, status: SspSectionStatus.UnderReview);

        var act = () => _service.SaveDraftAsync(
            system.Id, ProfileSectionType.MissionAndPurpose,
            "{\"updated\":true}", MoUserId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*INVALID_STATUS*under review*");
    }

    [Fact]
    public async Task SaveDraft_ApprovedSection_TransitionsToDraft_PreservesApprovedContent()
    {
        var system = await SeedSystemWithRolesAsync();
        var section = await SeedSectionAsync(system.Id, status: SspSectionStatus.Approved,
            draftContent: "{\"mission\":\"Approved version\"}");

        var result = await _service.SaveDraftAsync(
            system.Id, ProfileSectionType.MissionAndPurpose,
            "{\"mission\":\"Re-edited version\"}", MoUserId);

        result.GovernanceStatus.Should().Be(SspSectionStatus.Draft);
        result.DraftContent.Should().Be("{\"mission\":\"Re-edited version\"}");
        result.ApprovedContent.Should().Be("{\"mission\":\"Approved version\"}");
    }

    [Fact]
    public async Task SaveDraft_SystemOwner_Allowed()
    {
        var system = await SeedSystemAsync();
        await AssignRoleAsync(system.Id, "so-user", RmfRole.SystemOwner);

        var result = await _service.SaveDraftAsync(
            system.Id, ProfileSectionType.DataTypes,
            "{\"info\":\"SO edit\"}", "so-user");

        result.Should().NotBeNull();
        result.DraftContent.Should().Be("{\"info\":\"SO edit\"}");
    }

    [Fact]
    public async Task SaveDraft_Issm_Allowed()
    {
        var system = await SeedSystemWithRolesAsync();

        var result = await _service.SaveDraftAsync(
            system.Id, ProfileSectionType.EnvironmentAndDeployment,
            "{\"env\":\"Azure\"}", IssmUserId);

        result.Should().NotBeNull();
        result.DraftContent.Should().Be("{\"env\":\"Azure\"}");
    }

    [Fact]
    public async Task SaveDraft_CreatesAuditEntry()
    {
        var system = await SeedSystemWithRolesAsync();

        var result = await _service.SaveDraftAsync(
            system.Id, ProfileSectionType.MissionAndPurpose,
            "{\"mission\":\"test\"}", MoUserId);

        var audits = await _db.ProfileAuditEntries
            .Where(a => a.SystemProfileSectionId == result.Id)
            .ToListAsync();
        audits.Should().HaveCount(1);
        audits[0].Action.Should().Be("Drafted");
        audits[0].PerformedBy.Should().Be(MoUserId);
        audits[0].NewStatus.Should().Be(SspSectionStatus.Draft);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SubmitForReviewAsync
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Submit_HappyPath_TransitionsToUnderReview()
    {
        var system = await SeedSystemWithRolesAsync();
        await SeedSectionAsync(system.Id, ProfileSectionType.MissionAndPurpose);
        await SeedSectionAsync(system.Id, ProfileSectionType.DataTypes);

        var result = await _service.SubmitForReviewAsync(system.Id, null, MoUserId);

        result.SubmittedSections.Should().HaveCount(2);
        result.SubmittedBy.Should().Be(MoUserId);
    }

    [Fact]
    public async Task Submit_SpecificSections_OnlySubmitsRequested()
    {
        var system = await SeedSystemWithRolesAsync();
        await SeedSectionAsync(system.Id, ProfileSectionType.MissionAndPurpose);
        await SeedSectionAsync(system.Id, ProfileSectionType.DataTypes);

        var result = await _service.SubmitForReviewAsync(
            system.Id,
            [ProfileSectionType.MissionAndPurpose],
            MoUserId);

        result.SubmittedSections.Should().ContainSingle()
            .Which.Should().Be(ProfileSectionType.MissionAndPurpose);
    }

    [Fact]
    public async Task Submit_NeedsRevisionSection_CanResubmit()
    {
        var system = await SeedSystemWithRolesAsync();
        await SeedSectionAsync(system.Id, ProfileSectionType.MissionAndPurpose,
            status: SspSectionStatus.NeedsRevision);

        var result = await _service.SubmitForReviewAsync(system.Id, null, MoUserId);

        result.SubmittedSections.Should().Contain(ProfileSectionType.MissionAndPurpose);
    }

    [Fact]
    public async Task Submit_WrongStatus_AllApproved_Throws()
    {
        var system = await SeedSystemWithRolesAsync();
        await SeedSectionAsync(system.Id, status: SspSectionStatus.Approved);

        var act = () => _service.SubmitForReviewAsync(system.Id, null, MoUserId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*NO_SUBMITTABLE_SECTIONS*");
    }

    [Fact]
    public async Task Submit_NoSections_Throws()
    {
        var system = await SeedSystemWithRolesAsync();

        var act = () => _service.SubmitForReviewAsync(system.Id, null, MoUserId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*NO_SUBMITTABLE_SECTIONS*");
    }

    [Fact]
    public async Task Submit_NonMO_Throws()
    {
        var system = await SeedSystemWithRolesAsync();
        await SeedSectionAsync(system.Id);

        var act = () => _service.SubmitForReviewAsync(system.Id, null, IssmUserId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*UNAUTHORIZED*");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // WithdrawSectionAsync
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Withdraw_HappyPath_TransitionsToDraft()
    {
        var system = await SeedSystemWithRolesAsync();
        await SeedSectionAsync(system.Id, status: SspSectionStatus.UnderReview);

        var result = await _service.WithdrawSectionAsync(system.Id, null, MoUserId);

        result.WithdrawnSections.Should().ContainSingle()
            .Which.Should().Be(ProfileSectionType.MissionAndPurpose);
        result.WithdrawnBy.Should().Be(MoUserId);
    }

    [Fact]
    public async Task Withdraw_WrongStatus_Draft_Throws()
    {
        var system = await SeedSystemWithRolesAsync();
        await SeedSectionAsync(system.Id, status: SspSectionStatus.Draft);

        var act = () => _service.WithdrawSectionAsync(system.Id, null, MoUserId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*NO_WITHDRAWABLE_SECTIONS*");
    }

    [Fact]
    public async Task Withdraw_NonMO_Throws()
    {
        var system = await SeedSystemWithRolesAsync();
        await SeedSectionAsync(system.Id, status: SspSectionStatus.UnderReview);

        var act = () => _service.WithdrawSectionAsync(system.Id, null, IssmUserId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*UNAUTHORIZED*");
    }

    [Fact]
    public async Task Withdraw_CreatesAuditEntry()
    {
        var system = await SeedSystemWithRolesAsync();
        var section = await SeedSectionAsync(system.Id, status: SspSectionStatus.UnderReview);

        await _service.WithdrawSectionAsync(system.Id, null, MoUserId);

        var audits = await _db.ProfileAuditEntries
            .Where(a => a.SystemProfileSectionId == section.Id && a.Action == "Withdrawn")
            .ToListAsync();
        audits.Should().HaveCount(1);
        audits[0].PreviousStatus.Should().Be(SspSectionStatus.UnderReview);
        audits[0].NewStatus.Should().Be(SspSectionStatus.Draft);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ReviewSectionAsync
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Review_Approve_SetsApproved()
    {
        var system = await SeedSystemWithRolesAsync();
        await SeedSectionAsync(system.Id, status: SspSectionStatus.UnderReview,
            draftContent: "{\"mission\":\"Ready\"}");

        var result = await _service.ReviewSectionAsync(
            system.Id, ProfileSectionType.MissionAndPurpose,
            ReviewDecision.Approve, IssmUserId);

        result.GovernanceStatus.Should().Be(SspSectionStatus.Approved);
        result.ApprovedContent.Should().Be("{\"mission\":\"Ready\"}");
        result.ReviewedBy.Should().Be(IssmUserId);
    }

    [Fact]
    public async Task Review_RequestRevision_SetsNeedsRevision()
    {
        var system = await SeedSystemWithRolesAsync();
        await SeedSectionAsync(system.Id, status: SspSectionStatus.UnderReview);

        var result = await _service.ReviewSectionAsync(
            system.Id, ProfileSectionType.MissionAndPurpose,
            ReviewDecision.RequestRevision, IssmUserId, "Needs more detail");

        result.GovernanceStatus.Should().Be(SspSectionStatus.NeedsRevision);
        result.ReviewerComments.Should().Be("Needs more detail");
    }

    [Fact]
    public async Task Review_RequestRevision_CommentsRequired_Throws()
    {
        var system = await SeedSystemWithRolesAsync();
        await SeedSectionAsync(system.Id, status: SspSectionStatus.UnderReview);

        var act = () => _service.ReviewSectionAsync(
            system.Id, ProfileSectionType.MissionAndPurpose,
            ReviewDecision.RequestRevision, IssmUserId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*COMMENTS_REQUIRED*");
    }

    [Fact]
    public async Task Review_WrongStatus_Draft_Throws()
    {
        var system = await SeedSystemWithRolesAsync();
        await SeedSectionAsync(system.Id, status: SspSectionStatus.Draft);

        var act = () => _service.ReviewSectionAsync(
            system.Id, ProfileSectionType.MissionAndPurpose,
            ReviewDecision.Approve, IssmUserId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*INVALID_STATUS*");
    }

    [Fact]
    public async Task Review_NonIssm_Throws()
    {
        var system = await SeedSystemWithRolesAsync();
        await SeedSectionAsync(system.Id, status: SspSectionStatus.UnderReview);

        var act = () => _service.ReviewSectionAsync(
            system.Id, ProfileSectionType.MissionAndPurpose,
            ReviewDecision.Approve, MoUserId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*UNAUTHORIZED*");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // BatchApproveSectionsAsync
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task BatchApprove_ApprovesAllUnderReview()
    {
        var system = await SeedSystemWithRolesAsync();
        await SeedSectionAsync(system.Id, ProfileSectionType.MissionAndPurpose,
            status: SspSectionStatus.UnderReview);
        await SeedSectionAsync(system.Id, ProfileSectionType.DataTypes,
            status: SspSectionStatus.UnderReview);
        await SeedSectionAsync(system.Id, ProfileSectionType.UsersAndAccess,
            status: SspSectionStatus.Draft); // Should not be approved

        var result = await _service.BatchApproveSectionsAsync(system.Id, IssmUserId);

        result.ApprovedCount.Should().Be(2);
        result.ApprovedSections.Should().Contain(ProfileSectionType.MissionAndPurpose);
        result.ApprovedSections.Should().Contain(ProfileSectionType.DataTypes);
        result.ReviewedBy.Should().Be(IssmUserId);
    }

    [Fact]
    public async Task BatchApprove_NonIssm_Throws()
    {
        var system = await SeedSystemWithRolesAsync();
        await SeedSectionAsync(system.Id, status: SspSectionStatus.UnderReview);

        var act = () => _service.BatchApproveSectionsAsync(system.Id, MoUserId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*UNAUTHORIZED*");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GetCompletenessAsync — 5-mandatory denominator
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Completeness_EmptyProfile_AllNotStarted()
    {
        var system = await SeedSystemWithRolesAsync();

        var result = await _service.GetCompletenessAsync(system.Id);

        result.TotalSections.Should().Be(5);
        result.ApprovedPercentage.Should().Be(0);
        result.IsProfileComplete.Should().BeFalse();
        result.IncompleteSections.Should().HaveCount(5);
    }

    [Fact]
    public async Task Completeness_LeveragedAuth_ExcludedFromTotal()
    {
        var system = await SeedSystemWithRolesAsync();
        // Add only the optional LeveragedAuthorizations section as Approved
        await SeedSectionAsync(system.Id, ProfileSectionType.LeveragedAuthorizations,
            status: SspSectionStatus.Approved);

        var result = await _service.GetCompletenessAsync(system.Id);

        // LeveragedAuthorizations is not part of the 5 mandatory sections
        result.TotalSections.Should().Be(5);
        result.ApprovedPercentage.Should().Be(0);
        result.IncompleteSections.Should().HaveCount(5);
    }

    [Fact]
    public async Task Completeness_AllMandatoryApproved_100Percent()
    {
        var system = await SeedSystemWithRolesAsync();
        await SeedSectionAsync(system.Id, ProfileSectionType.MissionAndPurpose, SspSectionStatus.Approved);
        await SeedSectionAsync(system.Id, ProfileSectionType.UsersAndAccess, SspSectionStatus.Approved);
        await SeedSectionAsync(system.Id, ProfileSectionType.EnvironmentAndDeployment, SspSectionStatus.Approved);
        await SeedSectionAsync(system.Id, ProfileSectionType.DataTypes, SspSectionStatus.Approved);
        await SeedSectionAsync(system.Id, ProfileSectionType.PortsProtocolsAndServices, SspSectionStatus.Approved);

        var result = await _service.GetCompletenessAsync(system.Id);

        result.ApprovedPercentage.Should().Be(100);
        result.IsProfileComplete.Should().BeTrue();
        result.IncompleteSections.Should().BeEmpty();
    }

    [Fact]
    public async Task Completeness_PartialApproval_CorrectPercentage()
    {
        var system = await SeedSystemWithRolesAsync();
        await SeedSectionAsync(system.Id, ProfileSectionType.MissionAndPurpose, SspSectionStatus.Approved);
        await SeedSectionAsync(system.Id, ProfileSectionType.UsersAndAccess, SspSectionStatus.Draft);

        var result = await _service.GetCompletenessAsync(system.Id);

        // 1 out of 5 mandatory approved = 20%
        result.ApprovedPercentage.Should().Be(20);
        result.IsProfileComplete.Should().BeFalse();
        result.IncompleteSections.Should().HaveCount(4);
    }

    [Fact]
    public async Task Completeness_MissionOwnerAssigned_ReturnsInfo()
    {
        var system = await SeedSystemWithRolesAsync();

        var result = await _service.GetCompletenessAsync(system.Id);

        result.MissionOwnerAssigned.Should().BeTrue();
        result.MissionOwnerName.Should().Be(MoUserId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GetProfileOverviewAsync — NotStarted synthesis
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ProfileOverview_EmptyProfile_Returns6NotStartedEntries()
    {
        var system = await SeedSystemWithRolesAsync();

        var result = await _service.GetProfileOverviewAsync(system.Id);

        result.Sections.Should().HaveCount(6);
        result.Sections.Should().OnlyContain(s => s.GovernanceStatus == SspSectionStatus.NotStarted);
        result.SystemId.Should().Be(system.Id);
        result.SystemName.Should().Be("Test System");
    }

    [Fact]
    public async Task ProfileOverview_WithSections_SynthesizesMissing()
    {
        var system = await SeedSystemWithRolesAsync();
        await SeedSectionAsync(system.Id, ProfileSectionType.MissionAndPurpose, SspSectionStatus.Draft);
        await SeedSectionAsync(system.Id, ProfileSectionType.DataTypes, SspSectionStatus.Approved);

        var result = await _service.GetProfileOverviewAsync(system.Id);

        result.Sections.Should().HaveCount(6);
        var missionSection = result.Sections.Single(s => s.SectionType == ProfileSectionType.MissionAndPurpose);
        missionSection.GovernanceStatus.Should().Be(SspSectionStatus.Draft);
        var dataSection = result.Sections.Single(s => s.SectionType == ProfileSectionType.DataTypes);
        dataSection.GovernanceStatus.Should().Be(SspSectionStatus.Approved);
        var envSection = result.Sections.Single(s => s.SectionType == ProfileSectionType.EnvironmentAndDeployment);
        envSection.GovernanceStatus.Should().Be(SspSectionStatus.NotStarted);
    }

    [Fact]
    public async Task ProfileOverview_MissionOwner_IncludesInfo()
    {
        var system = await SeedSystemWithRolesAsync();

        var result = await _service.GetProfileOverviewAsync(system.Id);

        result.MissionOwner.Should().NotBeNull();
        result.MissionOwner!.UserId.Should().Be(MoUserId);
    }

    [Fact]
    public async Task ProfileOverview_NoMissionOwner_ReturnsNull()
    {
        var system = await SeedSystemAsync();

        var result = await _service.GetProfileOverviewAsync(system.Id);

        result.MissionOwner.Should().BeNull();
    }

    [Fact]
    public async Task ProfileOverview_CompletenessMetrics()
    {
        var system = await SeedSystemWithRolesAsync();
        await SeedSectionAsync(system.Id, ProfileSectionType.MissionAndPurpose, SspSectionStatus.Approved);
        await SeedSectionAsync(system.Id, ProfileSectionType.UsersAndAccess, SspSectionStatus.Draft);

        var result = await _service.GetProfileOverviewAsync(system.Id);

        // CompletedCount = sections not NotStarted = 2
        result.OverallCompleteness.CompletedCount.Should().Be(2);
        result.OverallCompleteness.ApprovedCount.Should().Be(1);
        result.OverallCompleteness.MandatorySections.Should().Be(5);
        result.OverallCompleteness.AllSections.Should().Be(6);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GetSectionDetailAsync
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SectionDetail_Exists_ReturnsWithChildren()
    {
        var system = await SeedSystemWithRolesAsync();
        var section = await SeedSectionAsync(system.Id, ProfileSectionType.UsersAndAccess);
        _db.Set<UserCategory>().Add(new UserCategory
        {
            SystemProfileSectionId = section.Id,
            CategoryName = "Admins",
            ApproximateCount = 5,
            SortOrder = 0
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetSectionDetailAsync(
            system.Id, ProfileSectionType.UsersAndAccess);

        result.Should().NotBeNull();
        result!.UserCategories.Should().HaveCount(1);
        result.UserCategories.First().CategoryName.Should().Be("Admins");
    }

    [Fact]
    public async Task SectionDetail_NotExists_ReturnsNull()
    {
        var system = await SeedSystemWithRolesAsync();

        var result = await _service.GetSectionDetailAsync(
            system.Id, ProfileSectionType.MissionAndPurpose);

        result.Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GetProfileTodosAsync
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ProfileTodos_IncludesIncompleteAndRevisionSections()
    {
        var system = await SeedSystemWithRolesAsync();
        await SeedSectionAsync(system.Id, ProfileSectionType.MissionAndPurpose, SspSectionStatus.Draft);
        await SeedSectionAsync(system.Id, ProfileSectionType.DataTypes, SspSectionStatus.NeedsRevision);
        await SeedSectionAsync(system.Id, ProfileSectionType.UsersAndAccess, SspSectionStatus.Approved);

        var result = await _service.GetProfileTodosAsync(system.Id, MoUserId);

        result.HasProfileTasks.Should().BeTrue();
        // Incomplete: Draft + 3 NotStarted sections (Env, PPS, LevAuth)
        result.IncompleteSections.Should().HaveCountGreaterThan(1);
        result.RevisionSections.Should().ContainSingle(t => t.SectionType == ProfileSectionType.DataTypes);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GetPendingReviewsAsync
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PendingReviews_ReturnsUnderReviewForIssm()
    {
        var system = await SeedSystemWithRolesAsync();
        await SeedSectionAsync(system.Id, ProfileSectionType.MissionAndPurpose, SspSectionStatus.UnderReview);
        await SeedSectionAsync(system.Id, ProfileSectionType.DataTypes, SspSectionStatus.Draft);

        var result = await _service.GetPendingReviewsAsync(IssmUserId);

        result.Should().ContainSingle();
        result[0].SectionType.Should().Be(ProfileSectionType.MissionAndPurpose);
        result[0].SystemId.Should().Be(system.Id);
    }

    [Fact]
    public async Task PendingReviews_NonIssm_ReturnsEmpty()
    {
        var system = await SeedSystemWithRolesAsync();
        await SeedSectionAsync(system.Id, ProfileSectionType.MissionAndPurpose, SspSectionStatus.UnderReview);

        var result = await _service.GetPendingReviewsAsync(UnauthorizedUserId);

        result.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // BusinessContext — SaveBusinessContextAsync
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SaveBusinessContext_HappyPath_CreatesDraft()
    {
        var system = await SeedSystemWithRolesAsync();
        var impl = new ControlImplementation
        {
            RegisteredSystemId = system.Id,
            ControlId = "AC-1",
            ImplementationStatus = ImplementationStatus.Planned
        };
        _db.ControlImplementations.Add(impl);
        await _db.SaveChangesAsync();

        var result = await _service.SaveBusinessContextAsync(
            system.Id, "AC-1", "Our access control procedures align with...", MoUserId);

        result.Should().NotBeNull();
        result.Content.Should().Be("Our access control procedures align with...");
        result.AuthoredBy.Should().Be(MoUserId);
    }

    [Fact]
    public async Task SaveBusinessContext_ControlNotFound_Throws()
    {
        var system = await SeedSystemWithRolesAsync();

        var act = () => _service.SaveBusinessContextAsync(
            system.Id, "NONEXISTENT-1", "content", MoUserId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*CONTROL_NOT_FOUND*");
    }

    [Fact]
    public async Task SaveBusinessContext_NonMO_Throws()
    {
        var system = await SeedSystemWithRolesAsync();
        _db.ControlImplementations.Add(new ControlImplementation
        {
            RegisteredSystemId = system.Id,
            ControlId = "AC-2",
            ImplementationStatus = ImplementationStatus.Planned
        });
        await _db.SaveChangesAsync();

        var act = () => _service.SaveBusinessContextAsync(
            system.Id, "AC-2", "content", IssmUserId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*UNAUTHORIZED*");
    }

    [Fact]
    public async Task SaveBusinessContext_UpdateExisting_UpdatesContent()
    {
        var system = await SeedSystemWithRolesAsync();
        var impl = new ControlImplementation
        {
            RegisteredSystemId = system.Id,
            ControlId = "AC-1",
            ImplementationStatus = ImplementationStatus.Planned
        };
        _db.ControlImplementations.Add(impl);
        await _db.SaveChangesAsync();

        await _service.SaveBusinessContextAsync(system.Id, "AC-1", "First draft", MoUserId);
        var result = await _service.SaveBusinessContextAsync(system.Id, "AC-1", "Updated draft", MoUserId);

        result.Content.Should().Be("Updated draft");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SetControlFlagAsync
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SetControlFlag_Issm_FlagsControl()
    {
        var system = await SeedSystemWithRolesAsync();

        await _service.SetControlFlagAsync(system.Id, "AC-1", true, IssmUserId);

        var flag = await _db.BusinessContextControlFlags
            .FirstOrDefaultAsync(f => f.RegisteredSystemId == system.Id && f.ControlId == "AC-1");
        flag.Should().NotBeNull();
        flag!.IsFlagged.Should().BeTrue();
    }

    [Fact]
    public async Task SetControlFlag_NonIssm_Throws()
    {
        var system = await SeedSystemWithRolesAsync();

        var act = () => _service.SetControlFlagAsync(system.Id, "AC-1", true, MoUserId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*UNAUTHORIZED*");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Full Governance Lifecycle
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullLifecycle_Draft_Submit_Review_ReEdit()
    {
        var system = await SeedSystemWithRolesAsync();

        // 1. Save draft
        var section = await _service.SaveDraftAsync(
            system.Id, ProfileSectionType.MissionAndPurpose,
            "{\"mission\":\"v1\"}", MoUserId);
        section.GovernanceStatus.Should().Be(SspSectionStatus.Draft);

        // 2. Submit
        var submitResult = await _service.SubmitForReviewAsync(system.Id, null, MoUserId);
        submitResult.SubmittedSections.Should().Contain(ProfileSectionType.MissionAndPurpose);

        // 3. Approve
        var reviewed = await _service.ReviewSectionAsync(
            system.Id, ProfileSectionType.MissionAndPurpose,
            ReviewDecision.Approve, IssmUserId);
        reviewed.GovernanceStatus.Should().Be(SspSectionStatus.Approved);
        reviewed.ApprovedContent.Should().Be("{\"mission\":\"v1\"}");

        // 4. Re-edit → preserves approved content
        var reEdited = await _service.SaveDraftAsync(
            system.Id, ProfileSectionType.MissionAndPurpose,
            "{\"mission\":\"v2\"}", MoUserId);
        reEdited.GovernanceStatus.Should().Be(SspSectionStatus.Draft);
        reEdited.ApprovedContent.Should().Be("{\"mission\":\"v1\"}");
        reEdited.DraftContent.Should().Be("{\"mission\":\"v2\"}");
    }

    [Fact]
    public async Task FullLifecycle_Submit_Withdraw_ReSubmit()
    {
        var system = await SeedSystemWithRolesAsync();

        await _service.SaveDraftAsync(
            system.Id, ProfileSectionType.DataTypes,
            "{\"types\":\"PII\"}", MoUserId);

        // Submit
        await _service.SubmitForReviewAsync(
            system.Id, [ProfileSectionType.DataTypes], MoUserId);

        // Withdraw
        var withdrawn = await _service.WithdrawSectionAsync(
            system.Id, [ProfileSectionType.DataTypes], MoUserId);
        withdrawn.WithdrawnSections.Should().Contain(ProfileSectionType.DataTypes);

        // Re-submit
        var resubmitted = await _service.SubmitForReviewAsync(
            system.Id, [ProfileSectionType.DataTypes], MoUserId);
        resubmitted.SubmittedSections.Should().Contain(ProfileSectionType.DataTypes);
    }

    [Fact]
    public async Task FullLifecycle_RequestRevision_ReSubmit()
    {
        var system = await SeedSystemWithRolesAsync();

        await _service.SaveDraftAsync(
            system.Id, ProfileSectionType.UsersAndAccess,
            "{\"users\":\"admins\"}", MoUserId);

        await _service.SubmitForReviewAsync(
            system.Id, [ProfileSectionType.UsersAndAccess], MoUserId);

        // Request revision
        var reviewed = await _service.ReviewSectionAsync(
            system.Id, ProfileSectionType.UsersAndAccess,
            ReviewDecision.RequestRevision, IssmUserId, "Add access methods");
        reviewed.GovernanceStatus.Should().Be(SspSectionStatus.NeedsRevision);
        reviewed.ReviewerComments.Should().Be("Add access methods");

        // MO fixes and resubmits
        await _service.SaveDraftAsync(
            system.Id, ProfileSectionType.UsersAndAccess,
            "{\"users\":\"admins\",\"access\":\"VPN\"}", MoUserId);

        var resubmitted = await _service.SubmitForReviewAsync(
            system.Id, [ProfileSectionType.UsersAndAccess], MoUserId);
        resubmitted.SubmittedSections.Should().Contain(ProfileSectionType.UsersAndAccess);
    }
}
