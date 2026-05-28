using Ato.Copilot.Core.Configuration.Auth;
using Ato.Copilot.Core.Models.Auth;
using Ato.Copilot.Core.Services.Auth;
using Azure;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Auth;

/// <summary>
/// Feature 051 T094 [US10] — contract test for
/// <see cref="AzureBlobAppendArchiveSink"/>. Uses
/// <see cref="Mock{AppendBlobClient}"/> to avoid touching Azure Storage
/// (no Azurite or live account required). Pins:
/// <list type="bullet">
///   <item>Blob path layout = <c>{yyyy}/{MM}/login-audit.jsonl</c>.</item>
///   <item>CreateIfNotExistsAsync is called before AppendBlockAsync.</item>
///   <item>Transient 5xx responses retry up to three attempts with
///         exponential backoff.</item>
///   <item>4xx responses surface immediately (no retry).</item>
/// </list>
/// </summary>
public sealed class AzureBlobAppendArchiveSinkTests
{
    // ─── Path layout ────────────────────────────────────────────────────

    [Fact]
    public async Task WriteBatchAsync_RequestsAppendBlob_AtYearMonthPath()
    {
        // Arrange — capture the blobPath the sink asks the factory for.
        string? capturedPath = null;
        var mockClient = BuildMockClient(successCount: 1, failureStatuses: null);

        var sut = NewSut(blobPath =>
        {
            capturedPath = blobPath;
            return mockClient.Object;
        });

        // Act
        await sut.WriteBatchAsync(SeedRows(count: 2), CancellationToken.None);

        // Assert — path layout is yyyy/MM/login-audit.jsonl. Use UtcNow's
        // values because the sink uses DateTimeOffset.UtcNow internally.
        capturedPath.Should().NotBeNull();
        var now = DateTimeOffset.UtcNow;
        capturedPath.Should().Be($"{now.Year:D4}/{now.Month:D2}/login-audit.jsonl");
    }

    // ─── Lifecycle — create then append ────────────────────────────────

