using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Ato.Copilot.Agents.Compliance.Services.Onboarding.NarrativeSeeds;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Interfaces.Storage;
using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Tests.Unit.Onboarding;

/// <summary>
/// Unit tests for <see cref="NarrativeSeedDocumentService"/> (T117 / FR-051..FR-054).
/// Citation-cascade guard fires only when the document is already
/// <see cref="NarrativeSeedIndexingStatus.Indexed"/>.
/// </summary>
public class NarrativeSeedDocumentServiceTests : IDisposable
{
    private readonly TestDbContextFactory _factory;
    private readonly Mock<IFileStorageProvider> _storage = new();
    private readonly Mock<IWizardAuditService> _audit = new();
    private readonly NarrativeSeedDocumentService _sut;

    public NarrativeSeedDocumentServiceTests()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"NarrativeSeed_{Guid.NewGuid()}")
            .Options;
        _factory = new TestDbContextFactory(options);
        _storage.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _sut = new NarrativeSeedDocumentService(
            _factory, _storage.Object, _audit.Object,
            Options.Create(new OnboardingOptions()),
            NullLogger<NarrativeSeedDocumentService>.Instance);
    }

    public void Dispose()
    {
        using var db = _factory.CreateDbContext();
        db.Database.EnsureDeleted();
    }

    [Fact]
    public async Task UploadAsync_PersistsAndAudits()
    {
        var tenantId = Guid.NewGuid();
        var actor = Guid.NewGuid();
        await using var ms = new MemoryStream(new byte[] { 1, 2, 3, 4 });

        var result = await _sut.UploadAsync(
            tenantId, actor,
            "Boundary diagram", new[] { "diagram", "boundary" },
            "boundary.pdf", "application/pdf",
            ms, ms.Length);

        result.Document.Id.Should().NotBe(Guid.Empty);
        result.Document.IndexingStatus.Should().Be(NarrativeSeedIndexingStatus.Pending);
        _storage.Verify(s => s.SaveAsync(
            It.Is<string>(k => k.StartsWith($"wizard/narrative-seeds/{tenantId:D}/")),
            It.IsAny<Stream>(), "application/pdf", It.IsAny<CancellationToken>()), Times.Once);
        _audit.Verify(a => a.RecordAsync(
            tenantId, actor, WizardAuditAction.NarrativeSeedUploaded,
            It.IsAny<string>(), null, null, It.IsAny<string>(), null,
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_PendingDocument_SoftDeletes()
    {
        var tenantId = Guid.NewGuid();
        var actor = Guid.NewGuid();
        await using var ms = new MemoryStream(new byte[] { 1, 2 });
        var r = await _sut.UploadAsync(
            tenantId, actor, "L", Array.Empty<string>(),
            "x.pdf", "application/pdf", ms, ms.Length);

        await _sut.DeleteAsync(tenantId, r.Document.Id, actor, confirmCitations: false);

        await using var db = _factory.CreateDbContext();
        var stored = await db.NarrativeSeedDocuments.FirstAsync(d => d.Id == r.Document.Id);
        stored.Status.Should().Be(NarrativeSeedStatus.Deleted);
        stored.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteAsync_IndexedWithoutConfirm_Throws()
    {
        var tenantId = Guid.NewGuid();
        var actor = Guid.NewGuid();
        await using var ms = new MemoryStream(new byte[] { 1, 2 });
        var r = await _sut.UploadAsync(
            tenantId, actor, "L", Array.Empty<string>(),
            "x.pdf", "application/pdf", ms, ms.Length);

        // Simulate indexing completion.
        await using (var db = _factory.CreateDbContext())
        {
            var doc = db.NarrativeSeedDocuments.First(d => d.Id == r.Document.Id);
            doc.IndexingStatus = NarrativeSeedIndexingStatus.Indexed;
            await db.SaveChangesAsync();
        }

        var act = async () => await _sut.DeleteAsync(tenantId, r.Document.Id, actor, confirmCitations: false);
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Be("WIZARD_NARRATIVE_SEED_HAS_CITATIONS");
    }

    [Fact]
    public async Task DeleteAsync_IndexedWithConfirm_Succeeds()
    {
        var tenantId = Guid.NewGuid();
        var actor = Guid.NewGuid();
        await using var ms = new MemoryStream(new byte[] { 1, 2 });
        var r = await _sut.UploadAsync(
            tenantId, actor, "L", Array.Empty<string>(),
            "x.pdf", "application/pdf", ms, ms.Length);

        await using (var db = _factory.CreateDbContext())
        {
            var doc = db.NarrativeSeedDocuments.First(d => d.Id == r.Document.Id);
            doc.IndexingStatus = NarrativeSeedIndexingStatus.Indexed;
            await db.SaveChangesAsync();
        }

        await _sut.DeleteAsync(tenantId, r.Document.Id, actor, confirmCitations: true);

        await using var db2 = _factory.CreateDbContext();
        var stored = await db2.NarrativeSeedDocuments.FirstAsync(d => d.Id == r.Document.Id);
        stored.Status.Should().Be(NarrativeSeedStatus.Deleted);
    }

    private sealed class TestDbContextFactory : IDbContextFactory<AtoCopilotContext>
    {
        private readonly DbContextOptions<AtoCopilotContext> _options;
        public TestDbContextFactory(DbContextOptions<AtoCopilotContext> options) => _options = options;
        public AtoCopilotContext CreateDbContext() => new(_options);
    }
}
