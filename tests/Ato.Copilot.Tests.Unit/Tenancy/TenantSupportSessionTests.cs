using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Mcp.Services.Tenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Tenancy;

public sealed class TenantSupportSessionTests : IAsyncLifetime
{
    private const string SigningKey = "synthetic-support-session-signing-key-over-32-bytes";
    private readonly string _path = Path.Combine(
        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "TestResults")).FullName,
        $"support-sessions-{Guid.NewGuid():N}.db");
    private readonly Guid _directory = Guid.NewGuid();
    private readonly Guid _actor = Guid.NewGuid();
    private readonly Guid _target = Guid.NewGuid();
    private readonly Guid _otherTarget = Guid.NewGuid();
    private DbContextOptions<AtoCopilotContext> _options = null!;

    public async Task InitializeAsync()
    {
        _options = new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite($"Data Source={_path};Pooling=False").Options;
        await using var db = Context();
        await db.Database.EnsureCreatedAsync();
        db.Tenants.AddRange(new() { Id = _target, DisplayName = "First target" },
            new() { Id = _otherTarget, DisplayName = "Second target" });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync()
    {
        if (File.Exists(_path)) File.Delete(_path);
        return Task.CompletedTask;
    }

    private AtoCopilotContext Context() => new(_options);

    private TenantImpersonationService Instance()
    {
        var factory = new Mock<IDbContextFactory<AtoCopilotContext>>();
        factory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Context);
        return new(SigningKey, new TenantSupportSessionStore(factory.Object));
    }

    [Fact]
    public async Task Revocation_IsVisibleToIndependentAndRecreatedInstances()
    {
        // Arrange
        var issuer = Instance();
        var reader = Instance();
        var token = await issuer.IssueWorkspaceTokenAsync(_actor.ToString(), _directory, _target, default);
        (await reader.ValidateWorkspaceTokenAsync(token.value, default)).Should().NotBeNull();

        // Act
        (await issuer.RevokeWorkspaceTokenAsync(token.value, _directory, _actor, "manual", default)).Should().BeTrue();
        var after = await reader.ValidateWorkspaceTokenAsync(token.value, default);
        var recreated = await Instance().ValidateWorkspaceTokenAsync(token.value, default);

        // Assert
        after.Should().BeNull();
        recreated.Should().BeNull();
        await using var db = Context();
        var persisted = await db.Set<TenantSupportSession>().SingleAsync();
        persisted.RevokedAt.Should().NotBeNull();
        persisted.RevocationReason.Should().Be("manual");
    }

    [Fact]
    public async Task Revocation_IsIdempotentAndDoesNotRevokeOtherSessions()
    {
        // Arrange
        var service = Instance();
        var first = await service.IssueWorkspaceTokenAsync(_actor.ToString(), _directory, _target, default);
        var second = await service.IssueWorkspaceTokenAsync(_actor.ToString(), _directory, _otherTarget, default);
        var otherActor = await service.IssueWorkspaceTokenAsync(Guid.NewGuid().ToString(), _directory, _target, default);

        // Act
        await service.RevokeWorkspaceTokenAsync(first.value, _directory, _actor, "manual", default);
        var repeated = await service.RevokeWorkspaceTokenAsync(first.value, _directory, _actor, "manual", default);

        // Assert
        repeated.Should().BeFalse();
        (await Instance().ValidateWorkspaceTokenAsync(first.value, default)).Should().BeNull();
        (await Instance().ValidateWorkspaceTokenAsync(second.value, default)).Should().NotBeNull();
        (await Instance().ValidateWorkspaceTokenAsync(otherActor.value, default)).Should().NotBeNull();
    }

    [Fact]
    public async Task AValidSignatureWithoutPersistentGrant_IsNotAuthority()
    {
        // Arrange
        var service = Instance();
        var untracked = service.IssueWorkspaceToken(_actor.ToString(), _directory, _target);
        service.Validate(untracked.value).Should().NotBeNull();
        var legacy = service.IssueToken(_actor.ToString(), Guid.NewGuid(), _target);

        // Act
        var untrackedResult = await service.ValidateWorkspaceTokenAsync(untracked.value, default);
        var legacyResult = await service.ValidateWorkspaceTokenAsync(legacy.value, default);

        // Assert
        untrackedResult.Should().BeNull();
        legacyResult.Should().BeNull();
        (await service.ValidateAsync(untracked.value, default)).Should().BeNull();
        (await service.ValidateAsync(legacy.value, default)).Should().NotBeNull("legacy crypto-only tokens cannot enter canonical workspace support");
    }

    [Theory]
    [InlineData("directory")]
    [InlineData("actor")]
    [InlineData("target")]
    [InlineData("issued")]
    [InlineData("expiry")]
    public async Task StoredBindingMustMatchEverySignedScopeField(string field)
    {
        // Arrange
        var service = Instance();
        var token = await service.IssueWorkspaceTokenAsync(_actor.ToString(), _directory, _target, default);
        await using (var db = Context())
        {
            var session = await db.Set<TenantSupportSession>().SingleAsync();
            switch (field)
            {
                case "directory": session.DirectoryTenantId = Guid.NewGuid(); break;
                case "actor": session.ObjectId = Guid.NewGuid(); break;
                case "target": session.TargetTenantId = _otherTarget; break;
                case "issued": session.IssuedAt = session.IssuedAt.AddSeconds(-1); break;
                case "expiry": session.ExpiresAt = session.ExpiresAt.AddSeconds(1); break;
            }
            await db.SaveChangesAsync();
        }

        // Act
        var result = await Instance().ValidateWorkspaceTokenAsync(token.value, default);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task WrongDirectoryOrActor_CannotRevokeAnotherSession()
    {
        // Arrange
        var service = Instance();
        var token = await service.IssueWorkspaceTokenAsync(_actor.ToString(), _directory, _target, default);

        // Act
        var wrongDirectory = () => service.RevokeWorkspaceTokenAsync(token.value, Guid.NewGuid(), _actor, "manual", default);
        var wrongActor = () => service.RevokeWorkspaceTokenAsync(token.value, _directory, Guid.NewGuid(), "manual", default);

        // Assert
        await wrongDirectory.Should().ThrowAsync<WorkspaceException>().Where(e => e.StatusCode == 403);
        await wrongActor.Should().ThrowAsync<WorkspaceException>().Where(e => e.StatusCode == 403);
        (await Instance().ValidateWorkspaceTokenAsync(token.value, default)).Should().NotBeNull();
    }

    [Fact]
    public async Task ExpiredSession_IsDeniedEvenWithinSignatureClockSkew_AndCanBeCleanedUp()
    {
        // Arrange
        var service = Instance();
        var issued = await service.IssueWorkspaceTokenAsync(_actor.ToString(), _directory, _target, default);
        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(issued.value);
        var expired = new JwtSecurityToken(token.Issuer, token.Audiences.Single(),
            token.Claims.Where(c => c.Type is not ("nbf" or "exp" or "iat")),
            DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddSeconds(-1),
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)), SecurityAlgorithms.HmacSha256));
        var value = handler.WriteToken(expired);
        var payload = service.ValidateIgnoringLifetime(value)!;
        await using (var db = Context())
        {
            var row = await db.Set<TenantSupportSession>().SingleAsync();
            row.IssuedAt = payload.IssuedAt;
            row.ExpiresAt = payload.ExpiresAt;
            await db.SaveChangesAsync();
        }

        // Act
        var result = await Instance().ValidateWorkspaceTokenAsync(value, default);
        var revoked = await service.RevokeWorkspaceTokenAsync(value, _directory, _actor, "expired", default);

        // Assert
        result.Should().BeNull();
        revoked.Should().BeTrue();
    }

    [Fact]
    public async Task StorageFailure_IsExplicitAndNeverFallsBackToSignatureOnly()
    {
        // Arrange
        var store = new Mock<ITenantSupportSessionStore>();
        store.Setup(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SqliteException("Synthetic unavailable store", 14));
        var service = new TenantImpersonationService(SigningKey, store.Object);
        var token = service.IssueWorkspaceToken(_actor.ToString(), _directory, _target);

        // Act
        var action = () => service.ValidateWorkspaceTokenAsync(token.value, default);

        // Assert
        await action.Should().ThrowAsync<WorkspaceException>()
            .Where(e => e.StatusCode == 503 && e.Code == "SUPPORT_SESSION_UNAVAILABLE");
    }

    [Fact]
    public async Task RolloutIsIdempotentAndDoesNotEnrollExistingTokens()
    {
        // Arrange
        await using var db = Context();
        await db.Database.ExecuteSqlRawAsync("DROP TABLE TenantSupportSessions;");

        // Act
        await TenantSupportSessionSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await TenantSupportSessionSchemaAdditions.ApplyAsync(db, NullLogger.Instance);

        // Assert
        (await db.Set<TenantSupportSession>().CountAsync()).Should().Be(0);
        TenantSupportSessionSchemaAdditions.SqlServerScript.Should().Contain("ON DELETE CASCADE")
            .And.Contain("IF NOT EXISTS").And.NotContain("INSERT INTO");
    }

    [Fact]
    public async Task MissingPersistence_CannotIssueOrAuthorizeWorkspaceSupport()
    {
        // Arrange
        var service = new TenantImpersonationService(SigningKey);
        var token = service.IssueWorkspaceToken(_actor.ToString(), _directory, _target);

        // Act
        var issue = () => service.IssueWorkspaceTokenAsync(_actor.ToString(), _directory, _target, default);
        var validate = () => service.ValidateWorkspaceTokenAsync(token.value, default);

        // Assert
        await issue.Should().ThrowAsync<WorkspaceException>().Where(e => e.StatusCode == 503);
        await validate.Should().ThrowAsync<WorkspaceException>().Where(e => e.StatusCode == 503);
    }

    [Fact]
    public async Task PersistenceFailures_CannotReportSuccessfulIssueOrRevocation()
    {
        // Arrange
        var store = new Mock<ITenantSupportSessionStore>();
        store.Setup(s => s.CreateAsync(It.IsAny<TenantSupportSession>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("Synthetic create failure", new SqliteException("Synthetic failure", 14)));
        store.Setup(s => s.RevokeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SqliteException("Synthetic revoke failure", 14));
        var service = new TenantImpersonationService(SigningKey, store.Object);
        var token = service.IssueWorkspaceToken(_actor.ToString(), _directory, _target);

        // Act
        var issue = () => service.IssueWorkspaceTokenAsync(_actor.ToString(), _directory, _target, default);
        var revoke = () => service.RevokeWorkspaceTokenAsync(token.value, _directory, _actor, "manual", default);

        // Assert
        await issue.Should().ThrowAsync<WorkspaceException>().Where(e => e.Code == "SUPPORT_SESSION_UNAVAILABLE");
        await revoke.Should().ThrowAsync<WorkspaceException>().Where(e => e.Code == "SUPPORT_SESSION_UNAVAILABLE");
    }

    [Fact]
    public async Task CancelledStoreRead_DoesNotBecomeAValidSession()
    {
        // Arrange
        var service = Instance();
        var token = await service.IssueWorkspaceTokenAsync(_actor.ToString(), _directory, _target, default);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        // Act
        var read = () => service.ValidateWorkspaceTokenAsync(token.value, cancelled.Token);

        // Assert
        await read.Should().ThrowAsync<OperationCanceledException>();
    }
}
