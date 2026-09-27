using System.Threading.Channels;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Tests.Unit.Services;

/// <summary>
/// Unit tests for SspExportService — export enqueue, listing, pagination,
/// template upload/delete/rename, size validation, and background processing.
/// </summary>
public class SspExportServiceTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly SspExportService _service;
    private readonly Mock<ISspService> _sspServiceMock;
    private readonly Mock<IDocumentTemplateService> _templateServiceMock;
    private readonly Mock<IOscalSspExportService> _oscalServiceMock;
    private readonly Mock<IOscalSchemaValidationService> _schemaValidatorMock;
    private readonly Mock<ISspExportNotifier> _notifierMock;
    private readonly Channel<SspExportJob> _channel;
    private readonly ExportSettings _settings;
    private readonly string _dbName;

    public SspExportServiceTests()
    {
        _dbName = $"SspExport_{Guid.NewGuid()}";

        var services = new ServiceCollection();
        services.AddDbContext<AtoCopilotContext>(opts =>
            opts.UseInMemoryDatabase(_dbName));
        _serviceProvider = services.BuildServiceProvider();

        _sspServiceMock = new Mock<ISspService>();
        _templateServiceMock = new Mock<IDocumentTemplateService>();
        _oscalServiceMock = new Mock<IOscalSspExportService>();
        _schemaValidatorMock = new Mock<IOscalSchemaValidationService>();
        _schemaValidatorMock
            .Setup(service => service.ValidateAsync(
                It.IsAny<string>(),
                "ssp",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OscalSchemaValidationResult { IsValid = true, ModelType = "ssp" });
        _notifierMock = new Mock<ISspExportNotifier>();
        _channel = Channel.CreateBounded<SspExportJob>(100);

        _settings = new ExportSettings
        {
            DataPath = Path.Combine(Directory.GetCurrentDirectory(), $"ssptest_{Guid.NewGuid()}"),
            RetentionDays = 30,
            MaxExportSizeBytes = 52_428_800,
            MaxTemplateSizeBytes = 10_485_760,
        };

        var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();

        _service = new SspExportService(
            scopeFactory,
            _sspServiceMock.Object,
            _templateServiceMock.Object,
            _oscalServiceMock.Object,
            _schemaValidatorMock.Object,
            _notifierMock.Object,
            Mock.Of<ILogger<SspExportService>>(),
            Options.Create(_settings),
            _channel);
    }

    private AtoCopilotContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase(_dbName)
            .Options;
        return new AtoCopilotContext(opts);
    }

    public void Dispose()
    {
        if (Directory.Exists(_settings.DataPath))
            Directory.Delete(_settings.DataPath, true);
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }

    // ─── EnqueueExportAsync ─────────────────────────────────────────────

    [Fact]
    public async Task RetentionWorker_UsesCanonicalKeyPreservingPurge()
    {
        // Arrange
        var service = new Mock<ISspExportService>();
        service.Setup(s => s.PurgeExpiredExportsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);
        using var services = new ServiceCollection().AddSingleton(service.Object).BuildServiceProvider();
        using var worker = new SspExportRetentionService(services.GetRequiredService<IServiceScopeFactory>(),
            Mock.Of<ILogger<SspExportRetentionService>>());

        // Act
        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        service.Verify(s => s.PurgeExpiredExportsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SnapshotRequests_ReplaySameKeyWithoutRegenerationOrDuplicateJob_RejectChangedIntent()
    {
        // Arrange
        await using var db = CreateDb();
        db.RegisteredSystems.Add(new RegisteredSystem { Id = "mission", Name = "DEMO mission", TenantId = Guid.NewGuid() });
        await db.SaveChangesAsync();
        _oscalServiceMock.Setup(s => s.ExportAsync("mission", true, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OscalExportResult("{}", [], new OscalStatistics(0, 0, 0, 0, 0)));

        // Act
        var first = await _service.CreatePreviewAsync("mission", "owner", idempotencyKey: "preview-request");
        var replay = await _service.CreatePreviewAsync("mission", "owner", idempotencyKey: "preview-request");
        var other = await _service.CreatePreviewAsync("mission", "owner", idempotencyKey: "different-preview");
        var export = await _service.EnqueueFromPreviewAsync("mission", first.PreviewId!.Value, "owner", idempotencyKey: "export-request");
        var exportReplay = await _service.EnqueueFromPreviewAsync("mission", first.PreviewId.Value, "owner", idempotencyKey: "export-request");
        var changed = () => _service.EnqueueFromPreviewAsync("mission", other.PreviewId!.Value, "owner", idempotencyKey: "export-request");

        // Assert
        replay.PreviewId.Should().Be(first.PreviewId);
        replay.ContentHash.Should().Be(first.ContentHash);
        exportReplay.Id.Should().Be(export.Id);
        await changed.Should().ThrowAsync<DbUpdateConcurrencyException>();
        _oscalServiceMock.Verify(s => s.ExportAsync("mission", true, true, It.IsAny<CancellationToken>()), Times.Exactly(2));
        _channel.Reader.TryRead(out var job).Should().BeTrue();
        job!.ExportId.Should().Be(export.Id);
        _channel.Reader.TryRead(out _).Should().BeFalse();
        (await db.SspExports.CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task SnapshotRequestKey_IsActorScoped_AndExpiredKeyCannotRecompute()
    {
        // Arrange
        await using var db = CreateDb();
        db.RegisteredSystems.Add(new RegisteredSystem { Id = "mission", Name = "DEMO mission", TenantId = Guid.NewGuid() });
        await db.SaveChangesAsync();
        _oscalServiceMock.Setup(s => s.ExportAsync("mission", true, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OscalExportResult("{}", [], new OscalStatistics(0, 0, 0, 0, 0)));
        var first = await _service.CreatePreviewAsync("mission", "owner-a", idempotencyKey: "shared-key");
        var other = await _service.CreatePreviewAsync("mission", "owner-b", idempotencyKey: "shared-key");
        (await db.SspExports.SingleAsync(e => e.Id == first.PreviewId)).ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1);
        await db.SaveChangesAsync();

        // Act
        await _service.PurgeExpiredExportsAsync();
        var retry = () => _service.CreatePreviewAsync("mission", "owner-a", idempotencyKey: "shared-key");

        // Assert
        first.PreviewId!.Value.Should().NotBe(other.PreviewId!.Value);
        await retry.Should().ThrowAsync<DbUpdateConcurrencyException>();
        (await db.SspExports.CountAsync()).Should().Be(2);
        _oscalServiceMock.Verify(s => s.ExportAsync("mission", true, true, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RetainedPreview_FinalJsonUsesIdenticalBytes_NotRegeneratedWorkingData()
    {
        // Arrange
        var source = "{\"system-security-plan\":{\"uuid\":\"DEMO retained bytes\"}}";
        _oscalServiceMock.Setup(s => s.ExportAsync("mission", true, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OscalExportResult(source, [], new OscalStatistics(7, 0, 0, 0, 0))
            {
                SourceManifest = new("GeneratedOscalContent", [new("ApprovedProfile", "section", "approval-v1", "hash-v1")], [])
            });

        // Act
        var preview = await _service.CreatePreviewAsync("mission", "reviewer");
        (await _service.ListExportsAsync("mission")).Should().BeEmpty("a retained preview is not a generated final export");
        _oscalServiceMock.Setup(s => s.ExportAsync("mission", true, true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Live source changed; must not be read again."));
        var export = await _service.EnqueueFromPreviewAsync("mission", preview.PreviewId!.Value, "reviewer");
        _channel.Reader.TryRead(out var job).Should().BeTrue();
        await _service.ProcessExportAsync(job!);
        var detail = await _service.GetExportAsync(export.Id);
        var file = await _service.GetExportFileStreamAsync(export.Id);
        using var reader = new StreamReader(file!.Value.Stream!);

        // Assert
        detail!.Status.Should().Be("Completed");
        detail.ContentHash.Should().Be(preview.ContentHash);
        (await reader.ReadToEndAsync()).Should().Be(source);
        detail.SourcePreviewId.Should().Be(preview.PreviewId);
        detail.SourceManifest!.Profiles.Single().VersionId.Should().Be("approval-v1");
    }

    [Theory]
    [InlineData("WrongSystem")]
    [InlineData("Expired")]
    [InlineData("ProfileGap")]
    [InlineData("ProviderGap")]
    [InlineData("Tampered")]
    public async Task RetainedPreview_RejectsUnusableSnapshotBeforeQueueing(string invalid)
    {
        // Arrange
        _oscalServiceMock.Setup(s => s.ExportAsync("mission", true, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OscalExportResult("{}", [], new OscalStatistics(0, 0, 0, 0, 0))
            {
                ProfileSourceGaps = invalid == "ProfileGap" ? ["Profile approval missing."] : [],
                ProviderProvenanceGaps = invalid == "ProviderGap" ? ["Provider authority unverified."] : []
            });
        var preview = await _service.CreatePreviewAsync("mission", "reviewer");
        await using var db = CreateDb();
        var row = await db.SspExports.SingleAsync();
        if (invalid == "Expired") { row.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1); await db.SaveChangesAsync(); }
        if (invalid == "Tampered") await File.WriteAllTextAsync(Path.Combine(_settings.ExportsPath, row.FilePath!), "tampered");

        // Act
        var enqueue = () => _service.EnqueueFromPreviewAsync(invalid == "WrongSystem" ? "other" : "mission",
            preview.PreviewId!.Value, "reviewer");

        // Assert
        await enqueue.Should().ThrowAsync<Exception>();
        (await db.SspExports.CountAsync(x => x.Status == "Pending")).Should().Be(0);
        _channel.Reader.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public async Task EnqueueExportAsync_CreatesPendingEntity()
    {
        var result = await _service.EnqueueExportAsync("sys-001", "docx", null, "user@test.com");

        result.Should().NotBeNull();
        result.SystemId.Should().Be("sys-001");
        result.Format.Should().Be("docx");
        result.Status.Should().Be("Pending");
        result.GeneratedBy.Should().Be("user@test.com");
        result.GeneratedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task EnqueueExportAsync_WritesJobToChannel()
    {
        var result = await _service.EnqueueExportAsync("sys-001", "pdf", null, "user@test.com");

        _channel.Reader.TryRead(out var job).Should().BeTrue();
        job!.ExportId.Should().Be(result.Id);
        job.SystemId.Should().Be("sys-001");
        job.Format.Should().Be("pdf");
    }

    [Theory]
    [InlineData("DOCX", "docx")]
    [InlineData("PDF", "pdf")]
    [InlineData("JSON", "json")]
    [InlineData("Docx", "docx")]
    public async Task EnqueueExportAsync_NormalizesFormat(string input, string expected)
    {
        var result = await _service.EnqueueExportAsync("sys-001", input, null, "user@test.com");
        result.Format.Should().Be(expected);
    }

    [Fact]
    public async Task EnqueueExportAsync_SetsExpiresAt()
    {
        var result = await _service.EnqueueExportAsync("sys-001", "docx", null, "user@test.com");
        result.ExpiresAt.Should().BeCloseTo(
            DateTimeOffset.UtcNow.AddDays(_settings.RetentionDays),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ProcessExportAsync_WithProviderProvenanceGap_MarksExportFailedWithoutWritingFile()
    {
        // Arrange
        _oscalServiceMock.Setup(service => service.ExportAsync("sys-gap", true, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OscalExportResult("{}", [], new OscalStatistics(0, 0, 0, 0, 0))
            {
                ProviderProvenanceGaps = ["Provider authorization: source issue date is unknown."]
            });
        var export = await _service.EnqueueExportAsync("sys-gap", "json", null, "user@test.com");
        _channel.Reader.TryRead(out var job).Should().BeTrue();

        // Act
        await _service.ProcessExportAsync(job!);

        // Assert
        await using var db = CreateDb();
        var failed = await db.SspExports.FindAsync(export.Id);
        failed!.Status.Should().Be("Failed");
        failed.ErrorMessage.Should().Contain("Provider authorization");
        Directory.Exists(Path.Combine(_settings.ExportsPath, "sys-gap")).Should().BeFalse();
    }

    [Fact]
    public async Task ProcessExportAsync_WithSchemaInvalidOscal_MarksExportFailedWithoutWritingFile()
    {
        // Arrange
        _oscalServiceMock
            .Setup(service => service.ExportAsync(
                "sys-invalid",
                true,
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OscalExportResult(
                "{\"invalid\":true}",
                [],
                new OscalStatistics(0, 0, 0, 0, 0)));
        _schemaValidatorMock
            .Setup(service => service.ValidateAsync(
                It.IsAny<string>(),
                "ssp",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OscalSchemaValidationResult
            {
                IsValid = false,
                ModelType = "ssp",
                Violations = [new OscalSchemaViolation { JsonPath = "$", Message = "Invalid SSP." }]
            });
        var export = await _service.EnqueueExportAsync("sys-invalid", "json", null, "user@test.com");
        _channel.Reader.TryRead(out var job).Should().BeTrue();

        // Act
        await _service.ProcessExportAsync(job!);

        // Assert
        await using var db = CreateDb();
        var failed = await db.SspExports.FindAsync(export.Id);
        failed.Should().NotBeNull();
        failed!.Status.Should().Be("Failed");
        failed.ErrorMessage.Should().StartWith("OSCAL_SCHEMA_VALIDATION_FAILED");
        Directory.Exists(Path.Combine(_settings.ExportsPath, "sys-invalid")).Should().BeFalse();
        _notifierMock.Verify(notifier => notifier.SendExportFailedAsync(
            "user@test.com",
            export.Id,
            It.Is<string>(error => error.StartsWith("OSCAL_SCHEMA_VALIDATION_FAILED")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnqueueExportAsync_PassesTemplateId()
    {
        var templateId = Guid.NewGuid();
        var result = await _service.EnqueueExportAsync("sys-001", "docx", templateId, "user@test.com");

        result.TemplateId.Should().Be(templateId);

        _channel.Reader.TryRead(out var job).Should().BeTrue();
        job!.TemplateId.Should().Be(templateId);
    }

    // ─── ListExportsAsync ───────────────────────────────────────────────

    [Fact]
    public async Task ListExportsAsync_ReturnsExportsForSystem()
    {
        await _service.EnqueueExportAsync("sys-001", "docx", null, "user@test.com");
        await _service.EnqueueExportAsync("sys-001", "pdf", null, "user@test.com");
        await _service.EnqueueExportAsync("sys-002", "json", null, "other@test.com");

        // Verify data was stored
        using var db = CreateDb();
        var count = await db.SspExports.CountAsync(e => e.SystemId == "sys-001");
        count.Should().Be(2);

        var results = await _service.ListExportsAsync("sys-001");
        results.Should().HaveCount(2);
    }

    [Fact]
    public async Task ListExportsAsync_RespectsLimitAndOffset()
    {
        for (int i = 0; i < 5; i++)
            await _service.EnqueueExportAsync("sys-001", "docx", null, "user@test.com");

        var page1 = await _service.ListExportsAsync("sys-001", limit: 2, offset: 0);
        var page2 = await _service.ListExportsAsync("sys-001", limit: 2, offset: 2);

        page1.Should().HaveCount(2);
        page2.Should().HaveCount(2);
        page1.Select(e => e.ExportId).Should().NotIntersectWith(page2.Select(e => e.ExportId));
    }

    // ─── GetExportAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task GetExportAsync_ReturnsExport()
    {
        var export = await _service.EnqueueExportAsync("sys-001", "docx", null, "user@test.com");

        var detail = await _service.GetExportAsync(export.Id);

        detail.Should().NotBeNull();
        detail!.ExportId.Should().Be(export.Id);
        detail.Format.Should().Be("docx");
    }

    [Fact]
    public async Task GetExportAsync_ReturnsNullForMissingId()
    {
        var result = await _service.GetExportAsync(Guid.NewGuid());
        result.Should().BeNull();
    }

    // ─── UploadTemplateAsync ────────────────────────────────────────────

    [Fact]
    public async Task UploadTemplateAsync_RejectsNonDocxFile()
    {
        using var stream = new MemoryStream([0x01, 0x02, 0x03]);

        var act = async () => await _service.UploadTemplateAsync(
            "test", null, stream, "template.txt", "user@test.com");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Invalid file format*");
    }

    [Fact]
    public async Task UploadTemplateAsync_RejectsOversizedFiles()
    {
        // Create a valid-looking DOCX header but too large
        _settings.MaxTemplateSizeBytes = 100;
        var largeData = new byte[200];
        using var stream = new MemoryStream(largeData);

        var act = async () => await _service.UploadTemplateAsync(
            "test", null, stream, "template.docx", "user@test.com");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*exceeds*");
    }

    // ─── DeleteTemplateAsync ────────────────────────────────────────────

    [Fact]
    public async Task DeleteTemplateAsync_ReturnsFalseForMissingTemplate()
    {
        var result = await _service.DeleteTemplateAsync(Guid.NewGuid(), "user@test.com");
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteTemplateAsync_SoftDeletesSetsIsActiveFalse()
    {
        var templateId = Guid.NewGuid();
        using (var db = CreateDb())
        {
            db.SspTemplates.Add(new SspTemplate
            {
                Id = templateId,
                Name = "Test Template",
                FilePath = "templates/test.docx",
                FileSize = 1000,
                MergeFields = "[]",
                IsActive = true,
                IsDefault = false,
                UploadedBy = "user@test.com",
                UploadedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var result = await _service.DeleteTemplateAsync(templateId, "admin@test.com");

        result.Should().BeTrue();

        using (var db = CreateDb())
        {
            var template = await db.SspTemplates.FindAsync(templateId);
            template!.IsActive.Should().BeFalse();
        }
    }

    // ─── UpdateTemplateAsync ────────────────────────────────────────────

    [Fact]
    public async Task UpdateTemplateAsync_ReturnsNullForMissingTemplate()
    {
        var result = await _service.UpdateTemplateAsync(Guid.NewGuid(), "New Name", null, "user@test.com");
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateTemplateAsync_RenamesTemplate()
    {
        var templateId = Guid.NewGuid();
        using (var db = CreateDb())
        {
            db.SspTemplates.Add(new SspTemplate
            {
                Id = templateId,
                Name = "Old Name",
                FilePath = "templates/test.docx",
                FileSize = 1000,
                MergeFields = "[]",
                IsActive = true,
                IsDefault = false,
                UploadedBy = "user@test.com",
                UploadedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var result = await _service.UpdateTemplateAsync(templateId, "New Name", null, "user@test.com");

        result.Should().NotBeNull();
        result!.Name.Should().Be("New Name");
    }

    [Fact]
    public async Task UpdateTemplateAsync_RejectsDuplicateName()
    {
        var targetId = Guid.NewGuid();
        using (var db = CreateDb())
        {
            db.SspTemplates.Add(new SspTemplate
            {
                Id = Guid.NewGuid(),
                Name = "Existing Name",
                FilePath = "templates/a.docx",
                FileSize = 1000,
                MergeFields = "[]",
                IsActive = true,
                IsDefault = false,
                UploadedBy = "user@test.com",
                UploadedAt = DateTimeOffset.UtcNow,
            });
            db.SspTemplates.Add(new SspTemplate
            {
                Id = targetId,
                Name = "Target Template",
                FilePath = "templates/b.docx",
                FileSize = 1000,
                MergeFields = "[]",
                IsActive = true,
                IsDefault = false,
                UploadedBy = "user@test.com",
                UploadedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var act = async () => await _service.UpdateTemplateAsync(
            targetId, "Existing Name", null, "user@test.com");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*already exists*");
    }

    // ─── PurgeExpiredExportsAsync ───────────────────────────────────────

    [Fact]
    public async Task PurgeExpiredExportsAsync_RemovesExpiredRecords()
    {
        using (var db = CreateDb())
        {
            db.SspExports.Add(new SspExport
            {
                Id = Guid.NewGuid(),
                SystemId = "sys-001",
                Format = "docx",
                Status = "Completed",
                GeneratedBy = "user@test.com",
                GeneratedAt = DateTimeOffset.UtcNow.AddDays(-31),
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
            });
            db.SspExports.Add(new SspExport
            {
                Id = Guid.NewGuid(),
                SystemId = "sys-001",
                Format = "pdf",
                Status = "Completed",
                GeneratedBy = "user@test.com",
                GeneratedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(29),
            });
            await db.SaveChangesAsync();
        }

        await _service.PurgeExpiredExportsAsync();

        using (var db = CreateDb())
        {
            var remaining = await db.SspExports.ToListAsync();
            remaining.Should().HaveCount(1);
            remaining[0].Format.Should().Be("pdf");
        }
    }

    // ─── ListTemplatesAsync ─────────────────────────────────────────────

    [Fact]
    public async Task ListTemplatesAsync_ReturnsOnlyActiveTemplates()
    {
        using (var db = CreateDb())
        {
            db.SspTemplates.Add(new SspTemplate
            {
                Id = Guid.NewGuid(),
                Name = "Active Template",
                FilePath = "templates/a.docx",
                FileSize = 1000,
                MergeFields = "[\"SystemName\"]",
                IsActive = true,
                IsDefault = false,
                UploadedBy = "user@test.com",
                UploadedAt = DateTimeOffset.UtcNow,
            });
            db.SspTemplates.Add(new SspTemplate
            {
                Id = Guid.NewGuid(),
                Name = "Deleted Template",
                FilePath = "templates/b.docx",
                FileSize = 1000,
                MergeFields = "[]",
                IsActive = false,
                IsDefault = false,
                UploadedBy = "user@test.com",
                UploadedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var results = await _service.ListTemplatesAsync();

        results.Should().HaveCount(1);
        results[0].Name.Should().Be("Active Template");
    }

    // ─── Helper ─────────────────────────────────────────────────────────

    private async Task<List<TemplateListDto>> ListTemplates()
    {
        return await _service.ListTemplatesAsync(limit: 100);
    }
}
