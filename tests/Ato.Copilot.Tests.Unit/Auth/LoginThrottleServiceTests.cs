using Ato.Copilot.Core.Configuration.Auth;
using Ato.Copilot.Core.Interfaces.Auth;
using Ato.Copilot.Core.Services.Auth;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Auth;

/// <summary>
/// Feature 051 T030 — contract test for <see cref="ILoginThrottleService"/>.
/// Verifies the bucket-key design (research § R7), Production / Development
/// threshold selection (analysis C11), per-identity vs per-IP independence,
/// reset semantics, and the FR-034 default thresholds.
/// </summary>
/// <remarks>
/// Uses <see cref="MemoryDistributedCache"/> as the
/// <see cref="IDistributedCache"/> backing store so the test does not need
/// Redis. The cache is shared across all SUT calls in a test method so
/// counter increments are visible end-to-end.
/// </remarks>
public sealed class LoginThrottleServiceTests
{
    private static (LoginThrottleService Sut, AuthOptions Options) BuildSut(
        string environmentName = "Production",
        AuthThrottleOptions? throttle = null)
    {
        var cache = new MemoryDistributedCache(
            Options.Create(new MemoryDistributedCacheOptions()));
        var options = new AuthOptions
        {
            Throttle = throttle ?? new AuthThrottleOptions(),
        };
        var envMock = new Mock<IHostEnvironment>();
        envMock.SetupGet(e => e.EnvironmentName).Returns(environmentName);
        var sut = new LoginThrottleService(
            cache,
            Options.Create(options),
            envMock.Object,
            NullLogger<LoginThrottleService>.Instance);
        return (sut, options);
    }

    // ─── Defaults match spec.md FR-034 (analysis C2) ────────────────────

    [Fact]
    public void AuthThrottleOptions_Defaults_MatchFR034()
    {
        // Arrange
        var defaults = new AuthThrottleOptions();

        // Act + Assert — Production 20/min/IP + 10/min/identity.
        defaults.Production.PerIpPerMinute.Should().Be(20);
        defaults.Production.PerIdentityPerMinute.Should().Be(10);
        // Development 100/min/IP + 100/min/identity.
        defaults.Development.PerIpPerMinute.Should().Be(100);
        defaults.Development.PerIdentityPerMinute.Should().Be(100);
    }

    // ─── Production: 21st attempt is throttled ──────────────────────────

    [Fact]
    public async Task RegisterAttemptAsync_TwentyFirstAttemptFromSameIp_IsThrottled()
    {
        // Arrange
        var (sut, _) = BuildSut(environmentName: "Production");
        const string ip = "10.0.0.1";

        // Act — register 20 attempts from distinct identities so only the
        // IP counter trips. Identities are distinct so the identity threshold
        // (10) does not interfere with our IP-threshold assertion path.
        for (int i = 0; i < 20; i++)
        {
            var d = await sut.RegisterAttemptAsync(ip, identityKey: $"user-{i}");
            d.Allowed.Should().BeTrue($"attempt {i + 1} of 20 is below the per-IP threshold");
        }
        var twentyFirst = await sut.RegisterAttemptAsync(ip, identityKey: "user-99");

        // Assert
        twentyFirst.Allowed.Should().BeFalse("the 21st attempt within 60s exceeds the per-IP threshold");
        twentyFirst.RetryAfter.Should().BeLessOrEqualTo(TimeSpan.FromSeconds(60));
        twentyFirst.RetryAfter.Should().BeGreaterThan(TimeSpan.Zero);
        twentyFirst.CurrentIpCount.Should().BeGreaterOrEqualTo(21);
    }

    // ─── Production: identity counter is independent of IP counter ──────

