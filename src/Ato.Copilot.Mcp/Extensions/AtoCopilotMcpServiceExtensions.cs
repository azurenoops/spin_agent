using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Azure.ResourceManager;
using Ato.Copilot.Agents.Extensions;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Extensions;
using Ato.Copilot.State.Extensions;

namespace Ato.Copilot.Mcp.Extensions;

/// <summary>
/// Provides the canonical MCP server service-registration pipeline used by
/// both production <c>Program.cs</c> startup and integration test scaffolding.
///
/// Centralizing this composition here ensures that any service required by an
/// MCP tool, dashboard endpoint, or hosted service is registered identically
/// in both contexts — eliminating DI drift that would otherwise surface as
/// strict-scope-validation failures only in tests.
/// </summary>
public static class AtoCopilotMcpServiceExtensions
{
    /// <summary>
    /// Registers the full ATO Copilot MCP service graph: core infrastructure,
    /// state management, every agent (Compliance, Configuration, KnowledgeBase,
    /// Document), the MCP server itself, dashboard services, and every feature
    /// service consumed by an MCP tool or HTTP endpoint.
    /// </summary>
    /// <param name="services">The service collection to register services in.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="includeHostedServices">
    /// When <c>true</c> (production default), registers all <see cref="IHostedService"/>
    /// implementations (background workers, retention, migrations, snapshots).
    /// Set to <c>false</c> from integration test scaffolding to skip hosted services
    /// that would otherwise start during <c>WebApplicationFactory</c> server startup.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAtoCopilotMcp(
        this IServiceCollection services,
        IConfiguration configuration,
        bool includeHostedServices = true)
    {
        // Core infrastructure
        services.AddAtoCopilotCore(configuration);

        // State management
        services.AddInMemoryStateManagement();

        // Compliance agent + tools
        services.AddComplianceAgent(configuration);

        // Configuration agent + tools
        services.AddConfigurationAgent();

        // KnowledgeBase agent + services
        services.AddKnowledgeBaseAgent(configuration);

        // Document agent + adapter tools (document-centric orchestration)
        services.AddDocumentAgent();

        // MCP server
        services.AddMcpServer(configuration);

        // Dashboard services (Feature 030)
        services.AddScoped<Ato.Copilot.Core.Services.DashboardService>();
        services.AddScoped<Ato.Copilot.Core.Services.TodoService>();
        services.AddScoped<Ato.Copilot.Core.Services.CapabilityService>();
        services.AddSingleton<Ato.Copilot.Core.Services.ComponentService>();
        services.AddSingleton<Ato.Copilot.Core.Services.SystemCapabilityLinkService>();
        services.AddSingleton<Ato.Copilot.Core.Services.BoundaryLockService>();
        if (includeHostedServices)
        {
            services.AddHostedService<Ato.Copilot.Core.Services.BoundaryMigrationService>();
        }
        services.AddScoped<Ato.Copilot.Agents.Compliance.Services.EntraIdDiscoveryService>();
        services.AddSingleton<Ato.Copilot.Core.Services.NarrativeTemplateService>(sp =>
        {
            var chatClient = sp.GetService<IChatClient>();
            var aiOptions = sp.GetService<IOptions<AzureAiOptions>>()?.Value;
            var logger = sp.GetService<ILogger<Ato.Copilot.Core.Services.NarrativeTemplateService>>();
            return new Ato.Copilot.Core.Services.NarrativeTemplateService(chatClient, aiOptions, logger);
        });
        // FR-110 reuse-first audit (T218): IControlNarrativeService is the single
        // narrative-generation contract; both the concrete and the interface point
        // at the SAME singleton so DI counts as exactly 1 registration of the
        // interface — the CspInheritanceReuseAuditHealthCheck enforces this.
        services.AddSingleton<Ato.Copilot.Core.Interfaces.Compliance.IControlNarrativeService>(sp =>
            sp.GetRequiredService<Ato.Copilot.Core.Services.NarrativeTemplateService>());
        services.AddSingleton<Ato.Copilot.Core.Services.ComplianceTrendSnapshotService>();
        if (includeHostedServices)
        {
            services.AddHostedService(sp => sp.GetRequiredService<Ato.Copilot.Core.Services.ComplianceTrendSnapshotService>());
        }

        // Boundary services (Feature 033)
        services.AddScoped<Ato.Copilot.Core.Services.BoundaryDefinitionService>();
        services.AddSingleton(sp =>
            new Ato.Copilot.Agents.Compliance.Services.AzureResourceDiscoveryService(
                sp.GetRequiredService<ArmClient>(),
                sp.GetRequiredService<Ato.Copilot.Core.Services.ArmClientFactory>(),
                sp.GetRequiredService<ILogger<Ato.Copilot.Agents.Compliance.Services.AzureResourceDiscoveryService>>(),
                sp.GetRequiredService<IDbContextFactory<AtoCopilotContext>>()));

        // Roadmap services (Feature 031)
        services.AddScoped<Ato.Copilot.Core.Interfaces.Roadmap.IRoadmapService, Ato.Copilot.Core.Services.RoadmapService>();

        // Deviation services (Feature 035)
        services.AddSingleton<Ato.Copilot.Core.Interfaces.Compliance.IDeviationService, Ato.Copilot.Core.Services.DeviationService>();
        if (includeHostedServices)
        {
            services.AddHostedService<Ato.Copilot.Agents.Compliance.Services.DeviationExpirationService>();
        }

        // SSP Export services (Feature 037)
        services.Configure<ExportSettings>(configuration.GetSection(ExportSettings.SectionName));
        // Feature flags (Feature 040)
        services.Configure<FeatureOptions>(configuration.GetSection("Features"));
        services.AddSingleton(System.Threading.Channels.Channel.CreateBounded<Ato.Copilot.Core.Dtos.Dashboard.SspExportJob>(
            new System.Threading.Channels.BoundedChannelOptions(100) { FullMode = System.Threading.Channels.BoundedChannelFullMode.Wait }));
        services.AddScoped<Ato.Copilot.Core.Interfaces.Compliance.ISspExportService,
            Ato.Copilot.Agents.Compliance.Services.SspExportService>();
        services.AddSingleton<Ato.Copilot.Core.Interfaces.Compliance.ISspExportNotifier,
            Ato.Copilot.Mcp.Services.SignalRSspExportNotifier>();
        if (includeHostedServices)
        {
            services.AddHostedService<Ato.Copilot.Agents.Compliance.Services.SspExportBackgroundService>();
            services.AddHostedService<Ato.Copilot.Agents.Compliance.Services.SspExportRetentionService>();
        }

        // Feature 043: Control Inheritance CRM Export
        services.AddSingleton<Ato.Copilot.Mcp.Services.CrmExportService>();

        // Feature 043: CSP Profile Service
        services.AddSingleton<Ato.Copilot.Mcp.Services.CspProfileService>();

        // Feature 045: Capabilities Hub Import Service
        services.AddScoped<Ato.Copilot.Mcp.Services.CapabilityImportService>();

        // Feature 044: IOrgInheritanceService is registered by AddMcpServer (TryAddSingleton)

        // Feature 048 (US9 / US10): CSP-inherited components — uploaded ATO
        // ingestion + capability mapping. Registered as Scoped so the parser
        // dispatcher and extraction service can share the same DbContext per
        // request. The wrapper (CspCapabilityMappingService) composes the
        // FR-110-protected ICapabilityMappingService — which is registered
        // here exactly once as the NullCapabilityMappingService fallback
        // (FR-102 path: AI mapper unavailable → empty candidate list →
        // wrapper produces a single NeedsReview capability per component).
        // The CspInheritanceReuseAuditHealthCheck (T218) verifies the
        // single-registration invariant. When the AI-backed mapper from
        // Features 045 / 008 is wired through in the T227 slice, swap the
        // concrete in place — DO NOT add a second registration.
        services.Configure<Ato.Copilot.Core.Configuration.Tenancy.CspInheritedOptions>(
            configuration.GetSection(Ato.Copilot.Core.Configuration.Tenancy.CspInheritedOptions.SectionName));
        services.AddScoped<Ato.Copilot.Core.Interfaces.Compliance.ICapabilityMappingService,
            Ato.Copilot.Core.Services.Tenancy.NullCapabilityMappingService>();
        services.AddScoped<Ato.Copilot.Core.Interfaces.Tenancy.ICspAtoDocumentParser,
            Ato.Copilot.Core.Services.Tenancy.CspAtoDocumentParser>();
        services.AddScoped<Ato.Copilot.Core.Interfaces.Tenancy.ICspComponentExtractionService,
            Ato.Copilot.Core.Services.Tenancy.CspComponentExtractionService>();
        services.AddScoped<Ato.Copilot.Core.Interfaces.Tenancy.ICspCapabilityMappingService,
            Ato.Copilot.Core.Services.Tenancy.CspCapabilityMappingService>();
        services.AddScoped<Ato.Copilot.Core.Interfaces.Tenancy.ICspInheritedComponentService,
            Ato.Copilot.Core.Services.Tenancy.CspInheritedComponentService>();

        // Feature 041: Authorization Package generation pipeline
        services.AddSingleton(System.Threading.Channels.Channel.CreateBounded<Ato.Copilot.Core.Dtos.Dashboard.PackageExportJob>(
            new System.Threading.Channels.BoundedChannelOptions(20) { FullMode = System.Threading.Channels.BoundedChannelFullMode.Wait }));
        services.AddSingleton<Ato.Copilot.Core.Interfaces.Compliance.IPackageExportNotifier,
            Ato.Copilot.Mcp.Services.SignalRPackageExportNotifier>();
        if (includeHostedServices)
        {
            services.AddHostedService<Ato.Copilot.Agents.Compliance.Services.PackageBackgroundService>();
        }

        // Feature 038 — Evidence Repository: IFileStorageProvider + IEvidenceArtifactService are
        // registered by AddMcpServer (TryAddSingleton, configuration-driven). Only the background
        // version-purge hosted service is registered here.
        if (includeHostedServices)
        {
            services.AddHostedService<Ato.Copilot.Mcp.Services.EvidenceVersionPurgeService>();
        }

        // POA&M Management services (Feature 039)
        services.AddScoped<Ato.Copilot.Core.Services.PoamService>();
        services.AddScoped<Ato.Copilot.Core.Services.PoamSyncService>();
        services.AddScoped<Ato.Copilot.Core.Services.TicketingService>();
        services.AddScoped<Ato.Copilot.Core.Services.Ticketing.ITicketingProvider, Ato.Copilot.Core.Services.Ticketing.JiraProvider>();
        services.AddScoped<Ato.Copilot.Core.Services.Ticketing.ITicketingProvider, Ato.Copilot.Core.Services.Ticketing.ServiceNowProvider>();

        return services;
    }
}
