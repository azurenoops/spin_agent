using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Ato.Copilot.Agents.Compliance.Services.Onboarding.Emass;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Interfaces.Storage;
using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Tests.Unit.Onboarding;

/// <summary>
/// Unit tests for <see cref="EmassImportService"/> (T072 / FR-030..FR-038).
/// Verifies per-session lifecycle (Upload → enqueue parse → preview → enqueue commit),
/// audit trail emission, and partial-failure preservation (SC-007).
/// </summary>
public class EmassImportServiceTests : IDisposable
{
    private readonly TestDbContextFactory _factory;
    private readonly Mock<IFileStorageProvider> _storage = new();
    private readonly Mock<IWizardJobRunner> _runner = new();
    private readonly Mock<IWizardAuditService> _audit = new();
    private readonly EmassImportService _sut;

    public EmassImportServiceTests()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"EmassImportSvc_{Guid.NewGuid()}")
            .Options;
        _factory = new TestDbContextFactory(options);

        _runner
            .Setup(r => r.EnqueueAsync(
                It.IsAny<WizardJobType>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<EmassParseJobPayload>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((WizardJobType jt, Guid t, Guid u, EmassParseJobPayload _, CancellationToken __) =>
                new WizardJobStatus { Id = Guid.NewGuid(), TenantId = t, JobType = jt, EnqueuedBy = u });

        _runner
            .Setup(r => r.EnqueueAsync(
                It.IsAny<WizardJobType>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<EmassCommitJobPayload>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((WizardJobType jt, Guid t, Guid u, EmassCommitJobPayload _, CancellationToken __) =>
                new WizardJobStatus { Id = Guid.NewGuid(), TenantId = t, JobType = jt, EnqueuedBy = u });

        _sut = new EmassImportService(
            _factory, _storage.Object, _runner.Object, _audit.Object,
            NullLogger<EmassImportService>.Instance);
    }

    public void Dispose()
    {
        using var db = _factory.CreateDbContext();
        db.Database.EnsureDeleted();
    }

    [Fact]
    public async Task StartParseAsync_PersistsSessionAndEnqueuesParseJob()
    {
        var tenantId = Guid.NewGuid();
        var actor = Guid.NewGuid();
        await using var content = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });

        var (session, parseJobId) = await _sut.StartParseAsync(
            tenantId, "export.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            content, actor, Guid.NewGuid());

        session.TenantId.Should().Be(tenantId);
        session.OriginalFileName.Should().Be("export.xlsx");
        session.FileSizeBytes.Should().Be(5);
        session.ContentChecksumSha256.Should().NotBeNullOrEmpty();
        session.Format.Should().Be(EmassImportFormat.Xlsx);
        parseJobId.Should().NotBeEmpty();

        _storage.Verify(s => s.SaveAsync(
            It.Is<string>(k => k.Contains(tenantId.ToString())),
            It.IsAny<Stream>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);

        _runner.Verify(r => r.EnqueueAsync(
            WizardJobType.EmassParse,
            tenantId, actor,
            It.IsAny<EmassParseJobPayload>(),
            It.IsAny<CancellationToken>()), Times.Once);

        _audit.Verify(a => a.RecordAsync(
            tenantId, actor, WizardAuditAction.EmassUploaded,
            "EmassImportSession", session.Id,
            null, It.IsAny<string>(), null,
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);

        await using var db = _factory.CreateDbContext();
        var persisted = await db.EmassImportSessions.FirstAsync(x => x.Id == session.Id);
        persisted.Status.Should().Be(EmassImportStatus.Parsing);
        persisted.ParseJobId.Should().Be(parseJobId);
    }

    [Fact]
    public async Task StartParseAsync_ZipExtension_DetectsPackageZipFormat()
    {
        var tenantId = Guid.NewGuid();
        await using var content = new MemoryStream(new byte[] { 0x50, 0x4B });
        var (session, _) = await _sut.StartParseAsync(
            tenantId, "package.zip", "application/zip", content, Guid.NewGuid(), Guid.NewGuid());

        session.Format.Should().Be(EmassImportFormat.PackageZip);
    }

    [Fact]
    public async Task GetPreviewAsync_ReturnsNullWhenSessionMissing()
    {
        var preview = await _sut.GetPreviewAsync(Guid.NewGuid(), Guid.NewGuid());
        preview.Should().BeNull();
    }

    [Fact]
    public async Task GetPreviewAsync_DeserializesPersistedJson()
    {
        var tenantId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var preview = new EmassParseResult(
            new[]
            {
                new EmassParsedSystem("S1", "System One", 10, 0, null),
            },
            "Xlsx");

        await using (var db = _factory.CreateDbContext())
        {
            db.EmassImportSessions.Add(new EmassImportSession
            {
                Id = sessionId,
                TenantId = tenantId,
                OriginalFileName = "x.xlsx",
                StorageBlobKey = "key",
                ContentChecksumSha256 = "sha",
                Status = EmassImportStatus.Parsed,
                Preview = JsonSerializer.Serialize(preview),
            });
            await db.SaveChangesAsync();
        }

        var got = await _sut.GetPreviewAsync(tenantId, sessionId);

        got.Should().NotBeNull();
        got!.Systems.Should().ContainSingle(s => s.SystemIdentifier == "S1");
    }

    [Fact]
    public async Task CommitAsync_RequiresParsedStatus_Throws()
    {
        var tenantId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await using (var db = _factory.CreateDbContext())
        {
            db.EmassImportSessions.Add(new EmassImportSession
            {
                Id = sessionId,
                TenantId = tenantId,
                OriginalFileName = "x.xlsx",
                StorageBlobKey = "key",
                ContentChecksumSha256 = "sha",
                Status = EmassImportStatus.Parsing,
            });
            await db.SaveChangesAsync();
        }

        var act = async () => await _sut.CommitAsync(
            tenantId, sessionId,
            new[] { new EmassCommitInstruction("S1", EmassCommitDecision.Merge) },
            Guid.NewGuid(), Guid.NewGuid());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CommitAsync_EnqueuesCommitJobAndAudits()
    {
        var tenantId = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await using (var db = _factory.CreateDbContext())
        {
            db.EmassImportSessions.Add(new EmassImportSession
            {
                Id = sessionId,
                TenantId = tenantId,
                OriginalFileName = "x.xlsx",
                StorageBlobKey = "key",
                ContentChecksumSha256 = "sha",
                Status = EmassImportStatus.Parsed,
            });
            await db.SaveChangesAsync();
        }

        var commitId = await _sut.CommitAsync(
            tenantId, sessionId,
            new[]
            {
                new EmassCommitInstruction("S1", EmassCommitDecision.Merge),
                new EmassCommitInstruction("S2", EmassCommitDecision.Skip),
            },
            actor, Guid.NewGuid());

        commitId.Should().NotBeEmpty();
        _runner.Verify(r => r.EnqueueAsync(
            WizardJobType.EmassCommit,
            tenantId, actor,
            It.IsAny<EmassCommitJobPayload>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _audit.Verify(a => a.RecordAsync(
            tenantId, actor, WizardAuditAction.EmassCommitted,
            "EmassImportSession", sessionId,
            null, It.IsAny<string>(), null,
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);

        await using var verify = _factory.CreateDbContext();
        var s = await verify.EmassImportSessions.FirstAsync(x => x.Id == sessionId);
        s.Status.Should().Be(EmassImportStatus.Importing);
        s.CommitJobId.Should().Be(commitId);
    }

    [Fact]
    public async Task GetLogAsync_ReadsCommitJobResult()
    {
        var tenantId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var commitJobId = Guid.NewGuid();
        var entries = new[]
        {
            new EmassImportLogEntry("S1", "System One", "Merged", "rs-1", null),
            new EmassImportLogEntry("S2", "System Two", "Failed", null, "boom"),
        };

        await using (var db = _factory.CreateDbContext())
        {
            db.EmassImportSessions.Add(new EmassImportSession
            {
                Id = sessionId,
                TenantId = tenantId,
                OriginalFileName = "x.xlsx",
                StorageBlobKey = "key",
                ContentChecksumSha256 = "sha",
                Status = EmassImportStatus.Imported,
                CommitJobId = commitJobId,
            });
            db.WizardJobStatuses.Add(new WizardJobStatus
            {
                Id = commitJobId,
                TenantId = tenantId,
                JobType = WizardJobType.EmassCommit,
                Status = WizardJobState.Succeeded,
                Result = JsonSerializer.Serialize(entries),
            });
            await db.SaveChangesAsync();
        }

        var log = await _sut.GetLogAsync(tenantId, sessionId);

        log.Should().NotBeNull();
        log!.Entries.Should().HaveCount(2);
        log.Entries.Should().Contain(e => e.Outcome == "Merged" && e.RegisteredSystemId == "rs-1");
        log.Entries.Should().Contain(e => e.Outcome == "Failed" && e.Reason == "boom");
    }

    private sealed class TestDbContextFactory : IDbContextFactory<AtoCopilotContext>
    {
        private readonly DbContextOptions<AtoCopilotContext> _options;
        public TestDbContextFactory(DbContextOptions<AtoCopilotContext> options) => _options = options;
        public AtoCopilotContext CreateDbContext() => new(_options);
    }
}
