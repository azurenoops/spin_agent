using System.Net;
using System.Net.Http.Json;
using System.Text;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Mcp.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Server;

public sealed class ProviderNarrativeLibraryHttpTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly Guid _profileId = Guid.NewGuid();
    private readonly Guid _capabilityId = Guid.NewGuid();
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private const string Root = "/api/csp/narrative-library";

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication("Synthetic")
            .AddScheme<AuthenticationSchemeOptions, NarrativeLibraryHttpTests.SyntheticAuthentication>("Synthetic", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<ITenantContext>(new TenantContext(Guid.Empty, isCspAdmin: true) { IsWorkspaceRequest = true });
        builder.Services.AddScoped<AtoCopilotContext>(_ => new ProviderContext(
            new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(_connection).Options));
        builder.Services.AddScoped<NarrativeLibraryService>();
        builder.Services.AddScoped<ProviderNarrativeLibraryService>();
        builder.Services.AddScoped<NarrativeProposalService>();
        builder.Services.AddSingleton(new Mock<IControlNarrativeService>(MockBehavior.Strict).Object);
        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapScopedNarrativeLibraryEndpoints();
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            await db.Database.EnsureCreatedAsync();
            db.CspProfiles.Add(new() { Id = _profileId, DisplayName = "Synthetic provider", LegalEntityName = "Synthetic provider" });
            var component = new CspInheritedComponent { CspProfileId = _profileId, Name = "Access component",
                Status = CspInheritedComponentStatus.Published };
            db.CspInheritedComponents.Add(component);
            db.CspInheritedCapabilities.Add(new() { Id = _capabilityId, CspInheritedComponentId = component.Id,
                Name = "Access capability", Status = CspInheritedCapabilityStatus.Mapped, MappedNistControlIds = ["AC-2"] });
            db.NistControls.Add(new() { Id = "ac-2", Title = "Account management" });
            await db.SaveChangesAsync();
        }
        await _app.StartAsync();
        _client = _app.GetTestClient();
        _client.DefaultRequestHeaders.Add("X-Synthetic-Actor", "provider-administrator");
    }

    [Fact]
    public async Task ProviderWorkspaceImportsMapsAndPublishesUsingRealProviderIdentities()
    {
        // Arrange
        var access = await _client.GetFromJsonAsync<ProviderNarrativeAccessResponse>($"{Root}/access");
        using var form = new MultipartFormDataContent
        {
            { new StringContent("Provider reference"), "title" },
            { new StringContent("Provider"), "scope" },
            { new StringContent(access!.CspProfileId.ToString()), "scopeId" },
            { new ByteArrayContent(Encoding.UTF8.GetBytes("Unmapped provider input")), "file", "reference.txt" }
        };

        // Act
        var imported = await _client.PostAsync($"{Root}/imports", form);
        imported.StatusCode.Should().Be(HttpStatusCode.Created);
        var draft = await imported.Content.ReadFromJsonAsync<NarrativeReferenceResponse>();
        var passages = new[] { new NarrativeReferencePassage("AC-2", "Technical", "Unverified provider claim") };
        var edited = await _client.PatchAsJsonAsync($"{Root}/{draft!.Id}",
            new UpdateNarrativeReferenceDraftRequest(1, "ProviderCapability", _capabilityId.ToString(), passages));
        var published = await _client.PostAsJsonAsync($"{Root}/{draft.Id}/publish", new PublishNarrativeReferenceRequest(2, true, passages));
        var stale = await _client.PostAsJsonAsync($"{Root}/{draft.Id}/publish", new PublishNarrativeReferenceRequest(2, true, passages));

        // Assert
        access.CspProfileId.Should().Be(_profileId);
        access.Capabilities.Select(item => item.Id).Should().Equal(_capabilityId.ToString());
        edited.StatusCode.Should().Be(HttpStatusCode.OK);
        published.StatusCode.Should().Be(HttpStatusCode.OK);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await using var inspect = _app.Services.CreateAsyncScope();
        var db = inspect.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.RegisteredSystems.CountAsync()).Should().Be(0);
        (await db.ControlImplementations.CountAsync()).Should().Be(0);
        (await db.NarrativeReferences.CountAsync()).Should().Be(0);
        (await db.Set<ProviderNarrativeReference>().SingleAsync()).IsPublished.Should().BeTrue();
    }

    [Fact]
    public async Task ProviderWorkspaceRequiresAuthentication()
    {
        // Arrange
        _client.DefaultRequestHeaders.Remove("X-Synthetic-Actor");

        // Act
        var response = await _client.GetAsync(Root);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private sealed class ProviderContext(DbContextOptions<AtoCopilotContext> options) : AtoCopilotContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<ProviderNarrativeReference>();
        }
    }
}
