using Azure.Identity;
using Azure.ResourceManager;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>Validates Azure-only dashboard assessment admission and attachment configuration.</summary>
public sealed class AssessmentEnvironmentService : IAssessmentEnvironmentService
{
    private const string ConfigurationEvent = "AssessmentEnvironmentConfigured";
    private const string DetachmentEvent = "AssessmentEnvironmentDetached";
    private readonly IDbContextFactory<AtoCopilotContext> _factory;
    private readonly ITenantContextAccessor _tenants;
    private readonly IAzureAssessmentConnectionProbe _probe;
    private readonly GatewayOptions _gateway;
    private readonly string _deploymentCloud;
    private readonly OnboardingOptions _onboarding;
    private readonly ILogger<AssessmentEnvironmentService> _logger;

    /// <summary>Creates the tenant-scoped admission service.</summary>
    /// <param name="factory">Tenant-filtered database contexts.</param>
    /// <param name="tenants">Authenticated organization context.</param>
    /// <param name="probe">Fail-closed Azure access checks.</param>
    /// <param name="gateway">The same deployment configuration used by the default ARM client.</param>
    /// <param name="onboarding">Existing organization subscription limits.</param>
    /// <param name="armClients">Factory that owns the actual default assessment client.</param>
    /// <param name="logger">Structured logger.</param>
    public AssessmentEnvironmentService(
        IDbContextFactory<AtoCopilotContext> factory, ITenantContextAccessor tenants,
        IAzureAssessmentConnectionProbe probe, IOptions<GatewayOptions> gateway,
        IOptions<OnboardingOptions> onboarding, ArmClientFactory armClients,
        ILogger<AssessmentEnvironmentService> logger)
    {
        _factory = factory;
        _tenants = tenants;
        _probe = probe;
        _gateway = gateway.Value;
        _deploymentCloud = armClients.DefaultCloud;
        _onboarding = onboarding.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AssessmentReadinessResponse> GetReadinessAsync(
        string systemId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var system = await FindSystemAsync(db, systemId, cancellationToken);
        string? deploymentCloud = null;
        IReadOnlyList<AssessmentSubscriptionResponse> subscriptions = [];
        try
        {
            EnsureActiveOrganization(system);
            if (!_gateway.Azure.Enabled)
                throw DeploymentFailure("Azure assessment access is disabled for this deployment.");
            var cloud = ResolveDeploymentCloud();
            deploymentCloud = cloud.ToString();
            var ids = ValidateProfile(system.AzureProfile, cloud);
            var registrations = await LoadSelectedRegistrationsAsync(db, system.TenantId, ids, cancellationToken);
            ValidateRegistrations(ids, registrations, cloud);
            subscriptions = ids.Select(id => new AssessmentSubscriptionResponse(
                id.ToString(), registrations.Single(r => r.SubscriptionId == id).DisplayName)).ToArray();
            if (!await db.SecurityCategorizations.AnyAsync(c => c.RegisteredSystemId == systemId, cancellationToken))
                throw new AssessmentEnvironmentException(AssessmentEnvironmentErrors.CategorizationRequired,
                    "System categorization is required before running an assessment.",
                    "Complete the system's Categorization page, then check readiness again.");
            await _probe.CheckAsync(ids.Select(id => new AzureAssessmentSubscription(
                id, registrations.Single(r => r.SubscriptionId == id).ParentTenantId)).ToArray(), cancellationToken);
            return Readiness(system, deploymentCloud, subscriptions, null);
        }
        catch (AssessmentEnvironmentException failure)
        {
            _logger.LogWarning("Azure assessment admission blocked for system {SystemId}: {ErrorCode}",
                systemId, failure.ErrorCode);
            return Readiness(system, deploymentCloud, subscriptions, failure);
        }
    }

    /// <inheritdoc />
    public async Task<AssessmentEnvironmentResponse> GetConfigurationAsync(
        string systemId, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var system = await FindSystemAsync(db, systemId, cancellationToken);
        EnsureActiveOrganization(system);
        var cloud = ResolveDeploymentCloud();
        var registrations = await LoadOrganizationRegistrationsAsync(db, system.TenantId, cancellationToken);
        return Configuration(system, cloud, registrations);
    }

    /// <inheritdoc />
    public async Task<AssessmentEnvironmentResponse> ConfigureAsync(
        string systemId, UpdateAssessmentEnvironmentRequest request, string actorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var system = await FindSystemAsync(db, systemId, cancellationToken);
        EnsureActiveOrganization(system);
        var cloud = ResolveDeploymentCloud();
        if (!Enum.TryParse<AzureCloudEnvironment>(request.CloudEnvironment, true, out var requestedCloud) ||
            !string.Equals(requestedCloud.ToString(), request.CloudEnvironment, StringComparison.OrdinalIgnoreCase))
            throw UnsupportedCloud();
        ValidateCloud(requestedCloud, cloud);
        var ids = ParseSubscriptions(request.SubscriptionIds);
        var registrations = await LoadOrganizationRegistrationsAsync(db, system.TenantId, cancellationToken);
        ValidateRegistrations(ids, registrations, cloud);
        system.AzureProfile = new AzureEnvironmentProfile
        {
            CloudEnvironment = cloud,
            SubscriptionIds = ids.Select(id => id.ToString()).ToList(),
            ArmEndpoint = ArmEndpoint(cloud).AbsoluteUri,
            AuthenticationEndpoint = AuthenticationEndpoint(cloud).AbsoluteUri,
            PolicyEndpoint = ArmEndpoint(cloud).AbsoluteUri,
            DefenderEndpoint = ArmEndpoint(cloud).AbsoluteUri
        };
        system.ModifiedAt = DateTime.UtcNow;
        AddActivity(db, system, actorId, ConfigurationEvent,
            $"Azure assessment attachment configured for {cloud} with {ids.Count} subscription(s); access must be checked.");
        await db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Configured Azure assessment attachment for system {SystemId}, cloud {Cloud}, subscriptions {Count}",
            systemId, cloud, ids.Count);
        return Configuration(system, cloud, registrations);
    }

