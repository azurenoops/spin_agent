using Ato.Copilot.Mcp;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

/// <summary>
/// Dedicated collection for tests that wipe <c>CspProfile</c> on the shared
/// host. They must not share <see cref="TenancyTestCollectionDefinition"/>'s
/// factory — <c>ResetCspProfileAsync</c> in a class ctor left later Tenancy
/// classes returning <c>503 CSP_ONBOARDING_INCOMPLETE</c>
/// (CI 33570305880, debug 225414 H25).
/// </summary>
[CollectionDefinition("TenancyCspOnboarding", DisableParallelization = true)]
public sealed class TenancyCspOnboardingCollectionDefinition
    : ICollectionFixture<MultiTenantWebApplicationFactory<McpProgram>>
{
}
