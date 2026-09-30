using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Kanban;
using Ato.Copilot.Core.Models.Poam;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Services.Ticketing;

public sealed record TaskTicketView(
    string Id, string Provider, string? ExternalRef, string? ExternalUrl,
    string? ExternalStatus, string? ExternalAssignee, DateTime? LastSuccessfulSyncAt,
    string State, Guid RowVersion, string CorrelationKey, string? LastError);
public sealed record TaskTicketResponse(bool Configured, bool CanManage, TaskTicketView? Link)
{
    public string Mode => "ManualPullOnly";
    public bool WebhooksSupported => false;
    public bool BidirectionalSupported => false;
}

public sealed class TaskTicketService(
    AtoCopilotContext db,
    IEnumerable<ITicketingProvider> providers,
    ITenantContext tenant,
    ISystemWorkspaceAccessService access)
{
    public async Task AuthorizeConfigurationAsync(string systemId, bool write, CancellationToken ct = default)
    {
        if (tenant.EffectiveTenantId == Guid.Empty) throw new UnauthorizedAccessException();
        if (!await db.RegisteredSystems.AnyAsync(x => x.Id == systemId && x.TenantId == tenant.EffectiveTenantId, ct))
            throw new KeyNotFoundException("System is not accessible.");
        var permission = await access.GetAccessAsync(tenant.EffectiveTenantId, tenant.PersonId, systemId, false, ct);
        if (!permission.Permissions.CanRead) throw new KeyNotFoundException("System is not accessible.");
        if (write && !permission.Permissions.CanManageSystem) throw new UnauthorizedAccessException();
    }

    public async Task<TaskTicketResponse> GetAsync(string systemId, string taskId, CancellationToken ct = default)
    {
        var (_, canManage) = await AuthorizeAsync(systemId, taskId, false, ct);
        var config = await ConfigAsync(systemId, ct);
        return Response(config?.SyncEnabled == true, canManage, await LinkRecordAsync(systemId, taskId, ct));
    }

    public async Task<TaskTicketResponse> CreateAsync(string systemId, string taskId, CancellationToken ct = default)
    {
        var (task, _) = await AuthorizeAsync(systemId, taskId, true, ct);
        var config = await EnabledConfigAsync(systemId, ct);
        var existing = await LinkRecordAsync(systemId, taskId, ct);
        if (existing is not null)
        {
            if (existing.State == "Linked") return Response(true, true, existing);
            throw new InvalidOperationException("Creation already reserved or a ticket previously existed. Locate the remote correlation key and link an existing ticket.");
        }

        var link = NewLink(systemId, taskId, config);
        link.CreateAttempted = true;
        db.Set<TaskTicketLink>().Add(link);
        Audit(link, "CreateReserved");
        // This durable unique claim intentionally never expires: remote POST outcomes can be unknowable.
        await db.SaveChangesAsync(ct);
        var result = await Provider(config).CreateTaskAsync(
            new(task.Title, task.Description, link.CorrelationKey), config, ct);
        await AuthorizeAsync(systemId, taskId, true, ct);
        if (result.Success && ValidRef(result.ExternalRef, config.Provider))
        {
            link.ExternalRef = result.ExternalRef;
            link.State = "Linked";
            // Creation is not a status snapshot; lastSuccessfulSyncAt stays empty until an explicit pull.
        }
        else
        {
            link.State = "Uncertain";
            link.LastError = "Create outcome is uncertain. Search the provider for the correlation key, then link the existing ticket. Creation cannot be retried.";
        }
        Audit(link, link.State == "Linked" ? "Created" : "CreateUncertain");
        await db.SaveChangesAsync(ct);
        return Response(true, true, link);
    }

    public async Task<TaskTicketResponse> LinkAsync(string systemId, string taskId, string externalRef,
        Guid? rowVersion, CancellationToken ct = default)
    {
        await AuthorizeAsync(systemId, taskId, true, ct);
        var config = await EnabledConfigAsync(systemId, ct);
        if (!ValidRef(externalRef, config.Provider))
            throw new InvalidOperationException("Enter a Jira issue key or ServiceNow 32-character sys_id.");
        var link = await LinkRecordAsync(systemId, taskId, ct);
        if (link is not null) CheckVersion(link, rowVersion);
        var result = await Provider(config).PullAsync(externalRef, config, ct);
        await AuthorizeAsync(systemId, taskId, true, ct);
        if (!result.Success) throw new InvalidOperationException("The external ticket could not be read. Existing linkage is unchanged.");
        if (link is null)
        {
            link = NewLink(systemId, taskId, config);
            db.Set<TaskTicketLink>().Add(link);
        }
        else if (link.State == "Linked" && link.ExternalRef != externalRef)
            throw new InvalidOperationException("Unlink the current ticket before linking another.");
        link.Provider = config.Provider;
        link.BaseUrl = config.BaseUrl;
        link.ProjectKey = config.ProjectKeyOrTableName ?? "";
        link.TicketingIntegrationId = config.Id;
        link.ExternalRef = externalRef;
        link.State = "Linked";
        ApplySnapshot(link, result);
        Audit(link, "Linked");
        await db.SaveChangesAsync(ct);
        return Response(true, true, link);
    }

    public async Task<TaskTicketResponse> RefreshAsync(string systemId, string taskId, Guid rowVersion, CancellationToken ct = default)
    {
        await AuthorizeAsync(systemId, taskId, true, ct);
        var config = await EnabledConfigAsync(systemId, ct);
        var link = await RequiredLinkAsync(systemId, taskId, rowVersion, ct);
        if (link.State != "Linked" || link.ExternalRef is null)
            throw new InvalidOperationException("Link a ticket before refreshing.");
        if (link.Provider != config.Provider || link.BaseUrl != config.BaseUrl
            || link.ProjectKey != config.ProjectKeyOrTableName || link.TicketingIntegrationId != config.Id)
            throw new InvalidOperationException("The connector configuration changed. Unlink and explicitly relink the ticket.");
        var result = await Provider(config).PullAsync(link.ExternalRef, config, ct);
        await AuthorizeAsync(systemId, taskId, true, ct);
        if (result.Success) ApplySnapshot(link, result);
        else link.LastError = "Refresh failed. The last successful snapshot is retained.";
        Audit(link, result.Success ? "Refreshed" : "RefreshFailed");
        await db.SaveChangesAsync(ct);
        return Response(true, true, link);
    }

    public async Task<TaskTicketResponse> UnlinkAsync(string systemId, string taskId, Guid rowVersion, CancellationToken ct = default)
    {
        await AuthorizeAsync(systemId, taskId, true, ct);
        var link = await RequiredLinkAsync(systemId, taskId, rowVersion, ct);
        if (link.State is "Pending" or "Uncertain")
            throw new InvalidOperationException("Resolve the uncertain creation by linking the existing remote ticket before unlinking.");
        Audit(link, "Unlinked");
        link.ExternalRef = null;
        link.ExternalStatus = null;
        link.ExternalAssignee = null;
        link.LastSuccessfulSyncAt = null;
        link.LastError = null;
        link.State = "Unlinked";
        await db.SaveChangesAsync(ct);
        return Response((await ConfigAsync(systemId, ct))?.SyncEnabled == true, true, link);
    }

    private async Task<(RemediationTask Task, bool CanManage)> AuthorizeAsync(string systemId, string taskId, bool write, CancellationToken ct)
    {
        if (tenant.EffectiveTenantId == Guid.Empty) throw new UnauthorizedAccessException();
        var task = await db.RemediationTasks.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == taskId && item.TenantId == tenant.EffectiveTenantId, ct)
            ?? throw new KeyNotFoundException("Task is not accessible.");
        if (await RemediationScope.TaskSystemAsync(db, task, ct) != systemId)
            throw new KeyNotFoundException("Task is not accessible.");
        var permission = await access.GetAccessAsync(tenant.EffectiveTenantId, tenant.PersonId, systemId, false, ct);
        if (!permission.Permissions.CanRead) throw new KeyNotFoundException("Task is not accessible.");
        if (write && !permission.Permissions.CanManageRemediation) throw new UnauthorizedAccessException();
        return (task, permission.Permissions.CanManageRemediation);
    }

    private Task<TicketingIntegration?> ConfigAsync(string systemId, CancellationToken ct) =>
        db.TicketingIntegrations.AsNoTracking().SingleOrDefaultAsync(x =>
            x.TenantId == tenant.EffectiveTenantId && x.RegisteredSystemId == systemId, ct);
    private async Task<TicketingIntegration> EnabledConfigAsync(string systemId, CancellationToken ct)
    {
        var config = await ConfigAsync(systemId, ct);
        return config?.SyncEnabled == true ? config : throw new InvalidOperationException("Manual ticket integration is not configured or enabled.");
    }
    private Task<TaskTicketLink?> LinkRecordAsync(string systemId, string taskId, CancellationToken ct) =>
        db.Set<TaskTicketLink>().SingleOrDefaultAsync(x => x.TenantId == tenant.EffectiveTenantId
            && x.RegisteredSystemId == systemId && x.TaskId == taskId, ct);
    private async Task<TaskTicketLink> RequiredLinkAsync(string systemId, string taskId, Guid version, CancellationToken ct)
    {
        var link = await LinkRecordAsync(systemId, taskId, ct) ?? throw new KeyNotFoundException("Ticket link not found.");
        CheckVersion(link, version);
        return link;
    }
    private static void CheckVersion(TaskTicketLink link, Guid? version)
    {
        if (version is null || version != link.RowVersion)
            throw new DbUpdateConcurrencyException("The ticket link changed. Reload before trying again.");
    }
    private ITicketingProvider Provider(TicketingIntegration config) =>
        providers.SingleOrDefault(x => x.ProviderType == config.Provider)
        ?? throw new InvalidOperationException("Ticket provider is not supported.");
    private TaskTicketLink NewLink(string systemId, string taskId, TicketingIntegration config) => new()
    {
        TenantId = tenant.EffectiveTenantId, RegisteredSystemId = systemId, TaskId = taskId,
        TicketingIntegrationId = config.Id, Provider = config.Provider, BaseUrl = config.BaseUrl,
        ProjectKey = config.ProjectKeyOrTableName ?? "",
        CorrelationKey = "spin-task-" + Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes($"{tenant.EffectiveTenantId:D}/{systemId}/{taskId}"))).ToLowerInvariant()
    };
    private void Audit(TaskTicketLink link, string action)
    {
        // Set explicitly as well as the shared ConcurrentEntity save hook for provider-neutral tests.
        link.RowVersion = Guid.NewGuid();
        db.Set<TaskTicketAudit>().Add(new()
        {
            TenantId = tenant.EffectiveTenantId, TaskTicketLinkId = link.Id,
            ActorId = tenant.PersonId?.ToString() ?? "system", Action = action, ExternalRef = link.ExternalRef
        });
    }
    private static void ApplySnapshot(TaskTicketLink link, TicketSyncResult result)
    {
        link.ExternalStatus = result.ExternalStatus?[..Math.Min(100, result.ExternalStatus.Length)];
        link.ExternalAssignee = result.ExternalAssignee?[..Math.Min(200, result.ExternalAssignee.Length)];
        link.LastSuccessfulSyncAt = DateTime.UtcNow;
        link.LastError = null;
    }
    private static bool ValidRef(string? value, TicketingProvider provider) => value is not null && value.Length <= 200
        && Regex.IsMatch(value, provider == TicketingProvider.Jira ? @"^[A-Za-z][A-Za-z0-9_]*-[0-9]+$" : @"^[a-fA-F0-9]{32}$", RegexOptions.CultureInvariant);
    private static TaskTicketResponse Response(bool configured, bool canManage, TaskTicketLink? link) => new(configured, canManage,
        link is null ? null : new(link.Id, link.Provider.ToString(), link.ExternalRef,
            link.ExternalRef is null ? null : link.Provider == TicketingProvider.Jira
                ? $"{link.BaseUrl.TrimEnd('/')}/browse/{Uri.EscapeDataString(link.ExternalRef)}"
                : $"{link.BaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(link.ProjectKey)}.do?sys_id={Uri.EscapeDataString(link.ExternalRef)}",
            link.ExternalStatus, link.ExternalAssignee, link.LastSuccessfulSyncAt, link.State,
            link.RowVersion, link.CorrelationKey, link.LastError));
}
