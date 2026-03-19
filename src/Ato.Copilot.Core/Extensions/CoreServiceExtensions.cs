using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.AI.Agents.Persistent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenAI;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces;
using Ato.Copilot.Core.Models;
using Ato.Copilot.Core.Observability;
using Ato.Copilot.Core.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using System.ClientModel;

namespace Ato.Copilot.Core.Extensions;

/// <summary>
/// Extension methods for registering ATO Copilot Core services including
/// database context, Azure clients, and configuration bindings.
/// </summary>
public static class CoreServiceExtensions
{
    /// <summary>
    /// Adds core compliance services to the DI container including
    /// DbContext, ArmClient, configuration bindings, and HTTP client factory.
    /// </summary>
    /// <param name="services">The service collection to register services in.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAtoCopilotCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind configuration sections
        services.Configure<GatewayOptions>(configuration.GetSection(GatewayOptions.SectionName));
        services.Configure<AzureAiOptions>(configuration.GetSection(AzureAiOptions.SectionName));
        services.Configure<AzureAdOptions>(configuration.GetSection(AzureAdOptions.SectionName));
        services.Configure<PimServiceOptions>(configuration.GetSection(PimServiceOptions.SectionName));
        services.Configure<CacAuthOptions>(configuration.GetSection(CacAuthOptions.SectionName));
        services.Configure<RetentionPolicyOptions>(configuration.GetSection(RetentionPolicyOptions.SectionName));
        services.Configure<MonitoringOptions>(configuration.GetSection(MonitoringOptions.SectionName));
        services.Configure<AlertOptions>(configuration.GetSection(AlertOptions.SectionName));
        services.Configure<NotificationOptions>(configuration.GetSection(NotificationOptions.SectionName));
        services.Configure<EscalationOptions>(configuration.GetSection(EscalationOptions.SectionName));

        // Bind enterprise hardening configuration sections (Feature 029)
        services.Configure<ResilienceOptions>(configuration.GetSection(ResilienceOptions.SectionName));
        services.Configure<RateLimitingOptions>(configuration.GetSection(RateLimitingOptions.SectionName));
        services.Configure<CachingOptions>(configuration.GetSection(CachingOptions.SectionName));
        services.Configure<PaginationOptions>(configuration.GetSection(PaginationOptions.SectionName));
        services.Configure<StreamingOptions>(configuration.GetSection(StreamingOptions.SectionName));
        services.Configure<OpenTelemetryOptions>(configuration.GetSection(OpenTelemetryOptions.SectionName));

        // Register enterprise hardening singletons
        services.AddSingleton<HttpMetrics>();
        services.AddSingleton<IPathSanitizationService, PathSanitizationService>();
        services.AddSingleton<ResponseCacheService>();
        services.AddSingleton<OfflineModeService>();

        // Register IMemoryCache with configurable size limit (FR-020a)
        var cachingOptions = new CachingOptions();
        configuration.GetSection(CachingOptions.SectionName).Bind(cachingOptions);
        services.AddMemoryCache(options =>
        {
            options.SizeLimit = (long)cachingOptions.SizeLimitMb * 1024 * 1024;
        });