    [Fact]
    public async Task RegisterAttemptAsync_PerIdentityCounter_IsIndependentOfPerIpCounter()
    {
        // Arrange — identity threshold is 10/min in Production.
        var (sut, _) = BuildSut(environmentName: "Production");
        const string identity = "alice@example.com";

        // Act — 10 attempts for the same identity from DIFFERENT IPs.
        for (int i = 0; i < 10; i++)
        {
            var d = await sut.RegisterAttemptAsync(sourceIp: $"10.0.0.{i + 1}", identity);
            d.Allowed.Should().BeTrue($"attempt {i + 1} of 10 is at or below the per-identity threshold");
        }
        var eleventh = await sut.RegisterAttemptAsync(sourceIp: "10.0.0.99", identity);

        // Assert — the IP counter for 10.0.0.99 alone is 1 (well below 20),
        // so this must trip on the identity counter, NOT the IP counter.
        eleventh.Allowed.Should().BeFalse("identity threshold trips even when each IP saw only one attempt");
        eleventh.CurrentIdentityCount.Should().BeGreaterOrEqualTo(11);
    }

    // ─── Per-IP and per-identity buckets are separate keyspaces ─────────

    [Fact]
    public async Task RegisterAttemptAsync_DifferentIdentitySameIp_BothCountersIncrement()
    {
        // Arrange
        var (sut, _) = BuildSut(environmentName: "Production");
        const string ip = "10.0.0.5";

        // Act
        var first = await sut.RegisterAttemptAsync(ip, identityKey: "alice");
        var second = await sut.RegisterAttemptAsync(ip, identityKey: "bob");

        // Assert
        first.CurrentIpCount.Should().Be(1);
        first.CurrentIdentityCount.Should().Be(1);
        second.CurrentIpCount.Should().Be(2, "the IP counter shares across identities");
        second.CurrentIdentityCount.Should().Be(1, "Bob's identity counter is independent of Alice's");
    }

    // ─── ResetIdentityAsync clears identity, NOT IP ─────────────────────

    [Fact]
    public async Task ResetIdentityAsync_ClearsIdentityCounter_ButLeavesIpCounter()
    {
        // Arrange
        var (sut, _) = BuildSut(environmentName: "Production");
        const string ip = "10.0.0.7";
        const string identity = "carol@example.com";
        for (int i = 0; i < 5; i++)
        {
            await sut.RegisterAttemptAsync(ip, identity);
        }

        // Act
        await sut.ResetIdentityAsync(identity);
        var next = await sut.RegisterAttemptAsync(ip, identity);

        // Assert
        next.CurrentIdentityCount.Should().Be(1,
            "ResetIdentityAsync clears the identity counter; the next attempt starts the bucket fresh.");
        next.CurrentIpCount.Should().BeGreaterOrEqualTo(6,
            "ResetIdentityAsync MUST NOT clear the IP counter (shared NAT / proxy invariant).");
    }

    // ─── Development thresholds are looser ──────────────────────────────

    [Fact]
    public async Task RegisterAttemptAsync_DevelopmentEnvironment_AllowsLooserThresholds()
    {
        // Arrange — Development = 100 per IP per minute.
        var (sut, _) = BuildSut(environmentName: "Development");
        const string ip = "127.0.0.1";

        // Act — 25 attempts (above the Production 20 cap but below 100).
        LoginThrottleDecision decision = null!;
        for (int i = 0; i < 25; i++)
        {
            decision = await sut.RegisterAttemptAsync(ip, identityKey: $"user-{i}");
        }

        // Assert
        decision.Allowed.Should().BeTrue("Development threshold is 100/min/IP — 25 attempts are well below.");
    }

    // ─── Anonymous identity key is bucketed as "anonymous" ──────────────

    [Fact]
    public async Task RegisterAttemptAsync_NullIdentityKey_TreatedAsAnonymous()
    {
        // Arrange
        var (sut, _) = BuildSut(environmentName: "Production");
        const string ip = "10.0.0.3";

        // Act — null + empty + whitespace all map to the same "anonymous"
        // identity bucket so a brute-force IP cannot route around the
        // per-identity limit by withholding the identity.
        await sut.RegisterAttemptAsync(ip, identityKey: null);
        await sut.RegisterAttemptAsync(ip, identityKey: string.Empty);
        await sut.RegisterAttemptAsync(ip, identityKey: "   ");
        var fourth = await sut.RegisterAttemptAsync("10.0.0.4", identityKey: null);

        // Assert — Three anonymous attempts share the same bucket; the
        // fourth from a different IP still hits the same identity bucket.
        fourth.CurrentIdentityCount.Should().Be(4);
    }
}
