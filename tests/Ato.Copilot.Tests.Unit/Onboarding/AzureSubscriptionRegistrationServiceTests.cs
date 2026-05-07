using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Ato.Copilot.Agents.Compliance.Services.Onboarding.AzureSubscriptions;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Tests.Unit.Onboarding;

/// <summary>
/// Unit tests for <see cref="AzureSubscriptionRegistrationService"/> (T095 / FR-074).
/// Replace-set semantics: previously-selected-but-no-longer-visible subscriptions
/// are flagged Unavailable rather than removed; admin-deselected (still visible)
/// rows are hard-removed.
/// </summary>
public class AzureSubscriptionRegistrationServiceTests : IDisposable
{
    private readonly TestDbContextFactory _factory;
    private readonly Mock<IWizardAuditService> _audit = new();
    private readonly AzureSubscriptionRegistrationService _sut;

    public AzureSubscriptionRegistrationServiceTests()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"AzSubReg_{Guid.NewGuid()}")
            .Options;
        _factory = new TestDbContextFactory(options);
        var optsWrapper = Options.Create(new OnboardingOptions());
        _sut = new AzureSubscriptionRegistrationService(
            _factory, _audit.Object, optsWrapper,
            NullLogger<AzureSubscriptionRegistrationService>.Instance);
    }

    public void Dispose()
    {
        using var db = _factory.CreateDbContext();
        db.Database.EnsureDeleted();
    }

    [Fact]
    public async Task ReplaceAsync_FreshSelection_PersistsAndAudits()
    {
        var tenantId = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var sub1 = Guid.NewGuid();
        var visible = new List<AzureSubscriptionInfo>
        {
            new(sub1, "Sub One", Guid.NewGuid(), AzureEnvironment.AzureCloud),
        };

        var rows = await _sut.ReplaceAsync(tenantId, new[] { sub1 }, visible, actor);

        rows.Should().HaveCount(1);
        rows[0].Status.Should().Be(SubscriptionStatus.Selected);
        _audit.Verify(a => a.RecordAsync(
            tenantId, actor, WizardAuditAction.SubscriptionsSelected,
            It.IsAny<string>(), null,
            null, It.IsAny<string>(), null,
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReplaceAsync_PreviouslySelectedNoLongerVisible_FlaggedUnavailable()
    {
        var tenantId = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var stillVisible = Guid.NewGuid();
        var nowInvisible = Guid.NewGuid();

        await using (var seed = _factory.CreateDbContext())
        {
            seed.AzureSubscriptionRegistrations.AddRange(
                new AzureSubscriptionRegistration
                {
                    Id = Guid.NewGuid(), TenantId = tenantId,
                    SubscriptionId = stillVisible,
                    DisplayName = "A", Status = SubscriptionStatus.Selected,
                },
                new AzureSubscriptionRegistration
                {
                    Id = Guid.NewGuid(), TenantId = tenantId,
                    SubscriptionId = nowInvisible,
                    DisplayName = "B", Status = SubscriptionStatus.Selected,
                });
            await seed.SaveChangesAsync();
        }

        var visibleNow = new List<AzureSubscriptionInfo>
        {
            new(stillVisible, "A", Guid.NewGuid(), AzureEnvironment.AzureCloud),
        };

        var rows = await _sut.ReplaceAsync(
            tenantId,
            new[] { stillVisible, nowInvisible }, // admin keeps both selected
            visibleNow, actor);

        rows.Should().HaveCount(2);
        rows.Single(r => r.SubscriptionId == stillVisible).Status
            .Should().Be(SubscriptionStatus.Selected);
        rows.Single(r => r.SubscriptionId == nowInvisible).Status
            .Should().Be(SubscriptionStatus.Unavailable, "not visible to user → preserved as Unavailable per FR-074");
    }

    [Fact]
    public async Task ReplaceAsync_AdminDropsVisibleSub_HardRemoves()
    {
        var tenantId = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var keep = Guid.NewGuid();
        var drop = Guid.NewGuid();

        await using (var seed = _factory.CreateDbContext())
        {
            seed.AzureSubscriptionRegistrations.AddRange(
                new AzureSubscriptionRegistration
                {
                    Id = Guid.NewGuid(), TenantId = tenantId,
                    SubscriptionId = keep, DisplayName = "Keep",
                    Status = SubscriptionStatus.Selected,
                },
                new AzureSubscriptionRegistration
                {
                    Id = Guid.NewGuid(), TenantId = tenantId,
                    SubscriptionId = drop, DisplayName = "Drop",
                    Status = SubscriptionStatus.Selected,
                });
            await seed.SaveChangesAsync();
        }

        var visible = new List<AzureSubscriptionInfo>
        {
            new(keep, "Keep", Guid.NewGuid(), AzureEnvironment.AzureCloud),
            new(drop, "Drop", Guid.NewGuid(), AzureEnvironment.AzureCloud),
        };

        var rows = await _sut.ReplaceAsync(tenantId, new[] { keep }, visible, actor);

        rows.Should().HaveCount(1);
        rows.Single().SubscriptionId.Should().Be(keep);
    }

    [Fact]
    public async Task ReplaceAsync_SelectedNotInVisible_Throws()
    {
        var tenantId = Guid.NewGuid();
        var bogus = Guid.NewGuid();
        var visible = new List<AzureSubscriptionInfo>();
        var act = async () => await _sut.ReplaceAsync(tenantId, new[] { bogus }, visible, Guid.NewGuid());
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private sealed class TestDbContextFactory : IDbContextFactory<AtoCopilotContext>
    {
        private readonly DbContextOptions<AtoCopilotContext> _options;
        public TestDbContextFactory(DbContextOptions<AtoCopilotContext> options) => _options = options;
        public AtoCopilotContext CreateDbContext() => new(_options);
    }
}
