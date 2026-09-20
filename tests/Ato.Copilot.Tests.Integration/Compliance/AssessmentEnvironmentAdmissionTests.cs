using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Interceptors;
using Ato.Copilot.Core.Interfaces.Auth;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Kanban;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Mcp.Authorization;
using Ato.Copilot.Mcp.Endpoints;
using Ato.Copilot.Mcp.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Compliance;

public sealed class AssessmentEnvironmentAdmissionTests : IAsyncLifetime
{
    private const string ActorId = "synthetic-assessment-writer";
    private const string SubscriptionId = "00000000-0000-0000-0000-000000000981";
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Mock<IAtoComplianceEngine> _engine = new(MockBehavior.Strict);
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private IDbContextFactory<AtoCopilotContext> _factory = null!;
    private ITenantContextAccessor _tenants = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<ITenantContextAccessor, TenantContextAccessor>();
        builder.Services.AddSingleton<TenantStampingSaveChangesInterceptor>();
        builder.Services.AddDbContextFactory<AtoCopilotContext>((sp, options) =>
            options.UseInMemoryDatabase($"assessment-admission-{_tenantId}")
                .AddInterceptors(sp.GetRequiredService<TenantStampingSaveChangesInterceptor>()));
        builder.Services.AddSingleton(_engine.Object);
        builder.Services.AddSingleton(Mock.Of<IAuthorizationService>());
        builder.Services.AddSingleton(Mock.Of<IKanbanService>());
        builder.Services.AddSingleton(Mock.Of<IRemediationEngine>());
        builder.Services.AddSingleton(Mock.Of<IDualNarrativeService>());
        builder.Services.AddSingleton(Mock.Of<IUserContext>());
        builder.Services.AddScoped<ComplianceTrendSnapshotService>();
        builder.Services.AddScoped<ComponentService>();
        builder.Services.AddScoped<SystemCapabilityLinkService>();
        builder.Services.AddSingleton(new NarrativeTemplateService());
        builder.Services.AddSingleton(Mock.Of<IRmfLifecycleService>());
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(user => user.CurrentUserId).Returns(ActorId);
        builder.Services.AddSingleton(current.Object);
        builder.Services.AddAuthentication("AssessmentTest")
            .AddScheme<AuthenticationSchemeOptions, AssessmentTestAuthentication>("AssessmentTest", _ => { });
        builder.Services.AddAuthorization(Policies.RegisterPolicies);
        _app = builder.Build();
        _tenants = _app.Services.GetRequiredService<ITenantContextAccessor>();
        _factory = _app.Services.GetRequiredService<IDbContextFactory<AtoCopilotContext>>();
        _app.Use(async (_, next) =>
        {
            using var tenant = _tenants.Push(new TenantContext(_tenantId));
            await next();
        });
        _app.UseAuthentication();
        _app.UseAuthorization();
        var registrar = typeof(DashboardEndpoints).GetMethod(
            "MapAssessmentRoutes", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Assessment route registrar was not found.");
        registrar.Invoke(null, [_app.MapGroup("/api/dashboard").RequireAuthorization(), current.Object]);
        await using (var db = await _factory.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();
            db.Tenants.Add(new Tenant { Id = _tenantId, DisplayName = "Synthetic assessment organization" });
            db.NistControls.Add(new NistControl { Id = "AC-1", Family = "AC", Title = "Synthetic control" });
            await db.SaveChangesAsync();
        }
        await _app.StartAsync();
        _client = _app.GetTestClient();
        _client.DefaultRequestHeaders.Add("X-Assessment-Test-Role", ComplianceRoles.Analyst);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task RunAssessment_MissingAzureScope_RejectsWithoutDocumentationSideEffects(string? subscription)
    {
        // Arrange
        var profile = subscription is null ? null : new AzureEnvironmentProfile
        {
            CloudEnvironment = AzureCloudEnvironment.Government,
            SubscriptionIds = subscription.Length == 0 ? [] : [subscription]
        };
        var system = await SeedSystemAsync(profile);

        // Act
        var response = await _client.PostAsync(RunUrl(system.Id), null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("errorCode").GetString().Should().StartWith("ASSESSMENT_AZURE_");
        error.GetProperty("suggestion").GetString().Should().NotBeNullOrWhiteSpace();
        await AssertNoAssessmentEffectsAsync(system.Id);
    }

    [Theory]
    [InlineData(AzureCloudEnvironment.Government, "not-a-guid")]
    [InlineData(AzureCloudEnvironment.Commercial, SubscriptionId)]
    [InlineData(AzureCloudEnvironment.GovernmentAirGappedIl5, SubscriptionId)]
    [InlineData(AzureCloudEnvironment.GovernmentAirGappedIl6, SubscriptionId)]
    [InlineData((AzureCloudEnvironment)999, SubscriptionId)]
    public async Task RunAssessment_InvalidCloudOrSubscription_BlocksBeforeEngine(
        AzureCloudEnvironment cloud, string subscription)
    {
        // Arrange
        var system = await SeedSystemAsync(new AzureEnvironmentProfile
        {
            CloudEnvironment = cloud,
            SubscriptionIds = [subscription]
        });

        // Act
        var response = await _client.PostAsync(RunUrl(system.Id), null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertNoAssessmentEffectsAsync(system.Id);
    }

    [Fact]
    public async Task GetReadiness_MissingProfile_ExplainsConfigurationInsteadOfRunning()
    {
        // Arrange
        var system = await SeedSystemAsync(null);

        // Act
        var response = await _client.GetAsync($"/api/dashboard/systems/{system.Id}/assessment-readiness");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("isReady").GetBoolean().Should().BeFalse();
        result.GetProperty("errorCode").GetString().Should().Be("ASSESSMENT_AZURE_ENVIRONMENT_REQUIRED");
        result.GetProperty("configurationUrl").GetString().Should()
            .Be($"/systems/{system.Id}/profile/EnvironmentAndDeployment#azure-assessment-environment");
        await AssertNoAssessmentEffectsAsync(system.Id);
    }

    [Fact]
    public async Task ConfigureAndDetachEnvironment_UpdatesActualProfileAndKeepsAssessmentBlocked()
    {
        // Arrange
        var system = await SeedSystemAsync(null);
        using (var tenant = _tenants.Push(new TenantContext(_tenantId)))
        await using (var db = await _factory.CreateDbContextAsync())
        {
            db.AzureSubscriptionRegistrations.Add(new AzureSubscriptionRegistration
            {
                TenantId = _tenantId, SubscriptionId = Guid.Parse(SubscriptionId),
                ParentTenantId = Guid.NewGuid(), DisplayName = "Synthetic government subscription",
                Environment = AzureEnvironment.AzureUSGovernment, Status = SubscriptionStatus.Selected
            });
            await db.SaveChangesAsync();
        }
        var configurationUrl = $"/api/dashboard/systems/{system.Id}/assessment-environment";

        // Act
        var saved = await _client.PutAsJsonAsync(configurationUrl, new
        {
            cloudEnvironment = "Government", subscriptionIds = new[] { SubscriptionId }
        });

        // Assert
        saved.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var tenant = _tenants.Push(new TenantContext(_tenantId)))
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var persisted = await db.RegisteredSystems.SingleAsync(s => s.Id == system.Id);
            persisted.AzureProfile.Should().NotBeNull();
            persisted.AzureProfile!.SubscriptionIds.Should().Equal(SubscriptionId);
        }
        var detached = await _client.DeleteAsync(configurationUrl);
        detached.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var rejected = await _client.PostAsync(RunUrl(system.Id), null);
        rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertNoAssessmentEffectsAsync(system.Id);
    }

    [Fact]
    public async Task RunAssessment_ReaderCannotWriteAssessmentState()
    {
        // Arrange
        var system = await SeedSystemAsync(null);
        _client.DefaultRequestHeaders.Remove("X-Assessment-Test-Role");
        _client.DefaultRequestHeaders.Add("X-Assessment-Test-Role", ComplianceRoles.Viewer);

        // Act
        var response = await _client.PostAsync(RunUrl(system.Id), null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await AssertNoAssessmentEffectsAsync(system.Id);
    }

    private async Task<RegisteredSystem> SeedSystemAsync(AzureEnvironmentProfile? profile)
    {
        using var tenant = _tenants.Push(new TenantContext(_tenantId));
        await using var db = await _factory.CreateDbContextAsync();
        var system = new RegisteredSystem
        {
            TenantId = _tenantId, Name = "Synthetic assessment system", CreatedBy = ActorId,
            HostingEnvironment = "AzureGovernment", CurrentRmfStep = RmfPhase.Prepare,
            AzureProfile = profile
        };
        db.RegisteredSystems.Add(system);
        await db.SaveChangesAsync();
        db.SecurityCategorizations.Add(new SecurityCategorization
        {
            TenantId = _tenantId, RegisteredSystemId = system.Id, CategorizedBy = ActorId
        });
        db.ControlBaselines.Add(new ControlBaseline
        {
            TenantId = _tenantId, RegisteredSystemId = system.Id,
            BaselineLevel = "Low", ControlIds = ["AC-1"], TotalControls = 1
        });
        db.ControlImplementations.Add(new ControlImplementation
        {
            TenantId = _tenantId, RegisteredSystemId = system.Id, ControlId = "AC-1",
            Narrative = "Synthetic documentation, not evidence of an Azure assessment.",
            ImplementationStatus = ImplementationStatus.Planned, AiSuggested = false, AuthoredBy = ActorId
        });
        await db.SaveChangesAsync();
        return system;
    }

    private async Task AssertNoAssessmentEffectsAsync(string systemId)
    {
        using var tenant = _tenants.Push(new TenantContext(_tenantId));
        await using var db = await _factory.CreateDbContextAsync();
        (await db.Assessments.CountAsync()).Should().Be(0);
        (await db.Findings.CountAsync()).Should().Be(0);
        (await db.ControlEffectivenessRecords.CountAsync()).Should().Be(0);
        (await db.PoamItems.CountAsync()).Should().Be(0);
        (await db.ComplianceTrendSnapshots.CountAsync()).Should().Be(0);
        (await db.DashboardActivities.CountAsync(a => a.EventType == "AssessmentCompleted")).Should().Be(0);
        (await db.ControlImplementations.SingleAsync(i => i.RegisteredSystemId == systemId))
            .ImplementationStatus.Should().Be(ImplementationStatus.Planned);
        _engine.Invocations.Should().BeEmpty();
    }

    private static string RunUrl(string systemId) => $"/api/dashboard/systems/{systemId}/run-assessment";

    private sealed class AssessmentTestAuthentication(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Assessment-Test-Role"].ToString();
            if (string.IsNullOrEmpty(role))
                return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, ActorId), new Claim(ClaimTypes.Role, role)], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
