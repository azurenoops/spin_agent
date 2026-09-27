using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Services.ProviderAuthorizations;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.ProviderAuthorizations;

public sealed class ProviderMonitoringWorkerTests
{
    [Fact]
    public async Task Existing_watch_scheduler_uses_isolated_provider_context_without_changing_request_identity()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var original = new TenantContext(Guid.NewGuid()) { PersonId = Guid.NewGuid() };
        var personId = original.PersonId;
        var accessor = new TenantContextAccessor();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContextAccessor>(accessor);
        services.AddScoped<ITenantContext>(_ => original);
        services.AddDbContextFactory<AtoCopilotContext>(b => b.UseSqlite(connection));
        services.AddScoped<ProviderAuthorizationStore>();
        services.AddScoped<ProviderMonitoringService>();
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<AtoCopilotContext>>();
        var providerId = Guid.NewGuid();
        var offeringId = Guid.NewGuid();
        var evidenceId = Guid.NewGuid();
        var admin = new TenantContext(Guid.NewGuid(), isCspAdmin: true);
        using (accessor.Push(admin))
        {
            await using var db = await factory.CreateDbContextAsync();
            await db.Database.EnsureCreatedAsync();
            db.CspProfiles.Add(new() { Id = providerId, OnboardingState = Ato.Copilot.Core.Models.Tenancy.OnboardingState.Active });
            db.Add(new ProviderOffering { Id = offeringId, ProviderId = providerId, OfferingId = offeringId });
            var finding = new ProviderFinding { ProviderId = providerId, OfferingId = offeringId };
            db.Add(finding);
            db.Add(new ProviderFindingEvidence { Id = evidenceId, ProviderId = providerId, OfferingId = offeringId, FindingId = finding.Id,
                FileName = "reviewed.txt", State = "Reviewed", CreatedAt = DateTimeOffset.UtcNow.AddDays(-40) });
            await db.SaveChangesAsync();
            var service = new ProviderMonitoringService(new ProviderAuthorizationStore(factory, admin, NullLogger<ProviderAuthorizationStore>.Instance));
            await service.SaveAsync(offeringId, null, new(null, "Freshness", "EvidenceFreshness", evidenceId,
                new("Change.ageDays", "GreaterThanOrEqual", "30"), 60, "provider-owner", "CreateProviderImpactReview", true), "create", "actor", default);
        }
        using var requestScope = accessor.Push(original);
        var worker = new ComplianceWatchHostedService(factory, Mock.Of<IComplianceWatchService>(), Mock.Of<IAlertManager>(),
            Mock.Of<IComplianceEventSource>(), Options.Create(new MonitoringOptions()), NullLogger<ComplianceWatchHostedService>.Instance,
            provider.GetRequiredService<IServiceScopeFactory>(), accessor);

        // Act
        await worker.RunTenantChecksAsync(default);

        // Assert
        original.IsCspAdmin.Should().BeFalse();
        original.PersonId.Should().Be(personId);
        accessor.Current.Should().BeSameAs(original);
        using var verifyScope = accessor.Push(admin);
        await using var verify = await factory.CreateDbContextAsync();
        var review = await verify.Set<ProviderAuthorizationImpactReview>().SingleAsync();
        review.CreatedBy.Should().Be("system:provider-monitoring");
        review.ReviewedBy.Should().BeNull();
        verify.AuthorizationDecisions.Should().BeEmpty();
    }
}
