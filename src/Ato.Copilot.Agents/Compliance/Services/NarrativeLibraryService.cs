using System.Security.Cryptography;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Agents.Compliance.Services;

public sealed record NarrativeReferenceResponse(
    Guid Id, Guid ReferenceKey, string Title, string Scope, string ScopeId,
    string SourceName, string SourceSha256, int Version, int Revision, bool IsPublished,
    DateTimeOffset CreatedAt, string CreatedBy, DateTimeOffset? PublishedAt, string? PublishedBy,
    IReadOnlyList<NarrativeReferencePassage> Passages);

public sealed record NarrativeScopeCapability(string Id, string Name);
public sealed record NarrativeAccessResponse(Guid TenantId, string SystemName, bool CanAuthor,
    bool CanPublishShared, IReadOnlyList<NarrativeScopeCapability> Capabilities);

public sealed class NarrativeLibraryService(AtoCopilotContext db, ITenantContext tenant)
{
    private Guid TenantId => tenant.EffectiveTenantId;

    public async Task<NarrativeAccessResponse> GetAccessAsync(string systemId, string actor, CancellationToken cancellationToken = default)
    {
        await RequireSystemAsync(systemId, actor, false, cancellationToken);
        var shared = await HasSharedAuthorityAsync(actor, cancellationToken);
        var canAuthor = shared || await db.RmfRoleAssignments.AnyAsync(assignment => assignment.TenantId == TenantId &&
            assignment.RegisteredSystemId == systemId && assignment.UserId == actor && assignment.IsActive &&
            (assignment.RmfRole == RmfRole.MissionOwner || assignment.RmfRole == RmfRole.SystemOwner ||
             assignment.RmfRole == RmfRole.Isso || assignment.RmfRole == RmfRole.Issm), cancellationToken);
        var name = await db.RegisteredSystems.Where(system => system.TenantId == TenantId && system.Id == systemId)
            .Select(system => system.Name).SingleAsync(cancellationToken);
        var capabilities = shared ? await db.SecurityCapabilities.AsNoTracking().Where(capability => capability.TenantId == TenantId)
            .OrderBy(capability => capability.Name).Select(capability => new NarrativeScopeCapability(capability.Id, capability.Name))
            .ToListAsync(cancellationToken) : [];
        return new(TenantId, name, canAuthor, shared, capabilities);
    }

    public async Task<IReadOnlyList<NarrativeReferenceResponse>> ListAsync(
        string systemId, string actor, CancellationToken cancellationToken = default)
    {
        await RequireSystemAsync(systemId, actor, false, cancellationToken);
        var capabilityIds = db.ControlImplementations.Where(item => item.TenantId == TenantId && item.RegisteredSystemId == systemId)
            .Select(item => item.SecurityCapabilityId);
        var rows = await db.NarrativeReferences.AsNoTracking().Where(item => item.TenantId == TenantId &&
            (item.Scope == "Organization" || item.Scope == "System" && item.ScopeId == systemId ||
             item.Scope == "Capability" && capabilityIds.Contains(item.ScopeId)) &&
            (item.IsPublished || item.ImportedForSystemId == systemId && item.CreatedBy == actor))
            .OrderByDescending(item => item.CreatedAt).ToListAsync(cancellationToken);
        return rows.Select(ToResponse).ToList();
    }

    public async Task<NarrativeReferenceResponse> ImportAsync(
        string systemId, string actor, string title, string scope, string scopeId,
        string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        await RequireSystemAsync(systemId, actor, true, cancellationToken);
        await RequireScopeAsync(systemId, actor, scope, scopeId, cancellationToken);
        title = title.Trim();
        fileName = Path.GetFileName(fileName.Replace('\\', '/'));
        if (title.Length is 0 or > 200 || fileName.Length is 0 or > 200)
            throw new ArgumentException("Reference title and filename must contain 1-200 characters.");
        using var bytes = new MemoryStream();
        var block = new byte[81920];
        int count;
        while ((count = await content.ReadAsync(block, cancellationToken)) > 0)
        {
            if (bytes.Length + count > NarrativeLibraryParser.MaxBytes)
                throw new InvalidDataException("Reference uploads must not exceed 5 MB.");
            await bytes.WriteAsync(block.AsMemory(0, count), cancellationToken);
        }
        bytes.Position = 0;
        var passages = await NarrativeLibraryParser.ExtractAsync(bytes, fileName, cancellationToken);
        var previous = await db.NarrativeReferences.AsNoTracking().Where(item => item.TenantId == TenantId &&
            item.Scope == scope && item.ScopeId == scopeId && item.Title == title)
            .OrderByDescending(item => item.Version).FirstOrDefaultAsync(cancellationToken);
        var row = new NarrativeReference
        {
            TenantId = TenantId, Title = title, Scope = scope, ScopeId = scopeId, ImportedForSystemId = systemId,
            SourceName = fileName, SourceSha256 = Convert.ToHexString(SHA256.HashData(bytes.ToArray())),
            CreatedBy = actor, PassagesJson = JsonSerializer.Serialize(passages),
            OriginalPassagesJson = JsonSerializer.Serialize(passages),
            ReferenceKey = previous?.ReferenceKey ?? Guid.NewGuid(), Version = (previous?.Version ?? 0) + 1,
        };
        db.NarrativeReferences.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(row);
    }

