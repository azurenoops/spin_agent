using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Compliance;

public class DualNarrativeServiceTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly AtoCopilotContext _db;
    private readonly DualNarrativeService _service;
    private readonly IDisposable _tenantScope;
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-0001-0001-0001-aaaaaaaaaaaa");

    public DualNarrativeServiceTests()
    {
        var databaseName = $"DualNarrative_{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContextAccessor, TenantContextAccessor>();
        services.AddDbContext<AtoCopilotContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        _provider = services.BuildServiceProvider();
        var tenantAccessor = (TenantContextAccessor)_provider.GetRequiredService<ITenantContextAccessor>();
        _tenantScope = tenantAccessor.Push(new TenantContext(TenantId));
        _db = _provider.GetRequiredService<AtoCopilotContext>();
        _service = new DualNarrativeService(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<DualNarrativeService>.Instance);
    }

    [Fact]
    public async Task GetAsync_SplitsEvidenceAndIncludesCombinedInBothHalves()
    {
        // Arrange
        var implementation = await SeedImplementationAsync();
        _db.EvidenceArtifacts.AddRange(
            CreateEvidence("policy", implementation, EvidenceNarrativeType.Policy),
            CreateEvidence("technical", implementation, EvidenceNarrativeType.Technical),
            CreateEvidence("combined", implementation, EvidenceNarrativeType.Combined),
            CreateEvidence("unclassified", implementation, EvidenceNarrativeType.Unclassified));
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.GetAsync("system-1", "AC-1");

        // Assert
        result.PolicyEvidence.Select(item => item.Id).Should().BeEquivalentTo("policy", "combined");
        result.TechnicalEvidence.Select(item => item.Id).Should().BeEquivalentTo("technical", "combined");
        result.UnclassifiedEvidence.Select(item => item.Id).Should().Equal("unclassified");
    }

    [Fact]
    public async Task ConcurrentWrite_ReviewStartedAfterRead_RejectsStaleSave()
    {
        // Arrange
        var implementation = await SeedImplementationAsync();
        using var scope = _provider.CreateScope();
        var otherDb = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var reviewed = await otherDb.ControlImplementations.SingleAsync();
        reviewed.ApprovalStatus = SspSectionStatus.UnderReview;
        await otherDb.SaveChangesAsync();
        implementation.TechnicalNarrative = "Stale write";
        implementation.CurrentVersion++;

        // Act
        var act = () => _db.SaveChangesAsync();

        // Assert
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
        await otherDb.Entry(reviewed).ReloadAsync();
        reviewed.TechnicalNarrative.Should().Be("Existing technical");
        reviewed.ApprovalStatus.Should().Be(SspSectionStatus.UnderReview);
    }

    [Fact]
    public async Task UpdateAsync_UnderReview_RejectsWithoutChangingContentOrHistory()
    {
        // Arrange
        var implementation = await SeedImplementationAsync();
        implementation.ApprovalStatus = SspSectionStatus.UnderReview;
        await _db.SaveChangesAsync();

        // Act
        var act = () => _service.UpdateAsync("system-1", "AC-1", "Changed policy", true,
            "Changed technical", true, "Compliance.Analyst", "author");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("UNDER_REVIEW:*");
        _db.ChangeTracker.Clear();
        var saved = await _db.ControlImplementations.SingleAsync();
        saved.PolicyNarrative.Should().Be("Existing policy");
        saved.TechnicalNarrative.Should().Be("Existing technical");
        saved.ApprovalStatus.Should().Be(SspSectionStatus.UnderReview);
        saved.AuthoredBy.Should().Be("seed-user");
        saved.CurrentVersion.Should().Be(1);
        (await _db.NarrativeVersions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task UpdateAsync_ApprovedPolicyEdit_CreatesAttributedSnapshotAndPreservesApprovedHistory()
    {
        // Arrange
        var implementation = await SeedImplementationAsync();
        implementation.ApprovalStatus = SspSectionStatus.Approved;
        var approved = new NarrativeVersion
        {
            Id = "approved-version", TenantId = TenantId, ControlImplementationId = implementation.Id,
            VersionNumber = 1, Content = "Existing technical", Status = SspSectionStatus.Approved,
            AuthoredBy = "original-author", SnapshotJson = NarrativeContentSnapshot.Capture(implementation),
        };
        implementation.ApprovedVersionId = approved.Id;
        _db.NarrativeVersions.Add(approved);
        await _db.SaveChangesAsync();

        // Act
        await _service.UpdateAsync("system-1", "AC-1", "New policy", true,
            null, false, "Compliance.Analyst", "policy-author");

        // Assert
        _db.ChangeTracker.Clear();
        var saved = await _db.ControlImplementations.SingleAsync();
        saved.CurrentVersion.Should().Be(2);
        saved.ApprovalStatus.Should().Be(SspSectionStatus.Draft);
        saved.ApprovedVersionId.Should().Be(approved.Id);
        var versions = await _db.NarrativeVersions.OrderBy(version => version.VersionNumber).ToListAsync();
        versions.Should().HaveCount(2);
        versions[0].Status.Should().Be(SspSectionStatus.Approved);
        versions[0].AuthoredBy.Should().Be("original-author");
        versions[1].AuthoredBy.Should().Be("policy-author");
        versions[1].TenantId.Should().Be(TenantId);
        versions[1].Status.Should().Be(SspSectionStatus.Draft);
        versions[1].ChangeReason.Should().Contain("Policy");
        NarrativeContentSnapshot.Restore(saved, versions[1].SnapshotJson!);
        saved.PolicyNarrative.Should().Be("New policy");
        saved.TechnicalNarrative.Should().Be("Existing technical");
    }

    [Fact]
    public async Task UpdateAsync_StaleExpectedVersion_RejectsWithoutHistory()
    {
        // Arrange
        await SeedImplementationAsync();

        // Act
        var act = () => _service.UpdateAsync("system-1", "AC-1", "Stale", true,
            null, false, "Compliance.Analyst", "author", expectedVersion: 0);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("CONCURRENCY_CONFLICT:*");
        _db.ChangeTracker.Clear();
        (await _db.ControlImplementations.SingleAsync()).PolicyNarrative.Should().Be("Existing policy");
        (await _db.NarrativeVersions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task UpdateAsync_PolicyOnly_LeavesTechnicalNarrativeUnchanged()
    {
        // Arrange
        await SeedImplementationAsync();

        // Act
        var result = await _service.UpdateAsync(
            "system-1", "AC-1", "Updated policy", updatePolicy: true,
            technicalNarrative: null, updateTechnical: false,
            role: "Compliance.Analyst", authoredBy: "analyst-1");

        // Assert
        result.PolicyNarrative.Should().Be("Updated policy");
        result.TechnicalNarrative.Should().Be("Existing technical");
        result.CurrentVersion.Should().Be(2);
        result.ApprovalStatus.Should().Be(SspSectionStatus.Draft);
        result.AuthoredBy.Should().Be("analyst-1");
        _db.ChangeTracker.Clear();
        var saved = await _db.ControlImplementations.SingleAsync();
        saved.AuthoredBy.Should().Be("analyst-1");
        saved.ModifiedAt.Should().NotBeNull();
    }

    [Theory]
    [InlineData(false, "Human technical")]
    [InlineData(true, "Human technical")]
    [InlineData(true, null)]
    [InlineData(true, "")]
    public async Task UpdateAsync_ProvenanceTracksTechnicalEdits(bool updateTechnical, string? technical)
    {
        // Arrange
        var implementation = await SeedImplementationAsync();
        implementation.AiSuggested = true;
        implementation.IsAutoPopulated = true;
        implementation.ImplementationStatus = ImplementationStatus.Planned;
        await _db.SaveChangesAsync();

        // Act
        await _service.UpdateAsync("system-1", "AC-1", "Human policy", true,
            technical, updateTechnical, "Compliance.Analyst", "author");

        // Assert
        _db.ChangeTracker.Clear();
        var saved = await _db.ControlImplementations.SingleAsync();
        saved.AiSuggested.Should().Be(!updateTechnical);
        saved.IsAutoPopulated.Should().Be(!updateTechnical);
        saved.IsManuallyCustomized.Should().Be(updateTechnical);
        saved.ImplementationStatus.Should().Be(ImplementationStatus.Planned);
        saved.ApprovalStatus.Should().Be(SspSectionStatus.Draft);
        saved.PolicyNarrative.Should().Be("Human policy");
        saved.TechnicalNarrative.Should().Be(updateTechnical ? technical : "Existing technical");
    }

    [Fact]
    public async Task UpdateAsync_PlatformEngineerPolicyWrite_ThrowsForbidden()
    {
        // Arrange
        await SeedImplementationAsync();

        // Act
        var act = () => _service.UpdateAsync(
            "system-1", "AC-1", "Denied policy", updatePolicy: true,
            technicalNarrative: null, updateTechnical: false,
            role: "Compliance.PlatformEngineer", authoredBy: "engineer-1");

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*policy narrative*");
    }

    [Fact]
    public async Task UpdateAsync_RoleEndingInAnalyst_ThrowsForbidden()
    {
        // Arrange
        await SeedImplementationAsync();

        // Act
        var act = () => _service.UpdateAsync(
            "system-1", "AC-1", "Denied policy", updatePolicy: true,
            technicalNarrative: null, updateTechnical: false,
            role: "SuperAnalyst", authoredBy: "attacker-1");

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    public void Dispose()
    {
        _tenantScope.Dispose();
        _provider.Dispose();
    }

    private async Task<ControlImplementation> SeedImplementationAsync()
    {
        var system = new RegisteredSystem
        {
            TenantId = TenantId,
            Id = "system-1",
            Name = "Test System",
            Acronym = "TST",
            Description = "Test",
            HostingEnvironment = "Azure Government"
        };
        var implementation = new ControlImplementation
        {
            TenantId = TenantId,
            Id = "implementation-1",
            RegisteredSystemId = system.Id,
            ControlId = "AC-1",
            PolicyNarrative = "Existing policy",
            TechnicalNarrative = "Existing technical",
            AuthoredBy = "seed-user"
        };
        _db.AddRange(system, implementation);
        await _db.SaveChangesAsync();
        return implementation;
    }

    private static EvidenceArtifact CreateEvidence(
        string id,
        ControlImplementation implementation,
        EvidenceNarrativeType narrativeType) => new()
    {
        TenantId = TenantId,
        Id = id,
        RegisteredSystemId = implementation.RegisteredSystemId,
        ControlImplementationId = implementation.Id,
        FileName = $"{id}.pdf",
        ContentType = "application/pdf",
        StoragePath = id,
        ContentHash = new string('0', 64),
        UploadedBy = "seed-user",
        NarrativeType = narrativeType
    };
}