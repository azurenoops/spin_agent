using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Storage;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Mcp.Services;

namespace Ato.Copilot.Tests.Unit.Services;

public class EvidenceArtifactServiceTests : IDisposable
{
    private readonly Mock<IFileStorageProvider> _storageProvider;
    private readonly AtoCopilotContext _db;
    private readonly EvidenceArtifactService _sut;
    private readonly DbContextOptions<AtoCopilotContext> _dbOptions;

    public EvidenceArtifactServiceTests()
    {
        _storageProvider = new Mock<IFileStorageProvider>();
        _storageProvider
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _storageProvider
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(Encoding.UTF8.GetBytes("file-content")));
        _storageProvider
            .Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _dbOptions = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"EvidenceArtifactTests_{Guid.NewGuid()}")
            .Options;
        var factory = new TestDbContextFactory(_dbOptions);
        _db = factory.Context;

        _sut = new EvidenceArtifactService(
            factory,
            _storageProvider.Object,
            Mock.Of<ILogger<EvidenceArtifactService>>());
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    private static Stream MakeStream(string content = "test-file-content")
        => new MemoryStream(Encoding.UTF8.GetBytes(content));

    // ─── Upload ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Upload_ValidFile_CreatesArtifactAndSavesToStorage()
    {
        using var stream = MakeStream();

        var result = await _sut.UploadAsync(
            "sys-1", "report.pdf", "application/pdf", stream,
            ArtifactCategory.ScanResult, "admin@test.com",
            controlImplementationId: "ci-1");

        result.Should().NotBeNull();
        result.FileName.Should().Be("report.pdf");
        result.RegisteredSystemId.Should().Be("sys-1");
        result.ControlImplementationId.Should().Be("ci-1");
        result.ArtifactCategory.Should().Be(ArtifactCategory.ScanResult);
        result.ContentHash.Should().NotBeNullOrEmpty();
        result.FileSizeBytes.Should().BeGreaterThan(0);

        _storageProvider.Verify(
            s => s.SaveAsync(It.Is<string>(p => p.Contains("sys-1")), It.IsAny<Stream>(), "application/pdf", It.IsAny<CancellationToken>()),
            Times.Once);

        var saved = await _db.EvidenceArtifacts.FindAsync(result.Id);
        saved.Should().NotBeNull();
    }

    [Fact]
    public async Task Upload_ToCapability_CreatesArtifactWithCapabilityId()
    {
        using var stream = MakeStream();

        var result = await _sut.UploadAsync(
            "sys-1", "config.json", "application/json", stream,
            ArtifactCategory.ConfigurationExport, "admin@test.com",
            securityCapabilityId: "cap-1");

        result.SecurityCapabilityId.Should().Be("cap-1");
        result.ControlImplementationId.Should().BeNull();
    }

    // ─── Upload Validation ───────────────────────────────────────────────

    [Fact]
    public async Task Upload_NoTargetId_ThrowsArgumentException()
    {
        using var stream = MakeStream();

        var act = () => _sut.UploadAsync(
            "sys-1", "test.pdf", "application/pdf", stream,
            ArtifactCategory.Other, "admin@test.com");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*controlImplementationId or securityCapabilityId*");
    }

    [Fact]
    public async Task Upload_BothTargetIds_ThrowsArgumentException()
    {
        using var stream = MakeStream();

        var act = () => _sut.UploadAsync(
            "sys-1", "test.pdf", "application/pdf", stream,
            ArtifactCategory.Other, "admin@test.com",
            controlImplementationId: "ci-1",
            securityCapabilityId: "cap-1");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Only one*");
    }

    [Fact]
    public async Task Upload_ZeroByteFile_ThrowsArgumentException()
    {
        using var stream = new MemoryStream();

        var act = () => _sut.UploadAsync(
            "sys-1", "empty.pdf", "application/pdf", stream,
            ArtifactCategory.Other, "admin@test.com",
            controlImplementationId: "ci-1");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*empty*");
    }

    [Fact]
    public async Task Upload_DisallowedExtension_ThrowsArgumentException()
    {
        using var stream = MakeStream();

        var act = () => _sut.UploadAsync(
            "sys-1", "malware.exe", "application/octet-stream", stream,
            ArtifactCategory.Other, "admin@test.com",
            controlImplementationId: "ci-1");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*not allowed*");
    }

    [Fact]
    public async Task Upload_MismatchedContentType_ThrowsArgumentException()
    {
        using var stream = MakeStream();

        var act = () => _sut.UploadAsync(
            "sys-1", "test.pdf", "image/png", stream,
            ArtifactCategory.Other, "admin@test.com",
            controlImplementationId: "ci-1");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Content type*does not match*");
    }

    // ─── GetById ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetById_ExistingArtifact_ReturnsArtifact()
    {
        var artifact = CreateArtifact("a-1");
        _db.EvidenceArtifacts.Add(artifact);
        await _db.SaveChangesAsync();

        var result = await _sut.GetByIdAsync("a-1");

        result.Should().NotBeNull();
        result!.Id.Should().Be("a-1");
    }

    [Fact]
    public async Task GetById_DeletedArtifact_ReturnsNull()
    {
        var artifact = CreateArtifact("a-2");
        artifact.IsDeleted = true;
        _db.EvidenceArtifacts.Add(artifact);
        await _db.SaveChangesAsync();

        var result = await _sut.GetByIdAsync("a-2");

        result.Should().BeNull();
    }

    // ─── SoftDelete ──────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_ExistingArtifact_SoftDeletesSuccessfully()
    {
        var artifact = CreateArtifact("a-3");
        _db.EvidenceArtifacts.Add(artifact);
        await _db.SaveChangesAsync();

        var result = await _sut.DeleteAsync("a-3", "deleter@test.com");

        result.Should().BeTrue();
        var updated = await _db.EvidenceArtifacts.FindAsync("a-3");
        updated!.IsDeleted.Should().BeTrue();
        updated.DeletedBy.Should().Be("deleter@test.com");
        updated.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Delete_NonExistent_ReturnsFalse()
    {
        var result = await _sut.DeleteAsync("non-existent", "admin@test.com");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task Delete_AlreadyDeleted_ReturnsFalse()
    {
        var artifact = CreateArtifact("a-4");
        artifact.IsDeleted = true;
        _db.EvidenceArtifacts.Add(artifact);
        await _db.SaveChangesAsync();

        var result = await _sut.DeleteAsync("a-4", "admin@test.com");

        result.Should().BeFalse();
    }

    // ─── Replace ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Replace_ExistingArtifact_CreatesVersionAndUpdatesArtifact()
    {
        var artifact = CreateArtifact("a-5");
        var originalHash = artifact.ContentHash;
        _db.EvidenceArtifacts.Add(artifact);
        await _db.SaveChangesAsync();

        using var newStream = MakeStream("new-file-content-here");

        var result = await _sut.ReplaceAsync(
            "a-5", "new-report.pdf", "application/pdf", newStream,
            "replacer@test.com", retentionDays: 90);

        result.FileName.Should().Be("new-report.pdf");
        result.ContentHash.Should().NotBe(originalHash);

        var versions = await _db.EvidenceVersions
            .Where(v => v.EvidenceArtifactId == "a-5")
            .ToListAsync();
        versions.Should().HaveCount(1);
        versions[0].FileName.Should().Be("original.pdf");
        versions[0].ReplacedBy.Should().Be("replacer@test.com");
        versions[0].PurgeAfter.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public async Task Replace_NonExistent_ThrowsInvalidOperationException()
    {
        using var stream = MakeStream();

        var act = () => _sut.ReplaceAsync(
            "non-existent", "test.pdf", "application/pdf", stream,
            "admin@test.com");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ─── Download ────────────────────────────────────────────────────────

    [Fact]
    public async Task Download_ExistingArtifact_ReturnsStreamAndMetadata()
    {
        var artifact = CreateArtifact("a-6");
        _db.EvidenceArtifacts.Add(artifact);
        await _db.SaveChangesAsync();

        var result = await _sut.DownloadAsync("a-6");

        result.Should().NotBeNull();
        result!.Value.FileName.Should().Be("original.pdf");
        result.Value.ContentType.Should().Be("application/pdf");
        result.Value.Content.Should().NotBeNull();
    }

    [Fact]
    public async Task Download_NonExistent_ReturnsNull()
    {
        var result = await _sut.DownloadAsync("non-existent");

        result.Should().BeNull();
    }

    // ─── ListForSystem ───────────────────────────────────────────────────

    [Fact]
    public async Task ListForSystem_ReturnsPagedResults()
    {
        for (var i = 0; i < 5; i++)
        {
            _db.EvidenceArtifacts.Add(CreateArtifact($"ls-{i}", "sys-list"));
        }
        await _db.SaveChangesAsync();

        var (items, total) = await _sut.ListForSystemAsync("sys-list", page: 1, pageSize: 3);

        total.Should().Be(5);
        items.Should().HaveCount(3);
    }

    [Fact]
    public async Task ListForSystem_ExcludesDeletedArtifacts()
    {
        var a1 = CreateArtifact("ld-1", "sys-del");
        var a2 = CreateArtifact("ld-2", "sys-del");
        a2.IsDeleted = true;
        _db.EvidenceArtifacts.AddRange(a1, a2);
        await _db.SaveChangesAsync();

        var (items, total) = await _sut.ListForSystemAsync("sys-del");

        total.Should().Be(1);
        items.Should().HaveCount(1);
    }

    [Fact]
    public async Task ListForSystem_SearchByFileName()
    {
        var a1 = CreateArtifact("sf-1", "sys-search");
        a1.FileName = "security-scan.pdf";
        var a2 = CreateArtifact("sf-2", "sys-search");
        a2.FileName = "config-export.json";
        _db.EvidenceArtifacts.AddRange(a1, a2);
        await _db.SaveChangesAsync();

        var (items, _) = await _sut.ListForSystemAsync("sys-search", search: "security");

        items.Should().HaveCount(1);
        items[0].FileName.Should().Contain("security");
    }

    // ─── HashComputation ─────────────────────────────────────────────────

    [Fact]
    public async Task Upload_ComputesSha256Hash()
    {
        using var stream = MakeStream("deterministic-content");

        var result = await _sut.UploadAsync(
            "sys-hash", "test.txt", "text/plain", stream,
            ArtifactCategory.Other, "admin@test.com",
            controlImplementationId: "ci-hash");

        result.ContentHash.Should().NotBeNullOrEmpty();
        result.ContentHash.Should().HaveLength(64); // SHA-256 hex
    }

    [Fact]
    public async Task Upload_SameContent_ProducesSameHash()
    {
        using var stream1 = MakeStream("same-content");
        var r1 = await _sut.UploadAsync(
            "sys-h1", "a.txt", "text/plain", stream1,
            ArtifactCategory.Other, "admin@test.com",
            controlImplementationId: "ci-h1");

        using var stream2 = MakeStream("same-content");
        var r2 = await _sut.UploadAsync(
            "sys-h2", "b.txt", "text/plain", stream2,
            ArtifactCategory.Other, "admin@test.com",
            controlImplementationId: "ci-h2");

        r1.ContentHash.Should().Be(r2.ContentHash);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static EvidenceArtifact CreateArtifact(string id, string systemId = "sys-1")
        => new()
        {
            Id = id,
            RegisteredSystemId = systemId,
            FileName = "original.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = 1024,
            StoragePath = $"evidence/{systemId}/{id}/original.pdf",
            ContentHash = "abcdef1234567890",
            ArtifactCategory = ArtifactCategory.ScanResult,
            CollectionMethod = CollectionMethod.Manual,
            UploadedBy = "test@test.com",
            UploadedAt = DateTime.UtcNow,
        };
}
