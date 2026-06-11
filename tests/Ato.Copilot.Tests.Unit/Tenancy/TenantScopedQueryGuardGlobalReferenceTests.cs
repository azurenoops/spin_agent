using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Interceptors;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Tenancy;

/// <summary>
/// Issue #396 — Regression tests for <see cref="TenantScopedQueryGuardInterceptor"/>
/// <c>[GlobalReference]</c> exemption.
///
/// <para>
/// <c>TenantResolutionMiddleware</c> Stage A0 reads <c>CspProfile</c> (a
/// <c>[GlobalReference]</c> entity) BEFORE <c>accessor.Push(ctx)</c> runs at
/// Stage E. Without the exemption, the guard interceptor throws and every
/// API call returns HTTP 500 on cold start (cache miss).
/// </para>
///
/// <para>
/// Three correctness properties are tested:
/// <list type="number">
///   <item>A SELECT against a <c>[GlobalReference]</c> table (e.g. <c>CspProfiles</c>,
///   <c>Tenants</c>) succeeds when <c>accessor.Current == null</c> inside an HTTP
///   request.</item>
///   <item>A SELECT against a <c>[TenantScoped]</c> table still throws when
///   <c>accessor.Current == null</c> inside an HTTP request — the guard remains
///   active for tenant-scoped data.</item>
///   <item>All queries succeed when <c>accessor.Current != null</c> (fast early-
///   return path, unaffected by this change).</item>
/// </list>
/// </para>
/// </summary>
public class TenantScopedQueryGuardGlobalReferenceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _sp = null!;
    private Mock<IHttpContextAccessor> _httpContextAccessorMock = null!;
    private TenantContextAccessor _accessor = null!;

    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        await _connection.OpenAsync();

        _httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(new DefaultHttpContext());

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContextAccessor, TenantContextAccessor>();
        services.AddSingleton(_httpContextAccessorMock.Object);
        services.AddSingleton<TenantScopedQueryGuardInterceptor>();
        services.AddDbContext<AtoCopilotContext>((sp, opt) =>
        {
            opt.UseSqlite(_connection);
            opt.AddInterceptors(sp.GetRequiredService<TenantScopedQueryGuardInterceptor>());
        });

        _sp = services.BuildServiceProvider();
        _accessor = (TenantContextAccessor)_sp.GetRequiredService<ITenantContextAccessor>();

        // Create schema
        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        await db.Database.EnsureCreatedAsync();

        // Seed a Tenant row (GlobalReference) for look-up tests.
        db.Tenants.Add(new Tenant
        {
            Id = TenantId,
            DisplayName = "Test Tenant",
            CreatedBy = "seed",
        });

        // Seed a CspProfile row (GlobalReference). CspProfile has no TenantId column —
        // it is a singleton cross-tenant record.
        db.CspProfiles.Add(new CspProfile
        {
            LegalEntityName = "ACME DoD LLC",
            DisplayName = "ACME",
            CreatedBy = "seed",
        });

        // Seed an Organization row (TenantScoped) so the SELECT can find rows.
        using (_accessor.Push(new TenantContext(TenantId)))
        {
            db.Organizations.Add(new Organization
            {
                TenantId = TenantId,
                Name = "Test Org",
                CreatedBy = "seed",
            });
            await db.SaveChangesAsync();
        }
    }

    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // ─── [GlobalReference] queries must succeed without tenant context ────

    /// <summary>
    /// Reproduces the Stage A0 production scenario: HTTP request is active,
    /// accessor.Current is null, but the query targets <c>CspProfile</c>
    /// (<c>[GlobalReference]</c>). The guard MUST NOT throw.
    /// </summary>
    [Fact]
    public async Task GlobalReference_CspProfile_SucceedsWithoutTenantContext()
    {
        // Arrange — HTTP context active, NO tenant pushed (mimics Stage A0)
        _accessor.Current.Should().BeNull("precondition: no tenant context pushed");

        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        // Act — read CspProfile (GlobalReference) with no tenant context
        Func<Task> act = async () =>
        {
            _ = await db.CspProfiles.FirstOrDefaultAsync();
        };

        // Assert — must NOT throw; guard exempts [GlobalReference] entities
        await act.Should().NotThrowAsync(
            because: "CspProfile is [GlobalReference] and must be readable before tenant resolution");
    }

    /// <summary>
    /// <c>Tenant</c> is also <c>[GlobalReference]</c>. Verify the exemption
    /// applies to all global-reference tables, not only <c>CspProfile</c>.
    /// </summary>
    [Fact]
    public async Task GlobalReference_Tenant_SucceedsWithoutTenantContext()
    {
        _accessor.Current.Should().BeNull("precondition: no tenant context pushed");

        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        Func<Task> act = async () =>
        {
            _ = await db.Tenants.FirstOrDefaultAsync();
        };

        await act.Should().NotThrowAsync(
            because: "Tenant is [GlobalReference] and must be readable without a tenant context");
    }

    // ─── [TenantScoped] queries must still throw without tenant context ───

    /// <summary>
    /// The guard MUST still fire for <c>[TenantScoped]</c> entities when
    /// <c>accessor.Current == null</c> inside an HTTP request. The exemption
    /// only covers <c>[GlobalReference]</c> — it must not weaken the guard
    /// for any tenant-scoped data.
    /// </summary>
    [Fact]
    public async Task TenantScoped_ThrowsWhenNoTenantContext()
    {
        // Arrange — HTTP context active, NO tenant pushed
        _accessor.Current.Should().BeNull("precondition: no tenant context pushed");

        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        // Act — read Organizations (TenantScoped) with no tenant context
        Func<Task> act = async () =>
        {
            _ = await db.Organizations.FirstOrDefaultAsync();
        };

        // Assert — guard MUST throw: Organization is [TenantScoped]
        await act.Should().ThrowAsync<InvalidOperationException>(
            because: "Organization is [TenantScoped] and the guard must block reads without a tenant context")
            .WithMessage("*[SEC]*");
    }

    // ─── Guard is still bypassed entirely when accessor is populated ──────

    /// <summary>
    /// With <c>accessor.Current != null</c> (normal request path after Stage E),
    /// the guard is bypassed via the fast early-return. Both <c>[GlobalReference]</c>
    /// and <c>[TenantScoped]</c> queries succeed.
    /// </summary>
    [Fact]
    public async Task AllEntities_SucceedWhenTenantContextPushed()
    {
        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        using (_accessor.Push(new TenantContext(TenantId)))
        {
            Func<Task> actGlobal = async () =>
            {
                _ = await db.CspProfiles.FirstOrDefaultAsync();
            };

            Func<Task> actScoped = async () =>
            {
                _ = await db.Organizations.FirstOrDefaultAsync();
            };

            await actGlobal.Should().NotThrowAsync(
                because: "tenant context is live; guard early-returns regardless of entity type");

            await actScoped.Should().NotThrowAsync(
                because: "tenant context is live; guard early-returns regardless of entity type");
        }
    }

    // ─── No HTTP context → guard is never triggered ───────────────────────

    /// <summary>
    /// Background services (no <c>HttpContext</c>) are always allowed through.
    /// This is existing Allow-list 1 behaviour and must remain intact.
    /// </summary>
    [Fact]
    public async Task NoHttpContext_NeverThrows_EvenForTenantScoped()
    {
        // Arrange — simulate a background service (no HttpContext)
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns((HttpContext?)null);

        try
        {
            _accessor.Current.Should().BeNull("precondition: no tenant context pushed");

            await using var scope = _sp.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

            Func<Task> act = async () =>
            {
                _ = await db.Organizations.FirstOrDefaultAsync();
            };

            await act.Should().NotThrowAsync(
                because: "no HttpContext means background service; guard must not fire");
        }
        finally
        {
            // Restore for other tests in this class
            _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(new DefaultHttpContext());
        }
    }
}
