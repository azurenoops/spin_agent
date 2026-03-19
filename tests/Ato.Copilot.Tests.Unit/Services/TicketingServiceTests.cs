using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Poam;
using Ato.Copilot.Core.Services;
using Ato.Copilot.Core.Services.Ticketing;

namespace Ato.Copilot.Tests.Unit.Services;

public class TicketingServiceTests : IDisposable
{
    private readonly AtoCopilotContext _db;
    private readonly Mock<ITicketingProvider> _jiraProvider;
    private readonly TicketingService _sut;

    private const string SystemId = "sys-ticket-001";
    private const string PoamId = "poam-ticket-001";
    private const string ConfigId = "config-001";

    public TicketingServiceTests()
    {
        var dbOptions = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"TicketingTests_{Guid.NewGuid()}")
            .Options;
        _db = new AtoCopilotContext(dbOptions);

        _jiraProvider = new Mock<ITicketingProvider>();
        _jiraProvider.Setup(p => p.ProviderType).Returns(TicketingProvider.Jira);

        var logger = Mock.Of<ILogger<TicketingService>>();
        _sut = new TicketingService(_db, new[] { _jiraProvider.Object }, logger);

        SeedData();
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    private void SeedData()
    {
        _db.RegisteredSystems.Add(new RegisteredSystem
        {
            Id = SystemId,
            Name = "Ticketing Test System",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure Government",
            CreatedBy = "test",
            IsActive = true,
        });

        _db.PoamItems.Add(new PoamItem
        {
            Id = PoamId,
            RegisteredSystemId = SystemId,
            SecurityControlNumber = "AC-2",
            Weakness = "Weak password policy",
            WeaknessSource = "STIG",
            CatSeverity = CatSeverity.CatII,
            Status = PoamStatus.Ongoing,
            PointOfContact = "test-user",
            ScheduledCompletionDate = DateTime.UtcNow.AddDays(30),
            CreatedBy = "test",
        });

        _db.SaveChanges();
    }

