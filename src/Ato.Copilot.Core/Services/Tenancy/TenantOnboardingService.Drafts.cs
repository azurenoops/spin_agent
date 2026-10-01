using System.Text;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Services.Tenancy;

public sealed partial class TenantOnboardingService
{
    private static readonly JsonSerializerOptions DraftJson = new(JsonSerializerDefaults.Web);

    private static void CheckTenantRevision(Tenant tenant, long? revision)
    {
        if (revision.HasValue && revision != tenant.OnboardingDraftRevision)
            throw new DbUpdateConcurrencyException("Tenant setup changed. Reload the saved values before applying your edits.");
    }

    public async Task<TenantOnboardingProgress> SaveDraftAsync(Guid tenantId, Guid actorUserId,
        SaveTenantDraftRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request.Values);
        if (request.SchemaVersion != 1 || request.ExpectedRevision < 0
            || !OrderedSteps.Append(StepNames.Submitted).Contains(request.CurrentStep))
            throw new ArgumentException("Unsupported draft schema, revision or step.");
        var json = JsonSerializer.Serialize(request.Values, DraftJson);
        if (Encoding.UTF8.GetByteCount(json) > 32768) throw new ArgumentException("Draft exceeds 32 KiB.");
        ValidateDraftLengths(request.Values);
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var tenant = await db.Tenants.SingleOrDefaultAsync(x => x.Id == tenantId, ct)
            ?? throw new KeyNotFoundException("Tenant was not found.");
        CheckTenantRevision(tenant, request.ExpectedRevision);
        if (request.Values.OrgProfile?.FirstOrganizationId is { } first
            && !await db.Organizations.AnyAsync(x => x.Id == first && x.TenantId == tenantId, ct))
            throw new ArgumentException("The selected organizational profile is not in this tenant.");
        tenant.OnboardingDraftJson = json;
        tenant.OnboardingDraftRevision++;
        tenant.OnboardingDraftStep = request.CurrentStep;
        tenant.UpdatedAt = DateTimeOffset.UtcNow;
        tenant.UpdatedBy = actorUserId.ToString();
        AppendAudit(db, tenantId, actorUserId, "DraftSaved", new { revision = tenant.OnboardingDraftRevision, step = request.CurrentStep });
        await db.SaveChangesAsync(ct);
        return await BuildProgressAsync(db, tenant, ct);
    }

    public async Task<TenantOnboardingProgress> DiscardDraftAsync(Guid tenantId, Guid actorUserId,
        long expectedRevision, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var tenant = await db.Tenants.SingleOrDefaultAsync(x => x.Id == tenantId, ct)
            ?? throw new KeyNotFoundException("Tenant was not found.");
        CheckTenantRevision(tenant, expectedRevision);
        tenant.OnboardingDraftJson = null;
        tenant.OnboardingDraftStep = null;
        tenant.OnboardingDraftRevision++;
        tenant.UpdatedAt = DateTimeOffset.UtcNow;
        tenant.UpdatedBy = actorUserId.ToString();
        AppendAudit(db, tenantId, actorUserId, "DraftDiscarded", new { revision = tenant.OnboardingDraftRevision });
        await db.SaveChangesAsync(ct);
        return await BuildProgressAsync(db, tenant, ct);
    }

    private static TenantDraftState? ReadTenantDraft(Tenant tenant)
        => tenant.OnboardingDraftJson is null ? null : new(tenant.OnboardingDraftSchemaVersion,
            tenant.OnboardingDraftRevision, tenant.OnboardingDraftStep ?? StepNames.LegalEntity,
            JsonSerializer.Deserialize<TenantDraftValues>(tenant.OnboardingDraftJson, DraftJson)
                ?? throw new InvalidOperationException("Persisted tenant draft is invalid."), tenant.UpdatedAt);

    private static TenantDraftValues TenantValues(Tenant tenant) => new(
        new(tenant.LegalEntityName ?? "", tenant.DoDComponent, tenant.TimeZone),
        new(tenant.HqAddressLine1 ?? "", tenant.HqAddressLine2, tenant.HqCity ?? "", tenant.HqStateOrProvince ?? "", tenant.HqPostalCode ?? "", tenant.HqCountry ?? ""),
        new(tenant.DefaultClassificationLevel.ToString()),
        new(tenant.AuthorizingOfficialName ?? "", tenant.AuthorizingOfficialEmail ?? ""),
        new(tenant.PrimaryPocName ?? "", tenant.PrimaryPocEmail ?? "", tenant.PrimaryPocPhone));

    private static async Task<TenantDraftValues> SubmittedValuesAsync(AtoCopilotContext db, Tenant tenant, CancellationToken ct)
    {
        var first = await db.Organizations.AsNoTracking().Where(x => x.TenantId == tenant.Id
            && (!tenant.OnboardingFirstOrganizationId.HasValue || x.Id == tenant.OnboardingFirstOrganizationId))
            .OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        return TenantValues(tenant) with { OrgProfile = first is null ? new("", null) : new(first.Name, first.Description, first.Id) };
    }

    private static void ApplyDraftSlice<T>(Tenant tenant, string step, T request)
    {
        tenant.OnboardingDraftRevision++;
        var draft = ReadTenantDraft(tenant);
        if (draft is null) return;
        var applied = TenantValues(tenant);
        var values = step switch
        {
            StepNames.LegalEntity => draft.Values with { LegalEntity = applied.LegalEntity },
            StepNames.HqAddress => draft.Values with { HqAddress = applied.HqAddress },
            StepNames.Classification => draft.Values with { Classification = applied.Classification },
            StepNames.Ao => draft.Values with { Ao = applied.Ao },
            StepNames.PrimaryPoc => draft.Values with { PrimaryPoc = applied.PrimaryPoc },
            StepNames.OrgProfile => draft.Values with { OrgProfile = request as OrgProfileStepRequest },
            _ => throw new ArgumentException("Unsupported tenant step.")
        };
        tenant.OnboardingDraftJson = JsonSerializer.Serialize(values, DraftJson);
    }

    private static bool HasUnappliedDraft(Tenant tenant, TenantDraftValues submitted)
    {
        var draft = ReadTenantDraft(tenant)?.Values;
        return draft is not null && (draft.LegalEntity is not null && draft.LegalEntity != submitted.LegalEntity
            || draft.HqAddress is not null && draft.HqAddress != submitted.HqAddress
            || draft.Classification is not null && draft.Classification != submitted.Classification
            || draft.Ao is not null && draft.Ao != submitted.Ao
            || draft.PrimaryPoc is not null && draft.PrimaryPoc != submitted.PrimaryPoc
            || draft.OrgProfile is not null && draft.OrgProfile != submitted.OrgProfile);
    }

    private static void ValidateDraftLengths(TenantDraftValues values)
    {
        static void Limit(string? text, int length)
        {
            if (text?.Length > length) throw new ArgumentException($"Draft field exceeds {length} characters.");
        }
        Limit(values.LegalEntity?.LegalEntityName, 300); Limit(values.LegalEntity?.DoDComponent, 120); Limit(values.LegalEntity?.TimeZone, 64);
        Limit(values.HqAddress?.HqAddressLine1, 200); Limit(values.HqAddress?.HqAddressLine2, 200);
        Limit(values.HqAddress?.HqCity, 120); Limit(values.HqAddress?.HqStateOrProvince, 120);
        Limit(values.HqAddress?.HqPostalCode, 20); Limit(values.HqAddress?.HqCountry, 80);
        Limit(values.Classification?.DefaultClassificationLevel, 32);
        Limit(values.Ao?.AuthorizingOfficialName, 200); Limit(values.Ao?.AuthorizingOfficialEmail, 254);
        Limit(values.PrimaryPoc?.PrimaryPocName, 200); Limit(values.PrimaryPoc?.PrimaryPocEmail, 254); Limit(values.PrimaryPoc?.PrimaryPocPhone, 40);
        Limit(values.OrgProfile?.Name, 200); Limit(values.OrgProfile?.Description, 2000);
    }
}
