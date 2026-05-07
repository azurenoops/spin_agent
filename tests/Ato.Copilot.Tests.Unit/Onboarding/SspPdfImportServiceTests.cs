using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Ato.Copilot.Agents.Compliance.Services.Onboarding.SspPdf;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Interfaces.Storage;
using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Tests.Unit.Onboarding;

/// <summary>
/// Unit tests for <see cref="SspPdfImportService"/> (T084 / FR-040..FR-046).
/// Verifies batch upload enqueues one job per PDF, corrections roundtrip,
/// and commit creates a `RegisteredSystem` with PDF-source provenance.
/// </summary>
public class SspPdfImportServiceTests : IDisposable
{
    private readonly TestDbContextFactory _factory;
    private readonly Mock<IFileStorageProvider> _storage = new();
    private readonly Mock<IWizardJobRunner> _runner = new();
    private readonly Mock<IWizardAuditService> _audit = new();
    private readonly Mock<IWizardArtifactDependencyService> _deps = new();
    private readonly SspPdfImportService _sut;

    public SspPdfImportServiceTests()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"SspPdfImportSvc_{Guid.NewGuid()}")
            .Options;
        _factory = new TestDbContextFactory(options);

        _runner
            .Setup(r => r.EnqueueAsync(
                It.IsAny<WizardJobType>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<SspPdfExtractJobPayload>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((WizardJobType jt, Guid t, Guid u, SspPdfExtractJobPayload _, CancellationToken __) =>
                new WizardJobStatus { Id = Guid.NewGuid(), TenantId = t, JobType = jt, EnqueuedBy = u });

        _deps
            .Setup(d => d.LinkAsync(
                It.IsAny<Guid>(), It.IsAny<ArtifactSourceKind>(), It.IsAny<Guid>(),
                It.IsAny<string>(), It.IsAny<ArtifactDependentKind>(), It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WizardArtifactDependency());

        _sut = new SspPdfImportService(
            _factory, _storage.Object, _runner.Object, _audit.Object, _deps.Object,
            NullLogger<SspPdfImportService>.Instance);
    }

    public void Dispose()
    {
        using var db = _factory.CreateDbContext();
        db.Database.EnsureDeleted();
    }

    [Fact]
    public async Task StartBatchAsync_PerPdfJob_PersistsAndAudits()
    {
        var tenantId = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var files = new List<(string, string, Stream)>
        {
            ("a.pdf", "application/pdf", new MemoryStream(new byte[] { 1, 2, 3 })),
            ("b.pdf", "application/pdf", new MemoryStream(new byte[] { 4, 5, 6 })),
        };

        var result = await _sut.StartBatchAsync(tenantId, files, actor, Guid.NewGuid());

        result.Sessions.Should().HaveCount(2);
        result.BatchId.Should().NotBeEmpty();

        _runner.Verify(r => r.EnqueueAsync(
            WizardJobType.SspPdfExtract,
            tenantId, actor,
            It.IsAny<SspPdfExtractJobPayload>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
        _audit.Verify(a => a.RecordAsync(
            tenantId, actor, WizardAuditAction.SspPdfUploaded,
            It.IsAny<string>(), It.IsAny<Guid>(),
            null, It.IsAny<string>(), null,
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Exactly(2));

        await using var db = _factory.CreateDbContext();
        (await db.SspPdfImportSessions
            .Where(s => s.BatchId == result.BatchId)
            .ToListAsync())
            .Should().AllSatisfy(s =>
            {
                s.Status.Should().Be(SspPdfStatus.Extracting);
                s.ExtractJobId.Should().NotBeNull();
            });
    }

    [Fact]
    public async Task UpdateCorrections_StoresAsJsonAndAudits()
    {
        var tenantId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await using (var db = _factory.CreateDbContext())
        {
            db.SspPdfImportSessions.Add(new SspPdfImportSession
            {
                Id = sessionId,
                TenantId = tenantId,
                BatchId = Guid.NewGuid(),
                OriginalFileName = "x.pdf",
                StorageBlobKey = "k",
                ContentChecksumSha256 = "sha",
                Status = SspPdfStatus.Extracted,
            });
            await db.SaveChangesAsync();
        }

        await _sut.UpdateCorrectionsAsync(
            tenantId, sessionId,
            new[] { new SspPdfFieldCorrection("system_name", "Corrected Name") },
            Guid.NewGuid(), Guid.NewGuid());

        await using var verify = _factory.CreateDbContext();
        var session = await verify.SspPdfImportSessions.FirstAsync(s => s.Id == sessionId);
        session.UserCorrections.Should().Contain("\"system_name\"");
        session.UserCorrections.Should().Contain("Corrected Name");
    }

    [Fact]
    public async Task CommitToSystem_AppliesCorrectionsAndCreatesProvenanceLinkedSystem()
    {
        var tenantId = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var extraction = new SspPdfExtractionResult(
            IsAccepted: true,
            RejectReason: null,
            RejectMessage: null,
            Fields: new List<SspPdfField>
            {
                new("system_identifier", "ACME-1", SspPdfFieldConfidence.High, null),
                new("system_name", "Acme Portal", SspPdfFieldConfidence.Medium, null),
            },
            PageCount: 12);
        var corrections = new[] { new SspPdfFieldCorrection("system_name", "Acme Portal v2") };

        await using (var db = _factory.CreateDbContext())
        {
            db.SspPdfImportSessions.Add(new SspPdfImportSession
            {
                Id = sessionId,
                TenantId = tenantId,
                BatchId = Guid.NewGuid(),
                OriginalFileName = "acme.pdf",
                StorageBlobKey = "k",
                ContentChecksumSha256 = "sha-acme",
                Status = SspPdfStatus.Extracted,
                ExtractionResult = JsonSerializer.Serialize(extraction),
                UserCorrections = JsonSerializer.Serialize(corrections),
            });
            await db.SaveChangesAsync();
        }

        var systemId = await _sut.CommitToSystemAsync(tenantId, sessionId, actor, Guid.NewGuid());

        systemId.Should().NotBeEmpty();
        await using var verify = _factory.CreateDbContext();
        var systems = await verify.RegisteredSystems.ToListAsync();
        systems.Should().ContainSingle();
        systems[0].Name.Should().Be("Acme Portal v2", "manual corrections override extraction");
        systems[0].Acronym.Should().Be("ACME-1");
        systems[0].CreatedBy.Should().Contain("ssp-pdf-import");
        systems[0].HostingEnvironment.Should().Contain("SSP PDF");

        var session = await verify.SspPdfImportSessions.FirstAsync(s => s.Id == sessionId);
        session.Status.Should().Be(SspPdfStatus.Imported);

        _deps.Verify(d => d.LinkAsync(
            tenantId, ArtifactSourceKind.SspPdfImportSession, sessionId,
            "sha-acme", ArtifactDependentKind.RegisteredSystem,
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
        _audit.Verify(a => a.RecordAsync(
            tenantId, actor, WizardAuditAction.SspPdfImported,
            It.IsAny<string>(), sessionId,
            null, It.IsAny<string>(), null,
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CommitToSystem_RequiresExtractedStatus_Throws()
    {
        var tenantId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await using (var db = _factory.CreateDbContext())
        {
            db.SspPdfImportSessions.Add(new SspPdfImportSession
            {
                Id = sessionId,
                TenantId = tenantId,
                BatchId = Guid.NewGuid(),
                OriginalFileName = "x.pdf",
                StorageBlobKey = "k",
                ContentChecksumSha256 = "sha",
                Status = SspPdfStatus.Uploaded,
            });
            await db.SaveChangesAsync();
        }

        var act = async () => await _sut.CommitToSystemAsync(tenantId, sessionId, Guid.NewGuid(), Guid.NewGuid());
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private sealed class TestDbContextFactory : IDbContextFactory<AtoCopilotContext>
    {
        private readonly DbContextOptions<AtoCopilotContext> _options;
        public TestDbContextFactory(DbContextOptions<AtoCopilotContext> options) => _options = options;
        public AtoCopilotContext CreateDbContext() => new(_options);
    }
}
