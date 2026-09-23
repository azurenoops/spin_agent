using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Services.Tenancy;

/// <summary>Factory-backed and singleton-safe; never caches a positive authorization decision.</summary>
public sealed class TenantSupportSessionStore(IDbContextFactory<AtoCopilotContext> factory) : ITenantSupportSessionStore
{
    public async Task CreateAsync(TenantSupportSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Id == Guid.Empty || session.DirectoryTenantId == Guid.Empty || session.ObjectId == Guid.Empty
            || session.TargetTenantId == Guid.Empty || session.ExpiresAt <= session.IssuedAt || session.RevokedAt.HasValue)
            throw new ArgumentException("A new support session requires a complete identity, target and valid lifetime.", nameof(session));
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        db.Set<TenantSupportSession>().Add(session);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<TenantSupportSession?> GetAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.Set<TenantSupportSession>().AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
    }

    public async Task<bool> RevokeAsync(Guid sessionId, Guid directoryTenantId, Guid objectId, Guid targetTenantId,
        string reason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 64)
            throw new ArgumentException("A revocation reason of 1-64 characters is required.", nameof(reason));
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var changed = await db.Set<TenantSupportSession>()
            .Where(s => s.Id == sessionId && s.DirectoryTenantId == directoryTenantId && s.ObjectId == objectId
                && s.TargetTenantId == targetTenantId && s.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.RevokedAt, now)
                .SetProperty(s => s.RevocationReason, reason), cancellationToken);
        return changed != 0;
    }
}
