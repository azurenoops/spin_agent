using Microsoft.EntityFrameworkCore;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;

namespace Ato.Copilot.Tests.Unit;

/// <summary>
/// Test helper that wraps a single shared <see cref="AtoCopilotContext"/> and
/// produces it on demand from <see cref="IDbContextFactory{TContext}"/> calls.
/// Returned contexts ignore <c>Dispose</c>/<c>DisposeAsync</c> so a service's
/// <c>await using</c> block does not break subsequent test reads on the same
/// instance. This mirrors a Singleton service consuming a per-method context
/// while preserving the test's single-context seed/assert pattern.
/// </summary>
internal sealed class TestDbContextFactory : IDbContextFactory<AtoCopilotContext>
{
    private readonly NonDisposingAtoCopilotContext _shared;

    public TestDbContextFactory(
        DbContextOptions<AtoCopilotContext> options,
        ITenantContextAccessor? tenantAccessor = null)
        => _shared = new NonDisposingAtoCopilotContext(options, tenantAccessor);

    /// <summary>Underlying shared context (use this in test setup/assertions).</summary>
    public AtoCopilotContext Context => _shared;

    public AtoCopilotContext CreateDbContext() => _shared;

    public Task<AtoCopilotContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<AtoCopilotContext>(_shared);

    private sealed class NonDisposingAtoCopilotContext : AtoCopilotContext
    {
        public NonDisposingAtoCopilotContext(
            DbContextOptions<AtoCopilotContext> options,
            ITenantContextAccessor? tenantAccessor) : base(options, tenantAccessor) { }
        public override void Dispose() { /* no-op: lifetime managed by test */ }
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
