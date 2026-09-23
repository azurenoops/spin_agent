using System.Data.Common;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Interceptors;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.State.Abstractions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class EvidenceIntegrityVerificationDomainTests : IAsyncLifetime
{
    private const string SystemId = "integrity-system";
    private const string AssessmentId = "integrity-assessment";
    private const string EvidenceId = "integrity-evidence";
    private const string Content = "sensitive-evidence-content-not-for-audit";
    private const string Collector = "original-automated-collector";
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _foreignTenant = Guid.NewGuid();
    private readonly Guid _person = Guid.NewGuid();
    private readonly Guid _otherPerson = Guid.NewGuid();
    private readonly Guid _directory = Guid.NewGuid();
    private readonly Guid _oid = Guid.NewGuid();
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly TenantContextAccessor _tenantAccessor = new();
    private readonly IdentityAccessor _identity = new();
    private readonly CommandObserver _commands = new();
    private DbContextOptions<AtoCopilotContext> _options = null!;
    private ServiceProvider _services = null!;
    private AssessmentArtifactService _service = null!;
    private TenantContext _workspace = null!;
    private bool _unboundDb;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseSqlite(_connection)
            .AddInterceptors(_commands, new TenantStampingSaveChangesInterceptor(_tenantAccessor,
                NullLogger<TenantStampingSaveChangesInterceptor>.Instance))
            .Options;
        await using (var db = NewDb())
        {
            await db.Database.EnsureCreatedAsync();
            db.Tenants.AddRange(
                new() { Id = _tenant, DisplayName = "Integrity organization", OnboardingState = OnboardingState.Active },
                new() { Id = _foreignTenant, DisplayName = "Foreign organization", OnboardingState = OnboardingState.Active });
            db.Persons.AddRange(
                new() { Id = _person, TenantId = _tenant, DisplayName = "Verifier", Email = "verifier@example.invalid" },
                new() { Id = _otherPerson, TenantId = _tenant, DisplayName = "Replacement", Email = "replacement@example.invalid" });
            db.OrganizationMemberships.Add(new()
            {
                TenantId = _tenant, PersonId = _person, DirectoryTenantId = _directory,
                ObjectId = _oid, GrantedBy = "fixture"
            });
            db.RegisteredSystems.AddRange(
                new() { Id = SystemId, TenantId = _tenant, Name = "Integrity system", CreatedBy = "fixture" },
                new() { Id = "foreign-system", TenantId = _foreignTenant, Name = "Foreign system", CreatedBy = "fixture" });
            db.Assessments.AddRange(
                new() { Id = AssessmentId, TenantId = _tenant, RegisteredSystemId = SystemId },
                new() { Id = "foreign-assessment", TenantId = _foreignTenant, RegisteredSystemId = "foreign-system" });
            db.Evidence.Add(new()
            {
                Id = EvidenceId, TenantId = _tenant, AssessmentId = AssessmentId, ControlId = "AC-1",
                Content = Content, ContentHash = Hash(Content), CollectorIdentity = Collector,
                CollectedBy = Collector, CollectionMethod = "Automated"
            });
            await db.SaveChangesAsync();
        }

        _identity.Current = new(_directory, _oid, "organization", _tenant, "ordinary", _person);
        _workspace = new(_tenant) { PersonId = _person, IsWorkspaceRequest = true };
        var factory = new Mock<IDbContextFactory<AtoCopilotContext>>();
        factory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => NewDb());
        var collection = new ServiceCollection();
        collection.AddSingleton<ITenantContextAccessor>(_tenantAccessor);
        collection.AddSingleton<IConversationIdentityAccessor>(_identity);
        collection.AddSingleton<ITenantContext>(_workspace);
        collection.AddScoped(_ => _unboundDb ? new AtoCopilotContext(_options) : NewDb());
        collection.AddSingleton<ISystemWorkspaceAccessService>(
            new SystemWorkspaceAccessService(factory.Object, _tenantAccessor));
        collection.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.Role, "Compliance.Auditor"), new Claim(ClaimTypes.Role, "CSP.Admin"),
                     new Claim("persona", "SCA"), new Claim("canManageEvidence", "true")], "forged-browser"))
            }
        });
        _services = collection.BuildServiceProvider();
        _service = new(_services.GetRequiredService<IServiceScopeFactory>(), NullLogger<AssessmentArtifactService>.Instance);
        _commands.Reads.Clear();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Theory]
    [InlineData(OrganizationRole.Assessor, "Sca")]
    [InlineData(OrganizationRole.Isso, "Isso")]
    [InlineData(OrganizationRole.Issm, "Issm")]
    public async Task EffectiveAssignedScaOrEvidenceManager_VerifiesAndAuditsQualifiedActor(
        OrganizationRole role, string effectiveRole)
    {
        // Arrange
        await AssignAsync(role);
        using var bound = _tenantAccessor.Push(_workspace);

        // Act
        var result = await _service.VerifyEvidenceAsync(EvidenceId);

        // Assert
        result.Status.Should().Be("verified");
        result.IntegrityVerifiedAt.Should().NotBeNull();
        result.CollectorIdentity.Should().Be(Collector);
        result.VerifierIdentity.Should().Be($"{_directory:D}/{_oid:D}");
        _commands.ContentReadInTransaction.Should().BeTrue();
        await using var db = NewDb();
        var evidence = await db.Evidence.SingleAsync();
        evidence.IntegrityVerifiedAt.Should().Be(result.IntegrityVerifiedAt);
        evidence.CollectorIdentity.Should().Be(Collector);
        evidence.CollectedBy.Should().Be(Collector);
        var audit = await db.AuditLogs.SingleAsync();
        audit.Action.Should().Be("EvidenceIntegrityVerification");
        audit.UserId.Should().Be($"{_directory:D}/{_oid:D}");
        audit.TenantId.Should().Be(_tenant);
        audit.Outcome.Should().Be(AuditOutcome.Success);
        audit.Details.Should().NotContain(Content).And.NotContain(Collector).And.NotContain(Hash(Content));
        using var details = JsonDocument.Parse(audit.Details);
        details.RootElement.GetProperty("evidenceId").GetString().Should().Be(EvidenceId);
        details.RootElement.GetProperty("assessmentId").GetString().Should().Be(AssessmentId);
        details.RootElement.GetProperty("systemId").GetString().Should().Be(SystemId);
        details.RootElement.GetProperty("status").GetString().Should().Be("verified");
        details.RootElement.GetProperty("effectiveRoles").EnumerateArray()
            .Select(r => r.GetString()).Should().Contain(effectiveRole);
    }

    [Theory]
    [InlineData(OrganizationRole.MissionOwner)]
    [InlineData(OrganizationRole.SystemOwner)]
    [InlineData(OrganizationRole.AuthorizingOfficial)]
    [InlineData(OrganizationRole.Administrator)]
    [InlineData(null)]
    public async Task OtherRoles_DeniedDespiteForgedGlobalAndBrowserClaims(OrganizationRole? role)
    {
        // Arrange
        if (role == OrganizationRole.Administrator)
            await AssignOrganizationAsync(role.Value);
        else if (role.HasValue)
            await AssignAsync(role.Value);
        using var bound = _tenantAccessor.Push(_workspace);

        // Act
        var verify = () => _service.VerifyEvidenceAsync(EvidenceId);

        // Assert
        await verify.Should().ThrowAsync<UnauthorizedAccessException>();
        await AssertDeniedWithoutContentOrWritesAsync();
    }

    [Fact]
    public async Task OrganizationScaDefault_AllowsUntilSystemOverrideAssignsAnotherPerson()
    {
        // Arrange
        await AssignOrganizationAsync(OrganizationRole.Assessor);
        using var bound = _tenantAccessor.Push(_workspace);
        var first = await _service.VerifyEvidenceAsync(EvidenceId);
        first.Status.Should().Be("verified");
        await using (var db = NewDb())
        {
            db.SystemRoleAssignments.Add(new()
            {
                TenantId = _tenant, RegisteredSystemId = SystemId, PersonId = _otherPerson,
                Role = OrganizationRole.Assessor, IsInherited = false
            });
            await db.SaveChangesAsync();
        }
        _commands.Reads.Clear();

        // Act
        var verify = () => _service.VerifyEvidenceAsync(EvidenceId);

        // Assert
        await verify.Should().ThrowAsync<UnauthorizedAccessException>();
        _commands.Reads.Should().NotContain(sql => sql.Contains("\"Content\""));
        await using var check = NewDb();
        (await check.Evidence.SingleAsync()).IntegrityVerifiedAt.Should().Be(first.IntegrityVerifiedAt);
        (await check.AuditLogs.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData("revoked")]
    [InlineData("removed")]
    [InlineData("wrong-directory")]
    [InlineData("wrong-object")]
    [InlineData("wrong-person")]
    [InlineData("wrong-tenant")]
    public async Task MembershipMustMatchEveryTrustedIdentityComponent(string mismatch)
    {
        // Arrange
        await AssignAsync(OrganizationRole.Assessor);
        await using (var db = NewDb())
        {
            var membership = await db.OrganizationMemberships.SingleAsync();
            switch (mismatch)
            {
                case "revoked": membership.RevokedAt = DateTimeOffset.UtcNow; break;
                case "removed": db.OrganizationMemberships.Remove(membership); break;
                case "wrong-directory": membership.DirectoryTenantId = Guid.NewGuid(); break;
                case "wrong-object": membership.ObjectId = Guid.NewGuid(); break;
                case "wrong-person": membership.PersonId = _otherPerson; break;
                case "wrong-tenant": membership.TenantId = _foreignTenant; break;
            }
            await db.SaveChangesAsync();
        }
        _commands.Reads.Clear();
        using var bound = _tenantAccessor.Push(_workspace);

        // Act
        var verify = () => _service.VerifyEvidenceAsync(EvidenceId);

        // Assert
        await verify.Should().ThrowAsync<UnauthorizedAccessException>();
        await AssertDeniedWithoutContentOrWritesAsync();
    }

    [Theory]
    [InlineData("missing-identity")]
    [InlineData("empty-directory")]
    [InlineData("empty-object")]
    [InlineData("empty-person")]
    [InlineData("missing-person")]
    [InlineData("missing-tenant")]
    [InlineData("system-tenant")]
    [InlineData("csp")]
    [InlineData("support")]
    [InlineData("wrong-bound-tenant")]
    [InlineData("wrong-bound-person")]
    [InlineData("not-workspace")]
    [InlineData("csp-bound-context")]
    [InlineData("impersonated-context")]
    [InlineData("suspended-context")]
    [InlineData("unbound-db")]
    [InlineData("unbound-tenant")]
    public async Task DirectDomainEntry_RejectsMissingOrUnboundIdentity(string scenario)
    {
        // Arrange
        await AssignAsync(OrganizationRole.Assessor);
        var identity = _identity.Current!;
        switch (scenario)
        {
            case "missing-identity": _identity.Current = null; break;
            case "empty-directory": _identity.Current = identity with { DirectoryId = Guid.Empty }; break;
            case "empty-object": _identity.Current = identity with { ObjectId = Guid.Empty }; break;
            case "empty-person": _identity.Current = identity with { PersonId = Guid.Empty }; break;
            case "missing-person": _identity.Current = identity with { PersonId = null }; break;
            case "missing-tenant": _identity.Current = identity with { TenantId = null }; break;
            case "system-tenant": _identity.Current = identity with { TenantId = Guid.Empty }; break;
            case "csp": _identity.Current = identity with { Kind = "csp" }; break;
            case "support": _identity.Current = identity with { Mode = "support" }; break;
            case "wrong-bound-tenant": _workspace.TenantId = _foreignTenant; break;
            case "wrong-bound-person": _workspace.PersonId = _otherPerson; break;
            case "not-workspace": _workspace.IsWorkspaceRequest = false; break;
            case "csp-bound-context": _workspace.IsCspAdmin = true; break;
            case "impersonated-context": _workspace.ImpersonatedTenantId = _tenant; break;
            case "suspended-context": _workspace.Status = TenantStatus.Suspended; break;
            case "unbound-db": _unboundDb = true; break;
        }
        using var bound = scenario == "unbound-tenant" ? null : _tenantAccessor.Push(_workspace);

        // Act
        var verify = () => _service.VerifyEvidenceAsync(EvidenceId);

        // Assert
        await verify.Should().ThrowAsync<UnauthorizedAccessException>();
        await AssertDeniedWithoutContentOrWritesAsync();
    }

    [Theory]
    [InlineData("foreign-evidence")]
    [InlineData("foreign-assessment")]
    [InlineData("foreign-system")]
    [InlineData("missing-assessment")]
    [InlineData("unowned-assessment")]
    [InlineData("unowned-evidence")]
    [InlineData("inactive-system")]
    [InlineData("disabled-tenant")]
    [InlineData("suspended-tenant")]
    [InlineData("missing-evidence")]
    public async Task OwnershipChain_MustBeCompleteSameTenantAndActive(string scenario)
    {
        // Arrange
        await AssignAsync(OrganizationRole.Assessor);
        await using (var db = NewDb())
        {
            var evidence = await db.Evidence.SingleAsync();
            var assessment = await db.Assessments.SingleAsync(a => a.Id == AssessmentId);
            switch (scenario)
            {
                case "foreign-evidence": evidence.TenantId = _foreignTenant; break;
                case "foreign-assessment": evidence.AssessmentId = "foreign-assessment"; break;
                case "foreign-system": assessment.RegisteredSystemId = "foreign-system"; break;
                case "missing-assessment": evidence.AssessmentId = "nonexistent"; break;
                case "unowned-assessment": assessment.RegisteredSystemId = null; break;
                case "unowned-evidence": evidence.AssessmentId = null; break;
                case "inactive-system": (await db.RegisteredSystems.SingleAsync(s => s.Id == SystemId)).IsActive = false; break;
                case "disabled-tenant": (await db.Tenants.SingleAsync(t => t.Id == _tenant)).Status = TenantStatus.Disabled; break;
                case "suspended-tenant": (await db.Tenants.SingleAsync(t => t.Id == _tenant)).Status = TenantStatus.Suspended; break;
                case "missing-evidence": db.Evidence.Remove(evidence); break;
            }
            await db.SaveChangesAsync();
        }
        _commands.Reads.Clear();
        using var bound = _tenantAccessor.Push(_workspace);

        // Act
        var verify = () => _service.VerifyEvidenceAsync(EvidenceId);

        // Assert
        await verify.Should().ThrowAsync<UnauthorizedAccessException>();
        await AssertDeniedWithoutContentOrWritesAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TamperedContent_PreservesTimestampAndCollectorAndAuditsFailure(bool previouslyVerified)
    {
        // Arrange
        await AssignAsync(OrganizationRole.Assessor);
        DateTime? priorTimestamp = previouslyVerified ? new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) : null;
        await using (var db = NewDb())
        {
            var evidence = await db.Evidence.SingleAsync();
            evidence.Content = "tampered";
            evidence.IntegrityVerifiedAt = priorTimestamp;
            await db.SaveChangesAsync();
        }
        using var bound = _tenantAccessor.Push(_workspace);

        // Act
        var result = await _service.VerifyEvidenceAsync(EvidenceId);

        // Assert
        result.Status.Should().Be("tampered");
        result.IntegrityVerifiedAt.Should().Be(priorTimestamp);
        result.CollectorIdentity.Should().Be(Collector);
        await using var check = NewDb();
        var persisted = await check.Evidence.SingleAsync();
        persisted.IntegrityVerifiedAt.Should().Be(priorTimestamp);
        persisted.CollectedBy.Should().Be(Collector);
        persisted.CollectorIdentity.Should().Be(Collector);
        var audit = await check.AuditLogs.SingleAsync();
        audit.Outcome.Should().Be(AuditOutcome.Failure);
        audit.UserId.Should().Be($"{_directory:D}/{_oid:D}");
        JsonDocument.Parse(audit.Details).RootElement.GetProperty("status").GetString().Should().Be("tampered");
    }

    [Fact]
    public async Task Cancellation_PropagatesWithoutContentOrWrites()
    {
        // Arrange
        await AssignAsync(OrganizationRole.Assessor);
        using var bound = _tenantAccessor.Push(_workspace);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        // Act
        var verify = () => _service.VerifyEvidenceAsync(EvidenceId, cancelled.Token);

        // Assert
        await verify.Should().ThrowAsync<OperationCanceledException>();
        await AssertDeniedWithoutContentOrWritesAsync();
    }

    [Fact]
    public async Task CancellationDuringPersistence_RollsBackAuditAndTimestamp()
    {
        // Arrange
        await AssignAsync(OrganizationRole.Assessor);
        using var bound = _tenantAccessor.Push(_workspace);
        using var cancelled = new CancellationTokenSource();
        _commands.CancelOnEvidenceWrite = cancelled;

        // Act
        var verify = () => _service.VerifyEvidenceAsync(EvidenceId, cancelled.Token);

        // Assert
        await verify.Should().ThrowAsync<OperationCanceledException>();
        await using var check = NewDb();
        (await check.Evidence.SingleAsync()).IntegrityVerifiedAt.Should().BeNull();
        (await check.AuditLogs.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData("INSERT INTO \"AuditLogs\"")]
    [InlineData("UPDATE \"Evidence\"")]
    public async Task PersistenceFailure_RollsBackBothTimestampAndAudit(string failingStatement)
    {
        // Arrange
        await AssignAsync(OrganizationRole.Assessor);
        using var bound = _tenantAccessor.Push(_workspace);
        _commands.FailStatement = failingStatement;

        // Act
        var verify = () => _service.VerifyEvidenceAsync(EvidenceId);

        // Assert
        await verify.Should().ThrowAsync<DbUpdateException>();
        _commands.FailStatement = null;
        await using var check = NewDb();
        (await check.Evidence.SingleAsync()).IntegrityVerifiedAt.Should().BeNull();
        (await check.AuditLogs.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetryingProvider_ExecutesWholeTransactionAndDoesNotDuplicateAudit(bool transientFailure)
    {
        // Arrange
        await AssignAsync(OrganizationRole.Assessor);
        _options = new DbContextOptionsBuilder<AtoCopilotContext>(_options)
            .UseSqlite(_connection, sqlite => sqlite.ExecutionStrategy(dependencies => new RetryingTestStrategy(dependencies)))
            .Options;
        if (transientFailure)
        {
            _commands.FailStatement = "UPDATE \"Evidence\"";
            _commands.TransientFailuresRemaining = 1;
        }
        using var bound = _tenantAccessor.Push(_workspace);

        // Act
        var result = await _service.VerifyEvidenceAsync(EvidenceId);

        // Assert
        result.Status.Should().Be("verified");
        _commands.ContentReadInTransaction.Should().BeTrue();
        await using var check = NewDb();
        (await check.Evidence.SingleAsync()).IntegrityVerifiedAt.Should().Be(result.IntegrityVerifiedAt);
        (await check.AuditLogs.CountAsync()).Should().Be(1);
        _commands.TransientFailuresRemaining.Should().Be(0);
    }

    private AtoCopilotContext NewDb() => new(_options, _tenantAccessor);

    private async Task AssignAsync(OrganizationRole role)
    {
        await using var db = NewDb();
        db.SystemRoleAssignments.Add(new()
        {
            TenantId = _tenant, RegisteredSystemId = SystemId, PersonId = _person, Role = role
        });
        await db.SaveChangesAsync();
        _commands.Reads.Clear();
    }

    private async Task AssignOrganizationAsync(OrganizationRole role)
    {
        await using var db = NewDb();
        db.OrganizationRoleAssignments.Add(new() { TenantId = _tenant, PersonId = _person, Role = role });
        await db.SaveChangesAsync();
        _commands.Reads.Clear();
    }

    private async Task AssertDeniedWithoutContentOrWritesAsync()
    {
        _commands.Reads.Should().NotContain(sql => sql.Contains("\"Content\""));
        await using var db = new AtoCopilotContext(_options);
        (await db.Evidence.AnyAsync(e => e.IntegrityVerifiedAt != null)).Should().BeFalse();
        (await db.AuditLogs.CountAsync()).Should().Be(0);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class IdentityAccessor : IConversationIdentityAccessor
    {
        public ConversationIdentity? Current { get; set; }
    }

    private sealed class TransientTestException : Exception;

    private sealed class RetryingTestStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, 1, TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => exception is TransientTestException;
    }

    private sealed class CommandObserver : DbCommandInterceptor
    {
        public List<string> Reads { get; } = [];
        public bool ContentReadInTransaction { get; private set; }
        public string? FailStatement { get; set; }
        public int TransientFailuresRemaining { get; set; }
        public CancellationTokenSource? CancelOnEvidenceWrite { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Reads.Add(command.CommandText);
            if (command.CommandText.Contains("\"Content\"") && command.CommandText.StartsWith("SELECT"))
                ContentReadInTransaction = command.Transaction is not null;
            if (CancelOnEvidenceWrite is not null && command.CommandText.Contains("UPDATE \"Evidence\""))
            {
                CancelOnEvidenceWrite.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (FailStatement is not null && command.CommandText.Contains(FailStatement, StringComparison.Ordinal))
            {
                if (TransientFailuresRemaining > 0)
                {
                    TransientFailuresRemaining--;
                    FailStatement = null;
                    throw new TransientTestException();
                }
                throw new InvalidOperationException("Injected persistence failure.");
            }
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
