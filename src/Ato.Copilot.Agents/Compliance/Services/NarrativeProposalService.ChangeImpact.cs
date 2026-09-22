using System.Text.Json;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Agents.Compliance.Services;

public sealed partial class NarrativeProposalService
{
    public async Task<NarrativeChangeImpactResult> QueueAsync(
        NarrativeChangeImpactRequest request, CancellationToken cancellationToken = default)
    {
        ValidateImpactRequest(request);
        if (!await db.RegisteredSystems.AnyAsync(item => item.TenantId == TenantId &&
            item.Id == request.SystemId && item.IsActive, cancellationToken))
            throw new KeyNotFoundException("System not found.");
        var controls = request.ControlIds.Select(id => id.Trim().ToUpperInvariant()).Distinct().ToArray();
        var implementations = await db.ControlImplementations.AsNoTracking().Where(item => item.TenantId == TenantId &&
            item.RegisteredSystemId == request.SystemId && controls.Contains(item.ControlId)).ToListAsync(cancellationToken);
        if (implementations.Count != controls.Length) throw new KeyNotFoundException("Affected control not found.");
        var ids = new List<Guid>();
        foreach (var implementation in implementations)
        foreach (var type in request.NarrativeTypes.Distinct())
        {
            var id = await QueueTargetAsync(request, implementation, type, cancellationToken);
            if (id.HasValue) ids.Add(id.Value);
        }
        await NarrativePersistence.SaveAsync(db, cancellationToken);
        return new(ids);
    }