    [Fact]
    public async Task WriteBatchAsync_CallsCreateIfNotExists_ThenAppendBlock()
    {
        // Arrange
        var mockClient = BuildMockClient(successCount: 1, failureStatuses: null);
        var sut = NewSut(_ => mockClient.Object);

        // Act
        await sut.WriteBatchAsync(SeedRows(count: 1), CancellationToken.None);

        // Assert
        mockClient.Verify(c => c.CreateIfNotExistsAsync(
                It.IsAny<BlobHttpHeaders>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "the sink must ensure the append blob exists before writing.");
        mockClient.Verify(c => c.AppendBlockAsync(
                It.IsAny<Stream>(),
                It.IsAny<AppendBlobAppendBlockOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ─── Transient 5xx — retries with backoff ──────────────────────────

    [Fact]
    public async Task WriteBatchAsync_TransientFailure_RetriesUpToThreeAttempts()
    {
        // Arrange — first two AppendBlockAsync calls return 503, third
        // succeeds. The sink should swallow the retries and return ok.
        var mockClient = BuildMockClient(
            successCount: 1,
            failureStatuses: new[] { 503, 502 });

        var sut = NewSut(_ => mockClient.Object);

        // Act
        var url = await sut.WriteBatchAsync(SeedRows(count: 1), CancellationToken.None);

        // Assert
        url.Should().NotBeNullOrEmpty();
        mockClient.Verify(c => c.AppendBlockAsync(
                It.IsAny<Stream>(),
                It.IsAny<AppendBlobAppendBlockOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(3),
            "two transient failures + one success = three calls.");
    }

    // ─── Non-transient (4xx) — no retry ────────────────────────────────

    [Fact]
    public async Task WriteBatchAsync_ClientError_DoesNotRetry_AndThrows()
    {
        // Arrange — 403 Forbidden on first attempt should surface
        // immediately. No retries; the hosted service catches the throw
        // and preserves rows in the hot table.
        var mockClient = BuildMockClient(
            successCount: 0,
            failureStatuses: new[] { 403 });

        var sut = NewSut(_ => mockClient.Object);

        // Act
        Func<Task> act = () => sut.WriteBatchAsync(SeedRows(count: 1), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<RequestFailedException>()
            .Where(ex => ex.Status == 403);
        mockClient.Verify(c => c.AppendBlockAsync(
                It.IsAny<Stream>(),
                It.IsAny<AppendBlobAppendBlockOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "4xx responses MUST NOT be retried.");
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private static AzureBlobAppendArchiveSink NewSut(
        Func<string, AppendBlobClient> clientFactory)
    {
        var options = Options.Create(new AuthOptions
        {
            Archive = new AuthArchiveOptions
            {
                Sink = ArchiveSinkKind.AzureBlobAppend,
                AzureBlobAccountUrl = "https://test.blob.core.usgovcloudapi.net",
                AzureBlobContainer = "audit-archive",
            },
        });
        return new AzureBlobAppendArchiveSink(
            options,
            NullLogger<AzureBlobAppendArchiveSink>.Instance,
            clientFactory);
    }

    /// <summary>
    /// Builds a <see cref="Mock{AppendBlobClient}"/> whose
    /// <c>AppendBlockAsync</c> first throws one
    /// <see cref="RequestFailedException"/> per entry in
    /// <paramref name="failureStatuses"/> and then returns
    /// <paramref name="successCount"/> successful responses (typically 1).
    /// Set <paramref name="successCount"/> to 0 to chain non-retryable
    /// failures only.
    /// </summary>
    private static Mock<AppendBlobClient> BuildMockClient(
        int successCount,
        int[]? failureStatuses)
    {
        var mock = new Mock<AppendBlobClient>(MockBehavior.Strict);

        // Stable Uri so the sink's return value is non-empty.
        mock.SetupGet(c => c.Uri)
            .Returns(new Uri("https://test.blob.core.usgovcloudapi.net/audit-archive/x.jsonl"));

        // CreateIfNotExistsAsync is best-effort — always succeed.
        mock.Setup(c => c.CreateIfNotExistsAsync(
                It.IsAny<BlobHttpHeaders>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue<BlobContentInfo>(
                BlobsModelFactory.BlobContentInfo(
                    eTag: new ETag("test"),
                    lastModified: DateTimeOffset.UtcNow,
                    contentHash: Array.Empty<byte>(),
                    versionId: null,
                    encryptionKeySha256: null,
                    encryptionScope: null,
                    blobSequenceNumber: 0),
                Mock.Of<Response>()));

        var setup = mock.SetupSequence(c => c.AppendBlockAsync(
            It.IsAny<Stream>(),
            It.IsAny<AppendBlobAppendBlockOptions>(),
            It.IsAny<CancellationToken>()));

        if (failureStatuses is not null)
        {
            foreach (var status in failureStatuses)
            {
                setup = setup.ThrowsAsync(new RequestFailedException(
                    status: status,
                    message: $"Simulated {status} from test mock",
                    errorCode: "Simulated",
                    innerException: null));
            }
        }

        for (int i = 0; i < successCount; i++)
        {
            setup = setup.ReturnsAsync(Response.FromValue<BlobAppendInfo>(
                BlobsModelFactory.BlobAppendInfo(
                    eTag: new ETag("ok"),
                    lastModified: DateTimeOffset.UtcNow,
                    contentHash: Array.Empty<byte>(),
                    contentCrc64: Array.Empty<byte>(),
                    blobAppendOffset: "0",
                    blobCommittedBlockCount: 1,
                    isServerEncrypted: true,
                    encryptionKeySha256: null,
                    encryptionScope: null),
                Mock.Of<Response>()));
        }

        return mock;
    }

    private static IReadOnlyList<LoginAuditEvent> SeedRows(int count)
    {
        var anchor = DateTimeOffset.UtcNow.AddMinutes(-30);
        var rows = new List<LoginAuditEvent>(count);
        for (int i = 0; i < count; i++)
        {
            rows.Add(new LoginAuditEvent
            {
                Id = Guid.NewGuid(),
                EventType = LoginAuditEventType.LoginSuccess,
                Oid = $"oid-{i}",
                Tid = null,
                EffectiveTenantId = Guid.NewGuid(),
                CorrelationId = $"corr-{i}",
                SourceIp = "10.0.0.1",
                UserAgent = "Mozilla/5.0",
                Surface = LoginSurface.Dashboard,
                OccurredAt = anchor.AddSeconds(i),
            });
        }
        return rows;
    }
}
