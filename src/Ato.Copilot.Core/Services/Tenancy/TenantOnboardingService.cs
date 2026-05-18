using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Services.Tenancy;

/// <summary>
/// EF-backed Tenant-and-Organization onboarding wizard
/// (Feature 048 US4 / FR-054 / FR-056). Each step submission updates a slice
/// of the <see cref="Tenant"/> row, records an
/// <see cref="AuditLogEntry"/> with <c>Action = "TenantOnboarding.&lt;StepName&gt;"</c>
/// (FR-056), and returns the updated <see cref="TenantOnboardingProgress"/>.
/// </summary>
/// <remarks>
/// The service is intentionally provider-agnostic and uses
/// <see cref="IDbContextFactory{TContext}"/> so it remains usable from
/// background services and tests. Audit emission is synchronous (same
/// transaction as the step write) to satisfy FR-056's "every step
/// submission" guarantee.
/// </remarks>
public sealed class TenantOnboardingService : ITenantOnboardingService
{
    /// <summary>Step-name discriminators used both as wizard step ids and as audit-action suffixes.</summary>
    public static class StepNames
    {
        public const string LegalEntity = "Tenant.LegalEntity";
        public const string HqAddress = "Tenant.HqAddress";
        public const string Classification = "Tenant.Classification";
        public const string Ao = "Tenant.Ao";
        public const string PrimaryPoc = "Tenant.PrimaryPoc";
        public const string OrgProfile = "Org.Profile";
        public const string Submitted = "Submitted";
    }

    private static readonly string[] OrderedSteps =
    {
        StepNames.LegalEntity,
        StepNames.HqAddress,
        StepNames.Classification,
        StepNames.Ao,
        StepNames.PrimaryPoc,
        StepNames.OrgProfile,
    };

    private readonly IDbContextFactory<AtoCopilotContext> _contextFactory;
    private readonly ILogger<TenantOnboardingService> _logger;

