using System.Text;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Compliance;

public sealed class ProviderNarrativeLibraryServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private ProviderContext _db = null!;
    private readonly TenantContext _provider = new(Guid.Empty, isCspAdmin: true);
    private ProviderNarrativeLibraryService _service = null!;
    private readonly Guid _profileId = Guid.NewGuid();
    private readonly Guid _capabilityId = Guid.NewGuid();
    private readonly Guid _componentId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _db = new ProviderContext(new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(_connection).Options);
        await _db.Database.EnsureCreatedAsync();
        _db.CspProfiles.Add(new() { Id = _profileId, DisplayName = "Synthetic provider", LegalEntityName = "Synthetic provider" });
        _db.CspInheritedComponents.Add(new() { Id = _componentId, CspProfileId = _profileId, Name = "Shared access",
            Status = CspInheritedComponentStatus.Published });
        _db.CspInheritedCapabilities.Add(new() { Id = _capabilityId, CspInheritedComponentId = _componentId,
            Name = "Account management", Status = CspInheritedCapabilityStatus.Mapped, MappedNistControlIds = ["AC-2"] });
        _db.NistControls.Add(new() { Id = "ac-2", Title = "Account management" });
        await _db.SaveChangesAsync();
        _service = new(_db, _provider, new NarrativeLibraryService(_db, _provider));
    }

    [Fact]
    public async Task UnpublishedReferenceAndUnpublishedComponentNeverReachCustomerGrounding()
    {
        // Arrange
        var (_, consumer) = await SeedCustomerAsync();
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("AC-2\nTechnical Narrative:\nUnverified claim."));
        var reference = await _service.ImportAsync("provider-author", "Reference", "ProviderCapability", _capabilityId.ToString(), "reference.txt", input);

        // Act
        var draft = await consumer.GetApplicableAsync("customer-system", "AC-2", "reader");
        await _service.PublishAsync(reference.Id, "provider-author", reference.Revision, reference.Passages, true);
        (await _db.CspInheritedComponents.SingleAsync()).Status = CspInheritedComponentStatus.Draft;
        await _db.SaveChangesAsync();
        var unpublishedComponent = await consumer.GetApplicableAsync("customer-system", "AC-2", "reader");

        // Assert
        draft.Should().BeEmpty();
        unpublishedComponent.Should().BeEmpty();
    }

    [Fact]
    public async Task ProviderCapabilityPublicationRejectsUnmappedPassageControls()
    {
        // Arrange
        _db.NistControls.Add(new() { Id = "au-6", Title = "Audit" });
        await _db.SaveChangesAsync();
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("AU-6\nPolicy Narrative:\nUnrelated claim."));
        var reference = await _service.ImportAsync("provider-author", "Reference", "ProviderCapability", _capabilityId.ToString(), "reference.txt", input);

        // Act
        var publish = () => _service.PublishAsync(reference.Id, "provider-author", 1, reference.Passages, true);

        // Assert
        await publish.Should().ThrowAsync<ArgumentException>();
        (await _db.Set<ProviderNarrativeReference>().SingleAsync()).IsPublished.Should().BeFalse();
    }

    [Fact]
    public async Task ProviderConsumptionCannotBorrowAnotherTenantsSystem()
    {
        // Arrange
        await SeedCustomerAsync();
        var foreignTenant = new TenantContext(Guid.NewGuid());
        var foreign = new ProviderNarrativeLibraryService(_db, foreignTenant, new NarrativeLibraryService(_db, foreignTenant));

        // Act
        var consume = () => foreign.GetApplicableAsync("customer-system", "AC-2", "reader");

        // Assert
        await consume.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task ProviderSchema_FreshAndRerunSupportsRealReferenceImport()
    {
        // Arrange
        await _db.Database.ExecuteSqlRawAsync("""DROP TABLE "ProviderNarrativeReferences";""");
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("AC-2\nPolicy Narrative:\nProvider reference."));

        // Act
        await NarrativeLibrarySchemaAdditions.ApplyAsync(_db);
        await NarrativeLibrarySchemaAdditions.ApplyAsync(_db);
        var result = await _service.ImportAsync("provider-author", "Reference", "Provider", _profileId.ToString(), "reference.txt", input);

        // Assert
        result.IsPublished.Should().BeFalse();
        (await _db.Set<ProviderNarrativeReference>().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ProviderRevision_RemovedControlDoesNotFallBackToOlderPublishedPassage()
    {
        // Arrange
        var (tenant, consumer) = await SeedCustomerAsync();
        _db.NistControls.Add(new() { Id = "au-6", Title = "Audit review" });
        await _db.SaveChangesAsync();
        using var firstInput = new MemoryStream(Encoding.UTF8.GetBytes("AC-2\nPolicy Narrative:\nOld claim."));
        var first = await _service.ImportAsync("provider-author", "Reference", "Provider", _profileId.ToString(), "first.txt", firstInput);
        await _service.PublishAsync(first.Id, "provider-author", first.Revision, first.Passages, true);
        using var nextInput = new MemoryStream(Encoding.UTF8.GetBytes("AU-6\nPolicy Narrative:\nReplacement reference without AC-2."));
        var next = await _service.ImportAsync("provider-author", "Reference", "Provider", _profileId.ToString(), "next.txt", nextInput);
        await _service.PublishAsync(next.Id, "provider-author", next.Revision, next.Passages, true);

        // Act
        var applicable = await consumer.GetApplicableAsync("customer-system", "AC-2", "reader");

        // Assert
        applicable.Should().BeEmpty();
        var publication = await _db.Set<ProviderNarrativeReferencePublication>().SingleAsync(item => item.ReferenceId == next.Id);
        var payload = System.Text.Json.JsonSerializer.Deserialize<NarrativeReferencePublicationPayload>(publication.PayloadJson);
        payload!.Targets.Select(item => item.ControlId).Should().Equal("AC-2", "AU-6");
        tenant.TenantId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GroundingIncludesOnlyApplicablePublishedProviderReferenceClaims()
    {
        // Arrange
        var (tenant, _) = await SeedCustomerAsync();
        _db.ControlImplementations.Add(new() { TenantId = tenant.TenantId, RegisteredSystemId = "customer-system", ControlId = "AC-2" });
        await _db.SaveChangesAsync();
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("AC-2\nPolicy Narrative:\nUnverified provider reference."));
        var reference = await _service.ImportAsync("provider-author", "Provider policy", "ProviderCapability",
            _capabilityId.ToString(), "reference.txt", input);
        await _service.PublishAsync(reference.Id, "provider-author", reference.Revision, reference.Passages, true);
        var generator = new Mock<IControlNarrativeService>();
        generator.Setup(item => item.GenerateGroundedDraftAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GroundedNarrativeDraft("Proposed", [], []));
        var proposals = new NarrativeProposalService(_db, tenant, new NarrativeLibraryService(_db, tenant), generator.Object);

        // Act
        var proposal = await proposals.GenerateAsync("customer-system", "AC-2", "Policy", "reader", 1);

        // Assert
        proposal.Provenance.GetProperty("referenceClaims").EnumerateArray()
            .Should().ContainSingle(item => item.GetProperty("Id").GetGuid() == reference.Id);
        proposal.MissingEvidence.Should().Contain(item => item.Contains("unknown", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ProviderConsumptionRejectsMalformedStoredSubscriptionInsteadOfHidingIt()
    {
        // Arrange
        var (_, consumer) = await SeedCustomerAsync();
        (await _db.CapabilitySubscriptions.SingleAsync()).CspInheritedCapabilityId = "invalid-identity";
        await _db.SaveChangesAsync();

        // Act
        var consume = () => consumer.GetApplicableAsync("customer-system", "AC-2", "reader");

        // Assert
        await consume.Should().ThrowAsync<InvalidOperationException>().WithMessage("SOURCE_STATE_INVALID:*");
    }

    [Fact]
    public async Task ProviderReference_RequiresPublishedApplicableSubscriptionAndNeverCreatesSystem()
    {
        // Arrange
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("AC-2\nPolicy Narrative:\nUnverified provider reference."));
        var draft = await _service.ImportAsync("provider-author", "Shared reference", "ProviderCapability",
            _capabilityId.ToString(), "reference.txt", input);
        var published = await _service.PublishAsync(draft.Id, "provider-author", draft.Revision, draft.Passages, true);
        (await _db.RegisteredSystems.CountAsync()).Should().Be(0);
        (await _db.Set<ProviderNarrativeReferencePublication>().SingleAsync()).Status.Should().Be("Pending");
        var tenant = new TenantContext(Guid.NewGuid());
        _db.RegisteredSystems.Add(new() { TenantId = tenant.TenantId, Id = "customer-system", Name = "Customer system" });
        _db.RmfRoleAssignments.Add(new() { TenantId = tenant.TenantId, RegisteredSystemId = "customer-system",
            UserId = "reader", RmfRole = RmfRole.SystemOwner });
        var subscription = new CapabilitySubscription { RegisteredSystemId = "customer-system",
            CspInheritedCapabilityId = _capabilityId.ToString() };
        _db.CapabilitySubscriptions.Add(subscription);
        await _db.SaveChangesAsync();
        var consumer = new ProviderNarrativeLibraryService(_db, tenant, new NarrativeLibraryService(_db, tenant));

        // Act
        var applicable = await consumer.GetApplicableAsync("customer-system", "AC-2", "reader");
        var unrelated = await consumer.GetApplicableAsync("customer-system", "AU-6", "reader");
        subscription.IsActive = false;
        await _db.SaveChangesAsync();
        var removed = await consumer.GetApplicableAsync("customer-system", "AC-2", "reader");

        // Assert
        applicable.Should().ContainSingle(item => item.Id == published.Id);
        unrelated.Should().BeEmpty();
        removed.Should().BeEmpty();
        (await _db.ControlImplementations.CountAsync()).Should().Be(0);
        (await _db.NarrativeReferences.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task PublicationAndSourceOutboxRollBackTogether()
    {
        // Arrange
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("AC-2\nPolicy Narrative:\nProvider reference."));
        var draft = await _service.ImportAsync("provider-author", "Reference", "Provider", _profileId.ToString(), "reference.txt", input);

        // Act
        await using (var transaction = await _db.Database.BeginTransactionAsync())
        {
            await _service.PublishAsync(draft.Id, "provider-author", draft.Revision, draft.Passages, true);
            (await _db.Set<ProviderNarrativeReferencePublication>().CountAsync()).Should().Be(1);
            await transaction.RollbackAsync();
        }
        _db.ChangeTracker.Clear();

        // Assert
        (await _db.Set<ProviderNarrativeReference>().SingleAsync()).IsPublished.Should().BeFalse();
        (await _db.Set<ProviderNarrativeReferencePublication>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ProviderDraft_EditScopeAndMappingThenPublishIsImmutable()
    {
        // Arrange
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("Unmapped provider input"));
        var draft = await _service.ImportAsync("provider-author", "Reference", "Provider", _profileId.ToString(), "reference.txt", input);

        // Act
        var edited = await _service.UpdateDraftAsync(draft.Id, "provider-author", 1, "ProviderCapability",
            _capabilityId.ToString(), [new("AC-2", "Technical", "Proposed reference language")]);
        var published = await _service.PublishAsync(draft.Id, "provider-author", 2, edited.Passages, true);
        var rewrite = () => _service.UpdateDraftAsync(draft.Id, "provider-author", published.Revision,
            "Provider", _profileId.ToString(), edited.Passages);

        // Assert
        edited.Scope.Should().Be("ProviderCapability");
        published.IsPublished.Should().BeTrue();
        await rewrite.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task OrganizationAndSupportContextsCannotReadOrWriteProviderPrivateLibrary()
    {
        // Arrange
        var organization = new TenantContext(Guid.NewGuid());
        var support = new TenantContext(Guid.Empty, isCspAdmin: true, impersonatedTenantId: Guid.NewGuid());
        var consumer = new ProviderNarrativeLibraryService(_db, organization, new NarrativeLibraryService(_db, organization));
        var impersonated = new ProviderNarrativeLibraryService(_db, support, new NarrativeLibraryService(_db, support));

        // Act
        var read = () => consumer.ListAsync("customer");
        var supportRead = () => impersonated.ListAsync("support");

        // Assert
        await read.Should().ThrowAsync<UnauthorizedAccessException>();
        await supportRead.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task<(TenantContext Tenant, ProviderNarrativeLibraryService Consumer)> SeedCustomerAsync()
    {
        var tenant = new TenantContext(Guid.NewGuid());
        _db.RegisteredSystems.Add(new() { TenantId = tenant.TenantId, Id = "customer-system", Name = "Customer system" });
        _db.RmfRoleAssignments.Add(new() { TenantId = tenant.TenantId, RegisteredSystemId = "customer-system",
            UserId = "reader", RmfRole = RmfRole.SystemOwner });
        _db.CapabilitySubscriptions.Add(new() { RegisteredSystemId = "customer-system", CspInheritedCapabilityId = _capabilityId.ToString() });
        await _db.SaveChangesAsync();
        return (tenant, new ProviderNarrativeLibraryService(_db, tenant, new NarrativeLibraryService(_db, tenant)));
    }

    private sealed class ProviderContext(DbContextOptions<AtoCopilotContext> options) : AtoCopilotContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<ProviderNarrativeReference>();
        }
    }
}
