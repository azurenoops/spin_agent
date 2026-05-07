using Microsoft.EntityFrameworkCore;
using Ato.Copilot.Core.Data.Context;

namespace Ato.Copilot.Tests.Integration;

/// <summary>
/// Test helper that wraps a single shared <see cref="AtoCopilotContext"/> and
/// produces it on demand from <see cref="IDbContextFactory{TContext}"/> calls.
/// Returned contexts ignore <c>Dispose</c>/<c>DisposeAsync</c> so a service's
/// <c>await using</c> block does not break subsequent test reads on the same
/// instance. This mirrors a Singleton service consuming a per-method context
/// while preserving the test's single-context seed/assert pattern.
/// </summary>
internal sealed class IntegrationTestDbContextFactory : IDbContextFactory<AtoCopilotContext>
{
    private readonly NonDisposingAtoCopilotContext _shared;

    public IntegrationTestDbContextFactory(DbContextOptions<AtoCopilotContext> options)
        => _shared = new NonDisposingAtoCopilotContext(options);

    /// <summary>Underlying shared context (use this in test setup/assertions).</summary>
    public AtoCopilotContext Context => _shared;

    public AtoCopilotContext CreateDbContext() => _shared;

    public Task<AtoCopilotContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<AtoCopilotContext>(_shared);

    private sealed class NonDisposingAtoCopilotContext : AtoCopilotContext
    {
        public NonDisposingAtoCopilotContext(DbContextOptions<AtoCopilotContext> options) : base(options) { }
        public override void Dispose() { /* no-op: lifetime managed by test */ }
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
