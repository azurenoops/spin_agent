using Xunit;

namespace Ato.Copilot.Tests.Integration.Auth;

/// <summary>
/// Forces all Feature 051 auth integration test classes onto a single xUnit
/// collection so they execute sequentially and share exactly one
/// <see cref="LoginAuthTestFactory"/> instance for the full collection lifetime.
/// </summary>
/// <remarks>
/// Without this definition each test class that declares
/// <c>IClassFixture&lt;LoginAuthTestFactory&gt;</c> gets its own factory instance
/// which is created and then disposed as soon as the class finishes. Because
/// <see cref="LoginAuthTestFactory"/> inherits
/// <see cref="Tenancy.MultiTenantWebApplicationFactory{TStartup}"/> and mutates
/// process-global environment variables (<c>ATO_*</c>) in its constructor, multiple
/// short-lived factories racing in parallel stomp on each other's env-var state and
/// the disposed instance's service provider throws
/// <see cref="ObjectDisposedException"/> on the next class.
///
/// Using <see cref="ICollectionFixture{T}"/> here means xUnit creates exactly ONE
/// <see cref="LoginAuthTestFactory"/> — alive from the first class through the last —
/// and disposes it only after all classes finish. Test classes decorated with
/// <c>[Collection("Auth")]</c> no longer need to declare
/// <c>IClassFixture&lt;LoginAuthTestFactory&gt;</c>; xUnit injects the shared
/// instance by constructor-parameter type matching.
/// </remarks>
[CollectionDefinition("Auth", DisableParallelization = true)]
public sealed class AuthTestCollectionDefinition
    : ICollectionFixture<LoginAuthTestFactory>
{
    // Marker class — no members beyond the ICollectionFixture hook.
    // xUnit reads the attribute and interface via reflection.
}
