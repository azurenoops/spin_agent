using System.Net.Mail;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Onboarding;

namespace Ato.Copilot.Agents.Compliance.Services.Onboarding;

/// <summary>
/// EF-backed implementation of <see cref="IOrganizationContextService"/> (FR-010..FR-014).
/// Singleton row per <see cref="OrganizationContext.TenantId"/>.
/// </summary>
public class OrganizationContextService : IOrganizationContextService
{
    private readonly IDbContextFactory<AtoCopilotContext> _contextFactory;
    private readonly IWizardAuditService _audit;
    private readonly ILogger<OrganizationContextService> _logger;

    public OrganizationContextService(
        IDbContextFactory<AtoCopilotContext> contextFactory,
        IWizardAuditService audit,
        ILogger<OrganizationContextService> logger)
    {
        _contextFactory = contextFactory;
        _audit = audit;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OrganizationContext?> GetAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        return await db.OrganizationContexts
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);
    }

    /// <inheritdoc />
    public async Task<OrganizationContext> UpsertAsync(
        Guid tenantId,
        OrganizationContextInput input,
        Guid actorUserId,
        Guid correlationId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        Validate(input);

        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var existing = await db.OrganizationContexts
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);

        var beforeJson = existing is null ? null : JsonSerializer.Serialize(Project(existing));

        var now = DateTimeOffset.UtcNow;
        if (existing is null)
        {
            existing = new OrganizationContext
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                OrganizationName = input.OrganizationName.Trim(),
                Branch = input.Branch,
                BranchQualifier = NullIfBlank(input.BranchQualifier),
                SubOrganization = NullIfBlank(input.SubOrganization),
                ClassificationPosture = input.ClassificationPosture,
                AuthoritativeRepositoryUrl = NullIfBlank(input.AuthoritativeRepositoryUrl),
                PrimaryPocEmail = NullIfBlank(input.PrimaryPocEmail),
                CreatedAt = now,
                CreatedBy = actorUserId,
                UpdatedAt = now,
                UpdatedBy = actorUserId,
            };
            db.OrganizationContexts.Add(existing);
        }
        else
        {
            existing.OrganizationName = input.OrganizationName.Trim();
            existing.Branch = input.Branch;
            existing.BranchQualifier = NullIfBlank(input.BranchQualifier);
            existing.SubOrganization = NullIfBlank(input.SubOrganization);
            existing.ClassificationPosture = input.ClassificationPosture;
            existing.AuthoritativeRepositoryUrl = NullIfBlank(input.AuthoritativeRepositoryUrl);
            existing.PrimaryPocEmail = NullIfBlank(input.PrimaryPocEmail);
            existing.UpdatedAt = now;
            existing.UpdatedBy = actorUserId;
        }

        await db.SaveChangesAsync(ct);

        var afterJson = JsonSerializer.Serialize(Project(existing));
        await _audit.RecordAsync(
            tenantId,
            actorUserId,
            WizardAuditAction.OrganizationContextSaved,
            resourceType: nameof(OrganizationContext),
            resourceId: existing.Id,
            beforeJson: beforeJson,
            afterJson: afterJson,
            effectsJson: null,
            correlationId: correlationId,
            ct: ct);

        using (_logger.BeginScope(new Dictionary<string, object?>
        {
            ["tenantId"] = tenantId,
            ["actorUserId"] = actorUserId,
            ["correlationId"] = correlationId,
        }))
        {
            _logger.LogInformation(
                "Organization context saved for tenant {TenantId} (branch {Branch}).",
                tenantId,
                existing.Branch);
        }

        return existing;
    }

    private static void Validate(OrganizationContextInput input)
    {
        if (string.IsNullOrWhiteSpace(input.OrganizationName))
        {
            throw new ArgumentException(
                "Organization name is required.",
                nameof(input.OrganizationName));
        }
        if (input.OrganizationName.Length > 256)
        {
            throw new ArgumentException(
                "Organization name must be 256 characters or fewer.",
                nameof(input.OrganizationName));
        }

        // FR-013: BranchQualifier is required when Branch == IndustryPartnerOther.
        if (input.Branch == BranchAffiliation.IndustryPartnerOther
            && string.IsNullOrWhiteSpace(input.BranchQualifier))
        {
            throw new ArgumentException(
                "Branch qualifier is required when branch is 'IndustryPartnerOther'.",
                nameof(input.BranchQualifier));
        }
        if (!string.IsNullOrEmpty(input.BranchQualifier) && input.BranchQualifier.Length > 128)
        {
            throw new ArgumentException(
                "Branch qualifier must be 128 characters or fewer.",
                nameof(input.BranchQualifier));
        }
        if (!string.IsNullOrEmpty(input.SubOrganization) && input.SubOrganization.Length > 256)
        {
            throw new ArgumentException(
                "Sub-organization must be 256 characters or fewer.",
                nameof(input.SubOrganization));
        }

        if (!string.IsNullOrWhiteSpace(input.AuthoritativeRepositoryUrl))
        {
            if (!Uri.TryCreate(input.AuthoritativeRepositoryUrl, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException(
                    "Authoritative repository URL must be an absolute http(s) URL.",
                    nameof(input.AuthoritativeRepositoryUrl));
            }
        }

        if (!string.IsNullOrWhiteSpace(input.PrimaryPocEmail))
        {
            try
            {
                _ = new MailAddress(input.PrimaryPocEmail);
            }
            catch (FormatException)
            {
                throw new ArgumentException(
                    "Primary POC email is not a valid RFC-5322 address.",
                    nameof(input.PrimaryPocEmail));
            }
        }
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static object Project(OrganizationContext c) => new
    {
        c.OrganizationName,
        Branch = c.Branch.ToString(),
        c.BranchQualifier,
        c.SubOrganization,
        ClassificationPosture = c.ClassificationPosture?.ToString(),
        c.AuthoritativeRepositoryUrl,
        c.PrimaryPocEmail,
    };
}
