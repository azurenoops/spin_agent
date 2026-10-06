using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Interfaces.Compliance;
using Microsoft.AspNetCore.DataProtection;

namespace Ato.Copilot.Agents.Compliance.Services;

public sealed record EnhancementAdditionInput(string ParentControlId, string ControlId,
    int ExpectedBaselineRevision, string Rationale, string? PolicyDraft, string? TechnicalDraft);
public sealed record RequirementMappingInput(int ExpectedVersion, IReadOnlyList<RequirementResponse> Responses,
    IReadOnlyDictionary<string, string> Parameters, string? FirstPassToken = null, IReadOnlyList<string>? FirstPassTokens = null);
public sealed record RequirementCoverageItem(string Id, string? Label, string Text,
    IReadOnlyList<RequirementResponse> Responses, string ResponseState, bool Reviewed, bool EvidenceGap);
public sealed record RequirementControlLink(string ControlId, string Title, bool Selected, bool HasNarrative);
public sealed record RequirementProposalSummary(string Id, string ControlId, string Rationale, string? PolicyDraft,
    string? TechnicalDraft, string Status, int Revision, string CreatedBy, DateTime CreatedAt,
    string? ReviewedBy, DateTime? ReviewedAt, bool CanAccept, string? ReviewNote = null);
public sealed record RequirementCoverageDetail(string SystemId, string ControlId, string? Framework,
    string? CatalogVersion, string? SourceUri, int BaselineRevision, int? NarrativeVersion,
    RequirementControlLink? Parent, IReadOnlyList<RequirementControlLink> Enhancements,
    IReadOnlyList<RequirementCoverageItem> Requirements, IReadOnlyList<CatalogParameter> Parameters,
    IReadOnlyDictionary<string, string> ParameterValues, IReadOnlyList<string> Gaps,
    IReadOnlyList<RequirementProposalSummary> Proposals, bool CanAuthor, bool CanReview, bool CanBind)
{
    public RequirementFirstPassProvenance? FirstPass { get; init; }
    public IReadOnlyList<RequirementFirstPassProvenance>? FirstPasses { get; init; }
}

