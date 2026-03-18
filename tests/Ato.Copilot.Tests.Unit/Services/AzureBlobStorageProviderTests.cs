using System.Text;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Ato.Copilot.Mcp.Services.Storage;

namespace Ato.Copilot.Tests.Unit.Services;

public class AzureBlobStorageProviderTests
{
    private readonly Mock<BlobContainerClient> _containerClient;
    private readonly AzureBlobStorageProvider _sut;

    public AzureBlobStorageProviderTests()
    {
        _containerClient = new Mock<BlobContainerClient>();
        _sut = new AzureBlobStorageProvider(
            _containerClient.Object,
            Mock.Of<ILogger<AzureBlobStorageProvider>>());
    }

    // ─── Save ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Save_UploadsBlob()
    {
        var blobClient = new Mock<BlobClient>();
        _containerClient
            .Setup(c => c.GetBlobClient("evidence/sys-1/art-1/test.pdf"))
            .Returns(blobClient.Object);
        _containerClient
            .Setup(c => c.CreateIfNotExistsAsync(It.IsAny<PublicAccessType>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<BlobContainerEncryptionScopeOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response<BlobContainerInfo>>());
        blobClient
            .Setup(b => b.UploadAsync(It.IsAny<Stream>(), It.IsAny<BlobUploadOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response<BlobContentInfo>>());

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("data"));

        await _sut.SaveAsync("evidence/sys-1/art-1/test.pdf", stream, "application/pdf");

        blobClient.Verify(b => b.UploadAsync(
            It.IsAny<Stream>(),
            It.Is<BlobUploadOptions>(o => o.HttpHeaders.ContentType == "application/pdf"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── Get ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_ExistingBlob_ReturnsStream()
    {
        var blobClient = new Mock<BlobClient>();
        _containerClient
            .Setup(c => c.GetBlobClient("test/path.txt"))
            .Returns(blobClient.Object);
        blobClient
            .Setup(b => b.ExistsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(true, Mock.Of<Response>()));

        var contentStream = new MemoryStream(Encoding.UTF8.GetBytes("blob-content"));
        var downloadResult = BlobsModelFactory.BlobDownloadStreamingResult(content: contentStream);
        blobClient
            .Setup(b => b.DownloadStreamingAsync(It.IsAny<BlobDownloadOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(downloadResult, Mock.Of<Response>()));

        var result = await _sut.GetAsync("test/path.txt");

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task Get_NonExistentBlob_ReturnsNull()
    {
        var blobClient = new Mock<BlobClient>();
        _containerClient
            .Setup(c => c.GetBlobClient("missing.txt"))
            .Returns(blobClient.Object);
        blobClient
            .Setup(b => b.ExistsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(false, Mock.Of<Response>()));

        var result = await _sut.GetAsync("missing.txt");

        result.Should().BeNull();
    }

    // ─── Delete ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_ExistingBlob_ReturnsTrue()
    {
        var blobClient = new Mock<BlobClient>();
        _containerClient
            .Setup(c => c.GetBlobClient("delete-me.txt"))
            .Returns(blobClient.Object);
        blobClient
            .Setup(b => b.DeleteIfExistsAsync(It.IsAny<DeleteSnapshotsOption>(), It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(true, Mock.Of<Response>()));

        var result = await _sut.DeleteAsync("delete-me.txt");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task Delete_NonExistentBlob_ReturnsFalse()
    {
        var blobClient = new Mock<BlobClient>();
        _containerClient
            .Setup(c => c.GetBlobClient("missing.txt"))
            .Returns(blobClient.Object);
        blobClient
            .Setup(b => b.DeleteIfExistsAsync(It.IsAny<DeleteSnapshotsOption>(), It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(false, Mock.Of<Response>()));

        var result = await _sut.DeleteAsync("missing.txt");

        result.Should().BeFalse();
    }

    // ─── Exists ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Exists_ExistingBlob_ReturnsTrue()
    {
        var blobClient = new Mock<BlobClient>();
        _containerClient
            .Setup(c => c.GetBlobClient("exists.txt"))
            .Returns(blobClient.Object);
        blobClient
            .Setup(b => b.ExistsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(true, Mock.Of<Response>()));

        var result = await _sut.ExistsAsync("exists.txt");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task Exists_NonExistentBlob_ReturnsFalse()
    {
        var blobClient = new Mock<BlobClient>();
        _containerClient
            .Setup(c => c.GetBlobClient("missing.txt"))
            .Returns(blobClient.Object);
        blobClient
            .Setup(b => b.ExistsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(false, Mock.Of<Response>()));

        var result = await _sut.ExistsAsync("missing.txt");

        result.Should().BeFalse();
    }
}
