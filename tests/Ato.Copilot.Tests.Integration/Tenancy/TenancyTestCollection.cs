using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

/// <summary>
/// Forces all Feature 048 tenancy integration test classes onto a single
/// xUnit collection so they execute sequentially. Required because
/// <see cref="MultiTenantWebApplicationFactory{TStartup}"/> mutates
/// <c>process</c>-wide environment variables (<c>ATO_*</c>) in its constructor
/// to override the MCP host's configuration; if two factories construct
/// in parallel they stomp on each other's settings.
/// </summary>
[CollectionDefinition("Tenancy", DisableParallelization = true)]
public sealed class TenancyTestCollectionDefinition
{
    // Marker class — no members. xUnit reads the attribute via reflection.
}
