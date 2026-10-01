using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

public sealed class CatalogSourceAdministrationHttpTests : IClassFixture<WorkspaceMembershipFactory>
{
    private readonly WebApplicationFactory<McpProgram> _factory;
    private readonly Mock<IFrameworkImportService> _importer = new();
    private static readonly Guid Directory = Guid.Parse("079ca000-0000-0000-0000-000000000001");

    public CatalogSourceAdministrationHttpTests(WorkspaceMembershipFactory factory)
    {
        _importer.Setup(x => x.CaptureSourceAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FrameworkSourceResult("NIST-800-53-R5", "synthetic-1",
                "https://example.invalid/catalog", "hash", DateTime.UtcNow, true));
        _importer.Setup(x => x.ImportFrameworkAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(2);
        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            foreach (var descriptor in services.Where(x => x.ServiceType == typeof(IHostedService)
                && x.ImplementationType != typeof(TenancySeedHostedService)).ToArray()) services.Remove(descriptor);
            services.RemoveAll<IFrameworkImportService>();
            services.AddSingleton(_importer.Object);
        }));
    }

    [Fact]
    public async Task ProviderAdministrator_CanCaptureSourcesAndImportExistingFrameworks()
    {
        // Arrange
        using var client = Client(Guid.NewGuid(), csp: true);

        // Act
        var status = await client.GetAsync("/api/dashboard/frameworks/source-management");
        var capture = await client.PostAsync("/api/dashboard/frameworks/NIST-800-53-R5/source", null);
        var import = await client.PostAsync("/api/dashboard/frameworks/NIST-800-53-R5/import", null);

        // Assert
        status.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await status.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("canManageSources").GetBoolean().Should().BeTrue();
        capture.StatusCode.Should().Be(HttpStatusCode.OK);
        import.StatusCode.Should().Be(HttpStatusCode.OK);
        _importer.Verify(x => x.CaptureSourceAsync("NIST-800-53-R5", false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrganizationAdministrator_CannotMutateGlobalSourcesOrUseLegacyImport(bool alsoPlatformAdministrator)
    {
        // Arrange
        using var initialized = _factory.CreateClient();
        var actor = Guid.NewGuid();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var person = new Person { TenantId = WorkspaceMembershipFactory.TenantAId,
                DisplayName = "Synthetic organization admin", Email = $"{actor:N}@example.invalid" };
            db.Add(person);
            await db.SaveChangesAsync();
            db.OrganizationMemberships.Add(new() { TenantId = person.TenantId, DirectoryTenantId = Directory,
                ObjectId = actor, PersonId = person.Id, GrantedBy = "fixture" });
            db.OrganizationRoleAssignments.Add(new() { TenantId = person.TenantId, PersonId = person.Id,
                Role = OrganizationRole.Administrator });
            await db.SaveChangesAsync();
        }
        using var client = Client(actor, csp: false);
        if (alsoPlatformAdministrator) client.DefaultRequestHeaders.Add("X-Test-Roles", "CSP.Admin");

        // Act
        var status = await client.GetFromJsonAsync<JsonElement>("/api/dashboard/frameworks/source-management");
        var capture = await client.PostAsync("/api/dashboard/frameworks/NIST-800-53-R5/source", null);
        var import = await client.PostAsync("/api/dashboard/frameworks/NIST-800-53-R5/import", null);
        var all = await client.PostAsync("/api/dashboard/frameworks/import", null);
        var backfill = await client.PostAsync("/api/dashboard/frameworks/backfill-sources", null);

        // Assert
        status.GetProperty("canManageSources").GetBoolean().Should().BeFalse();
        status.GetProperty("isPlatformAdministrator").GetBoolean().Should().Be(alsoPlatformAdministrator);
        capture.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        import.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        all.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        backfill.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _importer.Verify(x => x.CaptureSourceAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MissingSourceBackfill_ReportsPartialFailureWithoutInvokingFullImport()
    {
        // Arrange
        using var client = Client(Guid.NewGuid(), csp: true);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            foreach (var identifier in new[] { "NIST-800-53-R5", "FEDRAMP-R5" })
                if (!await db.ComplianceFrameworks.AnyAsync(x => x.Identifier == identifier))
                    db.ComplianceFrameworks.Add(new() { Identifier = identifier, Name = identifier });
            await db.SaveChangesAsync();
        }
        _importer.Setup(x => x.CaptureSourceAsync("FEDRAMP-R5", true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Synthetic unavailable source"));

        // Act
        var response = await client.PostAsync("/api/dashboard/frameworks/backfill-sources", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("failures").EnumerateArray().Should().Contain(x => x.GetProperty("identifier").GetString() == "FEDRAMP-R5");
        _importer.Verify(x => x.ImportAllAsync(It.IsAny<CancellationToken>()), Times.Never);
        _importer.Verify(x => x.ImportFrameworkAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnonymousCaller_CannotAccessCatalogAdministration()
    {
        // Arrange
        using var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsync("/api/dashboard/frameworks/NIST-800-53-R5/source", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private HttpClient Client(Guid actor, bool csp)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Tid", Directory.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Oid", actor.ToString());
        client.DefaultRequestHeaders.Add("X-Workspace-Kind", csp ? "csp" : "organization");
        client.DefaultRequestHeaders.Add("X-Workspace-Mode", "ordinary");
        if (csp) client.DefaultRequestHeaders.Add("X-Test-Roles", "CSP.Admin");
        else client.DefaultRequestHeaders.Add("X-Workspace-Tenant-Id", WorkspaceMembershipFactory.TenantAId.ToString());
        return client;
    }
}
