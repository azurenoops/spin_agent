using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Identity;
using Azure.ResourceManager;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class AzureAssessmentConnectionProbeTests
{
    private static readonly Guid Subscription = Guid.Parse("00000000-0000-0000-0000-000000000981");
    private static readonly Guid Directory = Guid.Parse("00000000-0000-0000-0000-000000000982");
    private static readonly AzureAssessmentSubscription[] Scope = [new(Subscription, Directory)];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CheckAsync_ReachableRequiredServices_UsesCorrectArmCloud(bool government)
    {
        // Arrange
        using var handler = new ProbeHttpHandler(request => Success(request));
        using var http = new HttpClient(handler);
        var environment = government ? ArmEnvironment.AzureGovernment : ArmEnvironment.AzurePublicCloud;
        var probe = CreateProbe(http, environment);

        // Act
        await probe.CheckAsync(Scope);

        // Assert
        handler.Requests.Should().HaveCount(4);
        handler.Requests.Should().OnlyContain(uri => uri.Host == environment.Endpoint.Host);
        handler.Requests.Should().Contain(uri => uri.AbsolutePath.EndsWith("/resources"));
        handler.Requests.Should().Contain(uri => uri.AbsolutePath.Contains("Microsoft.PolicyInsights"));
        handler.Requests.Should().Contain(uri => uri.AbsolutePath.Contains("Microsoft.Security/assessments"));
    }

    [Theory]
    [InlineData("Disabled", false)]
    [InlineData("Enabled", true)]
    public async Task CheckAsync_DisabledOrWrongDirectory_BlocksBeforeProviderQueries(string state, bool wrongDirectory)
    {
        // Arrange
        using var handler = new ProbeHttpHandler(_ => SubscriptionResponse(
            state, wrongDirectory ? Guid.NewGuid() : Directory));
        using var http = new HttpClient(handler);
        var probe = CreateProbe(http);

        // Act
        var act = () => probe.CheckAsync(Scope);

        // Assert
        (await act.Should().ThrowAsync<AssessmentEnvironmentException>()).Which.ErrorCode
            .Should().Be(AssessmentEnvironmentErrors.SubscriptionUnavailable);
        handler.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData(401, AssessmentEnvironmentErrors.AuthenticationRequired)]
    [InlineData(403, AssessmentEnvironmentErrors.AccessDenied)]
    [InlineData(404, AssessmentEnvironmentErrors.SubscriptionUnavailable)]
    [InlineData(429, AssessmentEnvironmentErrors.ConnectionUnavailable)]
    [InlineData(500, AssessmentEnvironmentErrors.ConnectionUnavailable)]
    public async Task CheckAsync_AzureErrors_AreSafeAndNeverBecomeEmptySuccess(int status, string code)
    {
        // Arrange
        using var handler = new ProbeHttpHandler(_ => new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent("{\"error\":{\"code\":\"SyntheticFailure\",\"message\":\"SENSITIVE_TEST_DETAIL\"}}",
                Encoding.UTF8, "application/json")
        });
        using var http = new HttpClient(handler);
        var probe = CreateProbe(http);

        // Act
        var act = () => probe.CheckAsync(Scope);

        // Assert
        var failure = (await act.Should().ThrowAsync<AssessmentEnvironmentException>()).Which;
        failure.ErrorCode.Should().Be(code);
        failure.Message.Should().NotContain("SENSITIVE_TEST_DETAIL");
        failure.Suggestion.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CheckAsync_CredentialFailure_ReturnsAuthenticationGuidance(bool unavailable)
    {
        // Arrange
        using var handler = new ProbeHttpHandler(request => Success(request));
        using var http = new HttpClient(handler);
        Exception failure = unavailable
            ? new CredentialUnavailableException("SENSITIVE_TEST_CREDENTIAL_DETAIL")
            : new AuthenticationFailedException("SENSITIVE_TEST_CREDENTIAL_DETAIL");
        var probe = CreateProbe(http, credential: new ThrowingCredential(failure));

        // Act
        var act = () => probe.CheckAsync(Scope);

        // Assert
        var result = (await act.Should().ThrowAsync<AssessmentEnvironmentException>()).Which;
        result.ErrorCode.Should().Be(AssessmentEnvironmentErrors.AuthenticationRequired);
        result.Message.Should().NotContain("SENSITIVE");
        handler.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CheckAsync_EmptyOrNullScope_RejectsWithoutNetwork(bool useNull)
    {
        // Arrange
        using var handler = new ProbeHttpHandler(request => Success(request));
        using var http = new HttpClient(handler);
        var probe = CreateProbe(http);

        // Act
        var act = () => probe.CheckAsync(useNull ? null! : []);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
        handler.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CheckAsync_CredentialNetworkOrTimeoutFailure_IsActionable(bool timeout)
    {
        // Arrange
        using var handler = new ProbeHttpHandler(request => Success(request));
        using var http = new HttpClient(handler);
        Exception failure = timeout ? new OperationCanceledException("Synthetic timeout")
            : new HttpRequestException("SENSITIVE_NETWORK_DETAIL");
        var probe = CreateProbe(http, credential: new ThrowingCredential(failure));

        // Act
        var act = () => probe.CheckAsync(Scope);

        // Assert
        var result = (await act.Should().ThrowAsync<AssessmentEnvironmentException>()).Which;
        result.ErrorCode.Should().Be(AssessmentEnvironmentErrors.ConnectionUnavailable);
        result.Message.Should().NotContain("SENSITIVE");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CheckAsync_MissingSubscriptionIdentityMetadata_Blocks(bool missingState)
    {
        // Arrange
        using var handler = new ProbeHttpHandler(_ => SubscriptionResponse(
            missingState ? null : "Enabled", missingState ? Directory : null));
        using var http = new HttpClient(handler);
        var probe = CreateProbe(http);

        // Act
        var act = () => probe.CheckAsync(Scope);

        // Assert
        (await act.Should().ThrowAsync<AssessmentEnvironmentException>()).Which.ErrorCode
            .Should().Be(AssessmentEnvironmentErrors.SubscriptionUnavailable);
    }

    [Fact]
    public async Task CheckAsync_CallerCancellation_PropagatesWithoutNetwork()
    {
        // Arrange
        using var handler = new ProbeHttpHandler(request => Success(request));
        using var http = new HttpClient(handler);
        var probe = CreateProbe(http);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        var act = () => probe.CheckAsync(Scope, cancellation.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        handler.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Microsoft.PolicyInsights")]
    [InlineData("Microsoft.Security/assessments")]
    public async Task CheckAsync_RequiredProviderDenied_DoesNotDeclareReady(string deniedPath)
    {
        // Arrange
        using var handler = new ProbeHttpHandler(request => request.RequestUri!.AbsolutePath.Contains(deniedPath)
            ? new HttpResponseMessage(HttpStatusCode.Forbidden)
            : Success(request));
        using var http = new HttpClient(handler);
        var probe = CreateProbe(http);

        // Act
        var act = () => probe.CheckAsync(Scope);

        // Assert
        (await act.Should().ThrowAsync<AssessmentEnvironmentException>()).Which.ErrorCode
            .Should().Be(AssessmentEnvironmentErrors.AccessDenied);
    }

    private static AzureAssessmentConnectionProbe CreateProbe(
        HttpClient http, ArmEnvironment? environment = null, TokenCredential? credential = null) =>
        new(new ArmClient(credential ?? new SyntheticCredential(), default, new ArmClientOptions
        {
            Environment = environment ?? ArmEnvironment.AzureGovernment,
            Transport = new HttpClientTransport(http),
            Retry = { MaxRetries = 0 }
        }), NullLogger<AzureAssessmentConnectionProbe>.Instance);

    private static HttpResponseMessage Success(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath.TrimEnd('/') == $"/subscriptions/{Subscription}"
            ? SubscriptionResponse("Enabled", Directory)
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}", Encoding.UTF8, "application/json")
            };

    private static HttpResponseMessage SubscriptionResponse(string? state, Guid? directory) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                id = $"/subscriptions/{Subscription}", subscriptionId = Subscription.ToString(),
                tenantId = directory?.ToString(), displayName = "Synthetic subscription", state
            }), Encoding.UTF8, "application/json")
        };

    private sealed class ProbeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request.RequestUri!);
            return Task.FromResult(respond(request));
        }
    }

    private sealed class SyntheticCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("synthetic-token-never-sent-to-azure", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    private sealed class ThrowingCredential(Exception failure) : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw failure;

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromException<AccessToken>(failure);
    }
}
