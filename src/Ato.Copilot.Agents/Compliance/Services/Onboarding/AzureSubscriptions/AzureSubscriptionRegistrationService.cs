using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Agents.Compliance.Services.Onboarding.AzureSubscriptions;

/// <summary>
/// Manages the tenant's <see cref="AzureSubscriptionRegistration"/> rows
/// (FR-072..FR-077). Replace-set semantics with Unavailable-flag preservation.
/// </summary>
public sealed class AzureSubscriptionRegistrationService : IAzureSubscriptionRegistrationService
{
    private readonly IDbContextFactory<AtoCopilotContext> _factory;
    private readonly IWizardAuditService _audit;
    private readonly IOptions<OnboardingOptions> _options;
    private readonly ILogger<AzureSubscriptionRegistrationService> _logger;

    public AzureSubscriptionRegistrationService(
        IDbContextFactory<AtoCopilotContext> factory,
        IWizardAuditService audit,
        IOptions<OnboardingOptions> options,
        ILogger<AzureSubscriptionRegistrationService> logger)
    {
        _factory = factory;
        _audit = audit;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AzureSubscriptionRegistration>> ListAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.AzureSubscriptionRegistrations
            .Where(s => s.TenantId == tenantId)
            .OrderBy(s => s.DisplayName)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AzureSubscriptionRegistration>> ReplaceAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> selectedSubscriptionIds,
        IReadOnlyList<AzureSubscriptionInfo> visibleSubscriptions,
        Guid actorUserId,
        CancellationToken ct = default)
    {
        var max = _options.Value.Limits.MaxAzureSubscriptionsPerTenant;
        if (selectedSubscriptionIds.Count > max)
        {
            throw new InvalidOperationException(
                $"Cannot register {selectedSubscriptionIds.Count} subscriptions; tenant limit is {max}.");
        }

        var visibleById = visibleSubscriptions.ToDictionary(v => v.SubscriptionId);

        await using var db = await _factory.CreateDbContextAsync(ct);
        var existing = await db.AzureSubscriptionRegistrations
            .Where(s => s.TenantId == tenantId)
            .ToListAsync(ct);
        var existingById = existing.ToDictionary(e => e.SubscriptionId);

        // Reject any *new* (not previously registered) selectedId that isn't
        // currently visible to the user. Pre-existing rows are allowed to
        // remain selected even when invisible — they're flagged Unavailable
        // (FR-074) below.
        var invalid = selectedSubscriptionIds
            .Where(id => !visibleById.ContainsKey(id) && !existingById.ContainsKey(id))
            .ToList();
        if (invalid.Count > 0)
        {
            throw new InvalidOperationException(
                $"Selected subscriptions not visible to user: {string.Join(", ", invalid)}.");
        }

        var now = DateTimeOffset.UtcNow;

        // Upsert + flag.
        foreach (var subId in selectedSubscriptionIds)
        {
            if (existingById.TryGetValue(subId, out var row))
            {
                row.Status = SubscriptionStatus.Selected;
                row.LastSeenVisibleAt = now;
                row.UpdatedAt = now;
                row.UpdatedBy = actorUserId;
                if (visibleById.TryGetValue(subId, out var info))
                {
                    row.DisplayName = info.DisplayName;
                    row.ParentTenantId = info.ParentTenantId;
                    row.Environment = info.Environment;
                }
            }
            else
            {
                var info = visibleById[subId];
                db.AzureSubscriptionRegistrations.Add(new AzureSubscriptionRegistration
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    SubscriptionId = subId,
                    DisplayName = info.DisplayName,
                    ParentTenantId = info.ParentTenantId,
                    Environment = info.Environment,
                    Status = SubscriptionStatus.Selected,
                    LastSeenVisibleAt = now,
                    CreatedAt = now,
                    CreatedBy = actorUserId,
                    UpdatedAt = now,
                    UpdatedBy = actorUserId,
                });
            }
        }

        // Existing rows not present in the new selection: per FR-074 we preserve
        // them but flag any that the admin can no longer see as Unavailable.
        // The contract reads "subscriptions removed from the request are hard-removed",
        // so we hard-remove rows the admin explicitly dropped (still visible),
        // and flag Unavailable any selected row that vanished from visibility.
        foreach (var row in existing)
        {
            var stillSelected = selectedSubscriptionIds.Contains(row.SubscriptionId);
            var stillVisible = visibleById.ContainsKey(row.SubscriptionId);
            if (!stillSelected && stillVisible)
            {
                db.AzureSubscriptionRegistrations.Remove(row);
            }
            else if (stillSelected && !stillVisible)
            {
                row.Status = SubscriptionStatus.Unavailable;
                row.UpdatedAt = now;
                row.UpdatedBy = actorUserId;
            }
        }

        await db.SaveChangesAsync(ct);

        await _audit.RecordAsync(
            tenantId, actorUserId, WizardAuditAction.SubscriptionsSelected,
            "AzureSubscriptionRegistration", null,
            beforeJson: null,
            afterJson: System.Text.Json.JsonSerializer.Serialize(selectedSubscriptionIds),
            effectsJson: null,
            correlationId: Guid.NewGuid(), ct);

        return await db.AzureSubscriptionRegistrations
            .Where(s => s.TenantId == tenantId)
            .OrderBy(s => s.DisplayName)
            .ToListAsync(ct);
    }

    public async Task RemoveAsync(Guid tenantId, Guid registrationId, Guid actorUserId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.AzureSubscriptionRegistrations
            .FirstOrDefaultAsync(s => s.Id == registrationId && s.TenantId == tenantId, ct);
        if (row is null) return;
        db.AzureSubscriptionRegistrations.Remove(row);
        await db.SaveChangesAsync(ct);
        await _audit.RecordAsync(
            tenantId, actorUserId, WizardAuditAction.SubscriptionRemoved,
            "AzureSubscriptionRegistration", registrationId,
            beforeJson: System.Text.Json.JsonSerializer.Serialize(new { row.SubscriptionId }),
            afterJson: null,
            effectsJson: null,
            correlationId: Guid.NewGuid(), ct);
    }
}

/// <summary>
/// Default <see cref="IAzureSubscriptionScopeResolver"/> backed by the
/// <see cref="AzureSubscriptionRegistration"/> table.
/// </summary>
public sealed class AzureSubscriptionScopeResolver : IAzureSubscriptionScopeResolver
{
    private readonly IDbContextFactory<AtoCopilotContext> _factory;

    public AzureSubscriptionScopeResolver(IDbContextFactory<AtoCopilotContext> factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<Guid>> GetSelectedSubscriptionIdsAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.AzureSubscriptionRegistrations
            .Where(s => s.TenantId == tenantId && s.Status == SubscriptionStatus.Selected)
            .Select(s => s.SubscriptionId)
            .ToListAsync(ct);
    }
}