        // Register HTTP client factory with default resilience pipeline (FR-001, T016)
        services.AddHttpClient();
        services.AddHttpClient("default", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        })
        .AddResilienceHandler("resilience-default", pipelineBuilder =>
        {
            pipelineBuilder.AddRetry(new Microsoft.Extensions.Http.Resilience.HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldRetryAfterHeader = true,
            });
            pipelineBuilder.AddTimeout(TimeSpan.FromSeconds(30));
        });

        // Register IChatClient from Azure OpenAI when configured
        RegisterChatClient(services, configuration);

        // Register PersistentAgentsClient from Azure AI Foundry when configured
        RegisterFoundryClient(services, configuration);

        // Register database context with provider selection
        RegisterDbContext(services, configuration);

        // Register Azure ARM client
        RegisterArmClient(services, configuration);

        return services;
    }

    /// <summary>
    /// Registers <see cref="IChatClient"/> as a singleton when an OpenAI endpoint is configured.
    /// Reads from the unified AzureAi configuration section.
    /// Uses API key or DefaultAzureCredential based on UseManagedIdentity.
    /// When Endpoint is empty or missing, registration is silently skipped — agents fall back
    /// to deterministic tool routing (Constitution Principle: graceful degradation).
    /// </summary>
    private static void RegisterChatClient(IServiceCollection services, IConfiguration configuration)
    {
        var endpoint = configuration.GetValue<string>("AzureAi:Endpoint");
        if (string.IsNullOrWhiteSpace(endpoint))
            return;

        var useManagedIdentity = configuration.GetValue<bool>("AzureAi:UseManagedIdentity");
        var chatDeploymentName = configuration.GetValue<string>("AzureAi:DeploymentName") ?? "gpt-4o";
        var apiKey = configuration.GetValue<string>("AzureAi:ApiKey");
        var cloudEnv = configuration.GetValue<string>("AzureAi:CloudEnvironment");

        services.AddSingleton<IChatClient>(sp =>
        {
            var logger = sp.GetService<ILogger<IChatClient>>();

            Azure.AI.OpenAI.AzureOpenAIClient azureClient;
            if (useManagedIdentity)
            {
                var resolvedCloudEnv = cloudEnv ?? "AzureGovernment";
                var authorityHost = resolvedCloudEnv.Equals("AzureCloud", StringComparison.OrdinalIgnoreCase)
                                 || resolvedCloudEnv.Equals("AzurePublicCloud", StringComparison.OrdinalIgnoreCase)
                    ? AzureAuthorityHosts.AzurePublicCloud
                    : AzureAuthorityHosts.AzureGovernment;

                var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
                {
                    AuthorityHost = authorityHost
                });

                azureClient = new Azure.AI.OpenAI.AzureOpenAIClient(new Uri(endpoint), credential);
                logger?.LogInformation(
                    "Registered IChatClient with DefaultAzureCredential for {Endpoint}, deployment {Deployment}",
                    endpoint, chatDeploymentName);
            }
            else
            {
                var resolvedApiKey = apiKey ?? string.Empty;
                azureClient = new Azure.AI.OpenAI.AzureOpenAIClient(
                    new Uri(endpoint),
                    new ApiKeyCredential(resolvedApiKey));
                logger?.LogInformation(
                    "Registered IChatClient with API key for {Endpoint}, deployment {Deployment}",
                    endpoint, chatDeploymentName);
            }

            return azureClient.AsChatClient(chatDeploymentName);
        });
    }

    /// <summary>
    /// Registers <see cref="PersistentAgentsClient"/> as a singleton when a Foundry project endpoint
    /// is configured. Reads from the unified AzureAi configuration section.
    /// Uses DefaultAzureCredential with the appropriate authority host for Gov/Commercial.
    /// When Endpoint is empty or missing, registration is silently skipped — agents fall back
    /// to IChatClient or deterministic tool routing.
    /// </summary>
    private static void RegisterFoundryClient(IServiceCollection services, IConfiguration configuration)
    {
        var endpoint = configuration.GetValue<string>("AzureAi:FoundryProjectEndpoint");
        if (string.IsNullOrWhiteSpace(endpoint))
            return;

        var cloudEnv = configuration.GetValue<string>("AzureAi:CloudEnvironment");

        var resolvedCloudEnv = cloudEnv ?? "AzureGovernment";
        var authorityHost = resolvedCloudEnv.Equals("AzureCloud", StringComparison.OrdinalIgnoreCase)
                         || resolvedCloudEnv.Equals("AzurePublicCloud", StringComparison.OrdinalIgnoreCase)
            ? AzureAuthorityHosts.AzurePublicCloud
            : AzureAuthorityHosts.AzureGovernment;

        services.AddSingleton(sp =>
        {
            var logger = sp.GetService<ILogger<PersistentAgentsClient>>();
            var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                AuthorityHost = authorityHost
            });

            logger?.LogInformation(
                "Registered PersistentAgentsClient with DefaultAzureCredential for {Endpoint} ({CloudEnv})",
                endpoint, resolvedCloudEnv);

            return new PersistentAgentsClient(endpoint, credential);
        });
    }

    /// <summary>
    /// Registers <see cref="AtoCopilotContext"/> with SQLite (development) or
    /// SQL Server (production) based on Database:Provider configuration.
    /// Defaults to SQLite with "Data Source=ato-copilot.db".
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    private static void RegisterDbContext(IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration.GetValue<string>("Database:Provider") ?? "SQLite";
        var connectionString = configuration.GetConnectionString("DefaultConnection")
                               ?? "Data Source=ato-copilot.db";

        services.AddDbContextFactory<AtoCopilotContext>(options =>
        {
            // Suppress PendingModelChangesWarning — model snapshot may lag behind
            // code-first changes during active development. EnsureCreated/Migrate
            // will apply the correct schema.
            options.ConfigureWarnings(w =>
                w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));

            if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                options.UseSqlServer(connectionString, sqlOptions =>
                {
                    sqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(30),
                        errorNumbersToAdd: null);
                    sqlOptions.CommandTimeout(30);
                });
            }
            else
            {
                options.UseSqlite(connectionString, sqliteOptions =>
                {
                    sqliteOptions.CommandTimeout(30);
                });
            }
        });
    }

    /// <summary>
    /// Registers <see cref="ArmClientFactory"/> and a default <see cref="ArmClient"/>
    /// singleton with dual-cloud support (AzureGovernment / AzureCloud).
    /// The factory lazily creates clients for both clouds; the singleton resolves
    /// to the configured default for backward compatibility with existing services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    private static void RegisterArmClient(IServiceCollection services, IConfiguration configuration)
    {
        var cloudEnv = configuration.GetValue<string>("Gateway:Azure:CloudEnvironment")
                       ?? "AzureGovernment";

        // Factory: provides ArmClient instances for any cloud
        services.AddSingleton(sp =>
            new ArmClientFactory(cloudEnv, sp.GetRequiredService<ILogger<ArmClientFactory>>()));

        // Default singleton ArmClient: delegates to the factory's default for backward compat
        services.AddSingleton(sp => sp.GetRequiredService<ArmClientFactory>().GetDefault());
    }
}