    public async Task<NarrativeReferenceResponse> PublishAsync(
        string systemId, Guid id, string actor, int expectedRevision,
        IReadOnlyList<NarrativeReferencePassage> passages, bool reviewed,
        CancellationToken cancellationToken = default)
    {
        await RequireSystemAsync(systemId, actor, true, cancellationToken);
        var row = await db.NarrativeReferences.FirstOrDefaultAsync(item => item.Id == id && item.TenantId == TenantId &&
            item.ImportedForSystemId == systemId, cancellationToken) ?? throw new KeyNotFoundException("Reference not found.");
        await RequireScopeAsync(systemId, actor, row.Scope, row.ScopeId, cancellationToken);
        if (row.IsPublished || row.Revision != expectedRevision)
            throw new InvalidOperationException("CONCURRENCY_CONFLICT: Reference was published or changed. Reload before reviewing.");
        if (!reviewed || passages is null || passages.Count is 0 or > 500 ||
            passages.Any(passage => passage is null || string.IsNullOrWhiteSpace(passage.Content) ||
                passage.Content.Length > 20_000 || passage.NarrativeType is not ("Policy" or "Technical") ||
                string.IsNullOrWhiteSpace(passage.ControlId) || passage.ControlId.Length > 20) ||
            passages.Sum(passage => passage.Content.Length) > 500_000)
            throw new ArgumentException("Review all passages and resolve their control and narrative-type mappings before publishing.");
        var normalized = passages.Select(passage => passage with
        { ControlId = passage.ControlId!.Trim().ToUpperInvariant(), Content = passage.Content.Trim() }).ToList();
        foreach (var controlId in normalized.Select(passage => passage.ControlId!).Distinct())
        {
            if (!await db.NistControls.AnyAsync(control => control.Id.ToUpper() == controlId, cancellationToken) &&
                !await db.ControlImplementations.AnyAsync(item => item.TenantId == TenantId &&
                    item.RegisteredSystemId == systemId && item.ControlId.ToUpper() == controlId, cancellationToken))
                throw new ArgumentException($"Unknown control mapping: {controlId}.");
        }
        row.PassagesJson = JsonSerializer.Serialize(normalized);
        row.IsPublished = true;
        row.PublishedBy = actor;
        row.PublishedAt = DateTime.UtcNow;
        row.Revision++;
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(row);
    }

    internal async Task RequireSystemAsync(string systemId, string actor, bool write, CancellationToken cancellationToken)
    {
        if (TenantId == Guid.Empty || !await db.RegisteredSystems.AnyAsync(
            system => system.TenantId == TenantId && system.Id == systemId && system.IsActive, cancellationToken))
            throw new KeyNotFoundException("System not found.");
        if (string.IsNullOrWhiteSpace(actor)) throw new UnauthorizedAccessException("An authenticated identity is required.");
        if (await HasSharedAuthorityAsync(actor, cancellationToken)) return;
        var assignments = db.RmfRoleAssignments.Where(assignment => assignment.TenantId == TenantId &&
            assignment.RegisteredSystemId == systemId && assignment.UserId == actor && assignment.IsActive);
        if (write) assignments = assignments.Where(assignment => assignment.RmfRole == RmfRole.MissionOwner ||
            assignment.RmfRole == RmfRole.SystemOwner || assignment.RmfRole == RmfRole.Isso || assignment.RmfRole == RmfRole.Issm);
        if (!await assignments.AnyAsync(cancellationToken))
            throw new UnauthorizedAccessException("An active system assignment is required.");
    }

    internal async Task<bool> HasSharedAuthorityAsync(string actor, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(actor, out var actorId)) return false;
        return await db.OrganizationRoleAssignments.AnyAsync(assignment => assignment.TenantId == TenantId &&
            assignment.RemovedAt == null && (assignment.Role == OrganizationRole.Issm || assignment.Role == OrganizationRole.Administrator) &&
            assignment.Person != null && assignment.Person.TenantId == TenantId &&
            (assignment.Person.Id == actorId || assignment.Person.EntraObjectId == actorId), cancellationToken);
    }

    private async Task RequireScopeAsync(string systemId, string actor, string scope, string scopeId, CancellationToken cancellationToken)
    {
        if (scope == "System" && scopeId == systemId) return;
        if (scope is not ("Organization" or "Capability")) throw new ArgumentException("Invalid reference scope.");
        if (!await HasSharedAuthorityAsync(actor, cancellationToken))
            throw new UnauthorizedAccessException("Shared references require tenant ISSM or Administrator authority.");
        if (scope == "Organization" && scopeId != TenantId.ToString() || scope == "Capability" &&
            !await db.SecurityCapabilities.AnyAsync(capability => capability.TenantId == TenantId && capability.Id == scopeId, cancellationToken))
            throw new ArgumentException("Reference scope target is not available in this tenant.");
    }

    private static NarrativeReferenceResponse ToResponse(NarrativeReference row) => new(
        row.Id, row.ReferenceKey, row.Title, row.Scope, row.ScopeId, row.SourceName, row.SourceSha256,
        row.Version, row.Revision, row.IsPublished, new DateTimeOffset(DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc)), row.CreatedBy,
        row.PublishedAt.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(row.PublishedAt.Value, DateTimeKind.Utc)) : null, row.PublishedBy,
        JsonSerializer.Deserialize<List<NarrativeReferencePassage>>(row.PassagesJson) ?? []);
}