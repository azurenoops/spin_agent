using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Mcp.Endpoints;
using Ato.Copilot.Mcp.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Integration;

public sealed class CapabilitySubscriptionEndpointTests : IAsyncLifetime
{
    private readonly InMemoryDatabaseRoot _databaseRoot = new();
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddDbContext<AtoCopilotContext>(options =>
            options.UseInMemoryDatabase("CapabilitySubscriptions", _databaseRoot));
        builder.Services
            .AddAuthentication("Test")
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
        builder.Services.AddAuthorization();
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(service => service.CurrentUserId).Returns("test-user");
        currentUser.SetupGet(service => service.CurrentUserName).Returns("Test User");
        builder.Services.AddSingleton(currentUser.Object);

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapCapabilitySubscriptionEndpoints();
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task GetCatalog_ReturnsOnlyMappedCapabilitiesUnderPublishedComponents()
    {
        // Arrange
        await using (var seedScope = _app.Services.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var published = CreateComponent("Published", CspInheritedComponentStatus.Published);
            var draft = CreateComponent("Draft", CspInheritedComponentStatus.Draft);
            var eligible = CreateCapability(published, "Eligible", CspInheritedCapabilityStatus.Mapped);
            var needsReview = CreateCapability(published, "Needs Review", CspInheritedCapabilityStatus.NeedsReview);
            var unpublished = CreateCapability(draft, "Unpublished", CspInheritedCapabilityStatus.Mapped);
            db.AddRange(published, draft, eligible, needsReview, unpublished);
            await db.SaveChangesAsync();
        }

        await using (var verifyScope = _app.Services.CreateAsyncScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            (await db.CspInheritedCapabilities.CountAsync()).Should().Be(3);
        }

        // Act
        var response = await _client.GetAsync("/api/dashboard/capability-library");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = payload.RootElement.GetProperty("items").EnumerateArray().ToList();
        items.Should().ContainSingle();
        items[0].GetProperty("name").GetString().Should().Be("Eligible");
    }

    [Fact]
    public async Task GetSubscriptions_WhenSystemIsNotVisible_ReturnsNotFound()
    {
        // Arrange
        const string missingSystemId = "other-tenant-system";

        // Act
        var response = await _client.GetAsync(
            $"/api/dashboard/systems/{missingSystemId}/capability-subscriptions");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Subscribe_WhenSystemIsNotVisible_DoesNotCreateSubscription()
    {
        // Arrange
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var published = CreateComponent("Published", CspInheritedComponentStatus.Published);
        var capability = CreateCapability(published, "Eligible", CspInheritedCapabilityStatus.Mapped);
        db.AddRange(published, capability);
        await db.SaveChangesAsync();

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/dashboard/systems/other-tenant-system/capability-subscriptions",
            new { capabilityId = capability.Id.ToString() });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await db.CapabilitySubscriptions.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(CspInheritedComponentStatus.Published, HttpStatusCode.Created, 1)]
    [InlineData(CspInheritedComponentStatus.Draft, HttpStatusCode.BadRequest, 0)]
    public async Task Subscribe_RequiresPublishedParent(
        CspInheritedComponentStatus componentStatus,
        HttpStatusCode expectedStatus,
        int expectedSubscriptionCount)
    {
        // Arrange
        const string systemId = "subscription-system";
        Guid capabilityId;
        await using (var seedScope = _app.Services.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.RegisteredSystems.Add(new RegisteredSystem
            {
                Id = systemId,
                Name = "Subscription System",
                SystemType = SystemType.MajorApplication,
                MissionCriticality = MissionCriticality.MissionSupport,
                HostingEnvironment = "Azure Government",
                CreatedBy = "test",
                IsActive = true,
            });
            var component = CreateComponent("Provider", componentStatus);
            var capability = CreateCapability(component, "Audit Logging", CspInheritedCapabilityStatus.Mapped);
            capabilityId = capability.Id;
            db.AddRange(component, capability);
            await db.SaveChangesAsync();
        }

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/dashboard/systems/{systemId}/capability-subscriptions",
            new { capabilityId = capabilityId.ToString() });

        // Assert
        response.StatusCode.Should().Be(expectedStatus);
        await using var verifyScope = _app.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await verifyDb.CapabilitySubscriptions.CountAsync()).Should().Be(expectedSubscriptionCount);
    }

    private static CspInheritedComponent CreateComponent(
        string name,
        CspInheritedComponentStatus status) => new()
    {
        CspProfileId = Guid.NewGuid(),
        Name = name,
        Description = $"{name} component",
        ComponentType = CspComponentType.Service,
        SourceFormat = SourceFormat.OscalJson,
        Status = status,
        ImportedBy = "test",
    };

    private static CspInheritedCapability CreateCapability(
        CspInheritedComponent component,
        string name,
        CspInheritedCapabilityStatus status) => new()
    {
        CspInheritedComponentId = component.Id,
        CspInheritedComponent = component,
        Name = name,
        Description = $"{name} capability",
        MappedNistControlIds = ["AC-2"],
        Status = status,
        CreatedBy = "test",
    };

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "test-user")],
                Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
