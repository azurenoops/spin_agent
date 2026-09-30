using Ato.Copilot.Core.Dtos.Dashboard;

namespace Ato.Copilot.Core.Interfaces.Compliance;

/// <summary>Reads only the exact authorized cloud/directory/subscription scope; never substitutes empty success on failure.</summary>
public interface ISystemEnvironmentAzureSource
{
    Task<IReadOnlyList<EnvironmentResource>> DiscoverAsync(EnvironmentRegistration registration,
        IReadOnlyList<string> permittedScopes, CancellationToken ct = default);
    Task<EnvironmentCheckState> CheckAccessAsync(ResolvedSystemEnvironmentScope scope,
        EnvironmentScopePurpose purpose, CancellationToken ct = default);
}