    /// <inheritdoc />
    public async Task DetachAsync(string systemId, string actorId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var system = await FindSystemAsync(db, systemId, cancellationToken);
        EnsureActiveOrganization(system);
        if (system.AzureProfile is null)
        {
            _logger.LogInformation("Azure assessment attachment already absent for system {SystemId}", systemId);
            return;
        }
        system.AzureProfile = null;
        system.ModifiedAt = DateTime.UtcNow;
        AddActivity(db, system, actorId, DetachmentEvent,
            "Azure assessment attachment removed; historical assessment records were preserved.");
        await db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Detached Azure assessment environment for system {SystemId}", systemId);
    }

    private static async Task<RegisteredSystem> FindSystemAsync(
        AtoCopilotContext db, string systemId, CancellationToken cancellationToken)
    {
        return await db.RegisteredSystems.FirstOrDefaultAsync(
            s => s.Id == systemId && s.IsActive, cancellationToken)
            ?? throw new AssessmentEnvironmentException(AssessmentEnvironmentErrors.SystemNotFound,
                "The system was not found in the current organization.",
                "Select an accessible active system and try again.");
    }

    private void EnsureActiveOrganization(RegisteredSystem system)
    {
        var current = _tenants.Current;
        if (current is null || current.EffectiveTenantId != system.TenantId || current.Status != TenantStatus.Active)
            throw new AssessmentEnvironmentException(AssessmentEnvironmentErrors.OrganizationRequired,
                "Select this system's organization before configuring or running an Azure assessment.",
                "Use the organization selector to activate the system's organization, then try again.");
    }

