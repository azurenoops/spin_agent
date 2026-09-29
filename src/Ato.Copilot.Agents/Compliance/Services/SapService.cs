using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Queries;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Implements SAP generation, customization, finalization, and retrieval.
/// Assembles control baselines, OSCAL assessment objectives, STIG mappings,
/// evidence data, and SCA-provided inputs into a structured SAP document.
/// </summary>
/// <remarks>Feature 018.</remarks>
public class SapService : ISapService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SapService> _logger;
    private readonly INistControlsService _nistControlsService;
    private readonly IStigKnowledgeService _stigKnowledgeService;
    private readonly IDocumentTemplateService? _documentTemplateService;

    /// <summary>Valid assessment methods per NIST SP 800-53A.</summary>
    private static readonly HashSet<string> ValidMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "Examine", "Interview", "Test"
    };

    /// <summary>Default assessment methods (all three) per clarification Q2.</summary>
    private static readonly List<string> DefaultMethods = new() { "Examine", "Interview", "Test" };

    public SapService(
        IServiceScopeFactory scopeFactory,
        ILogger<SapService> logger,
        INistControlsService nistControlsService,
        IStigKnowledgeService stigKnowledgeService,
        IDocumentTemplateService? documentTemplateService = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _nistControlsService = nistControlsService;
        _stigKnowledgeService = stigKnowledgeService;
        _documentTemplateService = documentTemplateService;
    }

    /// <inheritdoc />
    public async Task<SapDocument> GenerateSapAsync(
        SapGenerationInput input,
        string generatedBy = "mcp-user",
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sw = Stopwatch.StartNew();
        if (input.Format.ToLowerInvariant() is not ("markdown" or "docx" or "pdf"))
            throw new InvalidOperationException($"Unsupported format '{input.Format}'. Valid formats: markdown, docx, pdf.");
        if (input.Format.ToLowerInvariant() is "docx" or "pdf" && _documentTemplateService is null)
            throw new InvalidOperationException("Document template service is not available for DOCX/PDF export.");

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var warnings = new List<string>();

        // ── T009: Prerequisite validation ────────────────────────────────
        var system = await context.RegisteredSystems
            .FirstOrDefaultAsync(s => s.Id == input.SystemId, cancellationToken)
            ?? throw new InvalidOperationException($"System '{input.SystemId}' not found.");

        await Ato.Copilot.Core.Services.Roles.SystemWorkspaceAccessPolicy.RequireAsync(
            context, input.SystemId, permission => permission.CanGenerateSap, cancellationToken);

        // A retained draft is never regenerated or deleted. Operation/source keys also
        // resolve retries after that draft has subsequently been finalized.
        var retained = (await context.SecurityAssessmentPlans
            .Where(s => s.RegisteredSystemId == input.SystemId &&
                (s.Status == SapStatus.Draft
                 || input.GenerationRequestId != null && s.GenerationRequestId == input.GenerationRequestId
                 || input.PreviousPlanId != null && s.PreviousPlanId == input.PreviousPlanId))
            .OrderByDescending(s => input.GenerationRequestId != null && s.GenerationRequestId == input.GenerationRequestId)
            .ThenByDescending(s => s.GeneratedAt)
            .ThenBy(s => s.Id).Take(1)
            .LoadRetainedDetailsAsync(cancellationToken)).SingleOrDefault();
        if (input.PreviousPlanId is not null)
        {
            var previous = await context.SecurityAssessmentPlans.AsNoTracking()
                .SingleOrDefaultAsync(s => s.Id == input.PreviousPlanId && s.RegisteredSystemId == input.SystemId, cancellationToken)
                ?? throw new InvalidOperationException("Previous SAP does not belong to this system.");
            if (previous.Status != SapStatus.Finalized)
                throw new InvalidOperationException("Only a finalized plan can be revised into a new draft.");
            if (input.ExpectedContentHash is not null && input.ExpectedContentHash != HashContent(previous.Content))
                throw new DbUpdateConcurrencyException("The source SAP changed. Reload before revising.");
        }
        if (retained is not null)
        {
            if (input.GenerationRequestId is not null && retained.GenerationRequestId == input.GenerationRequestId
                && retained.PreviousPlanId != input.PreviousPlanId)
                throw new InvalidOperationException("The request ID was already used for a different plan source.");
            if (input.GenerationRequestId is null && retained.Status == SapStatus.Draft &&
                (input.ScopeNotes is not null || input.RulesOfEngagement is not null || input.TeamMembers is not null
                 || input.MethodOverrides is not null || input.ScheduleStart.HasValue || input.ScheduleEnd.HasValue))
                return await FormatDocumentAsync(await UpdateSapAsync(new SapUpdateInput(retained.Id, input.ScheduleStart, input.ScheduleEnd,
                    input.ScopeNotes, input.RulesOfEngagement, input.TeamMembers, input.MethodOverrides,
                    ExpectedRevision: retained.Revision, ExpectedContentHash: HashContent(retained.Content), UpdatedBy: generatedBy), cancellationToken),
                    input.Format, cancellationToken);
            return await FormatDocumentAsync(MapToSapDocument(retained), input.Format, cancellationToken);
        }
        if (input.GenerationRequestId is not null && input.PreviousPlanId is null
            && await context.SecurityAssessmentPlans.AnyAsync(s => s.RegisteredSystemId == input.SystemId, cancellationToken))
            throw new InvalidOperationException("Select the finalized plan to revise before creating another draft.");

        var baseline = await context.ControlBaselines
            .Include(b => b.Inheritances)
            .FirstOrDefaultAsync(b => b.RegisteredSystemId == input.SystemId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"No control baseline found for system '{input.SystemId}'. Select a baseline before generating a SAP.");

        if (baseline.ControlIds == null || baseline.ControlIds.Count == 0)
            throw new InvalidOperationException(
                $"Control baseline for system '{input.SystemId}' has no controls. Select a baseline with controls before generating a SAP.");

        if (input.PreviousPlanId is not null)
        {
            var previous = (await context.SecurityAssessmentPlans
                .Where(p => p.Id == input.PreviousPlanId && p.RegisteredSystemId == input.SystemId)
                .LoadRetainedDetailsAsync(cancellationToken)).Single();
            var revision = (SecurityAssessmentPlan)context.Entry(previous).CurrentValues.ToObject();
            revision.Id = Guid.NewGuid().ToString();
            revision.Status = SapStatus.Draft;
            revision.Revision = 1;
            revision.ContentHash = null;
            revision.FinalizedAt = null;
            revision.FinalizedBy = null;
            revision.GeneratedAt = DateTime.UtcNow;
            revision.GeneratedBy = generatedBy;
            revision.UpdatedAt = null;
            revision.UpdatedBy = null;
            revision.GenerationRequestId = input.GenerationRequestId;
            revision.PreviousPlanId = previous.Id;
            foreach (var original in previous.ControlEntries)
            {
                var copy = (SapControlEntry)context.Entry(original).CurrentValues.ToObject();
                copy.Id = Guid.NewGuid().ToString();
                copy.SecurityAssessmentPlanId = revision.Id;
                copy.AssessmentMethods = [.. original.AssessmentMethods];
                copy.AssessmentObjectives = [.. original.AssessmentObjectives];
                copy.EvidenceRequirements = [.. original.EvidenceRequirements];
                copy.StigBenchmarks = [.. original.StigBenchmarks];
                revision.ControlEntries.Add(copy);
            }
            foreach (var original in previous.TeamMembers)
            {
                var copy = (SapTeamMember)context.Entry(original).CurrentValues.ToObject();
                copy.Id = Guid.NewGuid().ToString();
                copy.SecurityAssessmentPlanId = revision.Id;
                revision.TeamMembers.Add(copy);
                if (revision.AssessmentLeadUserId == "team:" + original.Id)
                    revision.AssessmentLeadUserId = "team:" + copy.Id;
            }
            var revisionRoles = await context.RmfRoleAssignments
                .Where(r => r.RegisteredSystemId == input.SystemId && r.IsActive).ToListAsync(cancellationToken);
            revision.Content = RenderSapMarkdown(revision, revision.ControlEntries.ToList(),
                revision.ControlEntries.Where(e => !e.IsExcluded).SelectMany(e => e.StigBenchmarks)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase), system, baseline, revisionRoles);
            context.SecurityAssessmentPlans.Add(revision);
            await context.SaveChangesAsync(cancellationToken);
            return await FormatDocumentAsync(MapToSapDocument(revision), input.Format, cancellationToken);
        }

        // Warn if not in Assess phase
        if (system.CurrentRmfStep != RmfPhase.Assess)
            warnings.Add($"System is not in Assess phase (current: {system.CurrentRmfStep}). SAP is typically generated during RMF Step 4 — Assess.");

        // Warn if no SCA role assigned
        var roles = await context.RmfRoleAssignments
            .Where(r => r.RegisteredSystemId == input.SystemId && r.IsActive)
            .ToListAsync(cancellationToken);

        if (!roles.Any(r => r.RmfRole == RmfRole.Sca))
            warnings.Add("No Security Control Assessor (SCA) role assignment found. An SCA should be assigned before assessment.");

        // ── T012: Validate method overrides early ────────────────────────
        if (input.MethodOverrides != null)
        {
            foreach (var mo in input.MethodOverrides)
            {
                foreach (var method in mo.Methods)
                {
                    if (!ValidMethods.Contains(method))
                        throw new InvalidOperationException(
                            $"Invalid assessment method '{method}' for control '{mo.ControlId}'. Valid methods are: Examine, Interview, Test.");
                }
            }
        }

        // ── T010: Control scope assembly ─────────────────────────────────
        var inheritanceMap = baseline.Inheritances
            .ToDictionary(i => i.ControlId, i => i, StringComparer.OrdinalIgnoreCase);

        // ── T011: Objective extraction + T012: Method resolution +
        //    T013: STIG mapping + T014: Evidence mapping ──────────────────
        var controlEntries = new List<SapControlEntry>();
        var methodOverrideMap = input.MethodOverrides?
            .ToDictionary(m => m.ControlId, m => m, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, SapMethodOverrideInput>(StringComparer.OrdinalIgnoreCase);

        // Control/subscription matches alone do not establish system ownership.
        var evidenceCounts = await (
            from evidence in context.Evidence
            join assessment in context.Assessments on evidence.AssessmentId equals assessment.Id
            where evidence.TenantId == system.TenantId && assessment.TenantId == system.TenantId
                && assessment.RegisteredSystemId == system.Id
                && baseline.ControlIds.Contains(evidence.ControlId)
            select evidence)
            .GroupBy(e => e.ControlId)
            .Select(g => new { ControlId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.ControlId, g => g.Count, StringComparer.OrdinalIgnoreCase, cancellationToken);

        int controlsWithObjectives = 0;
        int evidenceGaps = 0;
        int customerCount = 0, inheritedCount = 0, sharedCount = 0;
        var stigBenchmarkIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var controlId in baseline.ControlIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // T010: Determine inheritance
            var inheritanceType = InheritanceType.Customer;
            string? provider = null;
            if (inheritanceMap.TryGetValue(controlId, out var inh))
            {
                inheritanceType = inh.InheritanceType;
                provider = inh.Provider;
            }

            switch (inheritanceType)
            {
                case InheritanceType.Customer: customerCount++; break;
                case InheritanceType.Inherited: inheritedCount++; break;
                case InheritanceType.Shared: sharedCount++; break;
            }

            // T011: Extract OSCAL objectives
            var enhancement = await _nistControlsService.GetControlEnhancementAsync(controlId, cancellationToken);
            var objectives = enhancement?.Objectives ?? new List<string>();
            var controlTitle = enhancement?.Title ?? controlId;
            if (objectives.Count > 0)
                controlsWithObjectives++;

            // T012: Resolve assessment methods
            var methods = new List<string>(DefaultMethods);
            bool isOverridden = false;
            string? overrideRationale = null;
            if (methodOverrideMap.TryGetValue(controlId, out var mo))
            {
                methods = new List<string>(mo.Methods);
                isOverridden = true;
                overrideRationale = mo.Rationale;
            }

            // T013: STIG/SCAP test plan
            var stigs = await _stigKnowledgeService.GetStigsByCciChainAsync(controlId, cancellationToken: cancellationToken);
            var benchmarks = stigs
                .Where(s => !string.IsNullOrEmpty(s.BenchmarkId))
                .Select(s => s.BenchmarkId!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var bid in benchmarks)
                stigBenchmarkIds.Add(bid);

            // T014: Evidence requirements
            var evidenceReqs = BuildEvidenceRequirements(methods);
            evidenceCounts.TryGetValue(controlId, out var collected);
            var expected = methods.Count; // one evidence per method type
            if (collected < expected)
                evidenceGaps++;

            // Build control family name from prefix
            var familyPrefix = controlId.Split('-')[0].ToUpperInvariant();

            var entry = new SapControlEntry
            {
                SecurityAssessmentPlanId = string.Empty, // will be set after SAP creation
                ControlId = controlId,
                ControlTitle = controlTitle,
                ControlFamily = familyPrefix,
                InheritanceType = inheritanceType,
                Provider = provider,
                AssessmentMethods = methods,
                AssessmentObjectives = objectives,
                EvidenceRequirements = evidenceReqs,
                StigBenchmarks = benchmarks,
                EvidenceExpected = expected,
                EvidenceCollected = collected,
                IsMethodOverridden = isOverridden,
                OverrideRationale = overrideRationale
            };
            controlEntries.Add(entry);
        }

        // ── Create SAP entity ────────────────────────────────────────────
        // Ensure AssessmentId is null (not empty string) when not provided — FK requires valid reference or NULL
        var resolvedAssessmentId = string.IsNullOrWhiteSpace(input.AssessmentId) ? null : input.AssessmentId;
        if (resolvedAssessmentId != null)
        {
            var assessmentExists = await context.Assessments
                .AnyAsync(a => a.Id == resolvedAssessmentId, cancellationToken);
            if (!assessmentExists)
            {
                warnings.Add($"AssessmentId '{resolvedAssessmentId}' not found — ignoring link.");
                resolvedAssessmentId = null;
            }
        }

        var sapTitle = $"Security Assessment Plan — {system.Name} — {DateTime.UtcNow:yyyy-MM-dd}";
        var sap = new SecurityAssessmentPlan
        {
            TenantId = system.TenantId,
            RegisteredSystemId = input.SystemId,
            GenerationRequestId = input.GenerationRequestId,
            PreviousPlanId = input.PreviousPlanId,
            AssessmentId = resolvedAssessmentId,
            Status = SapStatus.Draft,
            Title = sapTitle,
            BaselineLevel = baseline.BaselineLevel,
            ScopeNotes = input.ScopeNotes,
            RulesOfEngagement = input.RulesOfEngagement,
            ScheduleStart = input.ScheduleStart,
            ScheduleEnd = input.ScheduleEnd,
            TotalControls = baseline.ControlIds.Count,
            CustomerControls = customerCount,
            InheritedControls = inheritedCount,
            SharedControls = sharedCount,
            StigBenchmarkCount = stigBenchmarkIds.Count,
            GeneratedBy = generatedBy,
            Format = input.Format
        };

        // Link control entries to SAP
        foreach (var entry in controlEntries)
        {
            entry.TenantId = system.TenantId;
            entry.SecurityAssessmentPlanId = sap.Id;
            sap.ControlEntries.Add(entry);
        }

        // Add team members
        if (input.TeamMembers != null)
        {
            foreach (var tm in input.TeamMembers)
            {
                sap.TeamMembers.Add(new SapTeamMember
                {
                    SecurityAssessmentPlanId = sap.Id,
                    Name = tm.Name,
                    Organization = tm.Organization,
                    Role = tm.Role,
                    ContactInfo = tm.ContactInfo
                });
            }
        }

        // ── T015: Generate Markdown content ──────────────────────────────
        sap.Content = RenderSapMarkdown(sap, controlEntries, stigBenchmarkIds, system, baseline, roles);

        // ── T016: Persist ────────────────────────────────────────────────
        context.SecurityAssessmentPlans.Add(sap);
        await context.SaveChangesAsync(cancellationToken);

        // ── Build family summaries ───────────────────────────────────────
        var familySummaries = controlEntries
            .GroupBy(e => e.ControlFamily)
            .OrderBy(g => g.Key)
            .Select(g => new SapFamilySummary
            {
                Family = g.Key,
                ControlCount = g.Count(),
                CustomerCount = g.Count(e => e.InheritanceType == InheritanceType.Customer),
                InheritedCount = g.Count(e => e.InheritanceType == InheritanceType.Inherited),
                Methods = g.SelectMany(e => e.AssessmentMethods).Distinct().OrderBy(m => m).ToList()
            })
            .ToList();

        _logger.LogInformation(
            "SAP generated: sap_id={SapId}, system_id={SystemId}, control_count={TotalControls}, " +
            "customer={CustomerControls}, inherited={InheritedControls}, shared={SharedControls}, " +
            "stig_benchmarks={StigBenchmarks}, warnings={Warnings}, duration_ms={DurationMs}",
            sap.Id, input.SystemId, baseline.ControlIds.Count,
            customerCount, inheritedCount, sharedCount,
            stigBenchmarkIds.Count, warnings.Count, sw.ElapsedMilliseconds);

        // ── T040: Format dispatch ──────────────────────────────────────
        var outputContent = sap.Content;
        var format = input.Format.ToLowerInvariant();

        if (format == "docx" || format == "pdf")
        {
            if (_documentTemplateService == null)
                throw new InvalidOperationException(
                    "Document template service is not available for DOCX/PDF export.");

            if (format == "docx")
            {
                var docxBytes = await _documentTemplateService.RenderDocxAsync(
                    input.SystemId, "sap", templateId: null, cancellationToken, sap.Id);
                outputContent = Convert.ToBase64String(docxBytes);
            }
            else // pdf
            {
                var pdfBytes = await _documentTemplateService.RenderPdfAsync(
                    input.SystemId, "sap", progress: null, cancellationToken, sap.Id);
                outputContent = Convert.ToBase64String(pdfBytes);
            }
        }
        else if (format != "markdown")
        {
            throw new InvalidOperationException(
                $"Unsupported format '{input.Format}'. Valid formats: markdown, docx, pdf.");
        }

        return new SapDocument
        {
            SapId = sap.Id,
            SystemId = input.SystemId,
            AssessmentId = input.AssessmentId,
            Title = sapTitle,
            Status = "Draft",
            Format = input.Format,
            BaselineLevel = baseline.BaselineLevel,
            Content = outputContent,
            TotalControls = baseline.ControlIds.Count,
            CustomerControls = customerCount,
            InheritedControls = inheritedCount,
            SharedControls = sharedCount,
            StigBenchmarkCount = stigBenchmarkIds.Count,
            ControlsWithObjectives = controlsWithObjectives,
            EvidenceGaps = evidenceGaps,
            FamilySummaries = familySummaries,
            GeneratedAt = sap.GeneratedAt,
            Warnings = warnings
        };
    }

    /// <inheritdoc />
    public async Task<SapDocument> UpdateSapAsync(
        SapUpdateInput input,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        // ── T023: Load Draft SAP ─────────────────────────────────────────
        var sap = (await context.SecurityAssessmentPlans
            .Where(s => s.Id == input.SapId)
            .LoadRetainedDetailsAsync(cancellationToken)).SingleOrDefault()
            ?? throw new InvalidOperationException($"SAP '{input.SapId}' not found.");

        if (sap.Status == SapStatus.Finalized)
            throw new InvalidOperationException(
                $"SAP '{input.SapId}' is finalized and cannot be modified. Generate a new SAP using compliance_generate_sap.");

        await Ato.Copilot.Core.Services.Roles.SystemWorkspaceAccessPolicy.RequireAsync(
            context, sap.RegisteredSystemId, permission => permission.CanGenerateSap, cancellationToken);
        if (input.ExpectedRevision.HasValue && input.ExpectedRevision != sap.Revision)
            throw new DbUpdateConcurrencyException("The SAP revision changed. Refresh and review before saving.");
        if (input.ExpectedContentHash is not null && !string.Equals(input.ExpectedContentHash,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sap.Content))).ToLowerInvariant(),
            StringComparison.Ordinal))
            throw new InvalidOperationException("The SAP draft changed. Refresh and review before saving.");
        if (input.Title is not null && (string.IsNullOrWhiteSpace(input.Title) || input.Title.Length > 500)
            || input.AssessmentLead?.Length > 200 || input.AssessmentApproach?.Length > 4000 || input.ScopeNotes?.Length > 4000)
            throw new InvalidOperationException("Review the assessment title, lead, scope and approach field lengths.");
        if (input.Title is not null) sap.Title = input.Title.Trim();
        if (input.AssessmentLead is not null) sap.AssessmentLead = input.AssessmentLead.Trim();
        if (input.ReplaceLead || input.AssessmentLeadUserId is not null)
            sap.AssessmentLeadUserId = input.AssessmentLeadUserId;
        if (input.AssessmentApproach is not null) sap.AssessmentApproach = input.AssessmentApproach.Trim();

        var updatedFields = new List<string>();

        // ── T023: Apply scalar field updates ─────────────────────────────
        if (input.ReplaceSchedule || input.ScheduleStart.HasValue)
        {
            sap.ScheduleStart = input.ScheduleStart;
            updatedFields.Add("schedule_start");
        }
        if (input.ReplaceSchedule || input.ScheduleEnd.HasValue)
        {
            sap.ScheduleEnd = input.ScheduleEnd;
            updatedFields.Add("schedule_end");
        }
        if (input.ScopeNotes != null)
        {
            sap.ScopeNotes = input.ScopeNotes;
            updatedFields.Add("scope_notes");
        }
        if (input.RulesOfEngagement != null)
        {
            sap.RulesOfEngagement = input.RulesOfEngagement;
            updatedFields.Add("rules_of_engagement");
        }
        if (sap.ScheduleStart.HasValue && sap.ScheduleEnd.HasValue && sap.ScheduleEnd < sap.ScheduleStart)
            throw new ArgumentException("Schedule end must not precede schedule start.");
        if (input.IncludedControlIds is not null)
        {
            var included = input.IncludedControlIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var actual = sap.ControlEntries.Select(e => e.ControlId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var reasons = input.ExclusionReasons is null ? null
                : new Dictionary<string, string>(input.ExclusionReasons, StringComparer.OrdinalIgnoreCase);
            if (!included.IsSubsetOf(actual) || input.ExclusionReasons?.Keys.Any(id => !actual.Contains(id)) == true)
                throw new ArgumentException("Scope choices must belong to the retained SAP control entries.");
            foreach (var entry in sap.ControlEntries)
            {
                entry.IsExcluded = !included.Contains(entry.ControlId);
                entry.ExclusionRationale = entry.IsExcluded
                    ? reasons?.GetValueOrDefault(entry.ControlId) : null;
            }
        }

        // ── T024: Per-control method override persistence ────────────────
        int methodOverridesApplied = 0;
        if (input.MethodOverrides != null)
        {
            foreach (var mo in input.MethodOverrides)
            {
                // Validate methods
                foreach (var method in mo.Methods)
                {
                    if (!ValidMethods.Contains(method))
                        throw new InvalidOperationException(
                            $"Invalid assessment method '{method}' for control '{mo.ControlId}'. Valid methods are: Examine, Interview, Test.");
                }

                // Never accept an unrelated control through the focused procedures editor.
                var entry = sap.ControlEntries.FirstOrDefault(
                    e => string.Equals(e.ControlId, mo.ControlId, StringComparison.OrdinalIgnoreCase));
                if (entry is null && input.ExpectedRevision.HasValue)
                    throw new ArgumentException($"Control '{mo.ControlId}' is outside this SAP.");
                if (entry != null)
                {
                    entry.AssessmentMethods = new List<string>(mo.Methods);
                    entry.IsMethodOverridden = true;
                    entry.OverrideRationale = mo.Rationale;
                    // Recalculate evidence requirements
                    entry.EvidenceRequirements = BuildEvidenceRequirements(mo.Methods);
                    entry.EvidenceExpected = mo.Methods.Count;
                    methodOverridesApplied++;
                }
            }
            if (methodOverridesApplied > 0)
                updatedFields.Add("method_overrides");
        }

        // ── T025: Assessment team management ─────────────────────────────
        if (input.TeamMembers != null)
        {
            // Atomic replacement: clear existing, add new
            context.SapTeamMembers.RemoveRange(sap.TeamMembers);
            sap.TeamMembers.Clear();

            foreach (var tm in input.TeamMembers)
            {
                sap.TeamMembers.Add(new SapTeamMember
                {
                    SecurityAssessmentPlanId = sap.Id,
                    Name = tm.Name,
                    Organization = tm.Organization,
                    Role = tm.Role,
                    ContactInfo = tm.ContactInfo
                });
            }
            updatedFields.Add("team_members");
        }

        // ── T023: Re-render Markdown content ─────────────────────────────
        var system = await context.RegisteredSystems
            .FirstAsync(s => s.Id == sap.RegisteredSystemId, cancellationToken);
        var baseline = await context.ControlBaselines
            .Include(b => b.Inheritances)
            .FirstAsync(b => b.RegisteredSystemId == sap.RegisteredSystemId, cancellationToken);
        var roles = await context.RmfRoleAssignments
            .Where(r => r.RegisteredSystemId == sap.RegisteredSystemId && r.IsActive)
            .ToListAsync(cancellationToken);

        var includedEntries = sap.ControlEntries.Where(e => !e.IsExcluded).ToList();
        sap.TotalControls = includedEntries.Count;
        sap.CustomerControls = includedEntries.Count(e => e.InheritanceType == InheritanceType.Customer);
        sap.InheritedControls = includedEntries.Count(e => e.InheritanceType == InheritanceType.Inherited);
        sap.SharedControls = includedEntries.Count(e => e.InheritanceType == InheritanceType.Shared);
        sap.StigBenchmarkCount = includedEntries.SelectMany(e => e.StigBenchmarks).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        sap.Revision++;
        sap.UpdatedAt = DateTime.UtcNow;
        sap.UpdatedBy = input.UpdatedBy;
        sap.Content = RenderSapMarkdown(sap, sap.ControlEntries.ToList(),
            includedEntries.SelectMany(e => e.StigBenchmarks).ToHashSet(StringComparer.OrdinalIgnoreCase),
            system, baseline, roles);

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Updated SAP '{SapId}': fields={UpdatedFields}, methodOverrides={OverridesApplied}",
            sap.Id, string.Join(", ", updatedFields), methodOverridesApplied);

        // Build family summaries
        var familySummaries = sap.ControlEntries
            .Where(e => !e.IsExcluded)
            .GroupBy(e => e.ControlFamily)
            .OrderBy(g => g.Key)
            .Select(g => new SapFamilySummary
            {
                Family = g.Key,
                ControlCount = g.Count(),
                CustomerCount = g.Count(e => e.InheritanceType == InheritanceType.Customer),
                InheritedCount = g.Count(e => e.InheritanceType == InheritanceType.Inherited),
                Methods = g.SelectMany(e => e.AssessmentMethods).Distinct().OrderBy(m => m).ToList()
            })
            .ToList();

        return new SapDocument
        {
            SapId = sap.Id,
            SystemId = sap.RegisteredSystemId,
            AssessmentId = sap.AssessmentId,
            Title = sap.Title,
            AssessmentLead = sap.AssessmentLead,
            AssessmentApproach = sap.AssessmentApproach,
            ScopeNotes = sap.ScopeNotes,
            Status = "Draft",
            Format = sap.Format,
            BaselineLevel = sap.BaselineLevel,
            Content = sap.Content,
            TotalControls = sap.TotalControls,
            CustomerControls = sap.CustomerControls,
            InheritedControls = sap.InheritedControls,
            SharedControls = sap.SharedControls,
            StigBenchmarkCount = sap.StigBenchmarkCount,
            ControlsWithObjectives = sap.ControlEntries.Count(e => !e.IsExcluded && e.AssessmentObjectives.Count > 0),
            EvidenceGaps = sap.ControlEntries.Count(e => !e.IsExcluded && e.EvidenceCollected < e.EvidenceExpected),
            FamilySummaries = familySummaries,
            GeneratedAt = sap.GeneratedAt
        };
    }

    /// <inheritdoc />
    public Task<SapDocument> FinalizeSapAsync(
        string sapId,
        string finalizedBy = "mcp-user",
        CancellationToken cancellationToken = default) =>
        FinalizeSapAsync(sapId, finalizedBy, cancellationToken, null, null);

    /// <inheritdoc />
    public async Task<SapDocument> FinalizeSapAsync(
        string sapId, string finalizedBy, CancellationToken cancellationToken,
        string? expectedContentHash, long? expectedRevision)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        // ── T026: Load SAP and validate status ───────────────────────────
        var sap = (await context.SecurityAssessmentPlans.Where(s => s.Id == sapId)
            .LoadRetainedDetailsAsync(cancellationToken)).SingleOrDefault()
            ?? throw new InvalidOperationException($"SAP '{sapId}' not found.");

        if (sap.Status == SapStatus.Finalized)
            throw new InvalidOperationException(
                $"SAP '{sapId}' is already finalized and cannot be re-finalized.");

        await Ato.Copilot.Core.Services.Roles.SystemWorkspaceAccessPolicy.RequireAsync(
            context, sap.RegisteredSystemId, permission => permission.CanFinalizeSap, cancellationToken);
        if (expectedRevision.HasValue && expectedRevision != sap.Revision
            || expectedContentHash is not null && expectedContentHash != HashContent(sap.Content))
            throw new DbUpdateConcurrencyException("The SAP changed. Refresh and review before finalizing.");
        // ── T026: Compute SHA-256 of Content ─────────────────────────────
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(sap.Content));
        var contentHash = Convert.ToHexStringLower(hashBytes);

        // ── T026: Set finalization fields ────────────────────────────────
        sap.Status = SapStatus.Finalized;
        sap.FinalizedBy = finalizedBy;
        sap.FinalizedAt = DateTime.UtcNow;
        sap.ContentHash = contentHash;
        sap.Revision++;
        sap.UpdatedAt = sap.FinalizedAt;
        sap.UpdatedBy = finalizedBy;

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Finalized SAP '{SapId}' by '{FinalizedBy}', hash={ContentHash}",
            sapId, finalizedBy, contentHash);

        // Build family summaries
        var familySummaries = sap.ControlEntries
            .Where(e => !e.IsExcluded)
            .GroupBy(e => e.ControlFamily)
            .OrderBy(g => g.Key)
            .Select(g => new SapFamilySummary
            {
                Family = g.Key,
                ControlCount = g.Count(),
                CustomerCount = g.Count(e => e.InheritanceType == InheritanceType.Customer),
                InheritedCount = g.Count(e => e.InheritanceType == InheritanceType.Inherited),
                Methods = g.SelectMany(e => e.AssessmentMethods).Distinct().OrderBy(m => m).ToList()
            })
            .ToList();

        return new SapDocument
        {
            SapId = sap.Id,
            SystemId = sap.RegisteredSystemId,
            AssessmentId = sap.AssessmentId,
            Title = sap.Title,
            AssessmentLead = sap.AssessmentLead,
            AssessmentApproach = sap.AssessmentApproach,
            ScopeNotes = sap.ScopeNotes,
            Status = "Finalized",
            Format = sap.Format,
            BaselineLevel = sap.BaselineLevel,
            Content = sap.Content,
            ContentHash = contentHash,
            TotalControls = sap.TotalControls,
            CustomerControls = sap.CustomerControls,
            InheritedControls = sap.InheritedControls,
            SharedControls = sap.SharedControls,
            StigBenchmarkCount = sap.StigBenchmarkCount,
            ControlsWithObjectives = sap.ControlEntries.Count(e => !e.IsExcluded && e.AssessmentObjectives.Count > 0),
            EvidenceGaps = sap.ControlEntries.Count(e => !e.IsExcluded && e.EvidenceCollected < e.EvidenceExpected),
            FamilySummaries = familySummaries,
            GeneratedAt = sap.GeneratedAt,
            FinalizedAt = sap.FinalizedAt
        };
    }

    /// <inheritdoc />
    public async Task<SecurityAssessmentPlan?> GetWorkingSapAsync(
        string systemId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId);
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        return (await db.SecurityAssessmentPlans.AsNoTracking()
            .Where(x => x.RegisteredSystemId == systemId)
            .OrderWorkingFirst().Take(1)
            .LoadRetainedDetailsAsync(cancellationToken)).SingleOrDefault();
    }

    /// <inheritdoc />
    public async Task<SapDocument> GetSapAsync(
        string? sapId = null,
        string? systemId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(sapId) && string.IsNullOrWhiteSpace(systemId))
            throw new InvalidOperationException("Either sapId or systemId must be provided.");

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        SecurityAssessmentPlan? sap;

        if (!string.IsNullOrWhiteSpace(sapId))
        {
            // ── T036: sapId takes precedence ─────────────────────────────
            sap = (await context.SecurityAssessmentPlans.Where(s => s.Id == sapId)
                .LoadRetainedDetailsAsync(cancellationToken)).SingleOrDefault()
                ?? throw new InvalidOperationException($"SAP '{sapId}' not found.");
        }
        else
        {
            // System lookups share the same working-plan preference as the workspace.
            sap = (await context.SecurityAssessmentPlans
                .Where(s => s.RegisteredSystemId == systemId)
                .OrderWorkingFirst().Take(1)
                .LoadRetainedDetailsAsync(cancellationToken)).SingleOrDefault()
                ?? throw new InvalidOperationException($"SAP not found for system '{systemId}'.");
        }

        _logger.LogInformation(
            "Retrieved SAP: sap_id={SapId}, system_id={SystemId}, status={Status}",
            sap.Id, sap.RegisteredSystemId, sap.Status);

        return MapToSapDocument(sap);
    }

    /// <inheritdoc />
    public async Task<List<SapDocument>> ListSapsAsync(
        string systemId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        // ── T037: All SAPs for system, ordered by GeneratedAt descending ─
        var saps = await context.SecurityAssessmentPlans
            .Where(s => s.RegisteredSystemId == systemId)
            .OrderByDescending(s => s.GeneratedAt).ThenBy(s => s.Id)
            .LoadRetainedDetailsAsync(cancellationToken);

        _logger.LogInformation(
            "Listed SAPs for system '{SystemId}': count={SapCount}",
            systemId, saps.Count);

        return saps.Select(s => MapToSapDocument(s, includeContent: false)).ToList();
    }

    /// <inheritdoc />
    public async Task<SapValidationResult> ValidateSapAsync(
        string sapId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var sap = (await context.SecurityAssessmentPlans
            .Where(s => s.Id == sapId)
            .LoadRetainedDetailsAsync(cancellationToken)).SingleOrDefault()
            ?? throw new InvalidOperationException($"SAP '{sapId}' not found.");

        var result = new SapValidationResult();

        // ── Control coverage ─────────────────────────────────────────────
        var entries = sap.ControlEntries.Where(e => !e.IsExcluded).ToList();
        result.ControlsCovered = entries.Count;
        if (entries.Count == 0) result.Warnings.Add("No controls are included in the assessment scope.");

        // Controls missing assessment objectives
        var missingObjectives = entries.Count(e => e.AssessmentObjectives == null || e.AssessmentObjectives.Count == 0);
        result.ControlsMissingObjectives = missingObjectives;
        if (missingObjectives > 0)
        {
            result.Warnings.Add($"{missingObjectives} control(s) missing assessment objectives.");
        }

        // Controls missing assessment methods
        var missingMethods = entries.Count(e => e.AssessmentMethods == null || e.AssessmentMethods.Count == 0);
        result.ControlsMissingMethods = missingMethods;
        if (missingMethods > 0)
        {
            result.Warnings.Add($"{missingMethods} control(s) missing assessment methods.");
        }

        // ── Team check ───────────────────────────────────────────────────
        result.HasTeam = sap.TeamMembers.Any();
        if (!result.HasTeam)
        {
            result.Warnings.Add("No assessment team members assigned.");
        }

        // ── Schedule check ───────────────────────────────────────────────
        result.HasSchedule = sap.ScheduleStart.HasValue && sap.ScheduleEnd.HasValue;
        if (!result.HasSchedule)
        {
            result.Warnings.Add("Assessment schedule start and/or end dates not set.");
        }

        // ── Overall completeness ─────────────────────────────────────────
        result.IsComplete = result.Warnings.Count == 0;

        _logger.LogInformation(
            "SAP validation completed: sap_id={SapId}, is_complete={IsComplete}, warning_count={WarningCount}, controls_covered={ControlsCovered}",
            sapId, result.IsComplete, result.Warnings.Count, result.ControlsCovered);

        return result;
    }

    /// <inheritdoc />
    public async Task<SapDocument?> GetSapStatusAsync(
        string systemId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        // Return the same working plan selected by the plan/results workspace.
        var sap = (await context.SecurityAssessmentPlans
            .Where(s => s.RegisteredSystemId == systemId)
            .OrderWorkingFirst().Take(1)
            .LoadRetainedDetailsAsync(cancellationToken)).SingleOrDefault();

        if (sap == null)
            return null;

        return MapToSapDocument(sap, includeContent: false);
    }

    /// <summary>
    /// Cross-reference a finalized SAP's control scope with a ComplianceAssessment's findings.
    /// Identifies planned-but-unassessed and assessed-but-unplanned controls for SAR generation integration.
    /// </summary>
    /// <param name="sapId">SAP ID to align.</param>
    /// <param name="assessmentId">ComplianceAssessment ID to cross-reference.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Alignment result with gap analysis.</returns>
    /// <exception cref="InvalidOperationException">SAP or assessment not found.</exception>
    public async Task<SapSarAlignmentResult> GetSapSarAlignmentAsync(
        string sapId,
        string assessmentId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var sap = await context.SecurityAssessmentPlans
            .Include(s => s.ControlEntries)
            .FirstOrDefaultAsync(s => s.Id == sapId, cancellationToken)
            ?? throw new InvalidOperationException($"SAP '{sapId}' not found.");

        var assessment = await context.Assessments
            .Include(a => a.Findings)
            .FirstOrDefaultAsync(a => a.Id == assessmentId, cancellationToken)
            ?? throw new InvalidOperationException($"Assessment '{assessmentId}' not found.");

        // SAP planned control IDs
        var plannedControlIds = sap.ControlEntries
            .Where(e => !e.IsExcluded)
            .Select(e => e.ControlId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Assessment finding control IDs (distinct)
        var assessedControlIds = assessment.Findings
            .Select(f => f.ControlId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var result = new SapSarAlignmentResult
        {
            SapId = sapId,
            AssessmentId = assessmentId,
            PlannedAndAssessed = plannedControlIds.Intersect(assessedControlIds, StringComparer.OrdinalIgnoreCase).Count(),
            PlannedButUnassessed = plannedControlIds.Except(assessedControlIds, StringComparer.OrdinalIgnoreCase).OrderBy(c => c).ToList(),
            AssessedButUnplanned = assessedControlIds.Except(plannedControlIds, StringComparer.OrdinalIgnoreCase).OrderBy(c => c).ToList()
        };

        result.IsFullyAligned = result.PlannedButUnassessed.Count == 0;

        _logger.LogInformation(
            "SAP-SAR alignment completed: sap_id={SapId}, assessment_id={AssessmentId}, planned_and_assessed={PlannedAndAssessed}, planned_but_unassessed={PlannedButUnassessed}, assessed_but_unplanned={AssessedButUnplanned}",
            sapId, assessmentId, result.PlannedAndAssessed, result.PlannedButUnassessed.Count, result.AssessedButUnplanned.Count);

        return result;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Private Helpers
    // ═══════════════════════════════════════════════════════════════════════

    public static string HashContent(string content) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    private async Task<SapDocument> FormatDocumentAsync(SapDocument document, string format, CancellationToken ct)
    {
        document.Format = format;
        if (format.Equals("docx", StringComparison.OrdinalIgnoreCase))
            document.Content = Convert.ToBase64String(await _documentTemplateService!.RenderDocxAsync(
                document.SystemId, "sap", templateId: null, ct, document.SapId));
        else if (format.Equals("pdf", StringComparison.OrdinalIgnoreCase))
            document.Content = Convert.ToBase64String(await _documentTemplateService!.RenderPdfAsync(
                document.SystemId, "sap", progress: null, ct, document.SapId));
        return document;
    }

    /// <summary>Map a SecurityAssessmentPlan entity to a SapDocument DTO.</summary>
    private static SapDocument MapToSapDocument(SecurityAssessmentPlan sap, bool includeContent = true)
    {
        var familySummaries = sap.ControlEntries
            .Where(e => !e.IsExcluded)
            .GroupBy(e => e.ControlFamily)
            .OrderBy(g => g.Key)
            .Select(g => new SapFamilySummary
            {
                Family = g.Key,
                ControlCount = g.Count(),
                CustomerCount = g.Count(e => e.InheritanceType == InheritanceType.Customer),
                InheritedCount = g.Count(e => e.InheritanceType == InheritanceType.Inherited),
                Methods = g.SelectMany(e => e.AssessmentMethods).Distinct().OrderBy(m => m).ToList()
            })
            .ToList();

        return new SapDocument
        {
            SapId = sap.Id,
            SystemId = sap.RegisteredSystemId,
            AssessmentId = sap.AssessmentId,
            Title = sap.Title,
            AssessmentLead = sap.AssessmentLead,
            AssessmentApproach = sap.AssessmentApproach,
            ScopeNotes = sap.ScopeNotes,
            Status = sap.Status.ToString(),
            Format = sap.Format,
            BaselineLevel = sap.BaselineLevel,
            Content = includeContent ? sap.Content : string.Empty,
            ContentHash = sap.ContentHash,
            TotalControls = sap.TotalControls,
            CustomerControls = sap.CustomerControls,
            InheritedControls = sap.InheritedControls,
            SharedControls = sap.SharedControls,
            StigBenchmarkCount = sap.StigBenchmarkCount,
            ControlsWithObjectives = sap.ControlEntries.Count(e => !e.IsExcluded && e.AssessmentObjectives.Count > 0),
            EvidenceGaps = sap.ControlEntries.Count(e => !e.IsExcluded && e.EvidenceCollected < e.EvidenceExpected),
            FamilySummaries = familySummaries,
            GeneratedAt = sap.GeneratedAt,
            FinalizedAt = sap.FinalizedAt
        };
    }

    /// <summary>T014: Build evidence requirements prose per method type.</summary>
    private static List<string> BuildEvidenceRequirements(List<string> methods)
    {
        var reqs = new List<string>();
        foreach (var method in methods)
        {
            switch (method)
            {
                case "Examine":
                    reqs.Add("Documentation review — policies, procedures, configuration exports, and system artifacts");
                    break;
                case "Interview":
                    reqs.Add("Personnel interviews — system administrators, ISSOs, and security personnel");
                    break;
                case "Test":
                    reqs.Add("Technical testing — automated scans, manual verification, and functional tests");
                    break;
            }
        }
        return reqs;
    }

    /// <summary>T015: Render SAP Markdown content with all 15 sections.</summary>
    private static string RenderSapMarkdown(
        SecurityAssessmentPlan sap,
        List<SapControlEntry> controlEntries,
        HashSet<string> stigBenchmarkIds,
        RegisteredSystem system,
        ControlBaseline baseline,
        List<RmfRoleAssignment> roles)
    {
        var sb = new StringBuilder();
        var excluded = controlEntries.Where(e => e.IsExcluded).ToList();
        controlEntries = controlEntries.Where(e => !e.IsExcluded).ToList();

        // ── Section 1: Introduction ──────────────────────────────────────
        sb.AppendLine("# Security Assessment Plan (SAP)");
        sb.AppendLine();
        sb.AppendLine($"**Title**: {sap.Title}");
        if (!string.IsNullOrWhiteSpace(sap.AssessmentLead))
            sb.AppendLine($"**Assessment lead**: {sap.AssessmentLead}");
        if (!string.IsNullOrWhiteSpace(sap.AssessmentApproach))
            sb.AppendLine($"**Assessment approach**: {sap.AssessmentApproach}");
        sb.AppendLine($"**Generated**: {sap.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"**Generated By**: {sap.GeneratedBy}");
        sb.AppendLine($"**Status**: {sap.Status}");
        sb.AppendLine();

        // ── Section 2: System Description ────────────────────────────────
        sb.AppendLine("## 2. System Description");
        sb.AppendLine();
        sb.AppendLine($"**System Name**: {system.Name}");
        if (!string.IsNullOrWhiteSpace(system.Acronym))
            sb.AppendLine($"**Acronym**: {system.Acronym}");
        sb.AppendLine($"**Current RMF Step**: {system.CurrentRmfStep}");
        sb.AppendLine();

        // ── Section 3: Assessment Scope ──────────────────────────────────
        sb.AppendLine("## 3. Assessment Scope");
        sb.AppendLine();
        sb.AppendLine($"**Baseline Level**: {baseline.BaselineLevel}");
        sb.AppendLine($"**Total Controls**: {sap.TotalControls}");
        sb.AppendLine($"**Customer Controls**: {sap.CustomerControls}");
        sb.AppendLine($"**Inherited Controls**: {sap.InheritedControls}");
        sb.AppendLine($"**Shared Controls**: {sap.SharedControls}");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(sap.ScopeNotes))
        {
            sb.AppendLine($"**Scope Notes**: {sap.ScopeNotes}");
            sb.AppendLine();
        }

        // ── Section 4: Assessment Objectives ─────────────────────────────
        sb.AppendLine("## 4. Assessment Objectives");
        sb.AppendLine();
        var withObjectives = controlEntries.Where(e => e.AssessmentObjectives.Count > 0).ToList();
        sb.AppendLine($"{withObjectives.Count} of {controlEntries.Count} controls have OSCAL assessment objectives.");
        sb.AppendLine();

        // ── Section 5: Assessment Methods ────────────────────────────────
        sb.AppendLine("## 5. Assessment Methods");
        sb.AppendLine();
        sb.AppendLine("Assessment methods per NIST SP 800-53A: **Examine**, **Interview**, **Test**.");
        sb.AppendLine();
        var overriddenCount = controlEntries.Count(e => e.IsMethodOverridden);
        if (overriddenCount > 0)
            sb.AppendLine($"{overriddenCount} control(s) have custom method overrides.");
        sb.AppendLine();

        // ── Section 6: Assessment Procedures ─────────────────────────────
        sb.AppendLine("## 6. Assessment Procedures");
        sb.AppendLine();
        sb.AppendLine("Detailed per-control assessment procedures are listed in Appendix A — Control Matrix.");
        sb.AppendLine();
        foreach (var entry in controlEntries.OrderBy(e => e.ControlId))
        {
            sb.AppendLine($"### {entry.ControlId} — {entry.ControlTitle}");
            sb.AppendLine($"**Methods**: {string.Join(", ", entry.AssessmentMethods)}");
            if (!string.IsNullOrWhiteSpace(entry.OverrideRationale))
                sb.AppendLine($"**Method rationale**: {entry.OverrideRationale}");
            foreach (var objective in entry.AssessmentObjectives) sb.AppendLine($"- {objective}");
            sb.AppendLine();
        }

        // ── Section 7: Excluded Controls ─────────────────────────────────
        sb.AppendLine("## 7. Excluded Controls");
        sb.AppendLine();
        foreach (var entry in excluded.OrderBy(e => e.ControlId))
            sb.AppendLine($"- **{entry.ControlId}** ({entry.ControlTitle}) — Excluded from scope: {entry.ExclusionRationale ?? "No rationale recorded"}");
        var inherited = controlEntries.Where(e => e.InheritanceType == InheritanceType.Inherited).ToList();
        if (inherited.Count > 0)
        {
            sb.AppendLine($"{inherited.Count} controls are fully inherited and will be assessed via provider attestation:");
            sb.AppendLine();
            foreach (var ctrl in inherited.OrderBy(e => e.ControlId))
                sb.AppendLine($"- **{ctrl.ControlId}** ({ctrl.ControlTitle}) — Provider: {ctrl.Provider ?? "N/A"}");
            sb.AppendLine();
        }
        else
        {
            if (excluded.Count == 0) sb.AppendLine("No controls are excluded from direct assessment.");
            sb.AppendLine();
        }

        // ── Section 8: STIG Test Plan ────────────────────────────────────
        sb.AppendLine("## 8. STIG/SCAP Test Plan");
        sb.AppendLine();
        if (stigBenchmarkIds.Count > 0)
        {
            sb.AppendLine($"**STIG Benchmarks**: {stigBenchmarkIds.Count}");
            sb.AppendLine();
            sb.AppendLine("| Benchmark ID | Controls Covered |");
            sb.AppendLine("|-------------|-----------------|");
            foreach (var bid in stigBenchmarkIds.OrderBy(b => b))
            {
                var covered = controlEntries.Count(e => e.StigBenchmarks.Contains(bid));
                sb.AppendLine($"| {bid} | {covered} |");
            }
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("No STIG benchmarks mapped to baseline controls.");
            sb.AppendLine();
        }

        // ── Section 9: Assessment Team ───────────────────────────────────
        sb.AppendLine("## 9. Assessment Team");
        sb.AppendLine();
        if (sap.TeamMembers.Count > 0)
        {
            sb.AppendLine("| Name | Organization | Role | Contact |");
            sb.AppendLine("|------|-------------|------|---------|");
            foreach (var tm in sap.TeamMembers)
                sb.AppendLine($"| {tm.Name} | {tm.Organization} | {tm.Role} | {tm.ContactInfo ?? "—"} |");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("*Assessment team has not been assigned.*");
            sb.AppendLine();
        }

        // ── Section 10: Schedule ─────────────────────────────────────────
        sb.AppendLine("## 10. Assessment Schedule");
        sb.AppendLine();
        if (sap.ScheduleStart.HasValue && sap.ScheduleEnd.HasValue)
        {
            sb.AppendLine($"**Start Date**: {sap.ScheduleStart.Value:yyyy-MM-dd}");
            sb.AppendLine($"**End Date**: {sap.ScheduleEnd.Value:yyyy-MM-dd}");
            var duration = (sap.ScheduleEnd.Value - sap.ScheduleStart.Value).Days;
            sb.AppendLine($"**Duration**: {duration} days");
        }
        else
        {
            sb.AppendLine("*Assessment schedule has not been set.*");
        }
        sb.AppendLine();

        // ── Section 11: Rules of Engagement ──────────────────────────────
        sb.AppendLine("## 11. Rules of Engagement");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(sap.RulesOfEngagement))
            sb.AppendLine(sap.RulesOfEngagement);
        else
            sb.AppendLine("*Rules of engagement have not been defined.*");
        sb.AppendLine();

        // ── Section 12: Evidence Requirements ────────────────────────────
        sb.AppendLine("## 12. Evidence Requirements");
        sb.AppendLine();
        var gapControls = controlEntries.Where(e => e.EvidenceCollected < e.EvidenceExpected).ToList();
        sb.AppendLine($"**Evidence Gaps**: {gapControls.Count} of {controlEntries.Count} controls have incomplete evidence.");
        sb.AppendLine();

        // ── Section 13: Risk Approach ────────────────────────────────────
        sb.AppendLine("## 13. Risk Approach");
        sb.AppendLine();
        sb.AppendLine("Risk determinations will follow NIST SP 800-30 methodology. Findings will be categorized by likelihood and impact to produce a risk rating of Very Low, Low, Moderate, High, or Very High.");
        sb.AppendLine();

        // ── Section 14: Appendix A — Control Matrix ──────────────────────
        sb.AppendLine("## Appendix A: Control Matrix");
        sb.AppendLine();
        sb.AppendLine("| Control ID | Title | Family | Inheritance | Methods | Objectives | STIGs | Evidence Gap |");
        sb.AppendLine("|-----------|-------|--------|------------|---------|-----------|-------|-------------|");
        foreach (var entry in controlEntries.OrderBy(e => e.ControlId))
        {
            var methodStr = string.Join(", ", entry.AssessmentMethods);
            var objCount = entry.AssessmentObjectives.Count;
            var stigCount = entry.StigBenchmarks.Count;
            var gapFlag = entry.EvidenceCollected < entry.EvidenceExpected ? "Yes" : "No";
            sb.AppendLine($"| {entry.ControlId} | {entry.ControlTitle} | {entry.ControlFamily} | {entry.InheritanceType} | {methodStr} | {objCount} | {stigCount} | {gapFlag} |");
        }
        sb.AppendLine();

        // ── Section 15: Appendix B — STIG Benchmark List ─────────────────
        sb.AppendLine("## Appendix B: STIG Benchmark List");
        sb.AppendLine();
        if (stigBenchmarkIds.Count > 0)
        {
            foreach (var bid in stigBenchmarkIds.OrderBy(b => b))
            {
                var rules = controlEntries
                    .Where(e => e.StigBenchmarks.Contains(bid))
                    .Select(e => e.ControlId)
                    .OrderBy(c => c)
                    .ToList();
                sb.AppendLine($"- **{bid}**: {string.Join(", ", rules)}");
            }
        }
        else
        {
            sb.AppendLine("*No STIG benchmarks mapped.*");
        }
        sb.AppendLine();

        return sb.ToString();
    }
}
