using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Ato.Copilot.Agents.Compliance.Services.Onboarding;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Tests.Unit.Onboarding;

/// <summary>
/// Unit tests for <see cref="WizardArtifactInventoryService"/> (T126 / FR-093).
/// </summary>
public class WizardArtifactInventoryServiceTests : IDisposable
{
    private readonly TestDbContextFactory _factory;
    private readonly WizardArtifactInventoryService _sut;

    public WizardArtifactInventoryServiceTests()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"Inventory_{Guid.NewGuid()}")
            .Options;
        _factory = new TestDbContextFactory(options);
        _sut = new WizardArtifactInventoryService(_factory);
    }

    public void Dispose()
    {
        using var db = _factory.CreateDbContext();
        db.Database.EnsureDeleted();
    }

    [Fact]
    public async Task ListAsync_AggregatesAcrossAllKinds()
    {
        var tid = Guid.NewGuid();
        await SeedAsync(tid);

        var page = await _sut.ListAsync(tid);

        page.TotalCount.Should().Be(4);
        page.Items.Select(i => i.Kind).Distinct()
            .Should().Contain(new[]
            {
                ArtifactSourceKind.Template,
                ArtifactSourceKind.EmassImportSession,
                ArtifactSourceKind.SspPdfImportSession,
                ArtifactSourceKind.NarrativeSeedDocument,
            });
    }

    [Fact]
    public async Task ListAsync_FiltersByKind()
    {
        var tid = Guid.NewGuid();
        await SeedAsync(tid);
        var page = await _sut.ListAsync(tid, ArtifactSourceKind.Template);
        page.TotalCount.Should().Be(1);
        page.Items.Single().Kind.Should().Be(ArtifactSourceKind.Template);
    }

    [Fact]
    public async Task ListAsync_RollsUpDependentsCount()
    {
        var tid = Guid.NewGuid();
        var template = await SeedAsync(tid);

        await using (var db = _factory.CreateDbContext())
        {
            db.WizardArtifactDependencies.AddRange(
                new WizardArtifactDependency
                {
                    Id = Guid.NewGuid(), TenantId = tid,
                    SourceArtifactType = ArtifactSourceKind.Template, SourceArtifactId = template,
                    SourceVersionTag = "1.0",
                    DependentType = ArtifactDependentKind.SspExport, DependentId = Guid.NewGuid(),
                    IsStale = true, StaleSince = DateTimeOffset.UtcNow,
                },
                new WizardArtifactDependency
                {
                    Id = Guid.NewGuid(), TenantId = tid,
                    SourceArtifactType = ArtifactSourceKind.Template, SourceArtifactId = template,
                    SourceVersionTag = "1.0",
                    DependentType = ArtifactDependentKind.CrmExport, DependentId = Guid.NewGuid(),
                    IsStale = false,
                });
            await db.SaveChangesAsync();
        }

        var page = await _sut.ListAsync(tid, ArtifactSourceKind.Template);
        var row = page.Items.Single();
        row.DependentsCount.Should().Be(2);
        row.StaleDependentsCount.Should().Be(1);
    }

    [Fact]
    public async Task ListAsync_PaginationCapsAt200()
    {
        var tid = Guid.NewGuid();
        var page = await _sut.ListAsync(tid, pageSize: 9_999);
        page.PageSize.Should().Be(200);
    }

    private async Task<Guid> SeedAsync(Guid tid)
    {
        await using var db = _factory.CreateDbContext();
        var template = new OrganizationDocumentTemplate
        {
            Id = Guid.NewGuid(), TenantId = tid,
            TemplateType = TemplateType.Ssp,
            Label = "SSP", Version = "1.0",
            OriginalFileName = "ssp.docx",
            StorageBlobKey = "k", FileFormat = TemplateFileFormat.Docx,
            FileSizeBytes = 100, ContentChecksumSha256 = "x",
            IsDefault = true,
            ValidationStatus = TemplateValidationStatus.Compliant,
            Status = TemplateStatus.Active,
        };
        db.OrganizationDocumentTemplates.Add(template);
        db.EmassImportSessions.Add(new EmassImportSession
        {
            Id = Guid.NewGuid(), TenantId = tid,
            OriginalFileName = "emass.zip",
            StorageBlobKey = "k", FileSizeBytes = 100,
        });
        db.SspPdfImportSessions.Add(new SspPdfImportSession
        {
            Id = Guid.NewGuid(), TenantId = tid,
            OriginalFileName = "ssp.pdf",
            StorageBlobKey = "k", FileSizeBytes = 100,
            BatchId = Guid.NewGuid(),
        });
        db.NarrativeSeedDocuments.Add(new NarrativeSeedDocument
        {
            Id = Guid.NewGuid(), TenantId = tid,
            Label = "Seed", Tags = "[]",
            EvidenceArtifactId = Guid.Empty,
            IndexingStatus = NarrativeSeedIndexingStatus.Pending,
            Status = NarrativeSeedStatus.Active,
        });
        await db.SaveChangesAsync();
        return template.Id;
    }

    private sealed class TestDbContextFactory : IDbContextFactory<AtoCopilotContext>
    {
        private readonly DbContextOptions<AtoCopilotContext> _options;
        public TestDbContextFactory(DbContextOptions<AtoCopilotContext> options) => _options = options;
        public AtoCopilotContext CreateDbContext() => new(_options);
    }
}
