using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Ato.Copilot.Agents.Compliance.Services.Onboarding;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Tests.Unit.Onboarding;

/// <summary>
/// Unit tests for <see cref="OnboardingStateService"/> covering FR-006/FR-007/FR-008/FR-063.
/// Uses EF Core InMemory + a tiny in-test <see cref="IDbContextFactory{TContext}"/>
/// stand-in (matches the project-wide pattern in AuthPimToolTests).
/// </summary>
public class OnboardingStateServiceTests : IDisposable
{
    private readonly TestDbContextFactory _factory;
    private readonly Mock<IBootstrapAdministratorService> _bootstrap = new();
    private readonly Mock<IWizardAuditService> _audit = new();
    private readonly OnboardingStateService _sut;

    public OnboardingStateServiceTests()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"OnboardingStateServiceTests_{Guid.NewGuid()}")
            .Options;
        _factory = new TestDbContextFactory(options);

        _bootstrap.Setup(b => b.GrantAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BootstrapAdministratorResult(true, Guid.NewGuid(), null, null));

        _sut = new OnboardingStateService(
            _factory,
            _bootstrap.Object,
            _audit.Object,
            NullLogger<OnboardingStateService>.Instance);
    }

    public void Dispose()
    {
        using var db = _factory.CreateDbContext();
        db.Database.EnsureDeleted();
    }

    [Fact]
    public async Task GetAsync_CreatesEmptyStateWhenNotPresent()
    {
        var tenantId = Guid.NewGuid();

        var state = await _sut.GetAsync(tenantId);

        state.TenantId.Should().Be(tenantId);
        state.Status.Should().Be(TenantOnboardingStatus.NotStarted);
    }

    [Fact]
    public async Task StartAsync_TransitionsToInProgress()
    {
        var tenantId = Guid.NewGuid();
        var actor = Guid.NewGuid();

        var state = await _sut.StartAsync(tenantId, actor, "Bootstrap Admin", "admin@example.test", Guid.NewGuid());

        state.Status.Should().Be(TenantOnboardingStatus.InProgress);
        state.OnboardingStartedAt.Should().NotBeNull();
        _bootstrap.Verify(b => b.GrantAsync(
            tenantId, actor, "Bootstrap Admin", "admin@example.test",
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MarkStepCompletedAsync_PopulatesDurationAndPersists()
    {
        var tenantId = Guid.NewGuid();
        var actor = Guid.NewGuid();
        await _sut.StartAsync(tenantId, actor, null, null, Guid.NewGuid());

        await _sut.MarkStepCompletedAsync(
            tenantId,
            stepName: "OrganizationContext",
            durationMs: 1234,
            actor,
            Guid.NewGuid());

        await using var db = _factory.CreateDbContext();
        var state = await db.TenantOnboardingStates
            .Include(s => s.StepCompletions)
            .FirstAsync(s => s.TenantId == tenantId);
        var step = state.StepCompletions.Single();
        step.StepName.Should().Be("OrganizationContext");
        step.Status.Should().Be(OnboardingStepStatus.Completed);
        step.DurationMs.Should().Be(1234);
        state.LastStep.Should().Be("OrganizationContext");
    }

    [Fact]
    public async Task MarkStepCompletedAsync_EmitsStructuredAnalyticsEvent()
    {
        var tenantId = Guid.NewGuid();
        var actor = Guid.NewGuid();
        await _sut.StartAsync(tenantId, actor, null, null, Guid.NewGuid());

        var spy = new SpyLogger();
        var sutWithSpy = new OnboardingStateService(
            _factory, _bootstrap.Object, _audit.Object, spy);

        await sutWithSpy.MarkStepCompletedAsync(
            tenantId, "Roles", durationMs: 4711, actor, Guid.NewGuid());

        spy.Records.Should().Contain(r => r.Message.Contains("wizard.step_completed"));
        var hit = spy.Records.First(r => r.Message.Contains("wizard.step_completed"));
        hit.Scope.Should().ContainKey("tenantId");
        hit.Scope.Should().ContainKey("actorUserId");
        hit.Scope.Should().ContainKey("stepName");
        hit.Scope.Should().ContainKey("durationMs");
    }

    [Fact]
    public async Task MarkStepSkippedAsync_RecordsSkipped()
    {
        var tenantId = Guid.NewGuid();
        var actor = Guid.NewGuid();
        await _sut.StartAsync(tenantId, actor, null, null, Guid.NewGuid());

        await _sut.MarkStepSkippedAsync(tenantId, "Emass", durationMs: 0, actor, Guid.NewGuid());

        await using var db = _factory.CreateDbContext();
        var state = await db.TenantOnboardingStates
            .Include(s => s.StepCompletions)
            .FirstAsync(s => s.TenantId == tenantId);
        state.StepCompletions.Single().Status.Should().Be(OnboardingStepStatus.Skipped);
    }

    [Fact]
    public async Task MarkStepCompletedAsync_RejectsUnknownStep()
    {
        var tenantId = Guid.NewGuid();
        var actor = Guid.NewGuid();
        await _sut.StartAsync(tenantId, actor, null, null, Guid.NewGuid());

        var act = () => _sut.MarkStepCompletedAsync(
            tenantId, "NotAStep", durationMs: 0, actor, Guid.NewGuid());

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CompleteOnboardingAsync_SetsTerminalState()
    {
        var tenantId = Guid.NewGuid();
        var actor = Guid.NewGuid();
        await _sut.StartAsync(tenantId, actor, null, null, Guid.NewGuid());

        await _sut.CompleteOnboardingAsync(tenantId, actor, Guid.NewGuid());

        var state = await _sut.GetAsync(tenantId);
        state.Status.Should().Be(TenantOnboardingStatus.Completed);
        state.OnboardingCompletedAt.Should().NotBeNull();
    }

    private sealed class SpyLogger : ILogger<OnboardingStateService>
    {
        public List<(string Message, Dictionary<string, object?> Scope)> Records { get; } = new();
        private readonly Stack<Dictionary<string, object?>> _scopes = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            var d = new Dictionary<string, object?>();
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                foreach (var kv in pairs) d[kv.Key] = kv.Value;
            }
            _scopes.Push(d);
            return new ScopeEnd(this);
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var msg = formatter(state, exception);
            var merged = new Dictionary<string, object?>();
            foreach (var s in _scopes.Reverse())
            {
                foreach (var kv in s) merged[kv.Key] = kv.Value;
            }
            Records.Add((msg, merged));
        }

        private sealed class ScopeEnd : IDisposable
        {
            private readonly SpyLogger _l;
            public ScopeEnd(SpyLogger l) { _l = l; }
            public void Dispose() { if (_l._scopes.Count > 0) _l._scopes.Pop(); }
        }
    }

    private sealed class TestDbContextFactory : IDbContextFactory<AtoCopilotContext>
    {
        private readonly DbContextOptions<AtoCopilotContext> _options;
        public TestDbContextFactory(DbContextOptions<AtoCopilotContext> options) => _options = options;
        public AtoCopilotContext CreateDbContext() => new(_options);
    }
}
