using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Interceptors;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Core.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class AssessmentEnvironmentServiceTests : IDisposable
{
    private const string Actor = "synthetic-writer";
    private static readonly Guid Subscription = Guid.Parse("00000000-0000-0000-0000-000000000981");
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _directoryId = Guid.NewGuid();
    private readonly ServiceProvider _provider;
    private readonly ITenantContextAccessor _tenants;
    private readonly IDbContextFactory<AtoCopilotContext> _factory;
    private readonly Mock<IAzureAssessmentConnectionProbe> _probe = new(MockBehavior.Strict);
    private readonly GatewayOptions _gateway = new();
    private readonly OnboardingOptions _onboarding = new();

    public AssessmentEnvironmentServiceTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContextAccessor, TenantContextAccessor>();
        services.AddSingleton<TenantStampingSaveChangesInterceptor>();
        services.AddDbContextFactory<AtoCopilotContext>((sp, options) =>
            options.UseInMemoryDatabase($"assessment-environment-{_tenantId}")
                .AddInterceptors(sp.GetRequiredService<TenantStampingSaveChangesInterceptor>()));
        _provider = services.BuildServiceProvider();
        _tenants = _provider.GetRequiredService<ITenantContextAccessor>();
        _factory = _provider.GetRequiredService<IDbContextFactory<AtoCopilotContext>>();
        _probe.Setup(p => p.CheckAsync(It.IsAny<IReadOnlyList<AzureAssessmentSubscription>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    public void Dispose() => _provider.Dispose();

    [Theory]
    [InlineData(AzureCloudEnvironment.Government, "AzureGovernment", AzureEnvironment.AzureUSGovernment)]
    [InlineData(AzureCloudEnvironment.Commercial, "AzureCloud", AzureEnvironment.AzureCloud)]
    [InlineData(AzureCloudEnvironment.Commercial, "AzurePublicCloud", AzureEnvironment.AzureCloud)]
    [InlineData(AzureCloudEnvironment.Commercial, "AzureCommercial", AzureEnvironment.AzureCloud)]
    public async Task GetReadiness_EligibleAttachment_ProbesConfiguredCloudScope(
        AzureCloudEnvironment cloud, string deploymentCloud, AzureEnvironment registrationCloud)
    {
        // Arrange
        using var tenant = _tenants.Push(new TenantContext(_tenantId));
        _gateway.Azure.CloudEnvironment = deploymentCloud;
        var system = await SeedAsync(Profile(cloud), registrationCloud: registrationCloud);
        var before = DateTimeOffset.UtcNow;

        // Act
        var result = await CreateService().GetReadinessAsync(system.Id);

        // Assert
        result.IsReady.Should().BeTrue();
        result.CloudEnvironment.Should().Be(cloud.ToString());
        result.Subscriptions.Should().ContainSingle().Which.SubscriptionId.Should().Be(Subscription.ToString());
        result.CheckedAt.Should().BeOnOrAfter(before);
        _probe.Verify(p => p.CheckAsync(It.Is<IReadOnlyList<AzureAssessmentSubscription>>(s =>
            s.Count == 1 && s[0].SubscriptionId == Subscription && s[0].DirectoryTenantId == _directoryId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("absent", AssessmentEnvironmentErrors.EnvironmentRequired)]
    [InlineData("empty", AssessmentEnvironmentErrors.SubscriptionRequired)]
    [InlineData("malformed", AssessmentEnvironmentErrors.InvalidSubscription)]
    [InlineData("zero", AssessmentEnvironmentErrors.InvalidSubscription)]
    [InlineData("duplicate", AssessmentEnvironmentErrors.InvalidSubscription)]
    [InlineData("excessive", AssessmentEnvironmentErrors.InvalidSubscription)]
    [InlineData("commercial", AssessmentEnvironmentErrors.CloudMismatch)]
    [InlineData("il5", AssessmentEnvironmentErrors.UnsupportedCloud)]
    [InlineData("il6", AssessmentEnvironmentErrors.UnsupportedCloud)]
    [InlineData("unknown", AssessmentEnvironmentErrors.UnsupportedCloud)]
    [InlineData("proxy", AssessmentEnvironmentErrors.UnsupportedCloud)]
    [InlineData("custom-arm", AssessmentEnvironmentErrors.UnsupportedCloud)]
    [InlineData("relative-arm", AssessmentEnvironmentErrors.UnsupportedCloud)]
    [InlineData("custom-auth", AssessmentEnvironmentErrors.UnsupportedCloud)]
    [InlineData("custom-policy", AssessmentEnvironmentErrors.UnsupportedCloud)]
    [InlineData("custom-defender", AssessmentEnvironmentErrors.UnsupportedCloud)]
    public async Task GetReadiness_InvalidAttachment_BlocksWithoutAzure(string variant, string expectedCode)
    {
        // Arrange
        using var tenant = _tenants.Push(new TenantContext(_tenantId));
        var profile = InvalidProfile(variant);
        var system = await SeedAsync(profile);

        // Act
        var result = await CreateService().GetReadinessAsync(system.Id);

        // Assert
        result.IsReady.Should().BeFalse();
        result.ErrorCode.Should().Be(expectedCode);
        result.Suggestion.Should().NotBeNullOrWhiteSpace();
        _probe.Invocations.Should().BeEmpty();
    }

    [Theory]
    [InlineData("missing", AssessmentEnvironmentErrors.SubscriptionUnavailable)]
    [InlineData("unavailable", AssessmentEnvironmentErrors.SubscriptionUnavailable)]
    [InlineData("wrong-cloud", AssessmentEnvironmentErrors.CloudMismatch)]
    [InlineData("missing-directory", AssessmentEnvironmentErrors.SubscriptionUnavailable)]
    [InlineData("foreign", AssessmentEnvironmentErrors.SubscriptionUnavailable)]
    public async Task GetReadiness_IneligibleRegistration_BlocksWithoutAzure(string variant, string expectedCode)
    {
        // Arrange
        using var tenant = _tenants.Push(new TenantContext(_tenantId));
        var system = await SeedAsync(Profile(), addRegistration: false);
        if (variant != "missing")
        {
            var registration = Registration();
            if (variant == "unavailable") registration.Status = SubscriptionStatus.Unavailable;
            if (variant == "wrong-cloud") registration.Environment = AzureEnvironment.AzureCloud;
            if (variant == "missing-directory") registration.ParentTenantId = Guid.Empty;
            if (variant == "foreign") registration.TenantId = Guid.NewGuid();
            using var registrationTenant = _tenants.Push(new TenantContext(registration.TenantId));
            await using var db = await _factory.CreateDbContextAsync();
            db.AzureSubscriptionRegistrations.Add(registration);
            await db.SaveChangesAsync();
        }

        // Act
        var result = await CreateService().GetReadinessAsync(system.Id);

        // Assert
        result.IsReady.Should().BeFalse();
        result.ErrorCode.Should().Be(expectedCode);
        _probe.Invocations.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, "AzureGovernment")]
    [InlineData(true, "UnsupportedCloud")]
    [InlineData(true, "AzureCloud ")]
    public async Task GetReadiness_InvalidDeployment_Blocks(bool enabled, string cloud)
    {
        // Arrange
        using var tenant = _tenants.Push(new TenantContext(_tenantId));
        var system = await SeedAsync(Profile());
        _gateway.Azure.Enabled = enabled;
        _gateway.Azure.CloudEnvironment = cloud;

        // Act
        var result = await CreateService().GetReadinessAsync(system.Id);

        // Assert
        result.IsReady.Should().BeFalse();
        result.ErrorCode.Should().Be(AssessmentEnvironmentErrors.DeploymentConfiguration);
        _probe.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task GetReadiness_NoCategorization_BlocksBeforeProbe()
    {
        // Arrange
        using var tenant = _tenants.Push(new TenantContext(_tenantId));
        var system = await SeedAsync(Profile(), categorized: false);

        // Act
        var result = await CreateService().GetReadinessAsync(system.Id);

        // Assert
        result.ErrorCode.Should().Be(AssessmentEnvironmentErrors.CategorizationRequired);
        _probe.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task GetReadiness_ProbeFailure_ReturnsSafeActionableResult()
    {
        // Arrange
        using var tenant = _tenants.Push(new TenantContext(_tenantId));
        var system = await SeedAsync(Profile());
        _probe.Setup(p => p.CheckAsync(It.IsAny<IReadOnlyList<AzureAssessmentSubscription>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AssessmentEnvironmentException(
                AssessmentEnvironmentErrors.AccessDenied, "Azure read access is required.", "Contact the administrator."));

        // Act
        var result = await CreateService().GetReadinessAsync(system.Id);

        // Assert
        result.IsReady.Should().BeFalse();
        result.ErrorCode.Should().Be(AssessmentEnvironmentErrors.AccessDenied);
        result.Message.Should().Be("Azure read access is required.");
    }

    [Fact]
    public async Task GetReadiness_RequestCancellation_Propagates()
    {
        // Arrange
        using var tenant = _tenants.Push(new TenantContext(_tenantId));
        var system = await SeedAsync(Profile());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        var act = () => CreateService().GetReadinessAsync(system.Id, cancellation.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetReadiness_CspCrossOrganizationView_RequiresExplicitOrganizationSelection()
    {
        // Arrange
        using var tenant = _tenants.Push(new TenantContext(_tenantId));
        var system = await SeedAsync(Profile());
        using var cspContext = _tenants.Push(new TenantContext(Guid.NewGuid(), isCspAdmin: true));

        // Act
        var result = await CreateService().GetReadinessAsync(system.Id);

        // Assert
        result.ErrorCode.Should().Be(AssessmentEnvironmentErrors.OrganizationRequired);
        _probe.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task ConfigureAndDetach_ChangesOnlyAttachmentAndAuditMetadata()
    {
        // Arrange
        using var tenant = _tenants.Push(new TenantContext(_tenantId));
        var system = await SeedAsync(null);
        var service = CreateService();

        // Act
        var configuration = await service.ConfigureAsync(system.Id, new UpdateAssessmentEnvironmentRequest
        {
            CloudEnvironment = "Government", SubscriptionIds = [Subscription.ToString()]
        }, Actor);
        await service.DetachAsync(system.Id, Actor);
        var readiness = await service.GetReadinessAsync(system.Id);

        // Assert
        configuration.SubscriptionIds.Should().Equal(Subscription.ToString());
        readiness.ErrorCode.Should().Be(AssessmentEnvironmentErrors.EnvironmentRequired);
        _probe.Invocations.Should().BeEmpty();
        await using var db = await _factory.CreateDbContextAsync();
        (await db.Assessments.CountAsync()).Should().Be(0);
        (await db.DashboardActivities.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task GetConfiguration_UnknownLegacyRegistrationCloud_IsNotSelectable()
    {
        // Arrange
        using var tenant = _tenants.Push(new TenantContext(_tenantId));
        var system = await SeedAsync(null, registrationCloud: (AzureEnvironment)999);

        // Act
        var result = await CreateService().GetConfigurationAsync(system.Id);

        // Assert
        result.AvailableSubscriptions.Should().ContainSingle();
        result.AvailableSubscriptions[0].CloudEnvironment.Should().Be("Unknown");
        result.AvailableSubscriptions[0].IsAvailable.Should().BeFalse();
        _probe.Invocations.Should().BeEmpty();
    }

    [Theory]
    [InlineData(AssessmentEnvironmentErrors.SystemNotFound, 404)]
    [InlineData(AssessmentEnvironmentErrors.PermissionRequired, 403)]
    [InlineData(AssessmentEnvironmentErrors.OrganizationRequired, 409)]
    [InlineData(AssessmentEnvironmentErrors.ConnectionUnavailable, 503)]
    [InlineData(AssessmentEnvironmentErrors.EnvironmentRequired, 400)]
    public void ErrorStatus_PrerequisiteCategory_UsesExpectedHttpStatus(string code, int expected)
    {
        // Act
        var result = AssessmentEnvironmentErrors.HttpStatusCode(code);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("unsupported")]
    public async Task Configure_InvalidCloudName_RejectsWithoutChangingProfile(string? cloud)
    {
        // Arrange
        using var tenant = _tenants.Push(new TenantContext(_tenantId));
        var system = await SeedAsync(null);

        // Act
        var act = () => CreateService().ConfigureAsync(system.Id, new UpdateAssessmentEnvironmentRequest
        {
            CloudEnvironment = cloud, SubscriptionIds = [Subscription.ToString()]
        }, Actor);

        // Assert
        (await act.Should().ThrowAsync<AssessmentEnvironmentException>()).Which.ErrorCode
            .Should().Be(AssessmentEnvironmentErrors.UnsupportedCloud);
        await using var db = await _factory.CreateDbContextAsync();
        (await db.RegisteredSystems.SingleAsync()).AzureProfile.Should().BeNull();
        _probe.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task Configure_MissingSubscriptionCollection_Rejects()
    {
        // Arrange
        using var tenant = _tenants.Push(new TenantContext(_tenantId));
        var system = await SeedAsync(null);

        // Act
        var act = () => CreateService().ConfigureAsync(system.Id,
            new UpdateAssessmentEnvironmentRequest { CloudEnvironment = "Government" }, Actor);

        // Assert
        (await act.Should().ThrowAsync<AssessmentEnvironmentException>()).Which.ErrorCode
            .Should().Be(AssessmentEnvironmentErrors.SubscriptionRequired);
    }

    [Fact]
    public async Task Detach_AlreadyAbsent_IsIdempotentWithoutAuditOrAssessmentWrites()
    {
        // Arrange
        using var tenant = _tenants.Push(new TenantContext(_tenantId));
        var system = await SeedAsync(null);

        // Act
        await CreateService().DetachAsync(system.Id, Actor);

        // Assert
        await using var db = await _factory.CreateDbContextAsync();
        (await db.DashboardActivities.CountAsync()).Should().Be(0);
        _probe.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task GetReadiness_NewlyConfiguredTrustedEndpoints_AreAccepted()
    {
        // Arrange
        using var tenant = _tenants.Push(new TenantContext(_tenantId));
        var system = await SeedAsync(null);
        var service = CreateService();
        await service.ConfigureAsync(system.Id, new UpdateAssessmentEnvironmentRequest
        {
            CloudEnvironment = "Government", SubscriptionIds = [Subscription.ToString()]
        }, Actor);
        _probe.Invocations.Should().BeEmpty();

        // Act
        var result = await service.GetReadinessAsync(system.Id);

        // Assert
        result.IsReady.Should().BeTrue();
        _probe.Invocations.Should().ContainSingle();
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("directory")]
    [InlineData("subscription")]
    [InlineData("cloud")]
    public async Task GetConfiguration_IneligibleChoices_AreDisabled(string reason)
    {
        // Arrange
        using var tenant = _tenants.Push(new TenantContext(_tenantId));
        var system = await SeedAsync(null);
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var registration = await db.AzureSubscriptionRegistrations.SingleAsync();
            switch (reason)
            {
                case "unavailable": registration.Status = SubscriptionStatus.Unavailable; break;
                case "directory": registration.ParentTenantId = Guid.Empty; break;
                case "subscription": registration.SubscriptionId = Guid.Empty; break;
                case "cloud": registration.Environment = AzureEnvironment.AzureCloud; break;
            }
            await db.SaveChangesAsync();
        }

        // Act
        var result = await CreateService().GetConfigurationAsync(system.Id);

        // Assert
        result.AvailableSubscriptions.Should().ContainSingle().Which.IsAvailable.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(int.MaxValue)]
    public async Task GetReadiness_InvalidSubscriptionLimit_ReturnsConfigurationFailure(int limit)
    {
        // Arrange
        using var tenant = _tenants.Push(new TenantContext(_tenantId));
        var system = await SeedAsync(Profile());
        _onboarding.Limits.MaxAzureSubscriptionsPerTenant = limit;

        // Act
        var result = await CreateService().GetReadinessAsync(system.Id);

        // Assert
        result.ErrorCode.Should().Be(AssessmentEnvironmentErrors.DeploymentConfiguration);
        _probe.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task GetConfiguration_RegistrationCountExceedsLimit_ReturnsExplicitFailure()
    {
        // Arrange
        using var tenant = _tenants.Push(new TenantContext(_tenantId));
        var system = await SeedAsync(null);
        _onboarding.Limits.MaxAzureSubscriptionsPerTenant = 1;
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var additional = Registration();
            additional.SubscriptionId = Guid.NewGuid();
            db.AzureSubscriptionRegistrations.Add(additional);
            await db.SaveChangesAsync();
        }

        // Act
        var act = () => CreateService().GetConfigurationAsync(system.Id);

        // Assert
        (await act.Should().ThrowAsync<AssessmentEnvironmentException>()).Which.ErrorCode
            .Should().Be(AssessmentEnvironmentErrors.DeploymentConfiguration);
    }

    [Fact]
    public async Task Configure_OrganizationRegistrationLimitInvalid_DoesNotPersistBeforeRejecting()
    {
        // Arrange
        using var tenant = _tenants.Push(new TenantContext(_tenantId));
        var system = await SeedAsync(null);
        _onboarding.Limits.MaxAzureSubscriptionsPerTenant = 1;
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var additional = Registration();
            additional.SubscriptionId = Guid.NewGuid();
            db.AzureSubscriptionRegistrations.Add(additional);
            await db.SaveChangesAsync();
        }

        // Act
        var act = () => CreateService().ConfigureAsync(system.Id, new UpdateAssessmentEnvironmentRequest
        {
            CloudEnvironment = "Government", SubscriptionIds = [Subscription.ToString()]
        }, Actor);

        // Assert
        await act.Should().ThrowAsync<AssessmentEnvironmentException>();
        await using var verify = await _factory.CreateDbContextAsync();
        (await verify.RegisteredSystems.SingleAsync()).AzureProfile.Should().BeNull();
        (await verify.DashboardActivities.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetReadiness_UnknownOrInactiveSystem_ReturnsNotFound(bool inactive)
    {
        // Arrange
        using var tenant = _tenants.Push(new TenantContext(_tenantId));
        var systemId = Guid.NewGuid().ToString();
        if (inactive)
        {
            var system = await SeedAsync(null);
            systemId = system.Id;
            await using var db = await _factory.CreateDbContextAsync();
            (await db.RegisteredSystems.SingleAsync()).IsActive = false;
            await db.SaveChangesAsync();
        }

        // Act
        var act = () => CreateService().GetReadinessAsync(systemId);

        // Assert
        (await act.Should().ThrowAsync<AssessmentEnvironmentException>()).Which.ErrorCode
            .Should().Be(AssessmentEnvironmentErrors.SystemNotFound);
    }

    private AssessmentEnvironmentService CreateService() => new(
        _factory, _tenants, _probe.Object, Options.Create(_gateway), Options.Create(_onboarding),
        new ArmClientFactory(_gateway.Azure.CloudEnvironment, NullLogger<ArmClientFactory>.Instance),
        NullLogger<AssessmentEnvironmentService>.Instance);

    private async Task<RegisteredSystem> SeedAsync(AzureEnvironmentProfile? profile, bool addRegistration = true,
        bool categorized = true, AzureEnvironment registrationCloud = AzureEnvironment.AzureUSGovernment)
    {
        await using var db = await _factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
        db.Tenants.Add(new Tenant { Id = _tenantId, DisplayName = "Synthetic organization" });
        var system = new RegisteredSystem
        {
            TenantId = _tenantId, Name = "Synthetic system", HostingEnvironment = "Synthetic",
            CreatedBy = Actor, AzureProfile = profile
        };
        db.RegisteredSystems.Add(system);
        await db.SaveChangesAsync();
        if (categorized)
            db.SecurityCategorizations.Add(new SecurityCategorization
            {
                TenantId = _tenantId, RegisteredSystemId = system.Id, CategorizedBy = Actor
            });
        if (addRegistration)
        {
            var registration = Registration();
            registration.Environment = registrationCloud;
            db.AzureSubscriptionRegistrations.Add(registration);
        }
        await db.SaveChangesAsync();
        return system;
    }

    private AzureSubscriptionRegistration Registration() => new()
    {
        TenantId = _tenantId, SubscriptionId = Subscription, ParentTenantId = _directoryId,
        DisplayName = "Synthetic subscription", Environment = AzureEnvironment.AzureUSGovernment,
        Status = SubscriptionStatus.Selected
    };

    private static AzureEnvironmentProfile Profile(AzureCloudEnvironment cloud = AzureCloudEnvironment.Government) =>
        new() { CloudEnvironment = cloud, SubscriptionIds = [Subscription.ToString()] };

    private static AzureEnvironmentProfile? InvalidProfile(string variant)
    {
        var profile = Profile();
        switch (variant)
        {
            case "absent": return null;
            case "empty": profile.SubscriptionIds = []; break;
            case "malformed": profile.SubscriptionIds = ["not-a-guid"]; break;
            case "zero": profile.SubscriptionIds = [Guid.Empty.ToString()]; break;
            case "duplicate": profile.SubscriptionIds.Add(Subscription.ToString()); break;
            case "excessive": profile.SubscriptionIds = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid().ToString()).ToList(); break;
            case "commercial": profile.CloudEnvironment = AzureCloudEnvironment.Commercial; break;
            case "il5": profile.CloudEnvironment = AzureCloudEnvironment.GovernmentAirGappedIl5; break;
            case "il6": profile.CloudEnvironment = AzureCloudEnvironment.GovernmentAirGappedIl6; break;
            case "unknown": profile.CloudEnvironment = (AzureCloudEnvironment)999; break;
            case "proxy": profile.ProxyUrl = "https://proxy.invalid"; break;
            case "custom-arm": profile.ArmEndpoint = "https://arm.invalid"; break;
            case "relative-arm": profile.ArmEndpoint = "not-an-absolute-endpoint"; break;
            case "custom-auth": profile.AuthenticationEndpoint = "https://auth.invalid"; break;
            case "custom-policy": profile.PolicyEndpoint = "https://policy.invalid"; break;
            case "custom-defender": profile.DefenderEndpoint = "https://defender.invalid"; break;
            default: throw new ArgumentOutOfRangeException(nameof(variant));
        }
        return profile;
    }
}