    public TenantOnboardingService(
        IDbContextFactory<AtoCopilotContext> contextFactory,
        ILogger<TenantOnboardingService> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<TenantOnboardingProgress> GetStateAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct)
            ?? throw new InvalidOperationException($"Tenant {tenantId} not found.");
        return await BuildProgressAsync(db, tenant, ct);
    }

    public async Task<TenantOnboardingProgress> SubmitLegalEntityAsync(
        Guid tenantId, Guid actorUserId, LegalEntityStepRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.LegalEntityName))
            throw new ArgumentException("legalEntityName is required.", nameof(request));

        return await ApplyStepAsync(tenantId, actorUserId, StepNames.LegalEntity,
            request,
            t =>
            {
                t.LegalEntityName = request.LegalEntityName.Trim();
                t.DoDComponent = NullIfBlank(request.DoDComponent);
                if (!string.IsNullOrWhiteSpace(request.TimeZone))
                    t.TimeZone = request.TimeZone.Trim();
            }, ct);
    }

    public async Task<TenantOnboardingProgress> SubmitHqAddressAsync(
        Guid tenantId, Guid actorUserId, HqAddressStepRequest request, CancellationToken ct = default)
    {
        ValidateNonEmpty(request.HqAddressLine1, nameof(request.HqAddressLine1));
        ValidateNonEmpty(request.HqCity, nameof(request.HqCity));
        ValidateNonEmpty(request.HqStateOrProvince, nameof(request.HqStateOrProvince));
        ValidateNonEmpty(request.HqPostalCode, nameof(request.HqPostalCode));
        ValidateNonEmpty(request.HqCountry, nameof(request.HqCountry));

        return await ApplyStepAsync(tenantId, actorUserId, StepNames.HqAddress, request,
            t =>
            {
                t.HqAddressLine1 = request.HqAddressLine1.Trim();
                t.HqAddressLine2 = NullIfBlank(request.HqAddressLine2);
                t.HqCity = request.HqCity.Trim();
                t.HqStateOrProvince = request.HqStateOrProvince.Trim();
                t.HqPostalCode = request.HqPostalCode.Trim();
                t.HqCountry = request.HqCountry.Trim();
            }, ct);
    }

    public async Task<TenantOnboardingProgress> SubmitClassificationAsync(
        Guid tenantId, Guid actorUserId, ClassificationStepRequest request, CancellationToken ct = default)
    {
        if (!Enum.TryParse<ClassificationLevel>(
                request.DefaultClassificationLevel,
                ignoreCase: true,
                out var level))
        {
            throw new ArgumentException(
                $"defaultClassificationLevel must be one of: {string.Join(", ", Enum.GetNames<ClassificationLevel>())}.",
                nameof(request));
        }

        return await ApplyStepAsync(tenantId, actorUserId, StepNames.Classification, request,
            t => t.DefaultClassificationLevel = level, ct);
    }

    public async Task<TenantOnboardingProgress> SubmitAoAsync(
        Guid tenantId, Guid actorUserId, AoStepRequest request, CancellationToken ct = default)
    {
        ValidateNonEmpty(request.AuthorizingOfficialName, nameof(request.AuthorizingOfficialName));
        ValidateNonEmpty(request.AuthorizingOfficialEmail, nameof(request.AuthorizingOfficialEmail));

        return await ApplyStepAsync(tenantId, actorUserId, StepNames.Ao, request,
            t =>
            {
                t.AuthorizingOfficialName = request.AuthorizingOfficialName.Trim();
                t.AuthorizingOfficialEmail = request.AuthorizingOfficialEmail.Trim();
            }, ct);
    }

    public async Task<TenantOnboardingProgress> SubmitPrimaryPocAsync(
        Guid tenantId, Guid actorUserId, PrimaryPocStepRequest request, CancellationToken ct = default)
    {
        ValidateNonEmpty(request.PrimaryPocName, nameof(request.PrimaryPocName));
        ValidateNonEmpty(request.PrimaryPocEmail, nameof(request.PrimaryPocEmail));

        return await ApplyStepAsync(tenantId, actorUserId, StepNames.PrimaryPoc, request,
            t =>
            {
                t.PrimaryPocName = request.PrimaryPocName.Trim();
                t.PrimaryPocEmail = request.PrimaryPocEmail.Trim();
                t.PrimaryPocPhone = NullIfBlank(request.PrimaryPocPhone);
            }, ct);
    }

    public async Task<TenantOnboardingProgress> SubmitOrgProfileAsync(
        Guid tenantId, Guid actorUserId, OrgProfileStepRequest request, CancellationToken ct = default)
    {
        ValidateNonEmpty(request.Name, nameof(request.Name));

        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct)
            ?? throw new InvalidOperationException($"Tenant {tenantId} not found.");

        // Re-entrancy: if Org.Profile was already submitted, look up the
        // first organization for this tenant by step-completion timestamp
        // and update it in place rather than creating a duplicate.
        var existingOrg = await db.Organizations
            .Where(o => o.TenantId == tenantId)
            .OrderBy(o => o.Id)
            .FirstOrDefaultAsync(ct);

        if (existingOrg is null)
        {
            existingOrg = new Organization
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = request.Name.Trim(),
                Description = NullIfBlank(request.Description),
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = actorUserId.ToString(),
            };
            db.Organizations.Add(existingOrg);
        }
        else
        {
            existingOrg.Name = request.Name.Trim();
            existingOrg.Description = NullIfBlank(request.Description);
        }

        if (tenant.OnboardingState == OnboardingState.Pending)
            tenant.OnboardingState = OnboardingState.InWizard;
        tenant.UpdatedAt = DateTimeOffset.UtcNow;
        tenant.UpdatedBy = actorUserId.ToString();

        AppendAudit(db, tenantId, actorUserId, StepNames.OrgProfile, request);
        await db.SaveChangesAsync(ct);
        return await BuildProgressAsync(db, tenant, ct);
    }

    public async Task<TenantOnboardingProgress> SubmitFinalAsync(
        Guid tenantId, Guid actorUserId, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct)
            ?? throw new InvalidOperationException($"Tenant {tenantId} not found.");

        // Verify all required fields are populated.
        var missing = ListMissingRequiredFields(tenant);
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Cannot submit onboarding for tenant {tenantId}: required fields missing — {string.Join(", ", missing)}.");
        }

        // Verify a first organization exists.
        var firstOrg = await db.Organizations
            .Where(o => o.TenantId == tenantId)
            .OrderBy(o => o.Id)
            .FirstOrDefaultAsync(ct);
        if (firstOrg is null)
        {
            throw new InvalidOperationException(
                $"Cannot submit onboarding for tenant {tenantId}: no Organization row has been created.");
        }

        var wasActive = tenant.OnboardingState == OnboardingState.Active;
        tenant.OnboardingState = OnboardingState.Active;
        tenant.UpdatedAt = DateTimeOffset.UtcNow;
        tenant.UpdatedBy = actorUserId.ToString();

        AppendAudit(db, tenantId, actorUserId, StepNames.Submitted, new { tenantId, firstOrgId = firstOrg.Id });
        await db.SaveChangesAsync(ct);

        if (!wasActive)
        {
            _logger.LogInformation(
                "Tenant onboarding submitted: TenantId={TenantId} ActorUserId={ActorUserId} FirstOrgId={FirstOrgId}",
                tenantId, actorUserId, firstOrg.Id);
        }

        return await BuildProgressAsync(db, tenant, ct);
    }

    // ─── helpers ────────────────────────────────────────────────────────────

    private async Task<TenantOnboardingProgress> ApplyStepAsync<TRequest>(
        Guid tenantId,
        Guid actorUserId,
        string stepName,
        TRequest request,
        Action<Tenant> mutate,
        CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct)
            ?? throw new InvalidOperationException($"Tenant {tenantId} not found.");

        mutate(tenant);
        if (tenant.OnboardingState == OnboardingState.Pending)
            tenant.OnboardingState = OnboardingState.InWizard;
        tenant.UpdatedAt = DateTimeOffset.UtcNow;
        tenant.UpdatedBy = actorUserId.ToString();

        AppendAudit(db, tenantId, actorUserId, stepName, request!);
        await db.SaveChangesAsync(ct);
        return await BuildProgressAsync(db, tenant, ct);
    }

    private async Task<TenantOnboardingProgress> BuildProgressAsync(
        AtoCopilotContext db, Tenant tenant, CancellationToken ct)
    {
        // Re-entrancy: derive the completed-step set from the AuditLogs table.
        // Step names are recorded as "TenantOnboarding.<StepName>" so we filter
        // AuditLogs by the prefix and the home tenant id (FR-056). This keeps
        // the wizard re-entrant without requiring a dedicated state table.
        var prefix = "TenantOnboarding.";
        var actions = await db.AuditLogs
            .AsNoTracking()
            .Where(a => a.TenantId == tenant.Id && a.Action.StartsWith(prefix))
            .Select(a => a.Action)
            .Distinct()
            .ToListAsync(ct);

        var completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in actions)
        {
            var step = a.Substring(prefix.Length);
            if (!string.IsNullOrEmpty(step))
                completed.Add(step);
        }

        var firstOrgId = await db.Organizations
            .Where(o => o.TenantId == tenant.Id)
            .OrderBy(o => o.Id)
            .Select(o => (Guid?)o.Id)
            .FirstOrDefaultAsync(ct);

        var current = tenant.OnboardingState == OnboardingState.Active
            ? StepNames.Submitted
            : OrderedSteps.FirstOrDefault(s => !completed.Contains(s)) ?? StepNames.Submitted;

        return new TenantOnboardingProgress(
            tenant.Id, current,
            OrderedSteps.Where(completed.Contains).ToList(),
            tenant.OnboardingState,
            firstOrgId);
    }

    private static void AppendAudit<TPayload>(
        AtoCopilotContext db, Guid tenantId, Guid actorUserId, string stepName, TPayload payload)
    {
        var entry = new AuditLogEntry
        {
            Id = Guid.NewGuid().ToString(),
            TenantId = tenantId,
            ActorTenantId = tenantId,
            UserId = actorUserId.ToString(),
            UserRole = "Administrator",
            // FR-056: "TenantOnboarding.<StepName>". The Action column is
            // HasMaxLength(50); the longest step name is
            // "TenantOnboarding.Tenant.Classification" (37 chars).
            Action = $"TenantOnboarding.{stepName}",
            Timestamp = DateTime.UtcNow,
            Outcome = AuditOutcome.Success,
            Details = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = false,
            }),
        };
        db.AuditLogs.Add(entry);
    }

    private static List<string> ListMissingRequiredFields(Tenant tenant)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(tenant.LegalEntityName)) missing.Add("legalEntityName");
        if (string.IsNullOrWhiteSpace(tenant.HqAddressLine1)) missing.Add("hqAddressLine1");
        if (string.IsNullOrWhiteSpace(tenant.HqCity)) missing.Add("hqCity");
        if (string.IsNullOrWhiteSpace(tenant.HqStateOrProvince)) missing.Add("hqStateOrProvince");
        if (string.IsNullOrWhiteSpace(tenant.HqPostalCode)) missing.Add("hqPostalCode");
        if (string.IsNullOrWhiteSpace(tenant.HqCountry)) missing.Add("hqCountry");
        if (string.IsNullOrWhiteSpace(tenant.AuthorizingOfficialName)) missing.Add("authorizingOfficialName");
        if (string.IsNullOrWhiteSpace(tenant.AuthorizingOfficialEmail)) missing.Add("authorizingOfficialEmail");
        if (string.IsNullOrWhiteSpace(tenant.PrimaryPocName)) missing.Add("primaryPocName");
        if (string.IsNullOrWhiteSpace(tenant.PrimaryPocEmail)) missing.Add("primaryPocEmail");
        return missing;
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static void ValidateNonEmpty(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{field} is required.", field);
    }
}