    private async Task<Guid?> QueueTargetAsync(NarrativeChangeImpactRequest request,
        ControlImplementation implementation, string type, CancellationToken ct)
    {
        var deliveryKey = request.ImpactId is null ? null : Hash(JsonSerializer.Serialize(new
        { request.TenantId, request.ImpactId, request.SystemId, implementation.ControlId, NarrativeType = type }));
        var delivered = deliveryKey is null ? null : await db.Set<NarrativeImpactReceipt>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.TenantId == TenantId && item.Id == deliveryKey, ct);
        if (delivered is not null)
        {
            if (delivered.SourceContextJson != SourceContextJson(request))
                throw new InvalidOperationException("CONCURRENCY_CONFLICT: ImpactId was already delivered with different source context.");
            var status = await db.NarrativeProposals.Where(item => item.TenantId == TenantId &&
                item.Id == delivered.NarrativeProposalId).Select(item => item.Status).SingleAsync(ct);
            return status is "PendingGeneration" or "GenerationFailed" ? delivered.NarrativeProposalId : null;
        }
        var snapshot = await SnapshotAsync(implementation, type, request.Actor, ct);
        var hash = StateHash(snapshot);
        var existing = await db.NarrativeProposals.AsNoTracking().Where(item => item.TenantId == TenantId &&
            item.RegisteredSystemId == request.SystemId && item.ControlId == implementation.ControlId &&
            item.NarrativeType == type).OrderByDescending(item => item.CreatedAt).FirstOrDefaultAsync(ct);
        if (existing is not null && existing.StateHash == hash &&
            (existing.Status == "Approved" && Content(implementation, type) == existing.ProposedContent ||
             existing.BaseVersion == implementation.CurrentVersion && existing.BeforeContent == Content(implementation, type) &&
             existing.Status is "PendingGeneration" or "GenerationFailed" or "Draft"))
        {
            RecordDelivery(request, implementation.ControlId, type, deliveryKey, existing.Id);
            return existing.Status is "PendingGeneration" or "GenerationFailed" ? existing.Id : null;
        }
        var row = new NarrativeProposal
        {
            TenantId = TenantId, RegisteredSystemId = request.SystemId, ControlId = implementation.ControlId,
            NarrativeType = type, BaseVersion = implementation.CurrentVersion, BeforeContent = Content(implementation, type),
            StateHash = hash, ProvenanceJson = WithSourceContext(snapshot, request), Status = "PendingGeneration", CreatedBy = request.Actor,
            ChangeSourceKind = request.SourceKind, ChangeSourceId = request.SourceId,
            MissingEvidenceJson = JsonSerializer.Serialize(SourceGaps(snapshot)),
            DeduplicationKey = Hash($"event:{request.SystemId}:{implementation.ControlId}:{type}:{implementation.CurrentVersion}:{hash}:{existing?.Id}")
        };
        db.NarrativeProposals.Add(row);
        RecordDelivery(request, implementation.ControlId, type, deliveryKey, row.Id);
        return row.Id;
    }

    private void RecordDelivery(NarrativeChangeImpactRequest request, string controlId, string type,
        string? deliveryKey, Guid proposalId)
    {
        if (deliveryKey is null) return;
        db.Set<NarrativeImpactReceipt>().Add(new NarrativeImpactReceipt
        {
            Id = deliveryKey, TenantId = TenantId, ImpactId = request.ImpactId!,
            RegisteredSystemId = request.SystemId, ControlId = controlId, NarrativeType = type,
            NarrativeProposalId = proposalId, SourceContextJson = SourceContextJson(request),
            SourceKind = request.SourceKind, SourceId = request.SourceId, SourceActor = request.Actor
        });
    }

    private static string? SourceContextJson(NarrativeChangeImpactRequest request) =>
        request.SourceContext is null ? null : JsonSerializer.Serialize(request.SourceContext);

    private static string WithSourceContext(string snapshot, NarrativeChangeImpactRequest request)
    {
        if (request.SourceContext is null) return snapshot;
        var provenance = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(snapshot)
            ?? throw new InvalidOperationException("Narrative source snapshot is unavailable.");
        provenance.Add("changeOrigin", JsonSerializer.SerializeToElement(request.SourceContext));
        return JsonSerializer.Serialize(provenance);
    }

    public async Task<NarrativeProposalResponse> GenerateQueuedForActorAsync(
        string systemId, Guid proposalId, string actor, int expectedRevision, CancellationToken cancellationToken = default)
    {
        await library.RequireNarrativeAuthorAsync(systemId, actor, cancellationToken);
        var row = await db.NarrativeProposals.AsNoTracking().SingleOrDefaultAsync(item => item.TenantId == TenantId &&
            item.RegisteredSystemId == systemId && item.Id == proposalId, cancellationToken)
            ?? throw new KeyNotFoundException("Proposal not found.");
        if (row.Revision != expectedRevision || row.Status is not ("PendingGeneration" or "GenerationFailed"))
            throw new InvalidOperationException("CONCURRENCY_CONFLICT: Reload the proposal before generating or retrying.");
        await GenerateQueuedCoreAsync(proposalId,
            ct => library.RequireNarrativeAuthorAsync(systemId, actor, ct), expectedRevision, cancellationToken);
        var generated = await db.NarrativeProposals.AsNoTracking().SingleAsync(item => item.TenantId == TenantId &&
            item.RegisteredSystemId == systemId && item.Id == proposalId, cancellationToken);
        return Response(generated, false, false);
    }

    public Task GenerateQueuedAsync(Guid proposalId, CancellationToken cancellationToken = default) =>
        GenerateQueuedCoreAsync(proposalId, null, null, cancellationToken);

    private async Task GenerateQueuedCoreAsync(Guid proposalId, Func<CancellationToken, Task>? validateAuthority,
        int? expectedRevision, CancellationToken cancellationToken)
    {
        if (TenantId == Guid.Empty) throw new UnauthorizedAccessException("An affected organization context is required.");
        var row = await db.NarrativeProposals.SingleOrDefaultAsync(item => item.TenantId == TenantId &&
            item.Id == proposalId, cancellationToken) ?? throw new KeyNotFoundException("Proposal not found.");
        if (expectedRevision.HasValue && row.Revision != expectedRevision.Value)
            throw new InvalidOperationException("CONCURRENCY_CONFLICT: Queued proposal changed before generation.");
        if (row.Status is "Draft" or "Approved") return;
        if (row.Status is not ("PendingGeneration" or "GenerationFailed"))
            throw new InvalidOperationException("CONCURRENCY_CONFLICT: Proposal is no longer queued.");
        await EnsureQueuedStateAsync(row, cancellationToken);
        var revision = row.Revision;
        GroundedNarrativeDraft generated;
        try
        {
            generated = await generator.GenerateGroundedDraftAsync(row.NarrativeType, row.ProvenanceJson, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException or TimeoutException or ArgumentException ||
            exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            row.Status = "GenerationFailed";
            row.GenerationErrorCode = exception switch
            {
                InvalidOperationException when exception.Message.StartsWith("AI_NOT_AVAILABLE:", StringComparison.Ordinal) => "AI_NOT_AVAILABLE",
                ArgumentException => "GENERATION_INPUT_INVALID",
                TimeoutException => "GENERATION_TIMEOUT",
                OperationCanceledException => "GENERATION_CANCELLED",
                _ => "GENERATION_FAILED"
            };
            row.Revision++;
            await NarrativePersistence.SaveAsync(db, cancellationToken);
            throw;
        }
        await db.Entry(row).ReloadAsync(cancellationToken);
        if (row.Revision != revision)
            throw new InvalidOperationException("CONCURRENCY_CONFLICT: Queued proposal was processed concurrently.");
        if (validateAuthority is not null) await validateAuthority(cancellationToken);
        await EnsureQueuedStateAsync(row, cancellationToken);
        row.ProposedContent = generated.Narrative;
        row.ConflictsJson = JsonSerializer.Serialize(generated.Conflicts);
        row.MissingEvidenceJson = JsonSerializer.Serialize(generated.MissingEvidence.Concat(SourceGaps(row.ProvenanceJson)).Distinct());
        row.Status = "Draft";
        row.GenerationErrorCode = null;
        row.Revision++;
        await NarrativePersistence.SaveAsync(db, cancellationToken);
    }

    private async Task EnsureQueuedStateAsync(NarrativeProposal row, CancellationToken ct)
    {
        var implementation = await ImplementationAsync(row.RegisteredSystemId, row.ControlId, ct);
        if (implementation.CurrentVersion == row.BaseVersion && Content(implementation, row.NarrativeType) == row.BeforeContent &&
            StateHash(await SnapshotAsync(implementation, row.NarrativeType, row.CreatedBy, ct)) == row.StateHash)
        {
            EnsureVersion(implementation, row.BaseVersion);
            return;
        }
        row.Status = "Superseded";
        row.Revision++;
        await NarrativePersistence.SaveAsync(db, ct);
        throw new InvalidOperationException("CONCURRENCY_CONFLICT: Queued source state changed; reconcile again.");
    }

    private void ValidateImpactRequest(NarrativeChangeImpactRequest request)
    {
        if (request.TenantId == Guid.Empty || request.TenantId != TenantId)
            throw new UnauthorizedAccessException("Change impact must run in the affected tenant context.");
        if (string.IsNullOrWhiteSpace(request.SystemId) || request.ControlIds is null || request.ControlIds.Count is 0 or > 1000 ||
            request.ControlIds.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 20) ||
            request.NarrativeTypes is null || request.NarrativeTypes.Count is 0 or > 2 ||
            request.NarrativeTypes.Any(type => type is not ("Policy" or "Technical")) ||
            request.SourceKind is not ("OrganizationCapability" or "CspCapability" or "Inheritance" or "Component" or "Reference" or "Assessment" or "System") ||
            string.IsNullOrWhiteSpace(request.SourceId) || request.SourceId.Length > 200 ||
            string.IsNullOrWhiteSpace(request.Actor) || request.Actor.Length > 200 ||
            request.ImpactId is not null && (string.IsNullOrWhiteSpace(request.ImpactId) || request.ImpactId.Length > 128))
            throw new ArgumentException("Provide valid changed controls, narrative types, source and authenticated event actor.");
        ValidateSourceContext(request);
    }

    private static void ValidateSourceContext(NarrativeChangeImpactRequest request)
    {
        if (request.SourceContext is not { } context) return;
        if (string.IsNullOrWhiteSpace(context.SourceRevision) || context.SourceRevision.Length > 128 ||
            context.Cause is not ("ProviderChanged" or "SubscriptionRemoved" or "ResponsibilityChanged") ||
            string.IsNullOrWhiteSpace(context.BaselineId) || context.BaselineId.Length > 36 ||
            string.IsNullOrWhiteSpace(context.SubscriptionId) || context.SubscriptionId.Length > 36 ||
            context.CspProfileId == Guid.Empty || context.CspInheritedComponentId == Guid.Empty || context.CspCapabilityId == Guid.Empty ||
            context.PreviousInheritanceType is not (null or "Inherited" or "Shared" or "Customer" or "Undesignated") ||
            context.CurrentInheritanceType is not (null or "Inherited" or "Shared" or "Customer" or "Undesignated"))
            throw new ArgumentException("Provide a bounded source revision, cause and original baseline/subscription provenance.");
        if (request.SourceKind == "OrganizationCapability" &&
            (context.CspProfileId.HasValue || context.CspInheritedComponentId.HasValue || context.CspCapabilityId.HasValue))
            throw new ArgumentException("An organization capability change cannot claim CSP publication identity.");
    }
}
