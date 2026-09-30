using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>Rechecks current authority before existing subscription-wide collectors run.</summary>
public sealed class CanonicalEnvironmentCollectionGuard(
    IDbContextFactory<AtoCopilotContext> factory, IServiceScopeFactory scopeFactory)
{
    /// <summary>Blocks canonical exact-resource scope before a system assessment invokes a broad collector.</summary>
    public async Task EnsureSystemAsync(string systemId, EnvironmentScopePurpose purpose, CancellationToken ct)
    {
        await using var scope = CreateAuthorizedScope();
        var resolver = scope.ServiceProvider.GetRequiredService<ISystemEnvironmentScopeResolver>();
        var resolved = await resolver.ResolveAsync(systemId, purpose, ct);
        if (CanonicalEnvironmentExecutionGate.IsCanonical(resolved))
            throw CanonicalEnvironmentExecutionGate.Failure(resolved);
        if (purpose == EnvironmentScopePurpose.Monitoring)
            throw CanonicalEnvironmentExecutionGate.ReconciliationRequired();
    }

    /// <summary>Checks all visible system bindings rather than selecting the first system sharing a subscription.</summary>
    public async Task EnsureSubscriptionAsync(string subscriptionId, EnvironmentScopePurpose purpose,
        CancellationToken ct, bool requireAttachment = false)
    {
        await using var scope = CreateAuthorizedScope();
        var resolver = scope.ServiceProvider.GetRequiredService<ISystemEnvironmentScopeResolver>();
        await using var db = await factory.CreateDbContextAsync(ct);
        var systems = db.RegisteredSystems.AsNoTracking()
            .Where(x => x.IsActive).Select(x => new { x.Id, x.AzureProfile });
        await foreach (var system in systems.AsAsyncEnumerable().WithCancellation(ct))
        {
            var resolved = await resolver.ResolveAsync(system.Id, purpose, ct);
            var legacyMatch = system.AzureProfile?.SubscriptionIds.Any(
                x => string.Equals(x, subscriptionId, StringComparison.OrdinalIgnoreCase)) == true;
            if (!CanonicalEnvironmentExecutionGate.IsCanonical(resolved))
            {
                continue;
            }
            if (legacyMatch || resolved.Sources.Any(x => string.Equals(
                x.Registration.SubscriptionId.ToString(), subscriptionId, StringComparison.OrdinalIgnoreCase)))
                throw CanonicalEnvironmentExecutionGate.Failure(resolved);
        }
        if (requireAttachment)
            throw CanonicalEnvironmentExecutionGate.ReconciliationRequired();
    }

    /// <summary>Guards a resource-addressed read before either the cache or Azure is accessed.</summary>
    public Task EnsureResourceAsync(string resourceId, CancellationToken ct)
    {
        var parts = resourceId.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !parts[0].Equals("subscriptions", StringComparison.OrdinalIgnoreCase))
            throw new AssessmentEnvironmentException(CanonicalEnvironmentExecutionGate.Ineligible,
                "An Azure subscription resource identity is required.", CanonicalEnvironmentExecutionGate.Suggestion);
        return EnsureSubscriptionAsync(parts[1], EnvironmentScopePurpose.Assessment, ct);
    }

    private AsyncServiceScope CreateAuthorizedScope()
    {
        var scope = scopeFactory.CreateAsyncScope();
        var current = scope.ServiceProvider.GetService<ITenantContextAccessor>()?.Current;
        if (current is not null)
        {
            if (scope.ServiceProvider.GetService<ITenantContext>() is not TenantContext context)
            {
                scope.Dispose();
                throw new InvalidOperationException("Collection requires a tenant-scoped environment resolver.");
            }
            context.TenantId = current.TenantId;
            context.OrganizationId = current.OrganizationId;
            context.PersonId = current.PersonId;
            context.IsWorkspaceRequest = current.IsWorkspaceRequest;
            context.IsCspAdmin = current.IsCspAdmin;
            context.ImpersonatedTenantId = current.ImpersonatedTenantId;
            context.Status = current.Status;
        }
        return scope;
    }
}
