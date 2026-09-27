using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Microsoft.Data.Sqlite;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class ExternalAuthorizationRecordTests : IDisposable
{
    private readonly ServiceProvider _services;
    private readonly AuthorizationService _service;
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private static readonly Guid Tenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    public ExternalAuthorizationRecordTests()
    {
        var services = new ServiceCollection();
        services.AddScoped<ITenantContext>(_ => new TenantContext(Tenant));
        _connection.Open();
        services.AddDbContext<AtoCopilotContext>(o => o.UseSqlite(_connection));
        _services = services.BuildServiceProvider();
        _service = new AuthorizationService(_services.GetRequiredService<IServiceScopeFactory>(), NullLogger<AuthorizationService>.Instance);
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        db.Database.EnsureCreated();
        db.Tenants.Add(new Ato.Copilot.Core.Models.Tenancy.Tenant { Id = Tenant, DisplayName = "Mission organization" });
        db.RegisteredSystems.Add(new RegisteredSystem { Id = "system-a", TenantId = Tenant, Name = "Mission", CurrentRmfStep = RmfPhase.Prepare });
        db.RegisteredSystems.Add(new RegisteredSystem { Id = "system-b", TenantId = Tenant, Name = "Other mission", CurrentRmfStep = RmfPhase.Prepare });
        db.EvidenceArtifacts.Add(new EvidenceArtifact { Id = "source-a", TenantId = Tenant, RegisteredSystemId = "system-a",
            FileName = "AO decision.pdf", ContentHash = Hash, UploadedBy = "uploader", ContentType = "application/pdf" });
        db.AuthorizationPackages.Add(new AuthorizationPackage { Id = "package-a", TenantId = Tenant, RegisteredSystemId = "system-a",
            Status = PackageStatus.Completed, ContentHash = Hash, GeneratedBy = "generator", FilePath = "retained.zip", ExpiresAt = DateTimeOffset.UtcNow.AddDays(30) });
        db.SaveChanges();
    }
    private static ExternalAuthorizationRecordInput Input() => new("ATO", new DateTime(2026, 1, 2),
        new DateTime(2027, 1, 2), "Medium", "source-a", Hash, "Recorded issuing AO", "package-a", Hash,
        "Operate within the recorded boundary.", true, null);

    [Fact]
    public async Task Record_RetainsExactSourceBaselineAndActor_WithoutAdvancingPhase()
    {
        // Arrange / Act
        var result = await _service.RecordExternalAuthorizationAsync("system-a", Input(), "actor-a", "Recording AO");
        // Assert
        result.DecisionDate.Should().Be(new DateTime(2026, 1, 2));
        result.ExternalIssuingAuthority.Should().Be("Recorded issuing AO");
        result.SourceEvidenceId.Should().Be("source-a");
        result.SourceEvidenceHash.Should().Be(Hash);
        result.BaselinePackageId.Should().Be("package-a");
        result.BaselinePackageHash.Should().Be(Hash);
        result.RecordedBy.Should().Be("actor-a");
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.RegisteredSystems.SingleAsync(x => x.Id == "system-a")).CurrentRmfStep.Should().Be(RmfPhase.Prepare);
        (await db.DashboardActivities.SingleAsync()).EventType.Should().Be("ExternalAuthorizationRecorded");
    }

    [Theory]
    [InlineData("source-a", "wrong", "package-a", Hash)]
    [InlineData("source-a", Hash, "package-a", "wrong")]
    [InlineData("missing", Hash, "package-a", Hash)]
    [InlineData("source-a", Hash, "missing", Hash)]
    public async Task Record_RejectsMissingOrChangedSourceAndBaseline(string source, string sourceHash, string package, string packageHash)
    {
        // Arrange
        var input = Input() with { SourceEvidenceId = source, ExpectedSourceHash = sourceHash, BaselinePackageId = package, ExpectedPackageHash = packageHash };
        // Act / Assert
        await FluentActions.Awaiting(() => _service.RecordExternalAuthorizationAsync("system-a", input, "actor", "AO")).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Record_DoesNotAcceptAnotherSystemsPackage()
    {
        // Arrange
        using (var scope = _services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            (await db.AuthorizationPackages.SingleAsync()).RegisteredSystemId = "system-b";
            await db.SaveChangesAsync();
        }
        // Act / Assert
        await FluentActions.Awaiting(() => _service.RecordExternalAuthorizationAsync("system-a", Input(), "actor", "AO")).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Record_RequiresCurrentDecisionFence_AndPreservesPriorHistory()
    {
        // Arrange
        var first = await _service.RecordExternalAuthorizationAsync("system-a", Input(), "actor", "AO");
        // Act / Assert
        await FluentActions.Awaiting(() => _service.RecordExternalAuthorizationAsync("system-a", Input(), "actor", "AO")).Should().ThrowAsync<InvalidOperationException>();
        var second = await _service.RecordExternalAuthorizationAsync("system-a", Input() with { ExpectedActiveDecisionId = first.Id }, "actor", "AO");
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var prior = await db.AuthorizationDecisions.SingleAsync(x => x.Id == first.Id);
        prior.IsActive.Should().BeFalse();
        prior.SupersededById.Should().Be(second.Id);
        prior.SourceEvidenceHash.Should().Be(Hash);
    }

    [Fact]
    public async Task HistoricalRecord_DoesNotSupersedeCurrentDecision()
    {
        // Arrange
        var active = await _service.RecordExternalAuthorizationAsync("system-a", Input(), "actor", "AO");
        // Act
        var historical = await _service.RecordExternalAuthorizationAsync("system-a",
            Input() with { MakeCurrent = false, ExpectedActiveDecisionId = active.Id, DecisionDate = new DateTime(2025, 1, 1) }, "actor", "AO");
        // Assert
        historical.IsActive.Should().BeFalse();
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.AuthorizationDecisions.SingleAsync(x => x.IsActive)).Id.Should().Be(active.Id);
    }

    [Fact]
    public async Task PendingPackage_CannotBecomeTheRecordedDecisionBaseline()
    {
        // Arrange
        using (var scope = _services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            (await db.AuthorizationPackages.SingleAsync()).Status = PackageStatus.Pending;
            await db.SaveChangesAsync();
        }
        // Act / Assert
        await FluentActions.Awaiting(() => _service.RecordExternalAuthorizationAsync("system-a", Input(), "actor", "AO"))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*completed package*");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnavailableOrExpiredPackage_CannotBecomeARecordedBaseline(bool expired)
    {
        // Arrange
        using (var scope = _services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var package = await db.AuthorizationPackages.SingleAsync();
            if (expired) package.ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1);
            else package.FilePath = null;
            await db.SaveChangesAsync();
        }
        // Act / Assert
        await FluentActions.Awaiting(() => _service.RecordExternalAuthorizationAsync("system-a", Input(), "actor", "AO"))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("123")]
    public async Task InvalidDecisionType_CannotCreateARecord(string decisionType)
    {
        // Arrange / Act / Assert
        await FluentActions.Awaiting(() => _service.RecordExternalAuthorizationAsync("system-a", Input() with { DecisionType = decisionType }, "actor", "AO"))
            .Should().ThrowAsync<InvalidOperationException>();
    }
    public void Dispose() { _services.Dispose(); _connection.Dispose(); }
}
