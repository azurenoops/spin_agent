using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Implements RMF lifecycle management: system registration, step transitions,
/// and gate condition validation per DoDI 8510.01.
/// </summary>
public class RmfLifecycleService : IRmfLifecycleService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RmfLifecycleService> _logger;

    public RmfLifecycleService(
        IServiceScopeFactory scopeFactory,
        ILogger<RmfLifecycleService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<RegisteredSystem> RegisterSystemAsync(
        string name,
        SystemType systemType,
        MissionCriticality missionCriticality,
        string hostingEnvironment,
        string createdBy,
        string? acronym = null,
        string? description = null,
        AzureEnvironmentProfile? azureProfile = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));
        ArgumentException.ThrowIfNullOrWhiteSpace(hostingEnvironment, nameof(hostingEnvironment));
        ArgumentException.ThrowIfNullOrWhiteSpace(createdBy, nameof(createdBy));

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var system = new RegisteredSystem
        {
            Name = name.Trim(),
            Acronym = acronym?.Trim(),
            SystemType = systemType,
            Description = description?.Trim(),
            MissionCriticality = missionCriticality,
            HostingEnvironment = hostingEnvironment.Trim(),
            CreatedBy = createdBy,
            CurrentRmfStep = RmfPhase.Prepare,
            AzureProfile = azureProfile
        };

        context.RegisteredSystems.Add(system);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Registered system {SystemName} ({SystemId}) by {CreatedBy} | Type: {SystemType}",
            system.Name, system.Id, createdBy, systemType);

        return system;
    }

    /// <inheritdoc />
    public async Task<RegisteredSystem?> GetSystemAsync(
        string systemId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId, nameof(systemId));

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        return await context.RegisteredSystems
            .Include(s => s.SecurityCategorization)
                .ThenInclude(sc => sc!.InformationTypes)
            .Include(s => s.ControlBaseline)
            .Include(s => s.AuthorizationBoundaryDefinitions)
                .ThenInclude(d => d.ComponentAssignments)
            .Include(s => s.RmfRoleAssignments)
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.Id == systemId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<RegisteredSystem> Systems, int TotalCount)> ListSystemsAsync(
        bool activeOnly = true,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var query = context.RegisteredSystems.AsQueryable();

        if (activeOnly)
            query = query.Where(s => s.IsActive);

        var totalCount = await query.CountAsync(cancellationToken);

        var systems = await query
            .OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (systems, totalCount);
    }

    /// <inheritdoc />
    public async Task<RmfStepAdvanceResult> AdvanceRmfStepAsync(
        string systemId,
        RmfPhase targetStep,
        bool force = false,
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId, nameof(systemId));

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var system = await context.RegisteredSystems
            .Include(s => s.SecurityCategorization)
                .ThenInclude(sc => sc!.InformationTypes)
            .Include(s => s.ControlBaseline)
            .Include(s => s.AuthorizationBoundaryDefinitions)
                .ThenInclude(d => d.ComponentAssignments)
            .Include(s => s.RmfRoleAssignments)
            .Include(s => s.PrivacyThresholdAnalysis)
            .Include(s => s.PrivacyImpactAssessment)
            .Include(s => s.SystemInterconnections)
                .ThenInclude(ic => ic.Agreements)
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.Id == systemId, cancellationToken);

        if (system == null)
        {
            return new RmfStepAdvanceResult
            {
                Success = false,
                ErrorMessage = $"System '{systemId}' not found.",
                PreviousStep = RmfPhase.Prepare,
                NewStep = RmfPhase.Prepare
            };
        }

        var previousStep = system.CurrentRmfStep;
        var isForward = targetStep > previousStep;
        var isBackward = targetStep < previousStep;

        if (targetStep == previousStep)
        {
            return new RmfStepAdvanceResult
            {
                Success = false,
                System = system,
                PreviousStep = previousStep,
                NewStep = previousStep,
                ErrorMessage = $"System is already at step '{targetStep}'."
            };
        }

        // Gate checks for forward movement
        var gateResults = new List<GateCheckResult>();
        if (isForward)
        {
            gateResults = await CheckForwardGatesAsync(system, previousStep, targetStep, context, cancellationToken);
            var hasFailures = gateResults.Any(g => !g.Passed && g.Severity == "Error");

            if (hasFailures && !force)
            {
                return new RmfStepAdvanceResult
                {
                    Success = false,
                    System = system,
                    PreviousStep = previousStep,
                    NewStep = previousStep,
                    GateResults = gateResults,
                    ErrorMessage = "Gate conditions not met. Use force=true to override."
                };
            }
        }

        // Backward movement requires force
        if (isBackward && !force)
        {
            return new RmfStepAdvanceResult
            {
                Success = false,
                System = system,
                PreviousStep = previousStep,
                NewStep = previousStep,
                GateResults = new[]
                {
                    new GateCheckResult
                    {
                        GateName = "Backward Movement",
                        Passed = false,
                        Message = $"Backward movement from '{previousStep}' to '{targetStep}' requires force=true.",
                        Severity = "Error"
                    }
                },
                ErrorMessage = "Backward movement requires force=true."
            };
        }

        // Perform the transition
        system.CurrentRmfStep = targetStep;
        system.RmfStepUpdatedAt = DateTime.UtcNow;
        system.ModifiedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);

        var logLevel = force ? LogLevel.Warning : LogLevel.Information;
        _logger.Log(logLevel,
            "RMF step transition: {SystemName} ({SystemId}) {PreviousStep} → {TargetStep} | Forced: {Forced} | By: {UserId}",
            system.Name, system.Id, previousStep, targetStep, force, userId ?? "system");

        return new RmfStepAdvanceResult
        {
            Success = true,
            System = system,
            PreviousStep = previousStep,
            NewStep = targetStep,
            GateResults = gateResults,
            WasForced = force && gateResults.Any(g => !g.Passed)
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GateCheckResult>> CheckGateConditionsAsync(
        string systemId,
        RmfPhase targetStep,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId, nameof(systemId));

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var system = await context.RegisteredSystems
            .Include(s => s.SecurityCategorization)
                .ThenInclude(sc => sc!.InformationTypes)
            .Include(s => s.ControlBaseline)
            .Include(s => s.AuthorizationBoundaryDefinitions)
                .ThenInclude(d => d.ComponentAssignments)
            .Include(s => s.RmfRoleAssignments)
            .Include(s => s.PrivacyThresholdAnalysis)
            .Include(s => s.PrivacyImpactAssessment)
            .Include(s => s.SystemInterconnections)
                .ThenInclude(ic => ic.Agreements)
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.Id == systemId, cancellationToken);

        if (system == null)
        {
            return new[]
            {
                new GateCheckResult
                {
                    GateName = "System Exists",
                    Passed = false,
                    Message = $"System '{systemId}' not found.",
                    Severity = "Error"
                }
            };
        }

        return await CheckForwardGatesAsync(system, system.CurrentRmfStep, targetStep, context, cancellationToken);
    }

    // ─── Private Implementation ──────────────────────────────────────────

    /// <summary>
    /// Check gate conditions for all intermediate steps from current to target.
    /// </summary>
    private async Task<List<GateCheckResult>> CheckForwardGatesAsync(
        RegisteredSystem system,
        RmfPhase currentStep,
        RmfPhase targetStep,
        AtoCopilotContext context,
        CancellationToken cancellationToken)
    {
        var results = new List<GateCheckResult>();

        // Check each transition gate between current step and target
        for (var step = currentStep; step < targetStep; step++)
        {
            var nextStep = step + 1;
            results.AddRange(await GetGateChecksForTransitionAsync(system, step, nextStep, context, cancellationToken));
        }

        return results;
    }

    /// <summary>
    /// Get specific gate checks for a single step transition.
    /// </summary>
    private async Task<IEnumerable<GateCheckResult>> GetGateChecksForTransitionAsync(
        RegisteredSystem system,
        RmfPhase fromStep,
        RmfPhase toStep,
        AtoCopilotContext context,
        CancellationToken cancellationToken)
    {
        return (fromStep, toStep) switch
        {
            (RmfPhase.Prepare, RmfPhase.Categorize) => await CheckPrepareToCategorizeAsync(system, context, cancellationToken),
            (RmfPhase.Categorize, RmfPhase.Select) => CheckCategorizeToSelect(system),
            (RmfPhase.Select, RmfPhase.Implement) => CheckSelectToImplement(system),
            (RmfPhase.Implement, RmfPhase.Assess) => await CheckImplementToAssessAsync(system, context, cancellationToken),
            (RmfPhase.Assess, RmfPhase.Authorize) => await CheckAssessToAuthorizeAsync(system, context, cancellationToken),
            (RmfPhase.Authorize, RmfPhase.Monitor) => await CheckAuthorizeToMonitorAsync(system, context, cancellationToken),
            _ => Array.Empty<GateCheckResult>()
        };
    }

    /// <summary>Prepare → Categorize: Must have ≥1 role and ≥1 component in a boundary.</summary>
    private static async Task<IEnumerable<GateCheckResult>> CheckPrepareToCategorizeAsync(
        RegisteredSystem system,
        AtoCopilotContext context,
        CancellationToken cancellationToken)
    {
        var results = new List<GateCheckResult>();

        results.Add(new GateCheckResult
        {
            GateName = "RMF Roles Assigned",
            Passed = system.RmfRoleAssignments.Any(r => r.IsActive),
            Message = system.RmfRoleAssignments.Any(r => r.IsActive)
                ? $"{system.RmfRoleAssignments.Count(r => r.IsActive)} role(s) assigned."
                : "At least 1 RMF role must be assigned before categorization.",
            Severity = "Error"
        });

        var inScopeComponents = await context.ComponentSystemAssignments
            .CountAsync(csa => csa.RegisteredSystemId == system.Id
                            && csa.AuthorizationBoundaryDefinitionId != null,
                        cancellationToken);

        results.Add(new GateCheckResult
        {
            GateName = "Authorization Boundary Defined",
            Passed = inScopeComponents > 0,
            Message = inScopeComponents > 0
                ? $"{inScopeComponents} component(s) in boundary."
                : "At least 1 component must be assigned to the authorization boundary.",
            Severity = "Error"
        });

        // ─── Gate 3: Privacy Readiness (Feature 021) ─────────────────────
        var pta = system.PrivacyThresholdAnalysis;
        var piaApproved = system.PrivacyImpactAssessment?.Status == PiaStatus.Approved;
        var privacyPassed = pta != null && pta.Determination switch
        {
            PtaDetermination.PiaNotRequired => true,
            PtaDetermination.Exempt => true,
            PtaDetermination.PiaRequired => piaApproved,
            _ => false // PendingConfirmation or no PTA
        };

        results.Add(new GateCheckResult
        {
            GateName = "Privacy Readiness",
            Passed = privacyPassed,
            Message = privacyPassed
                ? pta!.Determination == PtaDetermination.PiaRequired
                    ? "PTA complete. PIA approved."
                    : $"PTA complete. Determination: {pta!.Determination}."
                : pta == null
                    ? "Privacy Threshold Analysis (PTA) must be completed before categorization."
                    : pta.Determination == PtaDetermination.PendingConfirmation
                        ? "PTA determination is pending confirmation. Resolve ambiguous PII info types."
                        : "PTA indicates PIA required, but PIA is not yet approved.",
            Severity = "Error"
        });

        // ─── Gate 4: Interconnection Documentation (Feature 021) ─────────
        var activeInterconnections = system.SystemInterconnections
            .Where(ic => ic.Status == InterconnectionStatus.Active)
            .ToList();

        bool interconnectionPassed;
        string interconnectionMessage;

        if (system.HasNoExternalInterconnections)
        {
            interconnectionPassed = true;
            interconnectionMessage = "System certified as having no external interconnections.";
        }
        else if (activeInterconnections.Count > 0)
        {
            var allCovered = activeInterconnections.All(ic =>
                ic.Agreements.Any(a =>
                    a.Status == AgreementStatus.Signed &&
                    (!a.ExpirationDate.HasValue || a.ExpirationDate.Value > DateTime.UtcNow)));

            interconnectionPassed = allCovered;
            interconnectionMessage = allCovered
                ? $"All {activeInterconnections.Count} active interconnection(s) have signed, current agreements."
                : "One or more active interconnections lack a signed, current agreement.";
        }
        else
        {
            // No interconnections and not certified
            interconnectionPassed = false;
            interconnectionMessage = "No interconnections registered and system is not certified as having none. " +
                "Register interconnections or certify no external interconnections.";
        }

        results.Add(new GateCheckResult
        {
            GateName = "Interconnection Documentation",
            Passed = interconnectionPassed,
            Message = interconnectionMessage,
            Severity = "Error"
        });

        return results;
    }

    /// <summary>Categorize → Select: SecurityCategorization must exist with ≥1 info type.</summary>
    private static IEnumerable<GateCheckResult> CheckCategorizeToSelect(RegisteredSystem system)
    {
        var hasCategorization = system.SecurityCategorization != null;
        var hasInfoTypes = system.SecurityCategorization?.InformationTypes.Any() ?? false;

        yield return new GateCheckResult
        {
            GateName = "Security Categorization Exists",
            Passed = hasCategorization,
            Message = hasCategorization
                ? "FIPS 199 categorization complete."
                : "Security categorization must be performed before baseline selection.",
            Severity = "Error"
        };

        yield return new GateCheckResult
        {
            GateName = "Information Types Defined",
            Passed = hasInfoTypes,
            Message = hasInfoTypes
                ? $"{system.SecurityCategorization!.InformationTypes.Count} information type(s) defined."
                : "At least 1 SP 800-60 information type must be defined.",
            Severity = "Error"
        };
    }

    /// <summary>Select → Implement: ControlBaseline must exist.</summary>
    private static IEnumerable<GateCheckResult> CheckSelectToImplement(RegisteredSystem system)
    {
        var hasBaseline = system.ControlBaseline != null;

        yield return new GateCheckResult
        {
            GateName = "Control Baseline Selected",
            Passed = hasBaseline,
            Message = hasBaseline
                ? $"Baseline '{system.ControlBaseline!.BaselineLevel}' selected with {system.ControlBaseline.TotalControls} controls."
                : "Control baseline must be selected before implementation.",
            Severity = "Error"
        };
    }

    /// <summary>Implement → Assess: ≥80% of baseline controls should have implementation narratives.</summary>
    private async Task<List<GateCheckResult>> CheckImplementToAssessAsync(
        RegisteredSystem system,
        AtoCopilotContext context,
        CancellationToken cancellationToken)
    {
        var results = new List<GateCheckResult>();

        var totalControls = system.ControlBaseline?.TotalControls ?? 0;
        if (totalControls == 0)
        {
            results.Add(new GateCheckResult
            {
                GateName = "Implementation Narratives",
                Passed = false,
                Message = "No control baseline found. Select a baseline before assessment.",
                Severity = "Error"
            });
            return results;
        }

        var narrativeCount = await context.Set<ControlImplementation>()
            .CountAsync(ci => ci.RegisteredSystemId == system.Id
                && !string.IsNullOrEmpty(ci.Narrative), cancellationToken);

        var coverage = (double)narrativeCount / totalControls;
        var passed = coverage >= 0.80;

        results.Add(new GateCheckResult
        {
            GateName = "Implementation Narratives",
            Passed = passed,
            Message = passed
                ? $"{narrativeCount}/{totalControls} controls have narratives ({coverage:P0} coverage)."
                : $"Only {narrativeCount}/{totalControls} controls have narratives ({coverage:P0}). ≥80% required.",
            Severity = "Error"
        });

        return results;
    }

    /// <summary>Assess → Authorize: At least one completed assessment must exist for this system.</summary>
    private async Task<List<GateCheckResult>> CheckAssessToAuthorizeAsync(
        RegisteredSystem system,
        AtoCopilotContext context,
        CancellationToken cancellationToken)
    {
        var results = new List<GateCheckResult>();

        var hasCompletedAssessment = await context.Set<ComplianceAssessment>()
            .AnyAsync(a => a.RegisteredSystemId == system.Id
                && a.Status == AssessmentStatus.Completed, cancellationToken);

        results.Add(new GateCheckResult
        {
            GateName = "Assessment Complete",
            Passed = hasCompletedAssessment,
            Message = hasCompletedAssessment
                ? "At least one completed compliance assessment exists."
                : "No completed assessment found. Run an assessment before authorization.",
            Severity = "Error"
        });

        return results;
    }

    /// <summary>Authorize → Monitor: An active authorization decision must exist.</summary>
    private async Task<List<GateCheckResult>> CheckAuthorizeToMonitorAsync(
        RegisteredSystem system,
        AtoCopilotContext context,
        CancellationToken cancellationToken)
    {
        var results = new List<GateCheckResult>();

        var activeDecision = await context.Set<AuthorizationDecision>()
            .FirstOrDefaultAsync(d => d.RegisteredSystemId == system.Id
                && d.IsActive, cancellationToken);

        var passed = activeDecision != null;

        results.Add(new GateCheckResult
        {
            GateName = "Authorization Decision",
            Passed = passed,
            Message = passed
                ? $"Active {activeDecision!.DecisionType} authorization issued on {activeDecision.DecisionDate:yyyy-MM-dd}."
                : "No active authorization decision found. Issue ATO/ATOwC/IATT before entering Monitor.",
            Severity = "Error"
        });

        return results;
    }
}
