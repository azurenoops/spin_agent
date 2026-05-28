using System.Text.Json;
using Ato.Copilot.Core.Configuration.Auth;
using Ato.Copilot.Core.Models.Auth;
using Ato.Copilot.Core.Services.Auth;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Auth;

/// <summary>
/// Feature 051 T092 [US10] — contract test for
/// <see cref="FileSystemArchiveSink"/>. Verifies the file-naming
/// convention, the JSON-Lines payload, the no-overwrite invariant, and
/// the configuration-root fallback per
/// <c>contracts/internal-services.md § 4.2 / § 4.4</c>.
/// </summary>
public sealed class FileSystemArchiveSinkTests : IDisposable
{
    private readonly string _tempRoot;

    public FileSystemArchiveSinkTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(),
            $"ato-archive-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    // ─── Happy path — one JSONL file per batch ─────────────────────────

    [Fact]
    public async Task WriteBatchAsync_WritesFileUnderRoot_YearMonth_PathConvention()
    {
        // Arrange
        var sut = NewSut();
        var rows = SeedRows(count: 3);

        // Act
        var path = await sut.WriteBatchAsync(rows, CancellationToken.None);

        // Assert
        path.Should().NotBeNullOrWhiteSpace();
        File.Exists(path).Should().BeTrue("the sink must create the file before returning.");

        // Filename matches login-audit-{guid}.jsonl.
        var fileName = Path.GetFileName(path);
        fileName.Should().StartWith("login-audit-")
            .And.EndWith(".jsonl");

        // Parent layout {root}/{yyyy}/{MM}.
        var monthDir = Path.GetDirectoryName(path)!;
        var yearDir = Path.GetDirectoryName(monthDir)!;
        var rootDir = Path.GetDirectoryName(yearDir)!;
        Path.GetFileName(monthDir).Should().HaveLength(2,
            "month directory MUST be zero-padded MM.");
        Path.GetFileName(yearDir).Should().HaveLength(4,
            "year directory MUST be 4-digit yyyy.");
        Path.GetFullPath(rootDir).Should().Be(Path.GetFullPath(_tempRoot));
    }

    [Fact]
    public async Task WriteBatchAsync_TwoCalls_CreateTwoFiles_NoOverwrite()
    {
        // Arrange
        var sut = NewSut();
        var rows = SeedRows(count: 2);

        // Act — two calls with the SAME batch content.
        var first = await sut.WriteBatchAsync(rows, CancellationToken.None);
        var second = await sut.WriteBatchAsync(rows, CancellationToken.None);

        // Assert
        first.Should().NotBe(second,
            "each call must mint a fresh GUID-suffixed filename — no overwrite.");
        File.Exists(first).Should().BeTrue();
        File.Exists(second).Should().BeTrue();
    }

    [Fact]
    public async Task WriteBatchAsync_EmitsOneJsonObjectPerLine_RoundTrippable()
    {
        // Arrange
        var sut = NewSut();
        var rows = SeedRows(count: 4);

        // Act
        var path = await sut.WriteBatchAsync(rows, CancellationToken.None);

        // Assert
        var lines = await File.ReadAllLinesAsync(path);
        lines.Should().HaveCount(4, "one JSON object per row.");
        foreach (var line in lines)
        {
            line.Should().NotBeNullOrWhiteSpace();
            // Each line MUST be valid standalone JSON.
            using var doc = JsonDocument.Parse(line);
            doc.RootElement.TryGetProperty("id", out _).Should().BeTrue(
                "the round-trippable id property survives camelCase serialization.");
            doc.RootElement.TryGetProperty("effectiveTenantId", out _).Should().BeTrue();
        }
    }

    [Fact]
    public async Task WriteBatchAsync_EmptyBatch_IsNoOp()
    {
        // Arrange
        var sut = NewSut();
        var rows = Array.Empty<LoginAuditEvent>();

        // Act
        var path = await sut.WriteBatchAsync(rows, CancellationToken.None);

        // Assert
        path.Should().BeEmpty(
            "contracts/internal-services.md § 4.2 — empty batch returns empty.");
        // No file should have been written under the root.
        Directory.EnumerateFiles(_tempRoot, "*.jsonl", SearchOption.AllDirectories)
            .Should().BeEmpty();
    }

    // ─── Configuration — FileSystemRoot fallback to "./archive" ────────

    [Fact]
    public async Task WriteBatchAsync_DefaultsToRelativeArchive_WhenRootBlank()
    {
        // Arrange — blank FileSystemRoot triggers the "./archive" default.
        var options = Options.Create(new AuthOptions
        {
            Archive = new AuthArchiveOptions
            {
                Sink = ArchiveSinkKind.FileSystem,
                FileSystemRoot = string.Empty,
            },
        });
        var sut = new FileSystemArchiveSink(
            options,
            NullLogger<FileSystemArchiveSink>.Instance);
        var rows = SeedRows(count: 1);

        // Act
        var path = await sut.WriteBatchAsync(rows, CancellationToken.None);

        // Assert
        path.Should().Contain($"{Path.DirectorySeparatorChar}archive{Path.DirectorySeparatorChar}",
            "FileSystemRoot defaults to './archive' when blank.");
        // Cleanup the file we just wrote in the working directory.
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private FileSystemArchiveSink NewSut()
    {
        var options = Options.Create(new AuthOptions
        {
            Archive = new AuthArchiveOptions
            {
                Sink = ArchiveSinkKind.FileSystem,
                FileSystemRoot = _tempRoot,
            },
        });
        return new FileSystemArchiveSink(
            options,
            NullLogger<FileSystemArchiveSink>.Instance);
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
