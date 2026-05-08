using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Agents.Compliance.Services.Onboarding;

/// <summary>
/// EF-backed wizard state service (FR-006/FR-007/FR-008/FR-063). Emits structured
/// <c>wizard.step_completed</c> / <c>wizard.step_skipped</c> Serilog events with
/// <c>tenantId</c>, <c>actorUserId</c>, <c>stepName</c>, and <c>durationMs</c> so the
/// SC-001 analytics dashboards can compute per-step distributions.
/// </summary>
public class OnboardingStateService : IOnboardingStateService
{
    private static readonly HashSet<string> KnownSteps = new(StringComparer.OrdinalIgnoreCase)
    {
        // Feature 047 wizard steps.
        "OrganizationContext",
        "Roles",
        "Emass",
        "SspPdf",
        "AzureSubscriptions",
        "Templates",
        "NarrativeSeeds",
        // Feature 048 (T092 / US4) tenant-and-organization onboarding steps.
        // These extend the same step machine so step completions are recorded
        // alongside the Feature 047 steps in OnboardingStepCompletion. The
        // Tenant.* steps are submitted via /api/onboarding/tenant/* endpoints
        // (TenantOnboardingEndpoints, T093) and the Org.Profile step creates
        // the first Organization row before transitioning Tenants.OnboardingState
        // to Active per FR-054.
        "Tenant.LegalEntity",
        "Tenant.HqAddress",
        "Tenant.Classification",
        "Tenant.Ao",
        "Tenant.PrimaryPoc",
        "Org.Profile",
    };

    private readonly IDbContextFactory<AtoCopilotContext> _contextFactory;
    private readonly IBootstrapAdministratorService _bootstrap;
    private readonly IWizardAuditService _audit;
    private readonly ILogger<OnboardingStateService> _logger;

    public OnboardingStateService(
        IDbContextFactory<AtoCopilotContext> contextFactory,
        IBootstrapAdministratorService bootstrap,
        IWizardAuditService audit,
        ILogger<OnboardingStateService> logger)
    {
        _contextFactory = contextFactory;
        _bootstrap = bootstrap;
        _audit = audit;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<TenantOnboardingState> GetAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var state = await db.TenantOnboardingStates
            .Include(s => s.StepCompletions)
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        if (state is null)
        {
            state = new TenantOnboardingState
            {
                TenantId = tenantId,
                Status = TenantOnboardingStatus.NotStarted,
            };
            db.TenantOnboardingStates.Add(state);
            await db.SaveChangesAsync(ct);
        }
        return state;
    }

    /// <inheritdoc />
    public async Task<TenantOnboardingState> StartAsync(
        Guid tenantId,
        Guid actorUserId,
        string? actorDisplayName,
        string? actorEmail,
        Guid correlationId,
        CancellationToken ct = default)
    {
        // Bootstrap admin grant (no-op if an admin already exists).
        await _bootstrap.GrantAsync(
            tenantId,
            actorUserId,
            actorDisplayName,
            actorEmail,
            correlationId,
            ct);

        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var state = await db.TenantOnboardingStates
            .Include(s => s.StepCompletions)
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        if (state is null)
        {
            state = new TenantOnboardingState
            {
                TenantId = tenantId,
                Status = TenantOnboardingStatus.InProgress,
                OnboardingStartedAt = DateTimeOffset.UtcNow,
                CreatedBy = actorUserId,
                UpdatedBy = actorUserId,
            };
            db.TenantOnboardingStates.Add(state);
        }
        else
        {
            if (state.Status == TenantOnboardingStatus.NotStarted)
            {
                state.Status = TenantOnboardingStatus.InProgress;
                state.OnboardingStartedAt = DateTimeOffset.UtcNow;
            }
            else if (state.Status == TenantOnboardingStatus.Completed)
            {
                state.Status = TenantOnboardingStatus.ReRunInProgress;
                state.LastReRunAt = DateTimeOffset.UtcNow;
            }
            state.UpdatedAt = DateTimeOffset.UtcNow;
            state.UpdatedBy = actorUserId;
        }
        await db.SaveChangesAsync(ct);
        return state;
    }