    // ─── GetConfigAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetConfigAsync_NoConfig_ReturnsNull()
    {
        var result = await _sut.GetConfigAsync(SystemId);
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetConfigAsync_WithConfig_ReturnsConfiguration()
    {
        _db.TicketingIntegrations.Add(new TicketingIntegration
        {
            Id = ConfigId,
            RegisteredSystemId = SystemId,
            Provider = TicketingProvider.Jira,
            BaseUrl = "https://test.atlassian.net",
            ProjectKeyOrTableName = "POAM",
            KeyVaultSecretUri = "https://vault.azure.net/secrets/jira-token",
            SyncEnabled = true,
        });
        await _db.SaveChangesAsync();

        var result = await _sut.GetConfigAsync(SystemId);
        result.Should().NotBeNull();
        result!.Provider.Should().Be(TicketingProvider.Jira);
        result.BaseUrl.Should().Be("https://test.atlassian.net");
    }

    // ─── ConfigureAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task ConfigureAsync_NewConfig_CreatesAndReturns()
    {
        _jiraProvider.Setup(p => p.TestConnectionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _sut.ConfigureAsync(
            SystemId, TicketingProvider.Jira,
            "https://test.atlassian.net", "POAM",
            "https://vault.azure.net/secrets/token", true);

        result.Should().NotBeNull();
        result.RegisteredSystemId.Should().Be(SystemId);
        result.SyncEnabled.Should().BeTrue();
        _db.TicketingIntegrations.Count().Should().Be(1);
    }

    [Fact]
    public async Task ConfigureAsync_ExistingConfig_Updates()
    {
        _db.TicketingIntegrations.Add(new TicketingIntegration
        {
            Id = ConfigId,
            RegisteredSystemId = SystemId,
            Provider = TicketingProvider.Jira,
            BaseUrl = "https://old.atlassian.net",
            ProjectKeyOrTableName = "OLD",
            KeyVaultSecretUri = "https://vault.azure.net/secrets/old",
            SyncEnabled = false,
        });
        await _db.SaveChangesAsync();

        _jiraProvider.Setup(p => p.TestConnectionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _sut.ConfigureAsync(
            SystemId, TicketingProvider.Jira,
            "https://new.atlassian.net", "NEW",
            "https://vault.azure.net/secrets/new", true);

        result.BaseUrl.Should().Be("https://new.atlassian.net");
        result.ProjectKeyOrTableName.Should().Be("NEW");
        result.SyncEnabled.Should().BeTrue();
        _db.TicketingIntegrations.Count().Should().Be(1);
    }

    [Fact]
    public async Task ConfigureAsync_ConnectionFails_Throws()
    {
        _jiraProvider.Setup(p => p.TestConnectionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var act = () => _sut.ConfigureAsync(
            SystemId, TicketingProvider.Jira,
            "https://bad.atlassian.net", "POAM",
            "https://vault.azure.net/secrets/token", true);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Connection test failed*");
    }

    [Fact]
    public async Task ConfigureAsync_UnsupportedProvider_Throws()
    {
        var act = () => _sut.ConfigureAsync(
            SystemId, TicketingProvider.ServiceNow,
            "https://test.service-now.com", "incident",
            "https://vault.azure.net/secrets/token", true);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not supported*");
    }

    // ─── SyncTicketAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task SyncTicketAsync_Push_CreatesTicketAndRecords()
    {
        await SeedConfigAsync();

        _jiraProvider.Setup(p => p.PushAsync(It.IsAny<PoamItem>(), It.IsAny<TicketingIntegration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TicketSyncResult { Success = true, ExternalRef = "POAM-123" });

        var result = await _sut.SyncTicketAsync(PoamId, "push");

        result.Success.Should().BeTrue();
        result.ExternalRef.Should().Be("POAM-123");
        _db.PoamTicketSyncs.Count().Should().Be(1);
        var poam = await _db.PoamItems.FindAsync(PoamId);
        poam!.ExternalTicketRef.Should().Be("POAM-123");
    }

    [Fact]
    public async Task SyncTicketAsync_Pull_UpdatesFromExternal()
    {
        await SeedConfigAsync();
        var poam = await _db.PoamItems.FindAsync(PoamId);
        poam!.ExternalTicketRef = "POAM-123";
        await _db.SaveChangesAsync();

        _jiraProvider.Setup(p => p.PullAsync("POAM-123", It.IsAny<TicketingIntegration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TicketSyncResult { Success = true, ExternalRef = "POAM-123", ExternalStatus = "Done" });

        var result = await _sut.SyncTicketAsync(PoamId, "pull");

        result.Success.Should().BeTrue();
        result.ExternalStatus.Should().Be("Done");
        _db.PoamTicketSyncs.Count().Should().Be(1);
    }

    [Fact]
    public async Task SyncTicketAsync_PushFails_RecordsErrorSync()
    {
        await SeedConfigAsync();

        _jiraProvider.Setup(p => p.PushAsync(It.IsAny<PoamItem>(), It.IsAny<TicketingIntegration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TicketSyncResult { Success = false, Error = "API returned 500" });

        var result = await _sut.SyncTicketAsync(PoamId, "push");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("500");
        var sync = _db.PoamTicketSyncs.First();
        sync.SyncStatus.Should().Be(TicketSyncStatus.Error);
        sync.LastSyncError.Should().Contain("500");
    }

    [Fact]
    public async Task SyncTicketAsync_SyncDisabled_Throws()
    {
        _db.TicketingIntegrations.Add(new TicketingIntegration
        {
            Id = ConfigId,
            RegisteredSystemId = SystemId,
            Provider = TicketingProvider.Jira,
            BaseUrl = "https://test.atlassian.net",
            ProjectKeyOrTableName = "POAM",
            KeyVaultSecretUri = "https://vault.azure.net/secrets/token",
            SyncEnabled = false,
        });
        await _db.SaveChangesAsync();

        var act = () => _sut.SyncTicketAsync(PoamId, "push");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*disabled*");
    }

    [Fact]
    public async Task SyncTicketAsync_PoamNotFound_Throws()
    {
        var act = () => _sut.SyncTicketAsync("nonexistent", "push");
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task SyncTicketAsync_NoConfig_Throws()
    {
        var act = () => _sut.SyncTicketAsync(PoamId, "push");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not configured*");
    }

    // ─── BulkSyncAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task BulkSyncAsync_SyncsAllActivePoams()
    {
        await SeedConfigAsync();

        _db.PoamItems.Add(new PoamItem
        {
            Id = "poam-ticket-002",
            RegisteredSystemId = SystemId,
            SecurityControlNumber = "AC-3",
            Weakness = "Missing MFA",
            WeaknessSource = "Scan",
            CatSeverity = CatSeverity.CatI,
            Status = PoamStatus.Ongoing,
            PointOfContact = "test-user",
            ScheduledCompletionDate = DateTime.UtcNow.AddDays(30),
            CreatedBy = "test",
        });
        await _db.SaveChangesAsync();

        _jiraProvider.Setup(p => p.PushAsync(It.IsAny<PoamItem>(), It.IsAny<TicketingIntegration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TicketSyncResult { Success = true, ExternalRef = "POAM-BULK" });

        var results = await _sut.BulkSyncAsync(SystemId, "push");

        results.Should().HaveCount(2);
        results.Should().OnlyContain(r => r.Success);
    }

    [Fact]
    public async Task BulkSyncAsync_PartialFailure_ReportsEachResult()
    {
        await SeedConfigAsync();

        _db.PoamItems.Add(new PoamItem
        {
            Id = "poam-ticket-003",
            RegisteredSystemId = SystemId,
            SecurityControlNumber = "AC-4",
            Weakness = "Open ports",
            WeaknessSource = "Nessus",
            CatSeverity = CatSeverity.CatIII,
            Status = PoamStatus.Delayed,
            PointOfContact = "test-user",
            ScheduledCompletionDate = DateTime.UtcNow.AddDays(15),
            CreatedBy = "test",
        });
        await _db.SaveChangesAsync();

        var callCount = 0;
        _jiraProvider.Setup(p => p.PushAsync(It.IsAny<PoamItem>(), It.IsAny<TicketingIntegration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1
                    ? new TicketSyncResult { Success = true, ExternalRef = "POAM-OK" }
                    : new TicketSyncResult { Success = false, Error = "Rate limited" };
            });

        var results = await _sut.BulkSyncAsync(SystemId, "push");

        results.Should().HaveCount(2);
        results.Count(r => r.Success).Should().Be(1);
        results.Count(r => !r.Success).Should().Be(1);
    }

    [Fact]
    public async Task BulkSyncAsync_NoConfig_Throws()
    {
        var act = () => _sut.BulkSyncAsync(SystemId, "push");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not configured*");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private async Task SeedConfigAsync()
    {
        _db.TicketingIntegrations.Add(new TicketingIntegration
        {
            Id = ConfigId,
            RegisteredSystemId = SystemId,
            Provider = TicketingProvider.Jira,
            BaseUrl = "https://test.atlassian.net",
            ProjectKeyOrTableName = "POAM",
            KeyVaultSecretUri = "https://vault.azure.net/secrets/jira-token",
            SyncEnabled = true,
        });
        await _db.SaveChangesAsync();
    }
}
