using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Services.Tenancy;

/// <summary>
/// EF-backed service that owns the singleton <see cref="CspProfile"/> row
/// and drives the User Story 7 onboarding wizard
/// (Feature 048 FR-006 / FR-090 / FR-092). Each step submission updates a
/// slice of the row; <see cref="SubmitAsync"/> finalizes onboarding,
/// invalidates the 30 s <see cref="IMemoryCache"/> entry, and emits a
/// structured audit log line per FR-092.
/// </summary>
/// <remarks>
/// The service uses <see cref="IDbContextFactory{TContext}"/> so it is safe
/// to register as a singleton or as a scoped dependency. Reads are served
/// from <see cref="IMemoryCache"/> to avoid a per-request DB hit on the
/// CSP-onboarding gate path; the cache is invalidated whenever the row is
/// mutated. Singleton uniqueness is enforced in code: every mutating method
/// re-queries the first row by <see cref="CspProfile.CreatedAt"/> and
/// upserts in place.
/// </remarks>
public sealed class CspProfileService : ICspProfileService
{
    /// <summary>Cache key used by both reads and invalidations.</summary>
    public const string CacheKey = "csp-profile:singleton";

    /// <summary>30-second TTL — same contract as the per-tenant Status cache.</summary>
    public static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly IDbContextFactory<AtoCopilotContext> _contextFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CspProfileService> _logger;

    public CspProfileService(
        IDbContextFactory<AtoCopilotContext> contextFactory,
        IMemoryCache cache,
        ILogger<CspProfileService> logger)
    {
        _contextFactory = contextFactory;
        _cache = cache;
        _logger = logger;
    }