    /// <inheritdoc />
    public Task MarkStepSkippedAsync(
        Guid tenantId,
        string stepName,
        long durationMs,
        Guid actorUserId,
        Guid correlationId,
        CancellationToken ct = default)
        => MarkStepInternalAsync(
            tenantId, stepName, durationMs, actorUserId, correlationId,
            OnboardingStepStatus.Skipped,
            "wizard.step_skipped", ct);

    /// <inheritdoc />
    public Task MarkStepCompletedAsync(
        Guid tenantId,
        string stepName,
        long durationMs,
        Guid actorUserId,
        Guid correlationId,
        CancellationToken ct = default)
        => MarkStepInternalAsync(
            tenantId, stepName, durationMs, actorUserId, correlationId,
            OnboardingStepStatus.Completed,
            "wizard.step_completed", ct);

    private async Task MarkStepInternalAsync(
        Guid tenantId,
        string stepName,
        long durationMs,
        Guid actorUserId,
        Guid correlationId,
        OnboardingStepStatus status,
        string eventName,
        CancellationToken ct)
    {
        if (!KnownSteps.Contains(stepName))
            throw new ArgumentException($"Unknown wizard step: {stepName}", nameof(stepName));

        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var state = await db.TenantOnboardingStates
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException(
                $"Onboarding has not started for tenant {tenantId}");

        var stepNumber = StepNumber(stepName);

        var existing = await db.OnboardingStepCompletions
            .FirstOrDefaultAsync(
                c => c.TenantOnboardingStateId == state.Id && c.StepName == stepName,
                ct);
        if (existing is null)
        {
            existing = new OnboardingStepCompletion
            {
                TenantOnboardingStateId = state.Id,
                StepName = stepName,
                ActorUserId = actorUserId,
            };
            db.OnboardingStepCompletions.Add(existing);
        }
        existing.Status = status;
        existing.DurationMs = durationMs;
        existing.CompletedAt = DateTimeOffset.UtcNow;

        state.LastStep = stepName;
        state.UpdatedAt = DateTimeOffset.UtcNow;
        state.UpdatedBy = actorUserId;
        if (state.Status == TenantOnboardingStatus.NotStarted)
            state.Status = TenantOnboardingStatus.InProgress;

        await db.SaveChangesAsync(ct);

        // FR-063 / SC-001 structured event.
        using (_logger.BeginScope(new Dictionary<string, object?>
        {
            ["tenantId"] = tenantId,
            ["actorUserId"] = actorUserId,
            ["stepName"] = stepName,
            ["stepNumber"] = stepNumber,
            ["durationMs"] = durationMs,
            ["correlationId"] = correlationId,
        }))
        {
            _logger.LogInformation(
                "{Event} step={StepName} step_number={StepNumber} duration_ms={DurationMs}",
                eventName, stepName, stepNumber, durationMs);
        }
    }

    /// <inheritdoc />
    public async Task CompleteOnboardingAsync(
        Guid tenantId,
        Guid actorUserId,
        Guid correlationId,
        CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var state = await db.TenantOnboardingStates
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException(
                $"Onboarding has not started for tenant {tenantId}");

        state.Status = TenantOnboardingStatus.Completed;
        state.OnboardingCompletedAt = DateTimeOffset.UtcNow;
        state.UpdatedAt = DateTimeOffset.UtcNow;
        state.UpdatedBy = actorUserId;
        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "wizard.completed tenantId={TenantId} actorUserId={ActorUserId}",
            tenantId, actorUserId);
    }

    private static int StepNumber(string stepName) => stepName switch
    {
        "OrganizationContext" => 1,
        "Roles" => 2,
        "Emass" => 3,
        "SspPdf" => 4,
        "AzureSubscriptions" => 5,
        "Templates" => 6,
        "NarrativeSeeds" => 7,
        // Feature 048 (T092 / US4) tenant onboarding steps occupy a separate
        // numbering scheme (101..106) so analytics can distinguish them from
        // Feature 047's 7-step wizard but keep them in the same event stream.
        "Tenant.LegalEntity" => 101,
        "Tenant.HqAddress" => 102,
        "Tenant.Classification" => 103,
        "Tenant.Ao" => 104,
        "Tenant.PrimaryPoc" => 105,
        "Org.Profile" => 106,
        _ => 0,
    };
}