    private AzureCloudEnvironment ResolveDeploymentCloud()
    {
        var configured = _deploymentCloud;
        if (string.Equals(configured, "AzureCloud", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(configured, "AzurePublicCloud", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(configured, "AzureCommercial", StringComparison.OrdinalIgnoreCase))
            return AzureCloudEnvironment.Commercial;
        if (string.Equals(configured, "AzureGovernment", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(configured, "AzureUSGovernment", StringComparison.OrdinalIgnoreCase))
            return AzureCloudEnvironment.Government;
        throw DeploymentFailure("The deployment's Azure cloud configuration is not supported for assessment.");
    }

    private IReadOnlyList<Guid> ValidateProfile(AzureEnvironmentProfile? profile, AzureCloudEnvironment cloud)
    {
        if (profile is null)
            throw new AssessmentEnvironmentException(AssessmentEnvironmentErrors.EnvironmentRequired,
                "Connect an Azure environment to this system before running an assessment.",
                "Open Configure Environment and attach eligible organization subscriptions.");
        ValidateCloud(profile.CloudEnvironment, cloud);
        if (!string.IsNullOrWhiteSpace(profile.ProxyUrl) ||
            !MatchesEndpoint(profile.ArmEndpoint, ArmEndpoint(cloud)) ||
            !MatchesEndpoint(profile.AuthenticationEndpoint, AuthenticationEndpoint(cloud)) ||
            !MatchesEndpoint(profile.PolicyEndpoint, ArmEndpoint(cloud)) ||
            !MatchesEndpoint(profile.DefenderEndpoint, ArmEndpoint(cloud)))
            throw new AssessmentEnvironmentException(AssessmentEnvironmentErrors.UnsupportedCloud,
                "This Azure attachment requires custom endpoints or a proxy that the deployed assessment client does not support.",
                "Configure a supported connected environment; use a separately supported workflow for disconnected environments.");
        return ParseSubscriptions(profile.SubscriptionIds);
    }

    private static void ValidateCloud(AzureCloudEnvironment profileCloud, AzureCloudEnvironment deploymentCloud)
    {
        if (profileCloud is not (AzureCloudEnvironment.Commercial or AzureCloudEnvironment.Government))
            throw UnsupportedCloud();
        if (profileCloud != deploymentCloud)
            throw new AssessmentEnvironmentException(AssessmentEnvironmentErrors.CloudMismatch,
                "The system's Azure cloud does not match this deployment's assessment cloud.",
                "Choose an eligible subscription in the deployment's cloud or contact the platform administrator.");
    }

    private IReadOnlyList<Guid> ParseSubscriptions(IReadOnlyCollection<string>? values)
    {
        if (values is null || values.Count == 0)
            throw new AssessmentEnvironmentException(AssessmentEnvironmentErrors.SubscriptionRequired,
                "Attach at least one Azure subscription to this system before assessment.",
                "Use Configure Environment to select eligible organization subscriptions.");
        var ids = new List<Guid>();
        var unique = new HashSet<Guid>();
        if (values.Count > MaxSubscriptions)
            throw InvalidSubscriptions();
        foreach (var value in values)
        {
            if (!Guid.TryParse(value, out var id) || id == Guid.Empty || !unique.Add(id))
                throw InvalidSubscriptions();
            ids.Add(id);
        }
        return ids;
    }

    private async Task<List<AzureSubscriptionRegistration>> LoadSelectedRegistrationsAsync(
        AtoCopilotContext db, Guid tenantId, IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        return await db.AzureSubscriptionRegistrations.AsNoTracking()
            .Where(r => r.TenantId == tenantId && ids.Contains(r.SubscriptionId))
            .Take(MaxSubscriptions)
            .ToListAsync(cancellationToken);
    }

    private async Task<List<AzureSubscriptionRegistration>> LoadOrganizationRegistrationsAsync(
        AtoCopilotContext db, Guid tenantId, CancellationToken cancellationToken)
    {
        var registrations = await db.AzureSubscriptionRegistrations.AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .OrderBy(r => r.DisplayName)
            .Take(MaxSubscriptions + 1)
            .ToListAsync(cancellationToken);
        if (registrations.Count > MaxSubscriptions)
            throw DeploymentFailure("The organization's subscription registrations exceed its configured limit.");
        return registrations;
    }

    private static void ValidateRegistrations(
        IReadOnlyList<Guid> ids, IReadOnlyList<AzureSubscriptionRegistration> registrations, AzureCloudEnvironment cloud)
    {
        foreach (var id in ids)
        {
            var matches = registrations.Where(r => r.SubscriptionId == id).ToArray();
            if (matches.Length != 1 || matches[0].Status != SubscriptionStatus.Selected ||
                matches[0].ParentTenantId == Guid.Empty)
                throw new AssessmentEnvironmentException(AssessmentEnvironmentErrors.SubscriptionUnavailable,
                    "An attached subscription is not currently eligible in this system's organization.",
                    "Refresh the organization's Azure Subscription Settings, then select an available subscription.");
            if (RegistrationCloudName(matches[0].Environment) != cloud.ToString())
                throw new AssessmentEnvironmentException(AssessmentEnvironmentErrors.CloudMismatch,
                    "An attached subscription's registered cloud does not match the system and deployment.",
                    "Refresh the registration and select subscriptions from the deployment's supported cloud.");
        }
    }

    private int MaxSubscriptions => _onboarding.Limits.MaxAzureSubscriptionsPerTenant is > 0 and < int.MaxValue
        ? _onboarding.Limits.MaxAzureSubscriptionsPerTenant
        : throw DeploymentFailure("The organization's Azure subscription limit is invalid.");

    private static bool IsEligibleRegistration(AzureSubscriptionRegistration registration, AzureCloudEnvironment cloud) =>
        registration.Status == SubscriptionStatus.Selected &&
        registration.ParentTenantId != Guid.Empty &&
        registration.SubscriptionId != Guid.Empty &&
        RegistrationCloudName(registration.Environment) == cloud.ToString();

    private static string RegistrationCloudName(AzureEnvironment environment) => environment switch
    {
        AzureEnvironment.AzureCloud => nameof(AzureCloudEnvironment.Commercial),
        AzureEnvironment.AzureUSGovernment => nameof(AzureCloudEnvironment.Government),
        _ => "Unknown"
    };

    private static Uri ArmEndpoint(AzureCloudEnvironment cloud) => cloud == AzureCloudEnvironment.Government
        ? ArmEnvironment.AzureGovernment.Endpoint : ArmEnvironment.AzurePublicCloud.Endpoint;

    private static Uri AuthenticationEndpoint(AzureCloudEnvironment cloud) => cloud == AzureCloudEnvironment.Government
        ? AzureAuthorityHosts.AzureGovernment : AzureAuthorityHosts.AzurePublicCloud;

    private static bool MatchesEndpoint(string? configured, Uri expected) =>
        string.IsNullOrWhiteSpace(configured) ||
        (Uri.TryCreate(configured, UriKind.Absolute, out var actual) && actual == expected);

    private static AssessmentReadinessResponse Readiness(
        RegisteredSystem system, string? deploymentCloud,
        IReadOnlyList<AssessmentSubscriptionResponse> subscriptions, AssessmentEnvironmentException? failure) =>
        new(system.Id, failure is null, failure?.ErrorCode,
            failure?.Message ?? "The Azure environment is ready for assessment.", failure?.Suggestion,
            $"/systems/{system.Id}/profile/EnvironmentAndDeployment#azure-assessment-environment",
            deploymentCloud, system.AzureProfile?.CloudEnvironment.ToString(), subscriptions, DateTimeOffset.UtcNow);

    private static AssessmentEnvironmentResponse Configuration(
        RegisteredSystem system, AzureCloudEnvironment cloud, IReadOnlyList<AzureSubscriptionRegistration> registrations) =>
        new(system.Id, cloud.ToString(), system.AzureProfile?.CloudEnvironment.ToString(),
            system.AzureProfile?.SubscriptionIds.ToArray() ?? [],
            registrations.Select(r => new AssessmentSubscriptionOptionResponse(
                r.SubscriptionId.ToString(), r.DisplayName, RegistrationCloudName(r.Environment),
                IsEligibleRegistration(r, cloud))).ToArray());

    private static AssessmentEnvironmentException UnsupportedCloud() => new(
        AssessmentEnvironmentErrors.UnsupportedCloud,
        "Only supported connected Commercial or Government environments can run this assessment.",
        "Configure a supported connected environment. Disconnected IL5/IL6 assessment is not enabled by a cloud label.");

    private static AssessmentEnvironmentException InvalidSubscriptions() => new(
        AssessmentEnvironmentErrors.InvalidSubscription,
        "The Azure subscription selection contains invalid, duplicate or too many identifiers.",
        "Select unique valid subscriptions from the organization's registration list.");

    private static AssessmentEnvironmentException DeploymentFailure(string message) => new(
        AssessmentEnvironmentErrors.DeploymentConfiguration, message,
        "Ask the platform administrator to review the deployment's Azure assessment configuration.");

    private static void AddActivity(
        AtoCopilotContext db, RegisteredSystem system, string actorId, string eventType, string summary) =>
        db.DashboardActivities.Add(new DashboardActivity
        {
            TenantId = system.TenantId, RegisteredSystemId = system.Id, Actor = actorId,
            EventType = eventType, Summary = summary, RelatedEntityType = nameof(AzureEnvironmentProfile),
            RelatedEntityId = system.Id
        });
}
