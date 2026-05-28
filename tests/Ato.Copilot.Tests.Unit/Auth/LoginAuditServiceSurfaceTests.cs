using System.Reflection;
using Ato.Copilot.Core.Interfaces.Auth;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Auth;

/// <summary>
/// Feature 051 T087 [US10] — reflection guard for
/// <see cref="ILoginAuditService"/>. Pins the public surface to the
/// three append + read methods defined in
/// <c>contracts/internal-services.md § 1.4</c>:
/// <c>{ AppendAsync, ListAsync, ListSystemTenantAsync }</c>. Adding any
/// other public method — for example <c>UpdateAsync</c> or
/// <c>DeleteAsync</c> — would silently break the append-only contract
/// and is blocked by this test.
/// </summary>
/// <remarks>
/// Originally lived in <c>LoginAuditServiceAppendTests</c> per Phase 2;
/// promoted to its own file in Phase 7 (T087) so the surface guard is
/// discoverable independently of the write-path coverage.
/// </remarks>
public sealed class LoginAuditServiceSurfaceTests
{
    [Fact]
    public void Interface_HasExactlyThreePublicMethods()
    {
        // Arrange
        var methods = typeof(ILoginAuditService)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .ToHashSet();

        // Act + Assert
        methods.Should().BeEquivalentTo(
            new[] { "AppendAsync", "ListAsync", "ListSystemTenantAsync" },
            "contracts/internal-services.md § 1.4 — no Update or Delete on the surface.");
    }
}
