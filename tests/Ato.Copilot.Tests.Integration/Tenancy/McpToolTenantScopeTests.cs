using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

/// <summary>
/// T039 [US1]: Invokes a representative MCP-tool query path under two
/// different <see cref="ITenantContext"/> scopes and asserts the result
/// sets are disjoint.
/// </summary>
/// <remarks>
/// In the absence of a full MCP server harness in this test, we exercise
/// the same surface every MCP tool relies on: <c>AtoCopilotContext</c> with
/// the active <see cref="ITenantContextAccessor"/>. The "tool" here is a
/// minimal lambda that returns the names of all <see cref="Organization"/>
/// rows the current tenant can see — equivalent to <c>list-organizations</c>.
/// </remarks>
public class McpToolTenantScopeTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _sp = null!;
    private TenantContextAccessor _accessor = null!;
    private static readonly Guid TenantA = Guid.Parse("11111111-eeee-eeee-eeee-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-eeee-eeee-eeee-222222222222");

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddSingleton<ITenantContextAccessor, TenantContextAccessor>();
        services.AddDbContext<AtoCopilotContext>(opt => opt.UseSqlite(_connection));
        _sp = services.BuildServiceProvider();
        _accessor = (TenantContextAccessor)_sp.GetRequiredService<ITenantContextAccessor>();

        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        await db.Database.EnsureCreatedAsync();

        db.Tenants.Add(new Tenant { Id = TenantA, DisplayName = "Coastal", CreatedBy = "test" });
        db.Tenants.Add(new Tenant { Id = TenantB, DisplayName = "Eagle",   CreatedBy = "test" });

        db.Organizations.Add(new Organization { Id = Guid.NewGuid(), TenantId = TenantA, Name = "Coastal-Air", CreatedBy = "test" });
        db.Organizations.Add(new Organization { Id = Guid.NewGuid(), TenantId = TenantA, Name = "Coastal-Sea", CreatedBy = "test" });
        db.Organizations.Add(new Organization { Id = Guid.NewGuid(), TenantId = TenantB, Name = "Eagle-Land",  CreatedBy = "test" });

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        await _connection.DisposeAsync();
    }

    /// <summary>
    /// Stand-in for an MCP tool: returns all visible organization names.
    /// </summary>
    private async Task<List<string>> ListOrganizationNamesAsync()
    {
        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        return await db.Organizations.AsNoTracking()
            .OrderBy(o => o.Name)
            .Select(o => o.Name)
            .ToListAsync();
    }

    [Fact]
    public async Task McpTool_UnderDifferentTenantScopes_ReturnsDisjointResults()
    {
        List<string> coastalNames;
        using (_accessor.Push(new TenantContext(TenantA)))
        {
            coastalNames = await ListOrganizationNamesAsync();
        }

        List<string> eagleNames;
        using (_accessor.Push(new TenantContext(TenantB)))
        {
            eagleNames = await ListOrganizationNamesAsync();
        }

        coastalNames.Should().BeEquivalentTo(new[] { "Coastal-Air", "Coastal-Sea" });
        eagleNames.Should().BeEquivalentTo(new[] { "Eagle-Land" });
        coastalNames.Should().NotIntersectWith(eagleNames,
            "tenant scoping must keep MCP-tool responses disjoint");
    }
}
