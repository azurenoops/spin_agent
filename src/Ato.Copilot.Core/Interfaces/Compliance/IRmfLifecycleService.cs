using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Interfaces.Compliance;

/// <summary>
/// Service for RMF lifecycle management: system registration, RMF step transitions,
/// and gate condition validation per DoDI 8510.01.
/// </summary>
public interface IRmfLifecycleService
{
    /// <summary>
    /// Register a new information system for RMF processing.
    /// Creates the <see cref="RegisteredSystem"/> entity with initial state of Prepare.
    /// </summary>
    /// <param name="name">System name (required, max 200 chars).</param>
    /// <param name="systemType">System type per DoDI 8510.01.</param>
    /// <param name="missionCriticality">Mission criticality designation.</param>
    /// <param name="hostingEnvironment">Hosting environment (e.g., "Azure Government").</param>
    /// <param name="createdBy">Identity of the user registering the system.</param>
    /// <param name="acronym">Optional system acronym.</param>
    /// <param name="description">Optional system description.</param>
    /// <param name="azureProfile">Optional Azure environment profile for cloud-hosted systems.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created RegisteredSystem entity.</returns>
    Task<RegisteredSystem> RegisterSystemAsync(
        string name,
        SystemType systemType,
        MissionCriticality missionCriticality,
        string hostingEnvironment,
        string createdBy,
        string? acronym = null,
        string? description = null,
        AzureEnvironmentProfile? azureProfile = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a registered system by ID, including navigation properties.
    /// </summary>
    /// <param name="systemId">System ID (GUID string).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="GetSystemResult"/> discriminating between:
    /// <list type="bullet">
    ///   <item><see cref="GetSystemResult.Found"/> — system exists and has a control baseline.</item>
    ///   <item><see cref="GetSystemResult.NotFound"/> — no row matches the provided input.</item>
    ///   <item><see cref="GetSystemResult.NoBaseline"/> — row exists but <c>ControlBaseline</c> is null.</item>
    /// </list>
    /// </returns>
    Task<GetSystemResult> GetSystemAsync(string systemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// List registered systems with pagination and optional active-only filter.
    /// Results are scoped to the caller's RBAC role.
    /// </summary>
    /// <param name="activeOnly">If true, only return active systems.</param>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Results per page (default 20, max 100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Paginated list of registered systems.</returns>
    Task<(IReadOnlyList<RegisteredSystem> Systems, int TotalCount)> ListSystemsAsync(
        bool activeOnly = true,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Advance a system to a new RMF step with gate condition validation.
    /// Forward movement checks gate conditions; backward movement requires force flag.
    /// </summary>
    /// <param name="systemId">System ID.</param>
    /// <param name="targetStep">Target RMF phase.</param>
    /// <param name="force">If true, override gate failures (audit-logged).</param>
    /// <param name="userId">Identity of the user performing the transition.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing updated system, gate check results, and any warnings.</returns>
    Task<RmfStepAdvanceResult> AdvanceRmfStepAsync(
        string systemId,
        RmfPhase targetStep,
        bool force = false,
        string? userId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check gate conditions for advancing to a target step without actually performing the transition.
    /// </summary>
    /// <param name="systemId">System ID.</param>
    /// <param name="targetStep">Target RMF phase to check gates for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of gate check results.</returns>
    Task<IReadOnlyList<GateCheckResult>> CheckGateConditionsAsync(
        string systemId,
        RmfPhase targetStep,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Discriminated union result for <see cref="IRmfLifecycleService.GetSystemAsync"/>.
/// </summary>
public abstract record GetSystemResult
{
    /// <summary>System was found and has a control baseline assigned.</summary>
    public record Found(RegisteredSystem System) : GetSystemResult;

    /// <summary>No system row matches the provided input.</summary>
    public record NotFound(string Input) : GetSystemResult;

    /// <summary>System row exists but <see cref="RegisteredSystem.ControlBaseline"/> is null.</summary>
    public record NoBaseline(RegisteredSystem System) : GetSystemResult;
}

/// <summary>
/// Result of an RMF step advance operation.
/// </summary>
public class RmfStepAdvanceResult
{
    /// <summary>Whether the advance succeeded.</summary>
    public bool Success { get; set; }

    /// <summary>The updated system (null if advance failed).</summary>
    public RegisteredSystem? System { get; set; }

    /// <summary>Previous RMF step.</summary>
    public RmfPhase PreviousStep { get; set; }

    /// <summary>New RMF step (same as previous if advance failed).</summary>
    public RmfPhase NewStep { get; set; }

    /// <summary>Gate check results.</summary>
    public IReadOnlyList<GateCheckResult> GateResults { get; set; } = Array.Empty<GateCheckResult>();

    /// <summary>Error message if advance failed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Whether the advance was forced (gate failures overridden).</summary>
    public bool WasForced { get; set; }
}

/// <summary>
/// Result of a single gate condition check.
/// </summary>
public class GateCheckResult
{
    /// <summary>Gate name (e.g., "Roles Assigned", "Boundary Defined").</summary>
    public string GateName { get; set; } = string.Empty;

    /// <summary>Whether this gate passed.</summary>
    public bool Passed { get; set; }

    /// <summary>Descriptive message about the gate result.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Severity level: "Error" (blocks), "Warning" (advisory), "Info".</summary>
    public string Severity { get; set; } = "Error";
}