/// <summary>Scoped requirement documentation and transactional enhancement selection.</summary>
public sealed partial class RequirementCoverageService(AtoCopilotContext db, ITenantContext tenant,
    ISystemWorkspaceAccessService accessService, ILogger<RequirementCoverageService> logger,
    IControlNarrativeService? generator = null, IDataProtectionProvider? dataProtection = null)
{
    private Guid TenantId => tenant.EffectiveTenantId;
    private Guid PersonId => tenant.PersonId ?? throw new UnauthorizedAccessException("An authenticated person assignment is required.");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task RequireReadAsync(string systemId, CancellationToken ct) => _ = await RequireAsync(systemId, ct);

    public Task<BaselineCatalogBinding> BindCatalogAsync(string systemId, string frameworkId,
        int expectedRevision, string rationale, string actor, CancellationToken ct) =>
        ExecuteMutationAsync(() => BindCatalogCoreAsync(systemId, frameworkId, expectedRevision, rationale, actor, ct), ct);

    public Task SaveMappingsAsync(string systemId, string controlId, RequirementMappingInput input, string actor, CancellationToken ct) =>
        ExecuteMutationAsync(() => SaveMappingsCoreAsync(systemId, controlId, input, actor, ct), ct);

    public Task ReviewMappingsAsync(string systemId, string controlId, int expectedVersion, string actor, CancellationToken ct) =>
        ExecuteMutationAsync(() => ReviewMappingsCoreAsync(systemId, controlId, expectedVersion, actor, ct), ct);

    public Task<RequirementEnhancementProposal> ProposeAsync(string systemId, EnhancementAdditionInput input, string actor, CancellationToken ct) =>
        ExecuteMutationAsync(() => ProposeCoreAsync(systemId, input, actor, ct), ct);

    public Task ReturnProposalAsync(string systemId, string proposalId, int expectedRevision, string note, string actor, CancellationToken ct) =>
        ExecuteMutationAsync(() => ReturnProposalCoreAsync(systemId, proposalId, expectedRevision, note, actor, ct), ct);

    public Task AcceptAsync(string systemId, string proposalId, int expectedRevision, string actor, CancellationToken ct) =>
        ExecuteMutationAsync(() => AcceptCoreAsync(systemId, proposalId, expectedRevision, actor, ct), ct);

    private Task ExecuteMutationAsync(Func<Task> operation, CancellationToken ct) =>
        ExecuteMutationAsync(async () => { await operation(); return true; }, ct);

    private Task<T> ExecuteMutationAsync<T>(Func<Task<T>> operation, CancellationToken ct)
    {
        var retry = false;
        return db.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
        {
            token.ThrowIfCancellationRequested();
            // A rolled-back attempt leaves tracked entities mutated; retry from persisted state.
            if (retry) db.ChangeTracker.Clear();
            retry = true;
            return await operation();
        }, ct);
    }

    private async Task<BaselineCatalogBinding> BindCatalogCoreAsync(string systemId, string frameworkId,
        int expectedRevision, string rationale, string actor, CancellationToken ct)
    {
        var permissions = await RequireAsync(systemId, ct);
        if (!permissions.CanManageSystem || !permissions.CanReviewNarratives)
            throw new UnauthorizedAccessException("Catalog reconciliation requires system management and narrative review permission.");
        RequireText(rationale, 2000, "Catalog reconciliation rationale");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var baseline = await BaselineAsync(systemId, ct);
        CheckRevision(baseline.CoverageRevision, expectedRevision);
        var framework = await CatalogSourceReader.ReadFrameworkAsync(db, x => x.Id == frameworkId && x.IsActive, ct)
            ?? throw new KeyNotFoundException("Framework is unavailable.");
        var sourceUri = framework.RequirementCatalogSourceUri ?? framework.CatalogUrl;
        if (string.IsNullOrWhiteSpace(framework.RequirementCatalogJson) || string.IsNullOrWhiteSpace(sourceUri))
            throw new InvalidOperationException("Catalog source is unavailable. Import an authoritative catalog first.");
        var catalog = RequirementCatalog.Parse(framework.RequirementCatalogJson);
        if (baseline.ControlIds.Any(id => !catalog.Controls.Any(x => x.DisplayId == id || x.Id == id)))
            throw new ArgumentException("Baseline contains controls not resolved by this catalog. Reconcile the identifiers before binding.");
        var binding = new BaselineCatalogBinding
        {
            TenantId = TenantId, ControlBaselineId = baseline.Id, FrameworkId = framework.Id,
            FrameworkIdentifier = framework.Identifier, CatalogVersion = catalog.Version,
            Publisher = framework.Publisher, SourceUri = sourceUri,
            CatalogJson = framework.RequirementCatalogJson, ContentHash = Hash(framework.RequirementCatalogJson),
            Rationale = rationale.Trim(), BoundBy = actor
        };
        db.BaselineCatalogBindings.Add(binding);
        baseline.RequirementCatalogBindingId = binding.Id;
        baseline.SourceFrameworkIdentifier = framework.Identifier;
        baseline.CatalogResolutionMessage = null;
        baseline.CoverageRevision++;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        logger.LogInformation("Bound requirement catalog {BindingId} for system {SystemId}", binding.Id, systemId);
        return binding;
    }

    public async Task<RequirementCoverageDetail> ReadAsync(string systemId, string controlId, CancellationToken ct)
    {
        var permissions = await RequireAsync(systemId, ct);
        var baseline = await BaselineAsync(systemId, ct);
        var implementation = await ImplementationAsync(systemId, controlId, ct);
        var binding = await BindingAsync(baseline, ct, required: false);
        if (binding is null)
            return new(systemId, controlId, null, null, null, baseline.CoverageRevision, implementation?.CurrentVersion,
                null, [], [], [], new Dictionary<string, string>(),
                [baseline.CatalogResolutionMessage ?? "Reference catalog association is being prepared automatically."], [],
                permissions.CanAuthorNarratives, permissions.CanReviewNarratives,
                permissions.CanManageSystem && permissions.CanReviewNarratives);
        var catalog = RequirementCatalog.Parse(binding.CatalogJson);
        var control = Resolve(catalog, controlId);
        implementation = await ImplementationAsync(systemId, control.DisplayId, ct, control.Id);
        var implementations = await db.ControlImplementations.AsNoTracking()
            .Where(x => x.TenantId == TenantId && x.RegisteredSystemId == systemId)
            .Select(x => x.ControlId).ToListAsync(ct);
        RequirementControlLink Link(CatalogRequirementControl c) => new(
            implementations.FirstOrDefault(id => id == c.DisplayId || id == c.Id)
                ?? baseline.ControlIds.FirstOrDefault(id => id == c.DisplayId || id == c.Id) ?? c.DisplayId, c.Title,
            baseline.ControlIds.Contains(c.DisplayId) || baseline.ControlIds.Contains(c.Id),
            implementations.Contains(c.DisplayId) || implementations.Contains(c.Id));
        var snapshot = Deserialize(implementation?.RequirementCoverageJson);
        var approved = Deserialize(implementation?.ApprovedRequirementCoverageJson);
        var gaps = await GapsAsync(binding, control, implementation, snapshot, ct);
        var reviewed = snapshot is not null && approved is not null && approved.ReviewedAt is not null
            && SameContent(snapshot, approved) && gaps.Count == 0;
        var requirements = new List<RequirementCoverageItem>();
        foreach (var requirement in control.Requirements)
        {
            var responses = snapshot?.Responses.Where(x => x.StatementId == requirement.Id).ToArray() ?? [];
            var evidenceGap = responses.Length == 0 || responses.Any(x => x.Evidence.Count == 0)
                || !await EvidenceValidAsync(systemId, responses.SelectMany(x => x.Evidence), ct);
            requirements.Add(new(requirement.Id, requirement.Label, requirement.Text, responses,
                responses.Length == 0 ? "Missing" : reviewed && !evidenceGap ? "Reviewed" : "Draft",
                reviewed && !evidenceGap, evidenceGap));
        }
        var proposals = await db.RequirementEnhancementProposals.AsNoTracking()
            .Where(x => x.TenantId == TenantId && x.ControlBaselineId == baseline.Id
                && (x.ParentControlId == control.DisplayId || x.ControlId == control.DisplayId || x.ControlId == control.Id))
            .OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync(ct);
        return new(systemId, controlId, binding.FrameworkIdentifier, binding.CatalogVersion, binding.SourceUri,
            baseline.CoverageRevision, implementation?.CurrentVersion,
            control.ParentId is null ? null : Link(catalog.Controls.Single(x => x.Id == control.ParentId)),
            catalog.Controls.Where(x => x.ParentId == control.Id).Select(Link).ToArray(), requirements,
            control.Parameters, snapshot?.Parameters ?? new Dictionary<string, string>(), gaps,
            proposals.Select(p => new RequirementProposalSummary(p.Id, p.ControlId, p.Rationale, p.PolicyDraft,
                p.TechnicalDraft, p.Status, p.Revision, p.CreatedBy, p.CreatedAt, p.ReviewedBy, p.ReviewedAt,
                p.Status == "Pending" && permissions.CanManageSystem && permissions.CanReviewNarratives
                    && p.AuthorPersonId != tenant.PersonId, p.ReviewNote)).ToArray(),
            permissions.CanAuthorNarratives, permissions.CanReviewNarratives,
            permissions.CanManageSystem && permissions.CanReviewNarratives) { FirstPass = snapshot?.FirstPass, FirstPasses = snapshot?.FirstPasses };
    }

    private async Task SaveMappingsCoreAsync(string systemId, string controlId, RequirementMappingInput input, string actor, CancellationToken ct)
    {
        var permissions = await RequireAsync(systemId, ct);
        if (!permissions.CanAuthorNarratives) throw new UnauthorizedAccessException("Narrative author permission is required.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var baseline = await BaselineAsync(systemId, ct);
        var binding = (await BindingAsync(baseline, ct))!;
        var control = Resolve(RequirementCatalog.Parse(binding.CatalogJson), controlId);
        var implementation = await ImplementationAsync(systemId, control.DisplayId, ct, control.Id)
            ?? throw new KeyNotFoundException("Create a selected control's narrative before mapping requirements.");
        CheckRevision(implementation.CurrentVersion, input.ExpectedVersion);
        if (implementation.ApprovalStatus == SspSectionStatus.UnderReview)
            throw new InvalidOperationException("Narrative is under review; finish that review before editing requirement mappings.");
        if (!baseline.ControlIds.Contains(control.DisplayId) && !baseline.ControlIds.Contains(control.Id))
            throw new InvalidOperationException("Control is not selected in the reviewed baseline.");
        if (input.Responses is null || input.Parameters is null || input.Responses.Count > 1000 || input.Parameters.Count > 1000)
            throw new ArgumentException("Provide bounded response and parameter collections.");
        if (input.Responses.Any(x => x is null)) throw new ArgumentException("Requirement responses cannot contain null entries.");
        if (input.Responses.GroupBy(x => (x.StatementId, x.Kind)).Any(g => g.Count() > 1))
            throw new ArgumentException("Duplicate requirement and narrative type mapping.");
        foreach (var response in input.Responses)
        {
            if (!control.Requirements.Any(x => x.Id == response.StatementId) || response.Kind is not ("Policy" or "Technical"))
                throw new ArgumentException("Response must reference a catalog requirement and Policy or Technical.");
            RequireText(response.Response, 8000, "Requirement response");
            if (response.Evidence is null || response.Evidence.Count > 100 || response.Evidence.Any(x => x is null))
                throw new ArgumentException("Provide a bounded evidence list.");
        }
        foreach (var parameter in input.Parameters)
            if (!control.Parameters.Any(x => x.Id == parameter.Key) || string.IsNullOrWhiteSpace(parameter.Value) || parameter.Value.Length > 2000)
                throw new ArgumentException("Parameter assignment must reference this catalog control and have a bounded value.");
        if (!await EvidenceValidAsync(systemId, input.Responses.SelectMany(x => x.Evidence), ct))
            throw new ArgumentException("Evidence must be a current, accessible artifact from this system with the expected content hash.");
        if (input.FirstPassTokens is not null && (input.FirstPassToken is not null || input.FirstPassTokens.Count is < 1 or > 2
            || input.FirstPassTokens.Any(t => t is null)))
            throw new ArgumentException("Provide at most one verified first-pass proof per narrative type.");
        var proofs = input.FirstPassTokens ?? (input.FirstPassToken is null ? [] : new[] { input.FirstPassToken });
        var provenance = new List<RequirementFirstPassProvenance>();
        foreach (var proof in proofs)
            provenance.Add(await ValidateFirstPassProofAsync(systemId, control.DisplayId, input.ExpectedVersion, proof, ct));
        if (provenance.Select(p => p.Kind).Distinct().Count() != provenance.Count)
            throw new ArgumentException("Duplicate first-pass narrative types are not allowed.");
        var snapshot = new RequirementCoverageSnapshot(binding.Id, binding.ContentHash, input.Responses,
            input.Parameters, NarrativeHash(implementation), PersonId, actor, DateTime.UtcNow)
        { FirstPass = provenance.Count == 1 ? provenance[0] : null, FirstPasses = provenance.Count > 1 ? provenance : null };
        implementation.RequirementCoverageJson = JsonSerializer.Serialize(snapshot, Json);
        AppendVersion(implementation, actor, "Saved explicit requirement mappings; content remains unreviewed.");
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private async Task ReviewMappingsCoreAsync(string systemId, string controlId, int expectedVersion, string actor, CancellationToken ct)
    {
        var permissions = await RequireAsync(systemId, ct);
        if (!permissions.CanReviewNarratives) throw new UnauthorizedAccessException("Narrative review permission is required.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var baseline = await BaselineAsync(systemId, ct);
        var binding = (await BindingAsync(baseline, ct))!;
        var control = Resolve(RequirementCatalog.Parse(binding.CatalogJson), controlId);
        var implementation = await ImplementationAsync(systemId, control.DisplayId, ct, control.Id) ?? throw new KeyNotFoundException("Narrative not found.");
        CheckRevision(implementation.CurrentVersion, expectedVersion);
        if (implementation.ApprovalStatus == SspSectionStatus.UnderReview)
            throw new InvalidOperationException("Narrative is under review; finish that review before reviewing requirement mappings.");
        var snapshot = Deserialize(implementation.RequirementCoverageJson) ?? throw new ArgumentException("No requirement mappings to review.");
        if (snapshot.AuthorPersonId == PersonId) throw new UnauthorizedAccessException("A different authorized person must review mappings.");
        var gaps = await GapsAsync(binding, control, implementation, snapshot, ct);
        if (gaps.Count > 0) throw new InvalidOperationException(string.Join(" ", gaps));
        var approved = snapshot with { ReviewerPersonId = PersonId, ReviewedBy = actor, ReviewedAt = DateTime.UtcNow };
        implementation.ApprovedRequirementCoverageJson = JsonSerializer.Serialize(approved, Json);
        AppendVersion(implementation, actor, "Reviewed explicit requirement responses and evidence; no control effectiveness determination.");
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private async Task<RequirementEnhancementProposal> ProposeCoreAsync(string systemId, EnhancementAdditionInput input, string actor, CancellationToken ct)
    {
        var permissions = await RequireAsync(systemId, ct);
        if (!permissions.CanAuthorNarratives) throw new UnauthorizedAccessException("Narrative author permission is required.");
        RequireText(input.Rationale, 2000, "Enhancement rationale");
        if (string.IsNullOrWhiteSpace(input.PolicyDraft) && string.IsNullOrWhiteSpace(input.TechnicalDraft))
            throw new ArgumentException("Provide a separate Policy or Technical draft.");
        if (input.PolicyDraft?.Length > 8000 || input.TechnicalDraft?.Length > 8000)
            throw new ArgumentException("Draft exceeds 8000 characters.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var baseline = await BaselineAsync(systemId, ct);
        CheckRevision(baseline.CoverageRevision, input.ExpectedBaselineRevision);
        var binding = (await BindingAsync(baseline, ct))!;
        var catalog = RequirementCatalog.Parse(binding.CatalogJson);
        var parent = Resolve(catalog, input.ParentControlId);
        var enhancement = Resolve(catalog, input.ControlId);
        if (enhancement.ParentId != parent.Id || enhancement.Withdrawn
            || !baseline.ControlIds.Contains(parent.DisplayId) && !baseline.ControlIds.Contains(parent.Id))
            throw new ArgumentException("Choose an eligible catalog enhancement of a selected parent.");
        if (baseline.ControlIds.Contains(enhancement.DisplayId) || baseline.ControlIds.Contains(enhancement.Id))
            throw new InvalidOperationException("The enhancement is already selected.");
        var key = Hash($"{baseline.Id}:{enhancement.Id}");
        var existing = await db.RequirementEnhancementProposals.SingleOrDefaultAsync(x => x.TenantId == TenantId && x.ActiveControlKey == key, ct);
        if (existing is not null)
        {
            if (existing.AuthorPersonId == PersonId && existing.Rationale == input.Rationale.Trim()
                && existing.PolicyDraft == input.PolicyDraft && existing.TechnicalDraft == input.TechnicalDraft)
                return existing;
            throw new InvalidOperationException("CONCURRENCY_CONFLICT: An enhancement addition is already pending.");
        }
        var existingNarrative = await ImplementationAsync(systemId, enhancement.DisplayId, ct, enhancement.Id);
        if (existingNarrative?.ApprovalStatus == SspSectionStatus.UnderReview)
            throw new InvalidOperationException("The enhancement narrative is under review. Finish that review before proposing selection.");
        var proposal = new RequirementEnhancementProposal
        {
            TenantId = TenantId, ControlBaselineId = baseline.Id, CatalogBindingId = binding.Id,
            BaseRevision = baseline.CoverageRevision, BaselineHash = BaselineHash(baseline),
            ExistingNarrativeVersion = existingNarrative?.CurrentVersion,
            ExistingNarrativeHash = existingNarrative is null ? null : ImplementationHash(existingNarrative),
            ActiveControlKey = key, ParentControlId = parent.DisplayId, ControlId = existingNarrative?.ControlId ?? enhancement.DisplayId,
            Rationale = input.Rationale.Trim(), PolicyDraft = input.PolicyDraft, TechnicalDraft = input.TechnicalDraft,
            AuthorPersonId = PersonId, CreatedBy = actor
        };
        db.RequirementEnhancementProposals.Add(proposal);
        await NarrativePersistence.SaveAsync(db, ct);
        await transaction.CommitAsync(ct);
        return proposal;
    }

    private async Task ReturnProposalCoreAsync(string systemId, string proposalId, int expectedRevision,
        string note, string actor, CancellationToken ct)
    {
        var permissions = await RequireAsync(systemId, ct);
        if (!permissions.CanManageSystem || !permissions.CanReviewNarratives)
            throw new UnauthorizedAccessException("Selection review requires system management and narrative review permission.");
        RequireText(note, 2000, "Review note");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var baseline = await BaselineAsync(systemId, ct);
        var proposal = await db.RequirementEnhancementProposals.SingleOrDefaultAsync(x => x.TenantId == TenantId
            && x.ControlBaselineId == baseline.Id && x.Id == proposalId, ct) ?? throw new KeyNotFoundException("Proposal not found.");
        if (proposal.AuthorPersonId == PersonId) throw new UnauthorizedAccessException("A different reviewer must review the proposal.");
        CheckRevision(proposal.Revision, expectedRevision);
        if (proposal.Status != "Pending") throw new InvalidOperationException("Proposal has already been reviewed.");
        proposal.Status = "NeedsRevision";
        proposal.ActiveControlKey = null;
        proposal.ReviewNote = note.Trim();
        proposal.ReviewerPersonId = PersonId;
        proposal.ReviewedBy = actor;
        proposal.ReviewedAt = DateTime.UtcNow;
        proposal.Revision++;
        await NarrativePersistence.SaveAsync(db, ct);
        await transaction.CommitAsync(ct);
    }

    private async Task AcceptCoreAsync(string systemId, string proposalId, int expectedRevision, string actor, CancellationToken ct)
    {
        var permissions = await RequireAsync(systemId, ct);
        if (!permissions.CanManageSystem || !permissions.CanReviewNarratives)
            throw new UnauthorizedAccessException("Selection acceptance requires system management and narrative review permission.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var baseline = await BaselineAsync(systemId, ct);
        var proposal = await db.RequirementEnhancementProposals.SingleOrDefaultAsync(x => x.Id == proposalId
            && x.TenantId == TenantId && x.ControlBaselineId == baseline.Id, ct) ?? throw new KeyNotFoundException("Proposal not found.");
        if (proposal.AuthorPersonId == PersonId) throw new UnauthorizedAccessException("A different authorized reviewer must accept the addition.");
        CheckRevision(proposal.Revision, expectedRevision);
        if (proposal.Status != "Pending" || proposal.BaseRevision != baseline.CoverageRevision || proposal.BaselineHash != BaselineHash(baseline))
            throw new InvalidOperationException("CONCURRENCY_CONFLICT: Proposal or baseline changed. Review a current proposal.");
        var binding = (await BindingAsync(baseline, ct))!;
        var catalog = RequirementCatalog.Parse(binding.CatalogJson);
        var control = Resolve(catalog, proposal.ControlId);
        if (control.Withdrawn || control.ParentId != Resolve(catalog, proposal.ParentControlId).Id)
            throw new InvalidOperationException("Catalog relationship is no longer eligible.");
        var implementation = await ImplementationAsync(systemId, control.DisplayId, ct, control.Id);
        if (implementation?.CurrentVersion != proposal.ExistingNarrativeVersion
            || (implementation is null ? null : ImplementationHash(implementation)) != proposal.ExistingNarrativeHash
            || implementation?.ApprovalStatus == SspSectionStatus.UnderReview)
            throw new InvalidOperationException("CONCURRENCY_CONFLICT: Existing enhancement narrative changed. Request a revised proposal.");
        baseline.ControlIds = [.. baseline.ControlIds, proposal.ControlId];
        baseline.TotalControls = baseline.ControlIds.Count;
        baseline.TailoredInControls++;
        baseline.ModifiedAt = DateTime.UtcNow;
        baseline.CoverageRevision++;
        db.ControlTailorings.Add(new()
        {
            TenantId = TenantId, ControlBaselineId = baseline.Id, ControlId = proposal.ControlId,
            Action = TailoringAction.Added, Rationale = proposal.Rationale, TailoredBy = actor
        });
        if (implementation is null)
        {
            implementation = new ControlImplementation { TenantId = TenantId, RegisteredSystemId = systemId, ControlId = proposal.ControlId };
            db.ControlImplementations.Add(implementation);
        }
        else
        {
            if (!await db.NarrativeVersions.AnyAsync(x => x.ControlImplementationId == implementation.Id
                && x.VersionNumber == implementation.CurrentVersion && x.TenantId == TenantId, ct))
                db.NarrativeVersions.Add(new()
                {
                    TenantId = TenantId, ControlImplementationId = implementation.Id, VersionNumber = implementation.CurrentVersion,
                    Content = implementation.Narrative ?? implementation.TechnicalNarrative ?? "",
                    SnapshotJson = NarrativeContentSnapshot.Capture(implementation), AuthoredBy = implementation.AuthoredBy,
                    AuthoredAt = implementation.AuthoredAt, Status = implementation.ApprovalStatus
                });
            implementation.CurrentVersion++;
        }
        if (proposal.PolicyDraft is not null) implementation.PolicyNarrative = proposal.PolicyDraft;
        if (proposal.TechnicalDraft is not null)
        {
            implementation.SetCombinedNarrative(proposal.TechnicalDraft);
            implementation.AiSuggested = false;
            implementation.IsAutoPopulated = false;
            implementation.IsManuallyCustomized = true;
        }
        implementation.ApprovalStatus = SspSectionStatus.Draft;
        implementation.AuthoredBy = proposal.CreatedBy;
        implementation.AuthoredAt = proposal.CreatedAt;
        implementation.ModifiedAt = DateTime.UtcNow;
        db.NarrativeVersions.Add(new()
        {
            TenantId = TenantId, ControlImplementationId = implementation.Id, VersionNumber = implementation.CurrentVersion,
            Content = implementation.Narrative ?? "", SnapshotJson = NarrativeContentSnapshot.Capture(implementation),
            AuthoredBy = proposal.CreatedBy, AuthoredAt = proposal.CreatedAt, Status = SspSectionStatus.Draft,
            ChangeReason = $"Draft from accepted enhancement selection {proposal.Id}; narrative approval is separate."
        });
        proposal.Status = "Accepted";
        proposal.ActiveControlKey = null;
        proposal.ReviewerPersonId = PersonId;
        proposal.ReviewedBy = actor;
        proposal.ReviewedAt = DateTime.UtcNow;
        proposal.Revision++;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        logger.LogInformation("Accepted enhancement proposal {ProposalId} for system {SystemId}; narrative remains Draft", proposal.Id, systemId);
    }

    private async Task<SystemWorkspacePermissions> RequireAsync(string systemId, CancellationToken ct)
    {
        if (!await db.RegisteredSystems.AnyAsync(x => x.Id == systemId && x.TenantId == TenantId && x.IsActive, ct))
            throw new KeyNotFoundException("System not found.");
        var access = await accessService.GetAccessAsync(TenantId, tenant.PersonId, systemId, tenant.IsCspAdmin, ct);
        if (!access.Permissions.CanRead) throw new KeyNotFoundException("System not found.");
        return access.Permissions;
    }

    private async Task<ControlBaseline> BaselineAsync(string systemId, CancellationToken ct) =>
        await db.ControlBaselines.SingleOrDefaultAsync(x => x.TenantId == TenantId && x.RegisteredSystemId == systemId, ct)
        ?? throw new KeyNotFoundException("System baseline not found.");

    private async Task<BaselineCatalogBinding?> BindingAsync(ControlBaseline baseline, CancellationToken ct, bool required = true)
    {
        var binding = await CatalogSourceReader.ReadBindingAsync(db, x => x.Id == baseline.RequirementCatalogBindingId
            && x.TenantId == TenantId && x.ControlBaselineId == baseline.Id, ct);
        if (binding is null && required) throw new InvalidOperationException("Catalog source needs reconciliation.");
        if (binding is not null && Hash(binding.CatalogJson) != binding.ContentHash)
            throw new InvalidOperationException("Catalog source integrity check failed.");
        return binding;
    }

    private async Task<ControlImplementation?> ImplementationAsync(string systemId, string controlId, CancellationToken ct, string? sourceId = null)
    {
        var matches = await db.ControlImplementations.Where(x => x.TenantId == TenantId && x.RegisteredSystemId == systemId
            && (x.ControlId == controlId || sourceId != null && x.ControlId == sourceId)).Take(2).ToListAsync(ct);
        if (matches.Count > 1)
            throw new InvalidOperationException("Multiple narratives refer to the same catalog control. Reconcile source/display identifiers before editing.");
        return matches.SingleOrDefault();
    }

    private static CatalogRequirementControl Resolve(RequirementCatalog catalog, string controlId) =>
        catalog.Controls.SingleOrDefault(x => x.Id == controlId || x.DisplayId == controlId)
        ?? throw new KeyNotFoundException("Control was not found in the system's pinned catalog.");

    private async Task<List<string>> GapsAsync(BaselineCatalogBinding binding, CatalogRequirementControl control,
        ControlImplementation? implementation, RequirementCoverageSnapshot? snapshot, CancellationToken ct)
    {
        var gaps = new List<string>();
        if (snapshot is null)
        {
            gaps.Add(implementation is null ? "Narrative response is missing." : "Existing narratives have unreviewed requirement mappings.");
            return gaps;
        }
        if (snapshot.BindingId != binding.Id || snapshot.CatalogHash != binding.ContentHash)
            gaps.Add("Requirement mappings reference a different catalog binding.");
        if (implementation is null || snapshot.NarrativeHash != NarrativeHash(implementation))
            gaps.Add("Narrative content changed after requirement mapping.");
        if (control.Requirements.Count == 0) gaps.Add("Structured source requirements are unavailable.");
        if (control.Requirements.Any(x => !snapshot.Responses.Any(r => r.StatementId == x.Id && !string.IsNullOrWhiteSpace(r.Response))))
            gaps.Add("Requirement responses are missing.");
        if (control.Parameters.Any(x => !snapshot.Parameters.TryGetValue(x.Id, out var value) || string.IsNullOrWhiteSpace(value)))
            gaps.Add("Organization-defined parameter values are missing.");
        if (snapshot.Responses.Any(x => x.Evidence.Count == 0) || implementation is not null
            && !await EvidenceValidAsync(implementation.RegisteredSystemId, snapshot.Responses.SelectMany(x => x.Evidence), ct))
            gaps.Add("Supporting evidence is missing or its version changed.");
        return gaps;
    }

    private async Task<bool> EvidenceValidAsync(string systemId, IEnumerable<RequirementEvidencePin> references, CancellationToken ct)
    {
        foreach (var pin in references.Distinct())
            if (string.IsNullOrWhiteSpace(pin.ContentHash) || !await db.EvidenceArtifacts.AnyAsync(x =>
                x.Id == pin.ArtifactId && x.TenantId == TenantId && x.RegisteredSystemId == systemId
                && !x.IsDeleted && x.ContentHash == pin.ContentHash, ct)) return false;
        return true;
    }

    private void AppendVersion(ControlImplementation implementation, string actor, string reason)
    {
        implementation.CurrentVersion++;
        implementation.ModifiedAt = DateTime.UtcNow;
        db.NarrativeVersions.Add(new()
        {
            TenantId = TenantId, ControlImplementationId = implementation.Id, VersionNumber = implementation.CurrentVersion,
            Content = implementation.Narrative ?? "", SnapshotJson = NarrativeContentSnapshot.Capture(implementation),
            Status = SspSectionStatus.Draft, AuthoredBy = actor, ChangeReason = reason
        });
    }

    private static bool SameContent(RequirementCoverageSnapshot left, RequirementCoverageSnapshot right) =>
        JsonSerializer.Serialize(left with { ReviewerPersonId = null, ReviewedBy = null, ReviewedAt = null }, Json)
        == JsonSerializer.Serialize(right with { ReviewerPersonId = null, ReviewedBy = null, ReviewedAt = null }, Json);
    public static RequirementCoverageSnapshot? Deserialize(string? json) =>
        json is null ? null : JsonSerializer.Deserialize<RequirementCoverageSnapshot>(json, Json)
            ?? throw new InvalidOperationException("Requirement snapshot is invalid.");
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string NarrativeHash(ControlImplementation value) => Hash(JsonSerializer.Serialize(new { value.PolicyNarrative, value.TechnicalNarrative }));
    private static string BaselineHash(ControlBaseline value) => Hash(JsonSerializer.Serialize(new { value.ControlIds, value.RequirementCatalogBindingId }));
    private static string ImplementationHash(ControlImplementation value) =>
        Hash($"{NarrativeContentSnapshot.Capture(value)}:{value.ApprovalStatus}:{value.ApprovedVersionId}");
    private static void CheckRevision(int actual, int expected)
    {
        if (actual != expected) throw new InvalidOperationException("CONCURRENCY_CONFLICT: Record changed. Reload before saving.");
    }
    private static void RequireText(string text, int maximum, string label)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > maximum) throw new ArgumentException($"{label} is required and must not exceed {maximum} characters.");
    }
}
