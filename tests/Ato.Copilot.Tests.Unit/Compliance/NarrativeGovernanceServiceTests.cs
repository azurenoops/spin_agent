using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Compliance;

/// <summary>
/// Unit tests for NarrativeGovernanceService (Feature 024).
/// T041: US1 — Version history, diff, rollback
/// T043: US2 — Submit, review, batch review, edit guards
/// T045: US3 — Approval progress dashboard
/// T046: US4 — Batch submit
/// T047: US5 — Concurrency validation
/// </summary>
public class NarrativeGovernanceServiceTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly AtoCopilotContext _db;
    private readonly NarrativeGovernanceService _service;
    private readonly SspService _sspService;

    public NarrativeGovernanceServiceTests()
    {
        var dbName = $"NarrativeGov_{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddDbContext<AtoCopilotContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();
        _db = _serviceProvider.GetRequiredService<AtoCopilotContext>();
        var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
        _service = new NarrativeGovernanceService(
            scopeFactory,
            Mock.Of<ILogger<NarrativeGovernanceService>>());
        _sspService = new SspService(
            scopeFactory,
            Mock.Of<ILogger<SspService>>());
    }

    public void Dispose() => _serviceProvider.Dispose();

    // ─── Seed Helpers ────────────────────────────────────────────────────────

    private async Task<(RegisteredSystem System, ControlImplementation Impl)> SeedSystemWithNarrativeAsync(
        string narrative = "Test narrative content",
        SspSectionStatus status = SspSectionStatus.Draft)
    {
        var system = new RegisteredSystem
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Test System",
            Acronym = "TST",
            Description = "Test system",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure Government"
        };
        _db.RegisteredSystems.Add(system);

        var impl = new ControlImplementation
        {
            Id = Guid.NewGuid().ToString(),
            RegisteredSystemId = system.Id,
            ControlId = "AC-1",
            Narrative = narrative,
            ImplementationStatus = ImplementationStatus.Implemented,
            ApprovalStatus = status,
            CurrentVersion = 1
        };
        _db.ControlImplementations.Add(impl);

        // Add initial version
        _db.Set<NarrativeVersion>().Add(new NarrativeVersion
        {
            Id = Guid.NewGuid().ToString(),
            ControlImplementationId = impl.Id,
            VersionNumber = 1,
            Content = narrative,
            Status = status,
            AuthoredBy = "seed-user",
            AuthoredAt = DateTime.UtcNow.AddHours(-1)
        });

        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return (system, impl);
    }

    // =========================================================================
    // T041: US1 — GetNarrativeHistoryAsync Tests
    // =========================================================================

    [Fact]
    public async Task GetNarrativeHistory_ReturnsVersions_NewestFirst()
    {
        var (system, impl) = await SeedSystemWithNarrativeAsync();

        // Add a second version
        _db.Set<NarrativeVersion>().Add(new NarrativeVersion
        {
            Id = Guid.NewGuid().ToString(),
            ControlImplementationId = impl.Id,
            VersionNumber = 2,
            Content = "Updated content",
            Status = SspSectionStatus.Draft,
            AuthoredBy = "user-2",
            AuthoredAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var (versions, totalCount) = await _service.GetNarrativeHistoryAsync(
            system.Id, "AC-1");

        totalCount.Should().Be(2);
        versions.Should().HaveCount(2);
        versions[0].VersionNumber.Should().Be(2); // newest first
        versions[1].VersionNumber.Should().Be(1);
    }

    [Fact]
    public async Task GetNarrativeHistory_Pagination_ReturnsCorrectPage()
    {
        var (system, impl) = await SeedSystemWithNarrativeAsync();

        // Add 4 more versions (total 5)
        for (int i = 2; i <= 5; i++)
        {
            _db.Set<NarrativeVersion>().Add(new NarrativeVersion
            {
                Id = Guid.NewGuid().ToString(),
                ControlImplementationId = impl.Id,
                VersionNumber = i,
                Content = $"Content v{i}",
                Status = SspSectionStatus.Draft,
                AuthoredBy = "user",
                AuthoredAt = DateTime.UtcNow.AddMinutes(i)
            });
        }
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var (versions, totalCount) = await _service.GetNarrativeHistoryAsync(
            system.Id, "AC-1", page: 2, pageSize: 2);

        totalCount.Should().Be(5);
        versions.Should().HaveCount(2);
        versions[0].VersionNumber.Should().Be(3);
        versions[1].VersionNumber.Should().Be(2);
    }

    [Fact]
    public async Task GetNarrativeHistory_EmptyHistory_ReturnsEmpty()
    {
        var system = new RegisteredSystem
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Empty System",
            Acronym = "EMP",
            Description = "No narratives",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure"
        };
        _db.RegisteredSystems.Add(system);
        _db.ControlImplementations.Add(new ControlImplementation
        {
            Id = Guid.NewGuid().ToString(),
            RegisteredSystemId = system.Id,
            ControlId = "AC-1",
            ImplementationStatus = ImplementationStatus.Planned,
            ApprovalStatus = SspSectionStatus.NotStarted,
            CurrentVersion = 0
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var (versions, totalCount) = await _service.GetNarrativeHistoryAsync(
            system.Id, "AC-1");

        totalCount.Should().Be(0);
        versions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetNarrativeHistory_SystemNotFound_Throws()
    {
        var act = () => _service.GetNarrativeHistoryAsync("nonexistent", "AC-1");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*SYSTEM_NOT_FOUND*");
    }

    // =========================================================================
    // T041: US1 — GetNarrativeDiffAsync Tests
    // =========================================================================

    [Fact]
    public async Task GetNarrativeDiff_ValidVersions_ReturnsDiff()
    {
        var (system, impl) = await SeedSystemWithNarrativeAsync("Original content");

        _db.Set<NarrativeVersion>().Add(new NarrativeVersion
        {
            Id = Guid.NewGuid().ToString(),
            ControlImplementationId = impl.Id,
            VersionNumber = 2,
            Content = "Updated content",
            Status = SspSectionStatus.Draft,
            AuthoredBy = "user-2",
            AuthoredAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var diff = await _service.GetNarrativeDiffAsync(
            system.Id, "AC-1", 1, 2);

        (diff.LinesAdded + diff.LinesRemoved).Should().BeGreaterThan(0);
        diff.UnifiedDiff.Should().NotBeNullOrWhiteSpace();
        diff.FromVersion.Should().Be(1);
        diff.ToVersion.Should().Be(2);
    }

    [Fact]
    public async Task GetNarrativeDiff_SameContent_NoChanges()
    {
        var (system, impl) = await SeedSystemWithNarrativeAsync("Same content");

        _db.Set<NarrativeVersion>().Add(new NarrativeVersion
        {
            Id = Guid.NewGuid().ToString(),
            ControlImplementationId = impl.Id,
            VersionNumber = 2,
            Content = "Same content",
            Status = SspSectionStatus.Draft,
            AuthoredBy = "user-2",
            AuthoredAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var diff = await _service.GetNarrativeDiffAsync(
            system.Id, "AC-1", 1, 2);

        (diff.LinesAdded + diff.LinesRemoved).Should().Be(0);
    }

    [Fact]
    public async Task GetNarrativeDiff_NonExistentVersion_Throws()
    {
        var (system, _) = await SeedSystemWithNarrativeAsync();

        var act = () => _service.GetNarrativeDiffAsync(
            system.Id, "AC-1", 1, 99);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*VERSION_NOT_FOUND*");
    }

    // =========================================================================
    // T041: US1 — RollbackNarrativeAsync Tests
    // =========================================================================

    [Fact]
    public async Task RollbackNarrative_CreatesNewVersion_WithPriorContent()
    {
        var (system, impl) = await SeedSystemWithNarrativeAsync("Original v1");

        // Add v2
        _db.Set<NarrativeVersion>().Add(new NarrativeVersion
        {
            Id = Guid.NewGuid().ToString(),
            ControlImplementationId = impl.Id,
            VersionNumber = 2,
            Content = "Updated v2",
            Status = SspSectionStatus.Draft,
            AuthoredBy = "user",
            AuthoredAt = DateTime.UtcNow
        });
        var implEntity = await _db.ControlImplementations.FindAsync(impl.Id);
        implEntity!.CurrentVersion = 2;
        implEntity.Narrative = "Updated v2";
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var newVersion = await _service.RollbackNarrativeAsync(
            system.Id, "AC-1", 1, "rollback-user");

        newVersion.VersionNumber.Should().Be(3);
        newVersion.Content.Should().Be("Original v1");
        newVersion.AuthoredBy.Should().Be("rollback-user");
        newVersion.ChangeReason.Should().Contain("Rolled back");
    }

    [Fact]
    public async Task RollbackNarrative_NonExistentTarget_Throws()
    {
        var (system, _) = await SeedSystemWithNarrativeAsync();

        var act = () => _service.RollbackNarrativeAsync(
            system.Id, "AC-1", 99, "user");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*VERSION_NOT_FOUND*");
    }

    [Fact]
    public async Task RollbackNarrative_UnderReview_Throws()
    {
        var (system, impl) = await SeedSystemWithNarrativeAsync(
            status: SspSectionStatus.UnderReview);

        var act = () => _service.RollbackNarrativeAsync(
            system.Id, "AC-1", 1, "user");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*UNDER_REVIEW*");
    }

    // =========================================================================
    // T043: US2 — SubmitNarrativeAsync Tests
    // =========================================================================

    [Fact]
    public async Task SubmitNarrative_Draft_TransitionsToUnderReview()
    {
        var (system, _) = await SeedSystemWithNarrativeAsync(
            status: SspSectionStatus.Draft);

        var version = await _service.SubmitNarrativeAsync(
            system.Id, "AC-1", "isso-user");

        version.Status.Should().Be(SspSectionStatus.UnderReview);
        version.SubmittedBy.Should().Be("isso-user");
        version.SubmittedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task SubmitNarrative_NeedsRevision_TransitionsToUnderReview()
    {
        var (system, _) = await SeedSystemWithNarrativeAsync(
            status: SspSectionStatus.NeedsRevision);

        var version = await _service.SubmitNarrativeAsync(
            system.Id, "AC-1", "isso-user");

        version.Status.Should().Be(SspSectionStatus.UnderReview);
    }

    [Fact]
    public async Task SubmitNarrative_AlreadyUnderReview_Throws()
    {
        var (system, _) = await SeedSystemWithNarrativeAsync(
            status: SspSectionStatus.UnderReview);

        var act = () => _service.SubmitNarrativeAsync(
            system.Id, "AC-1", "user");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*INVALID_STATUS*");
    }

    [Fact]
    public async Task SubmitNarrative_Approved_Throws()
    {
        var (system, _) = await SeedSystemWithNarrativeAsync(
            status: SspSectionStatus.Approved);

        var act = () => _service.SubmitNarrativeAsync(
            system.Id, "AC-1", "user");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*INVALID_STATUS*");
    }

    // =========================================================================
    // T043: US2 — ReviewNarrativeAsync Tests
    // =========================================================================

    [Fact]
    public async Task ReviewNarrative_Approve_SetsApprovedAndApprovedVersionId()
    {
        var (system, impl) = await SeedSystemWithNarrativeAsync(
            status: SspSectionStatus.UnderReview);

        var review = await _service.ReviewNarrativeAsync(
            system.Id, "AC-1", ReviewDecision.Approve, "issm-user");

        review.Decision.Should().Be(ReviewDecision.Approve);
        review.ReviewedBy.Should().Be("issm-user");

        // Verify ControlImplementation got updated
        _db.ChangeTracker.Clear();
        var updatedImpl = await _db.ControlImplementations.FindAsync(impl.Id);
        updatedImpl!.ApprovalStatus.Should().Be(SspSectionStatus.Approved);
        updatedImpl.ApprovedVersionId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ReviewNarrative_RequestRevision_SetsNeedsRevision()
    {
        var (system, impl) = await SeedSystemWithNarrativeAsync(
            status: SspSectionStatus.UnderReview);

        var review = await _service.ReviewNarrativeAsync(
            system.Id, "AC-1", ReviewDecision.RequestRevision, "issm-user",
            "Needs more detail in section 3.");

        review.Decision.Should().Be(ReviewDecision.RequestRevision);
        review.ReviewerComments.Should().Be("Needs more detail in section 3.");

        _db.ChangeTracker.Clear();
        var updatedImpl = await _db.ControlImplementations.FindAsync(impl.Id);
        updatedImpl!.ApprovalStatus.Should().Be(SspSectionStatus.NeedsRevision);
    }

    [Fact]
    public async Task ReviewNarrative_RequestRevision_NoComments_Throws()
    {
        var (system, _) = await SeedSystemWithNarrativeAsync(
            status: SspSectionStatus.UnderReview);

        var act = () => _service.ReviewNarrativeAsync(
            system.Id, "AC-1", ReviewDecision.RequestRevision, "issm-user");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*COMMENTS_REQUIRED*");
    }

    [Fact]
    public async Task ReviewNarrative_NotUnderReview_Throws()
    {
        var (system, _) = await SeedSystemWithNarrativeAsync(
            status: SspSectionStatus.Draft);

        var act = () => _service.ReviewNarrativeAsync(
            system.Id, "AC-1", ReviewDecision.Approve, "issm-user");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*INVALID_STATUS*");
    }

    // =========================================================================
    // T043: US2 — BatchReviewNarrativesAsync Tests
    // =========================================================================

    [Fact]
    public async Task BatchReview_Approve_ReviewsAllUnderReviewInFamily()
    {
        var system = new RegisteredSystem
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Batch System",
            Acronym = "BAT",
            Description = "Test",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure"
        };
        _db.RegisteredSystems.Add(system);

        // Add 3 AC controls: 2 UnderReview, 1 Draft
        foreach (var (controlId, status) in new[] {
            ("AC-1", SspSectionStatus.UnderReview),
            ("AC-2", SspSectionStatus.UnderReview),
            ("AC-3", SspSectionStatus.Draft) })
        {
            var id = Guid.NewGuid().ToString();
            _db.ControlImplementations.Add(new ControlImplementation
            {
                Id = id,
                RegisteredSystemId = system.Id,
                ControlId = controlId,
                Narrative = $"{controlId} text",
                ImplementationStatus = ImplementationStatus.Implemented,
                ApprovalStatus = status,
                CurrentVersion = 1
            });
            _db.Set<NarrativeVersion>().Add(new NarrativeVersion
            {
                Id = Guid.NewGuid().ToString(),
                ControlImplementationId = id,
                VersionNumber = 1,
                Content = $"{controlId} text",
                Status = status,
                AuthoredBy = "user",
                AuthoredAt = DateTime.UtcNow
            });
        }
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var (reviewed, skipped) = await _service.BatchReviewNarrativesAsync(
            system.Id, ReviewDecision.Approve, "issm-user",
            familyFilter: "AC");

        reviewed.Should().HaveCount(2);
        skipped.Should().BeEmpty(); // AC-3 (Draft) is not queried
    }

    [Fact]
    public async Task BatchReview_MutuallyExclusiveFilters_Throws()
    {
        var (system, _) = await SeedSystemWithNarrativeAsync();

        var act = () => _service.BatchReviewNarrativesAsync(
            system.Id, ReviewDecision.Approve, "issm-user",
            familyFilter: "AC", controlIds: new[] { "AC-1" });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*MUTUALLY_EXCLUSIVE_FILTERS*");
    }

    // =========================================================================
    // T043: US2 — Edit Guard (UNDER_REVIEW) Tests via WriteNarrativeAsync
    // =========================================================================

    [Fact]
    public async Task WriteNarrative_UnderReview_RejectsWithUnderReviewError()
    {
        var (system, impl) = await SeedSystemWithNarrativeAsync(
            status: SspSectionStatus.UnderReview);

        var act = () => _sspService.WriteNarrativeAsync(
            system.Id, "AC-1", "New content", "Implemented", "user");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*UNDER_REVIEW*");
    }

    // =========================================================================
    // T045: US3 — GetNarrativeApprovalProgressAsync Tests
    // =========================================================================

    [Fact]
    public async Task ApprovalProgress_CorrectStatusCounts()
    {
        var system = new RegisteredSystem
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Progress System",
            Acronym = "PRG",
            Description = "Test",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure"
        };
        _db.RegisteredSystems.Add(system);

        var statuses = new[]
        {
            ("AC-1", SspSectionStatus.Approved, "Narrative"),
            ("AC-2", SspSectionStatus.UnderReview, "Narrative"),
            ("AC-3", SspSectionStatus.Draft, "Narrative"),
            ("SI-1", SspSectionStatus.Approved, "Narrative"),
            ("SI-2", SspSectionStatus.NeedsRevision, "Narrative"),
        };

        foreach (var (controlId, status, narrative) in statuses)
        {
            _db.ControlImplementations.Add(new ControlImplementation
            {
                Id = Guid.NewGuid().ToString(),
                RegisteredSystemId = system.Id,
                ControlId = controlId,
                Narrative = narrative,
                ImplementationStatus = ImplementationStatus.Implemented,
                ApprovalStatus = status,
                CurrentVersion = 1
            });
        }
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var report = await _service.GetNarrativeApprovalProgressAsync(system.Id);

        report.TotalControls.Should().Be(5);
        report.TotalApproved.Should().Be(2);
        report.TotalUnderReview.Should().Be(1);
        report.TotalDraft.Should().Be(1);
        report.TotalNeedsRevision.Should().Be(1);
        report.OverallApprovalPercent.Should().Be(40.0);
    }

    [Fact]
    public async Task ApprovalProgress_ReviewQueue_ContainsUnderReviewControls()
    {
        var system = new RegisteredSystem
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Queue System",
            Acronym = "QUE",
            Description = "Test",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure"
        };
        _db.RegisteredSystems.Add(system);

        _db.ControlImplementations.Add(new ControlImplementation
        {
            Id = Guid.NewGuid().ToString(),
            RegisteredSystemId = system.Id,
            ControlId = "AC-1",
            Narrative = "Text",
            ImplementationStatus = ImplementationStatus.Implemented,
            ApprovalStatus = SspSectionStatus.UnderReview,
            CurrentVersion = 1
        });
        _db.ControlImplementations.Add(new ControlImplementation
        {
            Id = Guid.NewGuid().ToString(),
            RegisteredSystemId = system.Id,
            ControlId = "AC-2",
            Narrative = "Text",
            ImplementationStatus = ImplementationStatus.Implemented,
            ApprovalStatus = SspSectionStatus.Approved,
            CurrentVersion = 1
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var report = await _service.GetNarrativeApprovalProgressAsync(system.Id);

        report.ReviewQueue.Should().ContainSingle().Which.Should().Be("AC-1");
    }

    [Fact]
    public async Task ApprovalProgress_StalenessWarnings_ForDraftWithContent()
    {
        var system = new RegisteredSystem
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Stale System",
            Acronym = "STL",
            Description = "Test",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure"
        };
        _db.RegisteredSystems.Add(system);

        _db.ControlImplementations.Add(new ControlImplementation
        {
            Id = Guid.NewGuid().ToString(),
            RegisteredSystemId = system.Id,
            ControlId = "AC-1",
            Narrative = "Some narrative content",
            TechnicalNarrative = "Some narrative content",
            ImplementationStatus = ImplementationStatus.Implemented,
            ApprovalStatus = SspSectionStatus.Draft,
            CurrentVersion = 1
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var report = await _service.GetNarrativeApprovalProgressAsync(system.Id);

        report.StalenessWarnings.Should().ContainSingle();
        report.StalenessWarnings[0].ControlId.Should().Be("AC-1");
    }

    [Fact]
    public async Task ApprovalProgress_FamilyFilter_FiltersResults()
    {
        var system = new RegisteredSystem
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Filter System",
            Acronym = "FLT",
            Description = "Test",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure"
        };
        _db.RegisteredSystems.Add(system);

        _db.ControlImplementations.Add(new ControlImplementation
        {
            Id = Guid.NewGuid().ToString(),
            RegisteredSystemId = system.Id,
            ControlId = "AC-1",
            Narrative = "Text",
            ImplementationStatus = ImplementationStatus.Implemented,
            ApprovalStatus = SspSectionStatus.Approved,
            CurrentVersion = 1
        });
        _db.ControlImplementations.Add(new ControlImplementation
        {
            Id = Guid.NewGuid().ToString(),
            RegisteredSystemId = system.Id,
            ControlId = "SI-1",
            Narrative = "Text",
            ImplementationStatus = ImplementationStatus.Implemented,
            ApprovalStatus = SspSectionStatus.Draft,
            CurrentVersion = 1
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var report = await _service.GetNarrativeApprovalProgressAsync(
            system.Id, "AC");

        report.TotalControls.Should().Be(1);
        report.FamilyBreakdowns.Should().ContainSingle()
            .Which.Family.Should().Be("AC");
    }

    [Fact]
    public async Task ApprovalProgress_SystemNotFound_Throws()
    {
        var act = () => _service.GetNarrativeApprovalProgressAsync("nonexistent");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*SYSTEM_NOT_FOUND*");
    }

    // =========================================================================
    // T046: US4 — BatchSubmitNarrativesAsync Tests
    // =========================================================================

    [Fact]
    public async Task BatchSubmit_AllDraftSubmitted()
    {
        var system = new RegisteredSystem
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Batch Submit System",
            Acronym = "BSS",
            Description = "Test",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure"
        };
        _db.RegisteredSystems.Add(system);

        foreach (var controlId in new[] { "AC-1", "AC-2" })
        {
            var id = Guid.NewGuid().ToString();
            _db.ControlImplementations.Add(new ControlImplementation
            {
                Id = id,
                RegisteredSystemId = system.Id,
                ControlId = controlId,
                Narrative = $"{controlId} content",
                ImplementationStatus = ImplementationStatus.Implemented,
                ApprovalStatus = SspSectionStatus.Draft,
                CurrentVersion = 1
            });
            _db.Set<NarrativeVersion>().Add(new NarrativeVersion
            {
                Id = Guid.NewGuid().ToString(),
                ControlImplementationId = id,
                VersionNumber = 1,
                Content = $"{controlId} content",
                Status = SspSectionStatus.Draft,
                AuthoredBy = "user",
                AuthoredAt = DateTime.UtcNow
            });
        }
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var result = await _service.BatchSubmitNarrativesAsync(system.Id);

        result.SubmittedCount.Should().Be(2);
        result.SkippedCount.Should().Be(0);
        result.SubmittedControlIds.Should().HaveCount(2);
    }

    [Fact]
    public async Task BatchSubmit_MixedStatuses_SkipsNonDraft()
    {
        var system = new RegisteredSystem
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Mixed System",
            Acronym = "MIX",
            Description = "Test",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure"
        };
        _db.RegisteredSystems.Add(system);

        var statuses = new[] {
            ("AC-1", SspSectionStatus.Draft),
            ("AC-2", SspSectionStatus.Approved),
            ("AC-3", SspSectionStatus.UnderReview)
        };
        foreach (var (controlId, status) in statuses)
        {
            var id = Guid.NewGuid().ToString();
            _db.ControlImplementations.Add(new ControlImplementation
            {
                Id = id,
                RegisteredSystemId = system.Id,
                ControlId = controlId,
                Narrative = $"{controlId} content",
                ImplementationStatus = ImplementationStatus.Implemented,
                ApprovalStatus = status,
                CurrentVersion = 1
            });
            _db.Set<NarrativeVersion>().Add(new NarrativeVersion
            {
                Id = Guid.NewGuid().ToString(),
                ControlImplementationId = id,
                VersionNumber = 1,
                Content = $"{controlId} content",
                Status = status,
                AuthoredBy = "user",
                AuthoredAt = DateTime.UtcNow
            });
        }
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var result = await _service.BatchSubmitNarrativesAsync(system.Id);

        result.SubmittedCount.Should().Be(1);
        result.SubmittedControlIds.Should().Contain("AC-1");
        result.SkippedCount.Should().Be(2);
    }

    [Fact]
    public async Task BatchSubmit_FamilyFilter_OnlySubmitsMatchingFamily()
    {
        var system = new RegisteredSystem
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Family System",
            Acronym = "FAM",
            Description = "Test",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure"
        };
        _db.RegisteredSystems.Add(system);

        foreach (var controlId in new[] { "AC-1", "SI-1" })
        {
            var id = Guid.NewGuid().ToString();
            _db.ControlImplementations.Add(new ControlImplementation
            {
                Id = id,
                RegisteredSystemId = system.Id,
                ControlId = controlId,
                Narrative = $"{controlId} content",
                ImplementationStatus = ImplementationStatus.Implemented,
                ApprovalStatus = SspSectionStatus.Draft,
                CurrentVersion = 1
            });
            _db.Set<NarrativeVersion>().Add(new NarrativeVersion
            {
                Id = Guid.NewGuid().ToString(),
                ControlImplementationId = id,
                VersionNumber = 1,
                Content = $"{controlId} content",
                Status = SspSectionStatus.Draft,
                AuthoredBy = "user",
                AuthoredAt = DateTime.UtcNow
            });
        }
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var result = await _service.BatchSubmitNarrativesAsync(
            system.Id, familyFilter: "AC");

        result.SubmittedCount.Should().Be(1);
        result.SubmittedControlIds.Should().Contain("AC-1");
    }

    [Fact]
    public async Task BatchSubmit_NoDraftNarratives_Throws()
    {
        var system = new RegisteredSystem
        {
            Id = Guid.NewGuid().ToString(),
            Name = "No Draft System",
            Acronym = "NDR",
            Description = "Test",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure"
        };
        _db.RegisteredSystems.Add(system);

        _db.ControlImplementations.Add(new ControlImplementation
        {
            Id = Guid.NewGuid().ToString(),
            RegisteredSystemId = system.Id,
            ControlId = "AC-1",
            Narrative = "Text",
            ImplementationStatus = ImplementationStatus.Implemented,
            ApprovalStatus = SspSectionStatus.Approved,
            CurrentVersion = 1
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var act = () => _service.BatchSubmitNarrativesAsync(system.Id);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*NO_DRAFT_NARRATIVES*");
    }

    // =========================================================================
    // T047: US5 — Concurrency Validation (expected_version) Tests
    // =========================================================================

    [Fact]
    public async Task WriteNarrative_ExpectedVersionMatch_Succeeds()
    {
        var (system, impl) = await SeedSystemWithNarrativeAsync();

        // Should not throw when expectedVersion matches CurrentVersion
        var result = await _sspService.WriteNarrativeAsync(
            system.Id, "AC-1", "Updated content", "Implemented",
            "user", expectedVersion: 1);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task WriteNarrative_UpdatesTechnicalAndPreservesPolicyNarrative()
    {
        // Arrange
        var (system, seeded) = await SeedSystemWithNarrativeAsync();
        var implementation = await _db.ControlImplementations.FindAsync(seeded.Id);
        implementation!.PolicyNarrative = "Existing policy content";
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        // Act
        var result = await _sspService.WriteNarrativeAsync(
            system.Id, "AC-1", "Updated technical content", "Implemented", "user");

        // Assert
        result.Narrative.Should().Be("Updated technical content");
        result.TechnicalNarrative.Should().Be("Updated technical content");
        result.PolicyNarrative.Should().Be("Existing policy content");
    }

    [Fact]
    public async Task WriteNarrative_ExpectedVersionMismatch_Throws()
    {
        var (system, _) = await SeedSystemWithNarrativeAsync();

        var act = () => _sspService.WriteNarrativeAsync(
            system.Id, "AC-1", "New content", "Implemented",
            "user", expectedVersion: 99);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*CONCURRENCY_CONFLICT*");
    }

    [Fact]
    public async Task WriteNarrative_NullExpectedVersion_BypassesCheck()
    {
        var (system, _) = await SeedSystemWithNarrativeAsync();

        // Should succeed without concurrency check
        var result = await _sspService.WriteNarrativeAsync(
            system.Id, "AC-1", "Content without version check", "Implemented",
            "user");

        result.Should().NotBeNull();
    }
}
