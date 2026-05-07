using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Interfaces;
using Ato.Copilot.Core.Interfaces.Auth;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Storage;
using Ato.Copilot.Core.Services;
using Ato.Copilot.Mcp.Configuration;
using Ato.Copilot.Mcp.Models;
using Ato.Copilot.Mcp.Resilience;
using Ato.Copilot.Mcp.Server;
using Ato.Copilot.Mcp.Services;
using Ato.Copilot.Mcp.Services.Storage;
using Ato.Copilot.Mcp.Tools;

namespace Ato.Copilot.Mcp.Extensions;

public static class McpServiceExtensions
{
    /// <summary>
    /// Register MCP server services (tools, server, HTTP bridge).
    ///
    /// Also idempotently registers the small set of cross-cutting Singleton
    /// dependencies that MCP tools (and their Singleton transitive consumers
    /// such as <c>BaselineService</c>, <c>RemediationScriptExecutor</c>,
    /// <c>AuthorizationPackageService</c>, and <c>PackageValidationService</c>)
    /// require. These are registered with <see cref="ServiceCollectionDescriptorExtensions.TryAdd"/>
    /// semantics so production <c>Program.cs</c> can override the default
    /// implementations (e.g. swap to <see cref="AzureBlobStorageProvider"/>).
    /// </summary>
    public static IServiceCollection AddMcpServer(this IServiceCollection services, IConfiguration configuration)
    {
        // User context — IHttpContextAccessor enables cross-scope access to request identity
        services.AddHttpContextAccessor();
        services.AddScoped<IUserContext, HttpUserContext>();

        // ── Cross-cutting Singleton dependencies (TryAdd: production overrides win) ──
        // These satisfy strict-scope DI validation when the MCP container is built
        // standalone (production Program.cs and integration test scaffolding).

        // IPathSanitizationService — also registered by AddAtoCopilotCore in production
        services.TryAddSingleton<IPathSanitizationService, PathSanitizationService>();

        // IOrgInheritanceService — Feature 044 (consumed by BaselineService)
        services.TryAddSingleton<IOrgInheritanceService,
            Ato.Copilot.Agents.Compliance.Services.OrgInheritanceService>();

        // Feature 038 — Evidence Repository: IFileStorageProvider + IEvidenceArtifactService
        services.Configure<EvidenceOptions>(configuration.GetSection(EvidenceOptions.SectionName));
        var evidenceStorageProvider = configuration.GetValue<string>("Evidence:StorageProvider") ?? "Local";
        if (evidenceStorageProvider.Equals("AzureBlob", StringComparison.OrdinalIgnoreCase))
        {
            var connectionString = configuration.GetValue<string>("Evidence:AzureBlobConnectionString")
                ?? throw new InvalidOperationException("Evidence:AzureBlobConnectionString is required when StorageProvider is AzureBlob.");
            var containerName = configuration.GetValue<string>("Evidence:AzureBlobContainerName") ?? "evidence";
            services.TryAddSingleton<IFileStorageProvider>(sp =>
                new AzureBlobStorageProvider(
                    connectionString, containerName,
                    sp.GetRequiredService<ILogger<AzureBlobStorageProvider>>()));
        }
        else
        {
            var localPath = configuration.GetValue<string>("Evidence:LocalStoragePath") ?? "/data/evidence";
            services.TryAddSingleton<IFileStorageProvider>(sp =>
                new LocalFileStorageProvider(
                    localPath,
                    sp.GetRequiredService<ILogger<LocalFileStorageProvider>>()));
        }
        services.TryAddSingleton<IEvidenceArtifactService, EvidenceArtifactService>();

        // MCP Tools
        services.AddSingleton<ComplianceMcpTools>();
        services.AddSingleton<KnowledgeBaseMcpTools>();

        // Agent Orchestrator — confidence-scored multi-agent routing
        services.AddSingleton<AgentOrchestrator>();

        // MCP Server
        services.AddSingleton<McpServer>();
        services.AddSingleton<McpHttpBridge>();
        services.AddSingleton<SseEventBuffer>();

        return services;
    }

    /// <summary>
    /// Register MCP stdio background service for CLI mode
    /// </summary>
    public static IServiceCollection AddMcpStdioService(this IServiceCollection services)
    {
        services.AddHostedService<McpStdioService>();
        return services;
    }
}
