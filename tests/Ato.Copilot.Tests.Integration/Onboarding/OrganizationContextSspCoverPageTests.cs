using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Tests.Integration.Onboarding;

/// <summary>
/// Integration test confirming the SSP cover-page renderer reads
/// <see cref="OrganizationContext.OrganizationName"/> via the
/// <see cref="DocumentTemplateService"/> merge-data builder (T050 / FR-014 / T054).
/// </summary>
public class OrganizationContextSspCoverPageTests : IAsyncLifetime
{
    private ServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        var dbName = $"OrgContextCover_{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddDbContext<AtoCopilotContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddTransient(typeof(ILogger<>), typeof(NullLoggerAdapter<>));
        services.AddScoped<IDocumentTemplateService, DocumentTemplateService>();
        _services = services.BuildServiceProvider();

        // Seed RegisteredSystem + OrganizationContext.
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var tenantId = Guid.NewGuid();
        db.OrganizationContexts.Add(new OrganizationContext
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            OrganizationName = "Unrelated tenant organization",
            Branch = BranchAffiliation.CivilAgency,
        });
        db.RegisteredSystems.Add(new RegisteredSystem
        {
            Id = "sys-cover-test",
            TenantId = tenantId,
            Name = "Test System",
            Acronym = "TST",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure Government",
            CreatedBy = "tester",
        });
        db.OrganizationContexts.Add(new OrganizationContext
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrganizationName = "Department of Cover Page Testing",
            Branch = BranchAffiliation.AirForce,
            SubOrganization = "Office of Compliance",
        });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync()
    {
        _services.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task RenderDocxAsync_IncludesOrganizationName()
    {
        // Arrange
        using var scope = _services.CreateScope();
        var renderer = scope.ServiceProvider.GetRequiredService<IDocumentTemplateService>();

        // Act
        var docx = await renderer.RenderDocxAsync("sys-cover-test", "ssp");

        // Assert
        docx.Should().NotBeNull();
        docx.Length.Should().BeGreaterThan(0);

        // The built-in DOCX writer emits all merge fields as paragraph text.
        // Extract the document.xml body and assert the OrganizationName appears.
        using var ms = new MemoryStream(docx);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry = archive.GetEntry("word/document.xml");
        entry.Should().NotBeNull("word/document.xml is required by all DOCX files");
        using var entryStream = entry!.Open();
        using var reader = new StreamReader(entryStream, Encoding.UTF8);
        var documentXml = await reader.ReadToEndAsync();

        documentXml.Should().Contain("Department of Cover Page Testing",
            "the SSP cover-page renderer must inject OrganizationContext.OrganizationName (FR-014)");
        documentXml.Should().NotContain("Unrelated tenant organization",
            "an unscoped renderer must select the system's actual tenant, not the first organization row");
    }

    /// <summary>
    /// Adapter that fulfils <see cref="Microsoft.Extensions.Logging.ILogger{T}"/> by
    /// delegating to <see cref="NullLogger{T}.Instance"/>. Required because
    /// <see cref="NullLoggerFactory"/> alone does not register the open-generic.
    /// </summary>
    private sealed class NullLoggerAdapter<T> : ILogger<T>
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull
            => NullLogger<T>.Instance.BeginScope(state)!;

        public bool IsEnabled(LogLevel logLevel)
            => NullLogger<T>.Instance.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => NullLogger<T>.Instance.Log(logLevel, eventId, state, exception, formatter);
    }
}
