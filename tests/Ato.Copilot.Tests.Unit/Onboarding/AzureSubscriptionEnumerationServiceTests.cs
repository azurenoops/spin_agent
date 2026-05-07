using Azure;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Ato.Copilot.Agents.Compliance.Services.Onboarding.AzureSubscriptions;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Onboarding;

namespace Ato.Copilot.Tests.Unit.Onboarding;

/// <summary>
/// Unit tests for <see cref="AzureSubscriptionEnumerationService"/> (T094 / FR-070..FR-074).
/// Covers the three failure surfaces (consent / token / unreachable) using
/// fault-injecting <see cref="IDelegatedArmTokenProvider"/> doubles.
/// </summary>
public class AzureSubscriptionEnumerationServiceTests
{
    [Fact]
    public async Task EnumerateAsync_NoCredential_ReturnsConsentRequired()
    {
        var tokens = new Mock<IDelegatedArmTokenProvider>();
        tokens.Setup(t => t.GetCredentialAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TokenCredential?)null);

        var sut = new AzureSubscriptionEnumerationService(
            tokens.Object, NullLogger<AzureSubscriptionEnumerationService>.Instance);

        var result = await sut.EnumerateAsync(Guid.NewGuid(), Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(WizardErrorCodes.ArmConsentRequired);
        result.Suggestion.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task EnumerateAsync_TokenThrowsOnGetToken_ReturnsArmUnreachable()
    {
        // A credential whose GetToken throws is treated as ARM-unreachable.
        var tokens = new Mock<IDelegatedArmTokenProvider>();
        tokens.Setup(t => t.GetCredentialAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ThrowingCredential(new InvalidOperationException("network")));

        var sut = new AzureSubscriptionEnumerationService(
            tokens.Object, NullLogger<AzureSubscriptionEnumerationService>.Instance);

        var result = await sut.EnumerateAsync(Guid.NewGuid(), Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(WizardErrorCodes.ArmUnreachable);
    }

    [Fact]
    public async Task EnumerateAsync_TokenThrows403_ReturnsConsentRequired()
    {
        var tokens = new Mock<IDelegatedArmTokenProvider>();
        tokens.Setup(t => t.GetCredentialAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ThrowingCredential(
                new RequestFailedException(403, "insufficient_claims")));

        var sut = new AzureSubscriptionEnumerationService(
            tokens.Object, NullLogger<AzureSubscriptionEnumerationService>.Instance);

        var result = await sut.EnumerateAsync(Guid.NewGuid(), Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(WizardErrorCodes.ArmConsentRequired);
    }

    [Fact]
    public async Task EnumerateAsync_TokenThrows401_ReturnsTokenExpired()
    {
        var tokens = new Mock<IDelegatedArmTokenProvider>();
        tokens.Setup(t => t.GetCredentialAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ThrowingCredential(
                new RequestFailedException(401, "token expired")));

        var sut = new AzureSubscriptionEnumerationService(
            tokens.Object, NullLogger<AzureSubscriptionEnumerationService>.Instance);

        var result = await sut.EnumerateAsync(Guid.NewGuid(), Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(WizardErrorCodes.ArmTokenExpired);
    }

    private sealed class ThrowingCredential : TokenCredential
    {
        private readonly Exception _ex;
        public ThrowingCredential(Exception ex) => _ex = ex;
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => throw _ex;
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) => throw _ex;
    }
}
