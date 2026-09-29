using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Agents.Compliance.Services;

public sealed class ControlValidationLinkService(
    IDbContextFactory<AtoCopilotContext> contextFactory,
    ITenantContextAccessor tenantAccessor) : IControlValidationLinkService
{
    public async Task<IReadOnlyList<ControlValidationLink>> GetLinksAsync(
        string systemId,
        string controlId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var tenantId = GetTenantId();
        var implementation = await FindImplementationAsync(context, tenantId, systemId, controlId, cancellationToken);
        if (implementation is null)
            throw new ControlImplementationNotFoundException(systemId, controlId);

        return await context.ControlValidationLinks
            .Where(link => link.TenantId == tenantId
                && link.ControlImplementationId == implementation.Id)
            .OrderByDescending(link => link.AddedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public Task<ControlValidationLink> AddEvidenceLinkAsync(string systemId, string controlId, string evidenceId,
        string expectedHash, string addedBy, CancellationToken cancellationToken = default) =>
        AddLinkCoreAsync(systemId, controlId, ControlValidationLinkType.EvidenceArtifact,
            evidenceId, null, addedBy, expectedHash, cancellationToken);

    public Task<ControlValidationLink> AddLinkAsync(
        string systemId,
        string controlId,
        ControlValidationLinkType linkType,
        string linkTarget,
        string? description,
        string addedBy,
        CancellationToken cancellationToken = default) =>
        AddLinkCoreAsync(systemId, controlId, linkType, linkTarget, description, addedBy, null, cancellationToken);

    private async Task<ControlValidationLink> AddLinkCoreAsync(string systemId, string controlId,
        ControlValidationLinkType linkType, string linkTarget, string? description, string addedBy,
        string? expectedHash, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var tenantId = GetTenantId();
        var target = RequireTarget(linkTarget);
        var implementation = await FindImplementationAsync(context, tenantId, systemId, controlId, cancellationToken)
            ?? throw new ControlImplementationNotFoundException(systemId, controlId);

        // Validate the linked artifact, not merely the parent control. Touch its concurrency token
        // in the same SaveChanges transaction so replacement/deletion cannot race link creation.
        if (linkType == ControlValidationLinkType.EvidenceArtifact)
        {
            var artifactId = target.StartsWith("evidence://", StringComparison.Ordinal) ? target["evidence://".Length..]
                : target.StartsWith("artifact:", StringComparison.Ordinal) ? target["artifact:".Length..] : target;
            var artifact = await context.EvidenceArtifacts.SingleOrDefaultAsync(a =>
                a.Id == artifactId && a.RegisteredSystemId == systemId && a.TenantId == tenantId && !a.IsDeleted, cancellationToken)
                ?? throw new KeyNotFoundException("Evidence not found in this system.");
            if (expectedHash is not null && artifact.ContentHash != expectedHash)
                throw new DbUpdateConcurrencyException("Evidence changed. Reload before linking.");
            target = artifact.Id;
            context.Entry(artifact).Property(a => a.ContentHash).IsModified = true;
        }

        var exists = await context.ControlValidationLinks.AnyAsync(
            link => link.TenantId == tenantId
                && link.ControlImplementationId == implementation.Id
                && (link.LinkTarget == target || (linkType == ControlValidationLinkType.EvidenceArtifact
                    && (link.LinkTarget == "evidence://" + target || link.LinkTarget == "artifact:" + target))),
            cancellationToken);
        if (exists)
            throw new DuplicateControlValidationLinkException(target);

        var link = new ControlValidationLink
        {
            TenantId = tenantId,
            ControlImplementationId = implementation.Id,
            LinkType = linkType,
            LinkTarget = target,
            Description = description?.Trim(),
            AddedBy = addedBy,
            AddedAt = DateTime.UtcNow,
        };
        context.ControlValidationLinks.Add(link);
        await context.SaveChangesAsync(cancellationToken);
        return link;
    }

    public async Task<bool> DeleteLinkAsync(
        string linkId,
        string deletedBy,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var tenantId = GetTenantId();
        var link = await context.ControlValidationLinks.FirstOrDefaultAsync(
            candidate => candidate.Id == linkId
                && candidate.TenantId == tenantId,
            cancellationToken);
        if (link is null)
            return false;

        context.ControlValidationLinks.Remove(link);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> UpsertScanLinkAsync(
        string systemId,
        string controlId,
        string scanFindingRef,
        string? description,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var tenantId = GetTenantId();
        var target = RequireTarget(scanFindingRef);
        var implementation = await FindImplementationAsync(context, tenantId, systemId, controlId, cancellationToken)
            ?? throw new ControlImplementationNotFoundException(systemId, controlId);
        var link = await context.ControlValidationLinks.FirstOrDefaultAsync(
            candidate => candidate.TenantId == tenantId
                && candidate.ControlImplementationId == implementation.Id
                && candidate.LinkTarget == target,
            cancellationToken);

        if (link is not null)
        {
            link.Description = description?.Trim();
            link.ValidatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
            return false;
        }

        context.ControlValidationLinks.Add(new ControlValidationLink
        {
            TenantId = tenantId,
            ControlImplementationId = implementation.Id,
            LinkType = ControlValidationLinkType.ScanFinding,
            LinkTarget = target,
            Description = description?.Trim(),
            AddedBy = "iac-scan",
            AddedAt = DateTime.UtcNow,
            ValidatedAt = DateTime.UtcNow,
            IsAutomated = true,
        });
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static Task<ControlImplementation?> FindImplementationAsync(
        AtoCopilotContext context,
        Guid tenantId,
        string systemId,
        string controlId,
        CancellationToken cancellationToken) =>
        context.ControlImplementations.FirstOrDefaultAsync(
            implementation => implementation.TenantId == tenantId
                && implementation.RegisteredSystemId == systemId
                && implementation.ControlId == controlId,
            cancellationToken);

    private Guid GetTenantId() => tenantAccessor.Current?.EffectiveTenantId
        ?? throw new InvalidOperationException("An active tenant context is required for control validation links.");

    private static string RequireTarget(string linkTarget)
    {
        if (string.IsNullOrWhiteSpace(linkTarget))
            throw new ArgumentException("A validation link target is required.", nameof(linkTarget));
        return linkTarget.Trim();
    }
}