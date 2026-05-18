using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

/// <summary>
/// T091 [US4]: Verifies that two distinct tenants can register the *same*
/// Azure subscription id concurrently (FR-040). Pre-Feature-048 the
/// <c>AzureSubscriptionRegistrations</c> table treated <c>SubscriptionId</c>
/// as globally unique, which would have made multi-tenant onboarding
/// impossible when an organization shared a subscription across business
/// units. The composite unique constraint on
/// <c>(TenantId, SubscriptionId)</c> (registered in
/// <c>AtoCopilotContext.OnModelCreating</c>) keeps the per-tenant uniqueness
/// guarantee while allowing cross-tenant overlap.
/// </summary>
[Collection("Tenancy")]
public class CrossTenantSubscriptionRegistrationTests
    : IClassFixture<MultiTenantWebApplicationFactory<McpProgram>>
{
    private readonly MultiTenantWebApplicationFactory<McpProgram> _factory;

    public CrossTenantSubscriptionRegistrationTests(MultiTenantWebApplicationFactory<McpProgram> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SameSubscriptionId_RegisteredAcrossTwoTenants_BothPersist()
    {
        var tenantA = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        var tenantB = MultiTenantWebApplicationFactory<McpProgram>.TenantBId;
        var sharedSubscription = Guid.Parse("aabbccdd-aabb-ccdd-eeff-001122334455");

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var existing = await db.AzureSubscriptionRegistrations
                .Where(r => r.SubscriptionId == sharedSubscription)
                .ToListAsync();
            db.AzureSubscriptionRegistrations.RemoveRange(existing);
            await db.SaveChangesAsync();
        }

        // Register the subscription under Tenant A.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.AzureSubscriptionRegistrations.Add(new AzureSubscriptionRegistration
            {
                Id = Guid.NewGuid(),
                TenantId = tenantA,
                SubscriptionId = sharedSubscription,
                DisplayName = "Shared Sub (A)",
                ParentTenantId = Guid.NewGuid(),
                Status = SubscriptionStatus.Selected,
            });
            await db.SaveChangesAsync();
        }

        // Register the same subscription under Tenant B — must succeed.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.AzureSubscriptionRegistrations.Add(new AzureSubscriptionRegistration
            {
                Id = Guid.NewGuid(),
                TenantId = tenantB,
                SubscriptionId = sharedSubscription,
                DisplayName = "Shared Sub (B)",
                ParentTenantId = Guid.NewGuid(),
                Status = SubscriptionStatus.Selected,
            });
            await db.SaveChangesAsync();
        }

        // Re-registering under Tenant A *again* must violate the (TenantId, SubscriptionId)
        // composite uniqueness — proving per-tenant uniqueness still holds.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.AzureSubscriptionRegistrations.Add(new AzureSubscriptionRegistration
            {
                Id = Guid.NewGuid(),
                TenantId = tenantA,
                SubscriptionId = sharedSubscription,
                DisplayName = "Duplicate (A)",
                ParentTenantId = Guid.NewGuid(),
                Status = SubscriptionStatus.Selected,
            });

            var saveDuplicate = async () => await db.SaveChangesAsync();
            await saveDuplicate.Should().ThrowAsync<DbUpdateException>(
                "the (TenantId, SubscriptionId) composite unique index forbids per-tenant duplicates.");
        }

        // Final assertion — both rows survived.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var rows = await db.AzureSubscriptionRegistrations
                .AsNoTracking()
                .Where(r => r.SubscriptionId == sharedSubscription)
                .OrderBy(r => r.TenantId)
                .ToListAsync();

            rows.Should().HaveCount(2);
            rows.Select(r => r.TenantId).Should().BeEquivalentTo(new[] { tenantA, tenantB });
        }
    }
}
