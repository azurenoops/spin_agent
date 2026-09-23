using Microsoft.AspNetCore.SignalR;

namespace Ato.Copilot.Mcp.Hubs.Notifications;

/// <summary>Routes existing progress publishers through fresh resource authorization instead of raw groups.</summary>
public abstract class WorkspaceProgressLifetimeManager<THub>(
    ILogger<DefaultHubLifetimeManager<THub>> logger,
    WorkspaceProgressDeliveryService delivery,
    ProgressResourceKind kind) : DefaultHubLifetimeManager<THub>(logger) where THub : Hub
{
    public override Task SendGroupAsync(string groupName, string methodName, object?[] args, CancellationToken cancellationToken = default) =>
        delivery.SendGroupAsync(kind, groupName, methodName, args,
            (id, method, payload, ct) => base.SendConnectionAsync(id, method, payload, ct), cancellationToken);
}

public sealed class PackageProgressLifetimeManager(
    ILogger<DefaultHubLifetimeManager<PackageHub>> logger, WorkspaceProgressDeliveryService delivery)
    : WorkspaceProgressLifetimeManager<PackageHub>(logger, delivery, ProgressResourceKind.Package);

public sealed class ImportProgressLifetimeManager(
    ILogger<DefaultHubLifetimeManager<ImportProgressHub>> logger, WorkspaceProgressDeliveryService delivery)
    : WorkspaceProgressLifetimeManager<ImportProgressHub>(logger, delivery, ProgressResourceKind.Import);
