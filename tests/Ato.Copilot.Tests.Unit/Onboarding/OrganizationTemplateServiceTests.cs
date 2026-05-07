using System.IO.Compression;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Ato.Copilot.Agents.Compliance.Services.Onboarding.Templates;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Interfaces.Storage;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Onboarding;

namespace Ato.Copilot.Tests.Unit.Onboarding;

/// <summary>
/// Unit tests for <see cref="OrganizationTemplateService"/> (T107 / FR-080..FR-096).
/// Covers default-uniqueness invariant, format gates, and the cascade hook.
/// </summary>
public class OrganizationTemplateServiceTests : IDisposable
{
    private readonly TestDbContextFactory _factory;
    private readonly Mock<IFileStorageProvider> _storage = new();
    private readonly Mock<IWizardAuditService> _audit = new();
    private readonly Mock<IWizardArtifactDependencyService> _dependencies = new();
    private readonly OrganizationTemplateService _sut;

    public OrganizationTemplateServiceTests()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"OrgTpl_{Guid.NewGuid()}")
            .Options;
        _factory = new TestDbContextFactory(options);
        _storage.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _dependencies.Setup(d => d.FlagDependentsStaleAsync(It.IsAny<Guid>(),
                It.IsAny<ArtifactSourceKind>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _dependencies.Setup(d => d.ListBySourceAsync(It.IsAny<Guid>(),
                It.IsAny<ArtifactSourceKind>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<WizardArtifactDependency>());
        _sut = new OrganizationTemplateService(
            _factory, _storage.Object, _audit.Object, _dependencies.Object,
            Options.Create(new OnboardingOptions()),
            NullLogger<OrganizationTemplateService>.Instance);
    }

    public void Dispose()
    {
        using var db = _factory.CreateDbContext();
        db.Database.EnsureDeleted();
    }

    [Fact]
    public async Task UploadAsync_DocxIntoSspSlot_Persists()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var docx = BuildDocx("template body with {{system_name}} {{system_id}} {{baseline}} {{controls}}");

        await using var s = new MemoryStream(docx);
        var result = await _sut.UploadAsync(
            tenantId, actorId, TemplateType.Ssp, "Default SSP", "v1.0",
            "ssp-template.docx", s, docx.Length, isDefault: false);

        result.Template.Id.Should().NotBe(Guid.Empty);
        result.Template.IsDefault.Should().BeFalse();
        result.Template.ValidationStatus.Should().Be(TemplateValidationStatus.Compliant);
    }

    [Fact]
    public async Task UploadAsync_XlsxIntoSspSlot_ThrowsWrongFormat()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        await using var s = new MemoryStream(new byte[10]);

        var act = async () => await _sut.UploadAsync(
            tenantId, actorId, TemplateType.Ssp, "x", "v1",
            "ssp.xlsx", s, 10, false);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Be(WizardErrorCodes.TemplateWrongFormat);
    }

    [Fact]
    public async Task MarkDefaultAsync_DemotesPreviousDefault()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var docx = BuildDocx("body {{system_name}} {{system_id}} {{baseline}} {{controls}}");

        await using var s1 = new MemoryStream(docx);
        var first = await _sut.UploadAsync(
            tenantId, actorId, TemplateType.Ssp, "First", "v1",
            "first.docx", s1, docx.Length, isDefault: true);
        await using var s2 = new MemoryStream(docx);
        var second = await _sut.UploadAsync(
            tenantId, actorId, TemplateType.Ssp, "Second", "v2",
            "second.docx", s2, docx.Length, isDefault: false);

        var promoted = await _sut.MarkDefaultAsync(tenantId, second.Template.Id, actorId);

        promoted.IsDefault.Should().BeTrue();

        await using var db = _factory.CreateDbContext();
        var all = await db.OrganizationDocumentTemplates
            .Where(t => t.TenantId == tenantId && t.TemplateType == TemplateType.Ssp)
            .ToListAsync();
        all.Should().HaveCount(2);
        all.Single(t => t.Id == first.Template.Id).IsDefault.Should().BeFalse();
        all.Single(t => t.Id == second.Template.Id).IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_TemplateMarkedDefault_Throws()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var docx = BuildDocx("body {{system_name}} {{system_id}} {{baseline}} {{controls}}");
        await using var s = new MemoryStream(docx);
        var t = await _sut.UploadAsync(
            tenantId, actorId, TemplateType.Ssp, "Default", "v1",
            "ssp.docx", s, docx.Length, isDefault: true);

        var act = async () => await _sut.DeleteAsync(tenantId, t.Template.Id, actorId);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Be(WizardErrorCodes.TemplateDefaultProtected);
    }

    [Fact]
    public async Task ReplaceFileAsync_FlagsDependentsStale()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var docx = BuildDocx("body {{system_name}} {{system_id}} {{baseline}} {{controls}}");
        await using var s = new MemoryStream(docx);
        var t = await _sut.UploadAsync(
            tenantId, actorId, TemplateType.Ssp, "Default", "v1",
            "ssp.docx", s, docx.Length, isDefault: false);

        _dependencies.Setup(d => d.FlagDependentsStaleAsync(
                tenantId, ArtifactSourceKind.Template, t.Template.Id,
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        await using var s2 = new MemoryStream(docx);
        var result = await _sut.ReplaceFileAsync(
            tenantId, t.Template.Id, actorId, "ssp-v2.docx", s2, docx.Length, "v2");

        result.DependentsFlagged.Should().Be(3);
        _dependencies.Verify(d => d.FlagDependentsStaleAsync(
            tenantId, ArtifactSourceKind.Template, t.Template.Id,
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static byte[] BuildDocx(string body)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("word/document.xml");
            using var w = new StreamWriter(entry.Open());
            w.Write($"<?xml version=\"1.0\"?><document><body><p>{System.Net.WebUtility.HtmlEncode(body)}</p></body></document>");
        }
        return ms.ToArray();
    }

    private sealed class TestDbContextFactory : IDbContextFactory<AtoCopilotContext>
    {
        private readonly DbContextOptions<AtoCopilotContext> _options;
        public TestDbContextFactory(DbContextOptions<AtoCopilotContext> options) => _options = options;
        public AtoCopilotContext CreateDbContext() => new(_options);
    }
}