    public async Task<CspProfile?> GetAsync(CancellationToken ct = default)
    {
        // Cache-aside: serve the latest snapshot for up to 30 s; the cache
        // is invalidated on every write and on SubmitAsync.
        if (_cache.TryGetValue(CacheKey, out CspProfile? cached))
            return cached;

        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var row = await LoadSingletonAsync(db, ct);
        // The MCP host configures IMemoryCache with a SizeLimit; every entry
        // must declare its Size. The CspProfile is a small singleton so a
        // unit-size entry is appropriate.
        _cache.Set(CacheKey, row, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheTtl,
            Size = 1,
        });
        return row;
    }

    public CspOnboardingStep ComputeCurrentStep(CspProfile? profile)
    {
        if (profile is null)
            return CspOnboardingStep.Identity;
        if (profile.OnboardingState == OnboardingState.Active)
            return CspOnboardingStep.Complete;
        if (profile.IdentityCompletedAt is null)
            return CspOnboardingStep.Identity;
        if (profile.SupportCompletedAt is null)
            return CspOnboardingStep.SupportContact;
        if (profile.ClassificationCompletedAt is null)
            return CspOnboardingStep.Classification;
        return CspOnboardingStep.Review;
    }

    public async Task<CspProfile> EnsureCreatedAsync(string actor, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var existing = await LoadSingletonAsync(db, ct);
        if (existing is not null)
        {
            if (existing.OnboardingState == OnboardingState.Active)
                throw new CspAlreadyOnboardedException();
            return existing;
        }

        var now = DateTimeOffset.UtcNow;
        var row = new CspProfile
        {
            Id = Guid.NewGuid(),
            // Required fields receive provisional values; UpdateIdentityAsync
            // overwrites them on the first wizard step. The DB columns are
            // NOT NULL, so we cannot leave them empty.
            LegalEntityName = "Pending",
            DisplayName = "Pending",
            DefaultClassificationFloor = ClassificationLevel.Unclassified,
            OnboardingState = OnboardingState.InWizard,
            CreatedAt = now,
            CreatedBy = actor,
        };
        db.CspProfiles.Add(row);
        await db.SaveChangesAsync(ct);
        InvalidateCache();
        return row;
    }

    public async Task<CspProfile> UpdateIdentityAsync(
        string legalEntityName,
        string displayName,
        string? logoUrl,
        string actor,
        CancellationToken ct = default)
    {
        ValidateText(legalEntityName, nameof(legalEntityName), minLength: 2, maxLength: 256);
        ValidateText(displayName, nameof(displayName), minLength: 1, maxLength: 64);
        if (logoUrl is { Length: > 0 } && !Uri.TryCreate(logoUrl, UriKind.Absolute, out _))
            throw new ArgumentException("logoUrl must be an absolute URL.", nameof(logoUrl));

        return await ApplyAsync(actor, ct, row =>
        {
            row.LegalEntityName = legalEntityName.Trim();
            row.DisplayName = displayName.Trim();
            row.LogoUrl = string.IsNullOrWhiteSpace(logoUrl) ? null : logoUrl.Trim();
            row.IdentityCompletedAt ??= DateTimeOffset.UtcNow;
        });
    }

    public async Task<CspProfile> UpdateSupportAsync(
        string primarySupportEmail,
        string? supportPhone,
        string actor,
        CancellationToken ct = default)
    {
        ValidateText(primarySupportEmail, nameof(primarySupportEmail), minLength: 3, maxLength: 254);
        if (!primarySupportEmail.Contains('@'))
            throw new ArgumentException("primarySupportEmail must be a valid email.", nameof(primarySupportEmail));
        if (supportPhone is { Length: > 40 })
            throw new ArgumentException("supportPhone must be ≤ 40 characters.", nameof(supportPhone));

        return await ApplyAsync(actor, ct, row =>
        {
            row.PrimarySupportEmail = primarySupportEmail.Trim();
            row.SupportPhone = string.IsNullOrWhiteSpace(supportPhone) ? null : supportPhone.Trim();
            row.SupportCompletedAt ??= DateTimeOffset.UtcNow;
        });
    }

    public async Task<CspProfile> UpdateClassificationAsync(
        ClassificationLevel defaultClassificationFloor,
        string actor,
        CancellationToken ct = default)
    {
        if (!Enum.IsDefined(defaultClassificationFloor))
            throw new ArgumentException("defaultClassificationFloor is not a valid value.", nameof(defaultClassificationFloor));

        return await ApplyAsync(actor, ct, row =>
        {
            row.DefaultClassificationFloor = defaultClassificationFloor;
            row.ClassificationCompletedAt ??= DateTimeOffset.UtcNow;
        });
    }

    public async Task<CspProfile> SubmitAsync(string actor, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var row = await LoadSingletonAsync(db, ct)
            ?? throw new CspOnboardingIncompleteException(new[]
            {
                "Identity", "SupportContact", "Classification",
            });

        if (row.OnboardingState == OnboardingState.Active)
            throw new CspAlreadyOnboardedException();

        var missing = ListMissingRequiredFields(row);
        if (missing.Count > 0)
            throw new CspOnboardingIncompleteException(missing);

        var now = DateTimeOffset.UtcNow;
        row.OnboardingState = OnboardingState.Active;
        row.OnboardingCompletedAt = now;
        row.UpdatedAt = now;
        row.UpdatedBy = actor;

        await db.SaveChangesAsync(ct);
        InvalidateCache();

        // FR-092 audit emission. We use a structured log event because the
        // EF AuditLogEntry table is [TenantScoped] and CSP onboarding is a
        // global action. The Serilog pipeline is the production audit sink.
        _logger.LogInformation(
            "CspOnboarding.Complete cspProfileId={CspProfileId} legalEntityName={LegalEntityName} displayName={DisplayName} actor={Actor} completedAt={CompletedAt}",
            row.Id, row.LegalEntityName, row.DisplayName, actor, now);

        return row;
    }

    // ─── helpers ────────────────────────────────────────────────────────────

    private async Task<CspProfile> ApplyAsync(string actor, CancellationToken ct, Action<CspProfile> mutate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var row = await LoadSingletonAsync(db, ct);
        var isNew = row is null;
        if (row is null)
        {
            var now = DateTimeOffset.UtcNow;
            row = new CspProfile
            {
                Id = Guid.NewGuid(),
                LegalEntityName = "Pending",
                DisplayName = "Pending",
                DefaultClassificationFloor = ClassificationLevel.Unclassified,
                OnboardingState = OnboardingState.InWizard,
                CreatedAt = now,
                CreatedBy = actor,
            };
            db.CspProfiles.Add(row);
        }
        else if (row.OnboardingState == OnboardingState.Active)
        {
            throw new CspAlreadyOnboardedException();
        }

        mutate(row);
        if (row.OnboardingState == OnboardingState.Pending)
            row.OnboardingState = OnboardingState.InWizard;
        if (!isNew)
        {
            row.UpdatedAt = DateTimeOffset.UtcNow;
            row.UpdatedBy = actor;
        }

        await db.SaveChangesAsync(ct);
        InvalidateCache();
        return row;
    }

    private static Task<CspProfile?> LoadSingletonAsync(AtoCopilotContext db, CancellationToken ct) =>
        // SQLite does not translate ORDER BY on DateTimeOffset; the row is a
        // singleton so any deterministic key works. Order by Id (Guid → BLOB
        // in SQLite, TEXT in SQL Server) for stable behavior across providers.
        db.CspProfiles
            .OrderBy(p => p.Id)
            .FirstOrDefaultAsync(ct);

    private void InvalidateCache() => _cache.Remove(CacheKey);

    private static void ValidateText(string value, string field, int minLength, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{field} is required.", field);
        var trimmed = value.Trim();
        if (trimmed.Length < minLength)
            throw new ArgumentException($"{field} must be ≥ {minLength} characters.", field);
        if (trimmed.Length > maxLength)
            throw new ArgumentException($"{field} must be ≤ {maxLength} characters.", field);
    }

    private static List<string> ListMissingRequiredFields(CspProfile row)
    {
        var missing = new List<string>();
        if (row.IdentityCompletedAt is null)
            missing.Add("Identity");
        if (row.SupportCompletedAt is null)
            missing.Add("SupportContact");
        // Classification has a non-null default (Unclassified). The wizard
        // marks ClassificationCompletedAt only after an explicit POST so the
        // actor has confirmed the floor; Submit therefore requires it.
        if (row.ClassificationCompletedAt is null)
            missing.Add("Classification");
        return missing;
    }
}
