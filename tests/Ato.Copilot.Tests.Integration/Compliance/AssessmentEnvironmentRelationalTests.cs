using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Interceptors;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Compliance;

public sealed class AssessmentEnvironmentRelationalTests
{
    [Fact]
    public async Task ConfigureReplaceDetach_Sqlite_PreservesTenantAndHistoricalAssessment()
    {
        // Arrange
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContextAccessor, TenantContextAccessor>();
        services.AddSingleton<TenantStampingSaveChangesInterceptor>();
        services.AddDbContextFactory<AtoCopilotContext>((sp, options) =>
            options.UseSqlite(connection)
                .AddInterceptors(sp.GetRequiredService<TenantStampingSaveChangesInterceptor>()));
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<AtoCopilotContext>>();
        var tenants = provider.GetRequiredService<ITenantContextAccessor>();
        var tenantId = Guid.NewGuid();
        var directoryId = Guid.NewGuid();
        var subscriptions = new[] { Guid.NewGuid(), Guid.NewGuid() };
        using var tenant = tenants.Push(new TenantContext(tenantId));
        var system = new RegisteredSystem
        {
            TenantId = tenantId, Name = "Synthetic relational assessment system",
            CreatedBy = "synthetic-writer", HostingEnvironment = "AzureGovernment"
        };
        string historyId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();
            db.Tenants.Add(new Tenant { Id = tenantId, DisplayName = "Synthetic organization" });
            db.RegisteredSystems.Add(system);
            await db.SaveChangesAsync();
            foreach (var subscription in subscriptions)
                db.AzureSubscriptionRegistrations.Add(new AzureSubscriptionRegistration
                {
                    TenantId = tenantId, SubscriptionId = subscription, ParentTenantId = directoryId,
                    DisplayName = "Synthetic subscription", Environment = AzureEnvironment.AzureUSGovernment,
                    Status = SubscriptionStatus.Selected
                });
            var history = new ComplianceAssessment
            {
                TenantId = tenantId, RegisteredSystemId = system.Id, ScanType = "Manual",
                Status = AssessmentStatus.Completed, InitiatedBy = "synthetic-historical-assessor"
            };
            historyId = history.Id;
            db.Assessments.Add(history);
            await db.SaveChangesAsync();
        }
        var probe = new Mock<IAzureAssessmentConnectionProbe>(MockBehavior.Strict);
        var service = new AssessmentEnvironmentService(factory, tenants, probe.Object,
            Options.Create(new GatewayOptions()), Options.Create(new OnboardingOptions()),
            new ArmClientFactory("AzureGovernment", NullLogger<ArmClientFactory>.Instance),
            NullLogger<AssessmentEnvironmentService>.Instance);

        // Act
        await service.ConfigureAsync(system.Id, new UpdateAssessmentEnvironmentRequest
        {
            CloudEnvironment = "Government", SubscriptionIds = [subscriptions[0].ToString()]
        }, "synthetic-writer");
        await service.ConfigureAsync(system.Id, new UpdateAssessmentEnvironmentRequest
        {
            CloudEnvironment = "Government", SubscriptionIds = [subscriptions[1].ToString()]
        }, "synthetic-writer");
        var configured = await service.GetConfigurationAsync(system.Id);
        await service.DetachAsync(system.Id, "synthetic-writer");

        // Assert
        configured.SubscriptionIds.Should().Equal(subscriptions[1].ToString());
        await using var verification = await factory.CreateDbContextAsync();
        var persistedSystem = await verification.RegisteredSystems.SingleAsync();
        persistedSystem.AzureProfile.Should().BeNull();
        persistedSystem.TenantId.Should().Be(tenantId);
        var preserved = await verification.Assessments.SingleAsync();
        preserved.Id.Should().Be(historyId);
        preserved.ScanType.Should().Be("Manual");
        preserved.InitiatedBy.Should().Be("synthetic-historical-assessor");
        (await verification.DashboardActivities.CountAsync()).Should().Be(3);
        probe.Invocations.Should().BeEmpty();
    }
}
