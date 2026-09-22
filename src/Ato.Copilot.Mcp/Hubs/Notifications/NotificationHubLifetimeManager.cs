using Microsoft.AspNetCore.SignalR;
using Ato.Copilot.Mcp.Services;

namespace Ato.Copilot.Mcp.Hubs.Notifications;

/// <summary>
/// Adapts legacy group publishers to per-connection, reauthorized delivery. Never forwards a raw
/// user/wizard group to SignalR; membership and support-session validity are checked on every send.
/// </summary>
public sealed class NotificationHubLifetimeManager(
    ILogger<DefaultHubLifetimeManager<NotificationHub>> logger,
    NotificationDeliveryService delivery) : DefaultHubLifetimeManager<NotificationHub>(logger)
{
    public override Task SendGroupAsync(string groupName, string methodName, object?[] args,
        CancellationToken cancellationToken = default) =>
        delivery.SendLegacyGroupAsync(groupName, methodName, args,
            (id, method, arguments, ct) => base.SendConnectionAsync(id, method, arguments, ct),
            cancellationToken);
}

public static class NotificationServiceRegistration
{
    /// <summary>
    /// Register after AddSignalR. This implementation intentionally uses process-local subscriptions;
    /// a distributed SignalR lifetime manager must preserve per-delivery reauthorization.
    /// </summary>
    public static IServiceCollection AddWorkspaceNotifications(this IServiceCollection services)
    {
        services.AddAuthentication().AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions,
            NotificationHubAuthenticationHandler>(NotificationHubAuthenticationHandler.SchemeName, _ => { });
        services.AddSingleton<NotificationConnectionRegistry>();
        services.AddSingleton<NotificationDeliveryService>();
        services.AddSingleton<WorkspaceProgressDeliveryService>();
        services.AddScoped<INotificationAccessService, NotificationAccessService>();
        services.AddScoped<IWorkspaceNotificationService, WorkspaceNotificationService>();
        services.AddScoped<IWorkspaceHubTokenValidator, WorkspaceHubTokenValidator>();
        services.AddScoped<INotificationCapabilitiesService, NotificationCapabilitiesService>();
        services.AddSingleton<HubLifetimeManager<NotificationHub>, NotificationHubLifetimeManager>();
        services.AddSingleton<HubLifetimeManager<PackageHub>, PackageProgressLifetimeManager>();
        services.AddSingleton<HubLifetimeManager<ImportProgressHub>, ImportProgressLifetimeManager>();
        return services;
    }
}
