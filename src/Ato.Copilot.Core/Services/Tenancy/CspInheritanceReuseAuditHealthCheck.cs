using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Services.Tenancy;

/// <summary>
/// Snapshot of <see cref="IServiceCollection"/> taken at registration time so
/// the post-build <see cref="CspInheritanceReuseAuditHealthCheck"/> can
/// introspect descriptors. <see cref="IServiceCollection"/> itself is not
/// available after <c>BuildServiceProvider</c> runs, so the snapshot is
/// captured as a point-in-time immutable copy and registered as a singleton.
/// </summary>
public sealed class ServiceRegistrationSnapshot
{
    /// <summary>
    /// Immutable copy of every <see cref="ServiceDescriptor"/> in the
    /// collection at snapshot time.
    /// </summary>
    public IReadOnlyList<ServiceDescriptor> Descriptors { get; }

    public ServiceRegistrationSnapshot(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Descriptors = services.ToList().AsReadOnly();
    }
}

/// <summary>
/// FR-110 startup audit (Feature 048): asserts at startup that each FR-110
/// service interface has at most one DI registration. Throws fatally on
/// duplicate registration; no-ops against unregistered interfaces so that
/// types created later in the feature (e.g. <c>ICspAtoDocumentParser</c> at
/// T198 / T202) are auto-enforced once they begin appearing in
/// <see cref="IServiceCollection"/>.
/// </summary>
/// <remarks>
/// <para>
/// Resolution is by interface <see cref="System.Type.FullName"/> (string-based
/// reflection lookup) rather than via static type references — this is
/// intentional: it lets the health check be authored in T218 before T198 /
/// T202 / T204 / T206 / T225 introduce the implementations, and it keeps the
/// enforced list as a flat configuration string array that grows in lockstep
/// with FR-110.
/// </para>
/// <para>
/// Wiring to <c>Program.cs</c> is performed by T228 (Phase 16). T218 only
/// authors the class.
/// </para>
/// </remarks>
public sealed class CspInheritanceReuseAuditHealthCheck : IHostedService
{
    /// <summary>
    /// FR-110 enforced interfaces. Each MUST have exactly one DI registration
    /// (or zero — unregistered interfaces are no-ops until they begin
    /// appearing in <see cref="IServiceCollection"/>).
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><c>ICapabilityMappingService</c> — created by T218; impl wired by T204.</item>
    /// <item><c>IControlNarrativeService</c> — extracted by T218 over <c>NarrativeTemplateService</c>; registered alongside the existing concrete factory.</item>
    /// <item><c>IEvidenceArtifactService</c> — Feature 038, single registration today.</item>
    /// <item><c>IEvidenceStorageService</c> — Feature 038, single registration today.</item>
    /// <item><c>IOrgInheritanceService</c> — Feature 044, single registration today (corrected name; spec said <c>IOrgInheritanceDefaultService</c>).</item>
    /// <item><c>ICspAtoDocumentParser</c> — created by T218 as marker; surface populated by T198; impl wired by T206.</item>
    /// </list>
    /// </remarks>
    public static readonly IReadOnlyList<string> EnforcedInterfaceFullNames = new[]
    {
        "Ato.Copilot.Core.Interfaces.Compliance.ICapabilityMappingService",
        "Ato.Copilot.Core.Interfaces.Compliance.IControlNarrativeService",
        "Ato.Copilot.Core.Interfaces.Compliance.IEvidenceArtifactService",
        "Ato.Copilot.Core.Interfaces.Compliance.IEvidenceStorageService",
        "Ato.Copilot.Core.Interfaces.Compliance.IOrgInheritanceService",
        "Ato.Copilot.Core.Interfaces.Tenancy.ICspAtoDocumentParser",
    };

    private readonly ServiceRegistrationSnapshot _snapshot;
    private readonly ILogger<CspInheritanceReuseAuditHealthCheck> _logger;

    public CspInheritanceReuseAuditHealthCheck(
        ServiceRegistrationSnapshot snapshot,
        ILogger<CspInheritanceReuseAuditHealthCheck> logger)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var offenders = new List<(string FullName, int Count)>();

        foreach (var fullName in EnforcedInterfaceFullNames)
        {
            // String-based lookup — no compile-time type reference required.
            // Counts ALL descriptors whose ServiceType.FullName matches; the
            // raw ServiceCollection allows multiple descriptors per service
            // type (DI resolves the LAST one for non-IEnumerable<T> requests),
            // so > 1 here is a genuine FR-110 violation.
            var count = 0;
            foreach (var descriptor in _snapshot.Descriptors)
            {
                if (string.Equals(descriptor.ServiceType.FullName, fullName, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            if (count > 1)
            {
                offenders.Add((fullName, count));
                _logger.LogCritical(
                    "FR-110 violation: {Interface} has {Count} DI registrations; expected exactly 1.",
                    fullName, count);
            }
            else if (count == 1)
            {
                _logger.LogInformation(
                    "FR-110 audit: {Interface} OK (exactly 1 registration).",
                    fullName);
            }
            else
            {
                _logger.LogDebug(
                    "FR-110 audit: {Interface} not registered — skipping (will enforce once present).",
                    fullName);
            }
        }

        if (offenders.Count > 0)
        {
            var detail = string.Join(", ", offenders.Select(o => $"{o.FullName} ({o.Count})"));
            throw new InvalidOperationException(
                $"FR-110 violation: duplicate DI registrations detected for FR-110 reuse-audit services: {detail}. " +
                "Each enforced interface must have exactly one registration. " +
                "See specs/048-tenant-isolation/research-reuse-audit.md for the canonical reuse list.");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Extension that registers the FR-110 reuse-audit snapshot + hosted service.
/// Called from <c>Program.cs</c> startup by T228.
/// </summary>
public static class CspInheritanceReuseAuditServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ServiceRegistrationSnapshot"/> and
    /// <see cref="CspInheritanceReuseAuditHealthCheck"/>. MUST be called
    /// AFTER all other DI registrations so the snapshot captures the full
    /// service collection.
    /// </summary>
    public static IServiceCollection AddCspInheritanceReuseAudit(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(new ServiceRegistrationSnapshot(services));
        services.AddHostedService<CspInheritanceReuseAuditHealthCheck>();
        return services;
    }
}
