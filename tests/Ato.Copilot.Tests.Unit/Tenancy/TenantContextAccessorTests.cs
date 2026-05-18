using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Tenancy;

/// <summary>
/// T032: Verifies <see cref="TenantContextAccessor"/>'s push-pop lifecycle and
/// AsyncLocal flow. Catches regressions where the AsyncLocal scope leaks
/// across async boundaries.
/// </summary>
public class TenantContextAccessorTests
{
    [Fact]
    public void Current_IsNull_BeforePush()
    {
        var accessor = new TenantContextAccessor();
        accessor.Current.Should().BeNull();
    }

    [Fact]
    public void Push_SetsCurrent_AndDisposePops()
    {
        var accessor = new TenantContextAccessor();
        var ctx = new TenantContext(Guid.NewGuid());

        using (accessor.Push(ctx))
        {
            accessor.Current.Should().BeSameAs(ctx);
        }

        accessor.Current.Should().BeNull();
    }

    [Fact]
    public void Push_Nested_RestoresPreviousOnDispose()
    {
        var accessor = new TenantContextAccessor();
        var outer = new TenantContext(Guid.NewGuid());
        var inner = new TenantContext(Guid.NewGuid());

        using (accessor.Push(outer))
        {
            accessor.Current.Should().BeSameAs(outer);

            using (accessor.Push(inner))
            {
                accessor.Current.Should().BeSameAs(inner);
            }

            accessor.Current.Should().BeSameAs(outer);
        }

        accessor.Current.Should().BeNull();
    }

    [Fact]
    public async Task Push_FlowsAcrossTaskRunBoundary()
    {
        var accessor = new TenantContextAccessor();
        var ctx = new TenantContext(Guid.NewGuid());

        using (accessor.Push(ctx))
        {
            // The AsyncLocal should propagate into the spawned task.
            var observed = await Task.Run(() => accessor.Current);
            observed.Should().BeSameAs(ctx);
        }
    }

    [Fact]
    public async Task Push_DoesNotLeak_AcrossSiblingAsyncCalls()
    {
        var accessor = new TenantContextAccessor();
        var ctxA = new TenantContext(Guid.NewGuid());
        var ctxB = new TenantContext(Guid.NewGuid());

        async Task<Guid> WorkAsync(ITenantContext c)
        {
            using (accessor.Push(c))
            {
                await Task.Yield();
                return accessor.Current!.TenantId;
            }
        }

        var t1 = WorkAsync(ctxA);
        var t2 = WorkAsync(ctxB);

        var results = await Task.WhenAll(t1, t2);

        results[0].Should().Be(ctxA.TenantId);
        results[1].Should().Be(ctxB.TenantId);

        // After both complete, the ambient is back to null.
        accessor.Current.Should().BeNull();
    }

    [Fact]
    public void Push_NullContext_Throws()
    {
        var accessor = new TenantContextAccessor();
        Action act = () => accessor.Push(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TenantContext_EffectiveTenantId_FallsBackToTenantId_WhenNotImpersonating()
    {
        var tenantId = Guid.NewGuid();
        var ctx = new TenantContext(tenantId, isCspAdmin: false, impersonatedTenantId: null);

        ctx.EffectiveTenantId.Should().Be(tenantId);
    }

    [Fact]
    public void TenantContext_EffectiveTenantId_UsesImpersonatedId_WhenSet()
    {
        var tenantId = Guid.NewGuid();
        var impersonated = Guid.NewGuid();
        var ctx = new TenantContext(tenantId, isCspAdmin: true, impersonatedTenantId: impersonated);

        ctx.EffectiveTenantId.Should().Be(impersonated);
    }
}
