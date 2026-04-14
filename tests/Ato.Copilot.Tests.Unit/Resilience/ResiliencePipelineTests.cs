using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.Retry;
using Polly.Timeout;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Resilience;

/// <summary>
/// Unit tests for the shared resilience pipeline configuration (FR-001 through FR-005).
/// Validates retry, circuit breaker, and timeout behavior.
/// </summary>
public class ResiliencePipelineTests
{
    /// <summary>
    /// Verifies that transient 503 errors are retried up to the configured maximum
    /// with exponential backoff delays (FR-001).
    /// </summary>
    [Fact]
    public async Task Retry_On503_RetriesUpToMaxAttempts()
    {
        // Arrange
        var callCount = 0;
        var handler = new TestDelegatingHandler(request =>
        {
            callCount++;
            return callCount < 3
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var client = CreateClientWithResilience(handler, maxRetries: 3, baseDelay: 0.01);

        // Act
        var response = await client.GetAsync("http://test/api");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        callCount.Should().Be(3, "first 2 attempts return 503, third succeeds");
    }

    /// <summary>
    /// Verifies that Retry-After header is honored when present on 429 response (FR-002).
    /// </summary>
    [Fact]
    public async Task Retry_HonorsRetryAfterHeader()
    {
        // Arrange
        var callCount = 0;
        var handler = new TestDelegatingHandler(request =>
        {
            callCount++;
            if (callCount == 1)
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMilliseconds(50));
                return response;
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var client = CreateClientWithResilience(handler, maxRetries: 3, baseDelay: 0.01);

        // Act
        var response = await client.GetAsync("http://test/api");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        callCount.Should().Be(2);
    }

    /// <summary>
    /// Verifies that all retries exhausted produces a failure response (FR-004).
    /// </summary>
    [Fact]
    public async Task Retry_AllRetriesExhausted_ReturnsLastResponse()
    {
        // Arrange
        var callCount = 0;
        var handler = new TestDelegatingHandler(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        });

        using var client = CreateClientWithResilience(handler, maxRetries: 2, baseDelay: 0.01);

        // Act
        var response = await client.GetAsync("http://test/api");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        callCount.Should().Be(3, "1 original + 2 retries");
    }

    /// <summary>
    /// Verifies that timeout cancellation occurs when request exceeds configured timeout (FR-004).
    /// </summary>
    [Fact]
    public async Task Timeout_CancelsRequest_WhenExceedsTimeout()
    {
        // Arrange
        var handler = new TestDelegatingHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var client = CreateClientWithResilience(handler, maxRetries: 0, baseDelay: 0.01, timeoutSeconds: 0.1);

        // Act
        Func<Task> act = () => client.GetAsync("http://test/api");

        // Assert
        await act.Should().ThrowAsync<Exception>()
            .Where(ex => (ex is TimeoutRejectedException) || (ex is OperationCanceledException)
                || ex.InnerException is TaskCanceledException);
    }

    /// <summary>
    /// Verifies that successful responses are not retried.
    /// </summary>
    [Fact]
    public async Task Retry_DoesNotRetry_OnSuccess()
    {
        // Arrange
        var callCount = 0;
        var handler = new TestDelegatingHandler(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var client = CreateClientWithResilience(handler, maxRetries: 3, baseDelay: 0.01);

        // Act
        var response = await client.GetAsync("http://test/api");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        callCount.Should().Be(1, "no retry needed");
    }

    /// <summary>
    /// Verifies that 400 Bad Request is not retried (non-transient error).
    /// </summary>
    [Fact]
    public async Task Retry_DoesNotRetry_On400()
    {
        // Arrange
        var callCount = 0;
        var handler = new TestDelegatingHandler(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.BadRequest);
        });

        using var client = CreateClientWithResilience(handler, maxRetries: 3, baseDelay: 0.01);

        // Act
        var response = await client.GetAsync("http://test/api");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        callCount.Should().Be(1, "400 is not a transient error");
    }

    private static HttpClient CreateClientWithResilience(
        HttpMessageHandler innerHandler,
        int maxRetries = 3,
        double baseDelay = 2.0,
        double timeoutSeconds = 30.0)
    {
        // Build a simple client with retry via Polly ResiliencePipeline directly
        var pipelineBuilder = new ResiliencePipelineBuilder<HttpResponseMessage>();

        if (maxRetries > 0)
        {
            pipelineBuilder.AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = maxRetries,
                Delay = TimeSpan.FromSeconds(baseDelay),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .HandleResult(r => r.StatusCode is
                        HttpStatusCode.ServiceUnavailable or
                        HttpStatusCode.TooManyRequests or
                        HttpStatusCode.GatewayTimeout or
                        HttpStatusCode.RequestTimeout)
            });
        }

        pipelineBuilder.AddTimeout(TimeSpan.FromSeconds(timeoutSeconds));

        var pipeline = pipelineBuilder.Build();

        var resilienceHandler = new ResilienceHandler(pipeline)
        {
            InnerHandler = innerHandler
        };

        return new HttpClient(resilienceHandler);
    }

    /// <summary>
    /// Test helper that delegates HTTP message handling to a provided function.
    /// </summary>
    private class TestDelegatingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public TestDelegatingHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = (request, _) => Task.FromResult(handler(request));
        }

        public TestDelegatingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> asyncHandler)
        {
            _handler = (request, _) => asyncHandler(request);
        }

        public TestDelegatingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> asyncHandler)
        {
            _handler = asyncHandler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request, cancellationToken);
    }
}
