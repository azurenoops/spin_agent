using Xunit;

namespace Ato.Copilot.Tests.Integration;

/// <summary>
/// Serializes all test classes decorated with <c>[Collection("IntegrationTests")]</c>
/// so they run one at a time rather than in parallel.
/// </summary>
/// <remarks>
/// Tests in this collection each boot their own <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{T}"/>
/// via <see cref="Xunit.IAsyncLifetime"/> and mutate process-global environment
/// variables (<c>ASPNETCORE_ENVIRONMENT</c>, <c>ATO_*</c>) in their setup. Running
/// them in parallel causes those variables to stomp on each other and leads to
/// <see cref="System.ObjectDisposedException"/> when one class tears down its host
/// while another is still starting up.
///
/// No <see cref="Xunit.ICollectionFixture{T}"/> is declared here because each test
/// class manages its own factory lifetime through <see cref="Xunit.IAsyncLifetime"/>
/// — a shared fixture would force all classes onto a single host, which conflicts
/// with per-class DB isolation requirements.
/// </remarks>
[CollectionDefinition("IntegrationTests", DisableParallelization = true)]
public sealed class IntegrationTestsCollectionDefinition
{
    // Marker class — no members.
    // xUnit reads the CollectionDefinition attribute via reflection.
}
