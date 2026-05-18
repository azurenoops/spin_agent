using Ato.Copilot.Channels.Abstractions;
using Ato.Copilot.Channels.Configuration;
using Ato.Copilot.Channels.Implementations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Ato.Copilot.Channels.Extensions;

/// <summary>
/// Extension methods for registering Channels services in the DI container.
/// </summary>
public static class ChannelServiceExtensions
{
    /// <summary>
    /// Full registration with configuration binding.
    /// Registers IChannel, IChannelManager, IStreamingHandler (singletons),
    /// IMessageHandler (scoped), and IdleConnectionCleanupService (hosted).
    /// </summary>
    /// <remarks>
    /// The consumer must also register an <see cref="IConversationStateManager"/> implementation
    /// at the composition root — the Channels library does not own persistence.
    /// </remarks>
    public static IServiceCollection AddChannels(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ChannelOptions>(configuration.GetSection(ChannelOptions.SectionName));
        services.AddSingleton<IChannel, InMemoryChannel>();
        services.AddSingleton<IChannelManager, ChannelManager>();
        services.AddSingleton<IStreamingHandler, StreamingHandler>();
        services.TryAddSingleton<ITenantScopeBinder, NullTenantScopeBinder>();
        services.AddScoped<IMessageHandler, DefaultMessageHandler>();
        services.AddHostedService<IdleConnectionCleanupService>();
        return services;
    }

    /// <summary>
    /// Simplified registration for testing — uses default options, no config binding,
    /// and does not start IdleConnectionCleanupService.
    /// </summary>
    /// <remarks>
    /// The consumer must also register an <see cref="IConversationStateManager"/> implementation.
    /// </remarks>
    public static IServiceCollection AddInMemoryChannels(this IServiceCollection services)
    {
        services.AddSingleton(Options.Create(new ChannelOptions()));
        services.AddSingleton<IChannel, InMemoryChannel>();
        services.AddSingleton<IChannelManager, ChannelManager>();
        services.AddSingleton<IStreamingHandler, StreamingHandler>();
        services.TryAddSingleton<ITenantScopeBinder, NullTenantScopeBinder>();
        services.AddScoped<IMessageHandler, DefaultMessageHandler>();
        return services;
    }
}
