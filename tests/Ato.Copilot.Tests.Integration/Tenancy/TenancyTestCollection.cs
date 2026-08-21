using Ato.Copilot.Mcp;
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
/// <remarks>
/// Implements <see cref="ICollectionFixture{T}"/> so xUnit creates exactly ONE
/// <see cref="MultiTenantWebApplicationFactory{McpProgram}"/> instance for the
/// entire collection — alive from the first test class through the last — and
/// disposes it only after all classes finish. This eliminates the
/// <see cref="ObjectDisposedException"/> that occurred when xUnit disposed a
/// per-class fixture and then immediately injected the same (now-disposed) instance
/// into the next class's constructor via <c>IClassFixture&lt;&gt;</c>. Test classes
/// that previously declared <c>IClassFixture&lt;MultiTenantWebApplicationFactory&lt;McpProgram&gt;&gt;</c>
/// no longer need to do so; xUnit injects the shared collection fixture by
/// constructor-parameter type matching.
/// </remarks>
[CollectionDefinition("Tenancy", DisableParallelization = true)]
public sealed class TenancyTestCollectionDefinition
    : ICollectionFixture<MultiTenantWebApplicationFactory<McpProgram>>
{
    // Marker class — no members beyond the ICollectionFixture hook.
    // xUnit reads the attribute and interface via reflection.
}
