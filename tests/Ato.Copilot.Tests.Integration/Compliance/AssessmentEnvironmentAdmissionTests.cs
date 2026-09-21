using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Configuration;
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
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly Mock<IAzureAssessmentConnectionProbe> _probe = new(MockBehavior.Strict);
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
        _probe.Setup(p => p.CheckAsync(It.IsAny<IReadOnlyList<AzureAssessmentSubscription>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        builder.Services.AddSingleton(_probe.Object);
        builder.Services.Configure<GatewayOptions>(_ => { });
        builder.Services.Configure<OnboardingOptions>(_ => { });
        builder.Services.AddSingleton(new ArmClientFactory("AzureGovernment", NullLogger<ArmClientFactory>.Instance));
        builder.Services.AddScoped<IAssessmentEnvironmentService, AssessmentEnvironmentService>();
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task Issue961_RunAssessment_TemplateReviewDoesNotReplaceAzureAdmission(bool reviewed)
    {
        // Arrange
        var system = await SeedSystemAsync(null);
        var reviewer = reviewed ? "synthetic-reviewer" : null;
        using (var tenant = _tenants.Push(new TenantContext(_tenantId)))
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var implementation = await db.ControlImplementations.SingleAsync(item =>
                item.RegisteredSystemId == system.Id && item.ControlId == "AC-1");
            implementation.Narrative = "Deterministic scaffold";
            implementation.TechnicalNarrative = "Deterministic scaffold";
            implementation.IsAutoPopulated = true;
            implementation.ReviewedBy = reviewer;
            await db.SaveChangesAsync();
        }

        // Act
        var response = await _client.PostAsync(RunUrl(system.Id), null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("errorCode").GetString().Should().Be(AssessmentEnvironmentErrors.EnvironmentRequired);
        await AssertNoAssessmentEffectsAsync(system.Id);
        using var verificationTenant = _tenants.Push(new TenantContext(_tenantId));
        await using var verificationDb = await _factory.CreateDbContextAsync();
        var saved = await verificationDb.ControlImplementations.SingleAsync(item =>
            item.RegisteredSystemId == system.Id && item.ControlId == "AC-1");
        saved.Narrative.Should().Be("Deterministic scaffold");
        saved.TechnicalNarrative.Should().Be("Deterministic scaffold");
        saved.IsAutoPopulated.Should().BeTrue();
        saved.AiSuggested.Should().BeFalse();
        saved.ReviewedBy.Should().Be(reviewer);
        saved.ImplementationStatus.Should().Be(ImplementationStatus.Planned);
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
        await RegisterSubscriptionAsync();
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
        var ready = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/dashboard/systems/{system.Id}/assessment-readiness");
        ready.GetProperty("isReady").GetBoolean().Should().BeTrue();
        var detached = await _client.DeleteAsync(configurationUrl);
        detached.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var rejected = await _client.PostAsync(RunUrl(system.Id), null);
        rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertNoAssessmentEffectsAsync(system.Id);
    }

    [Fact]
    public async Task Issue823_ReadyAzureAssessment_PreservesAuthenticatedActorAndResponseContract()
    {
        // Arrange
        var system = await SeedSystemAsync(new AzureEnvironmentProfile
        {
            CloudEnvironment = AzureCloudEnvironment.Government, SubscriptionIds = [SubscriptionId]
        });
        await RegisterSubscriptionAsync();
        _engine.Setup(e => e.RunComprehensiveAssessmentAsync(
                SubscriptionId, null, null, It.IsAny<CancellationToken>()))
            .Returns(async (string subscription, string? _, IProgress<AssessmentProgress>? _, CancellationToken ct) =>
            {
                await using var db = await _factory.CreateDbContextAsync(ct);
                var assessment = new ComplianceAssessment
                {
                    TenantId = _tenantId, SubscriptionId = subscription, ScanType = "comprehensive",
                    Status = AssessmentStatus.Completed, TotalControls = 1, PassedControls = 1,
                    ComplianceScore = 100, InitiatedBy = "synthetic-engine"
                };
                db.Assessments.Add(assessment);
                await db.SaveChangesAsync(ct);
                return assessment;
            });

        // Act
        var response = await _client.PostAsync(RunUrl(system.Id), null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("scanType").GetString().Should().Be("comprehensive");
        result.GetProperty("systemId").GetString().Should().Be(system.Id);
        _probe.Verify(p => p.CheckAsync(It.IsAny<IReadOnlyList<AzureAssessmentSubscription>>(),
            It.IsAny<CancellationToken>()), Times.Once);
        using var tenant = _tenants.Push(new TenantContext(_tenantId));
        await using var db = await _factory.CreateDbContextAsync();
        var persisted = await db.Assessments.SingleAsync();
        persisted.RegisteredSystemId.Should().Be(system.Id);
        persisted.InitiatedBy.Should().Be(ActorId);
        (await db.ControlEffectivenessRecords.Select(e => e.AssessorId).Distinct().ToListAsync())
            .Should().Equal(ActorId);
        (await db.ControlImplementations.SingleAsync()).ImplementationStatus.Should().Be(ImplementationStatus.Planned);
    }

    [Theory]
    [InlineData(AssessmentEnvironmentErrors.AuthenticationRequired, 400)]
    [InlineData(AssessmentEnvironmentErrors.AccessDenied, 400)]
    [InlineData(AssessmentEnvironmentErrors.ConnectionUnavailable, 503)]
    public async Task RunAssessment_AzureAccessFailure_RejectsWithoutFallback(string code, int expectedStatus)
    {
        // Arrange
        var system = await SeedSystemAsync(new AzureEnvironmentProfile
        {
            CloudEnvironment = AzureCloudEnvironment.Government, SubscriptionIds = [SubscriptionId]
        });
        await RegisterSubscriptionAsync();
        _probe.Setup(p => p.CheckAsync(It.IsAny<IReadOnlyList<AzureAssessmentSubscription>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AssessmentEnvironmentException(code, "Synthetic access failure.", "Retry configuration."));

        // Act
        var response = await _client.PostAsync(RunUrl(system.Id), null);

        // Assert
        ((int)response.StatusCode).Should().Be(expectedStatus);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("errorCode").GetString().Should().Be(code);
        await AssertNoAssessmentEffectsAsync(system.Id);
    }

    [Fact]
    public async Task RunAssessment_RegistrationRevokedAfterReadiness_RevalidatesInsteadOfTrustingUi()
    {
        // Arrange
        var system = await SeedSystemAsync(new AzureEnvironmentProfile
        {
            CloudEnvironment = AzureCloudEnvironment.Government, SubscriptionIds = [SubscriptionId]
        });
        await RegisterSubscriptionAsync();
        var readiness = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/dashboard/systems/{system.Id}/assessment-readiness");
        readiness.GetProperty("isReady").GetBoolean().Should().BeTrue();
        using (var tenant = _tenants.Push(new TenantContext(_tenantId)))
        await using (var db = await _factory.CreateDbContextAsync())
        {
            (await db.AzureSubscriptionRegistrations.SingleAsync()).Status = SubscriptionStatus.Unavailable;
            await db.SaveChangesAsync();
        }

        // Act
        var response = await _client.PostAsync(RunUrl(system.Id), null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertNoAssessmentEffectsAsync(system.Id);
        _probe.Invocations.Should().ContainSingle();
    }

    [Fact]
    public async Task GetReadiness_ReaderRole_ExplainsMissingOperationPermission()
    {
        // Arrange
        var system = await SeedSystemAsync(null);
        _client.DefaultRequestHeaders.Remove("X-Assessment-Test-Role");
        _client.DefaultRequestHeaders.Add("X-Assessment-Test-Role", ComplianceRoles.Viewer);

        // Act
        var response = await _client.GetAsync($"/api/dashboard/systems/{system.Id}/assessment-readiness");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("errorCode").GetString().Should().Be(AssessmentEnvironmentErrors.PermissionRequired);
        await AssertNoAssessmentEffectsAsync(system.Id);
    }

    [Theory]
    [InlineData("assessment-readiness")]
    [InlineData("assessment-environment")]
    public async Task GetEnvironment_UnknownSystem_ReturnsStructuredNotFound(string action)
    {
        // Arrange
        var systemId = Guid.NewGuid().ToString();

        // Act
        var response = await _client.GetAsync($"/api/dashboard/systems/{systemId}/{action}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("errorCode").GetString().Should().Be(AssessmentEnvironmentErrors.SystemNotFound);
    }

    [Fact]
    public async Task GetConfiguration_NoAttachment_ReturnsEligibleOrganizationChoices()
    {
        // Arrange
        var system = await SeedSystemAsync(null);
        await RegisterSubscriptionAsync();

        // Act
        var response = await _client.GetAsync($"/api/dashboard/systems/{system.Id}/assessment-environment");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var configuration = await response.Content.ReadFromJsonAsync<JsonElement>();
        configuration.GetProperty("cloudEnvironment").ValueKind.Should().Be(JsonValueKind.Null);
        configuration.GetProperty("availableSubscriptions").GetArrayLength().Should().Be(1);
        configuration.GetProperty("availableSubscriptions")[0].GetProperty("isAvailable").GetBoolean().Should().BeTrue();
        _probe.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task Configure_InvalidSelection_ReturnsStructuredErrorWithoutSaving()
    {
        // Arrange
        var system = await SeedSystemAsync(null);

        // Act
        var response = await _client.PutAsJsonAsync(
            $"/api/dashboard/systems/{system.Id}/assessment-environment",
            new { cloudEnvironment = "Government", subscriptionIds = new[] { "not-a-subscription" } });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("errorCode").GetString().Should().Be(AssessmentEnvironmentErrors.InvalidSubscription);
        await AssertNoAssessmentEffectsAsync(system.Id);
    }

    [Fact]
    public async Task Detach_UnknownSystem_ReturnsStructuredNotFound()
    {
        // Arrange
        var systemId = Guid.NewGuid().ToString();

        // Act
        var response = await _client.DeleteAsync($"/api/dashboard/systems/{systemId}/assessment-environment");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("errorCode").GetString().Should().Be(AssessmentEnvironmentErrors.SystemNotFound);
    }

    [Fact]
    public async Task Configuration_ReaderRole_CannotReadOrChangeAttachment()
    {
        // Arrange
        var system = await SeedSystemAsync(null);
        _client.DefaultRequestHeaders.Remove("X-Assessment-Test-Role");
        _client.DefaultRequestHeaders.Add("X-Assessment-Test-Role", ComplianceRoles.Viewer);
        var url = $"/api/dashboard/systems/{system.Id}/assessment-environment";

        // Act
        var read = await _client.GetAsync(url);
        var save = await _client.PutAsJsonAsync(url, new
        {
            cloudEnvironment = "Government", subscriptionIds = new[] { SubscriptionId }
        });
        var remove = await _client.DeleteAsync(url);

        // Assert
        read.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        save.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        remove.StatusCode.Should().Be(HttpStatusCode.Forbidden);
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

    private async Task RegisterSubscriptionAsync()
    {
        using var tenant = _tenants.Push(new TenantContext(_tenantId));
        await using var db = await _factory.CreateDbContextAsync();
        db.AzureSubscriptionRegistrations.Add(new AzureSubscriptionRegistration
        {
            TenantId = _tenantId, SubscriptionId = Guid.Parse(SubscriptionId), ParentTenantId = Guid.NewGuid(),
            DisplayName = "Synthetic subscription", Environment = AzureEnvironment.AzureUSGovernment,
            Status = SubscriptionStatus.Selected
        });
        await db.SaveChangesAsync();
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
