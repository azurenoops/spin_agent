using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Tests.Integration.Notifications;

public class AlertNotificationDeliveryIntegrationTests
{
    [Theory]
    [InlineData(HttpStatusCode.Accepted, true, null)]
    [InlineData(HttpStatusCode.ServiceUnavailable, false, "HTTP_503")]
    public async Task SendNotification_ConfiguredWebhook_RecordsHttpOutcome(
        HttpStatusCode responseStatus,
        bool expectedDelivered,
        string? expectedError)
    {
        // Arrange
        const string webhookSecret = "integration-test-webhook-secret";
        const string webhookUrl = "https://hooks.example.test/compliance";
        var dbFactory = CreateDbFactory();
        var watchService = new Mock<IComplianceWatchService>();
        watchService
            .Setup(service => service.IsAlertSuppressed(
                It.IsAny<ComplianceAlert>(),
                It.IsAny<IReadOnlyList<SuppressionRule>>()))
            .Returns(false);

        await using (var db = dbFactory.CreateDbContext())
        {
            db.EscalationPaths.Add(new EscalationPath
            {
                Id = Guid.NewGuid(),
                Name = "Integration webhook",
                TriggerSeverity = AlertSeverity.High,
                Channel = NotificationChannel.Webhook,
                WebhookUrl = webhookUrl,
                IsEnabled = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var handler = new RecordingHttpMessageHandler(responseStatus);
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory
            .Setup(factory => factory.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(handler));
        var options = Options.Create(new NotificationOptions
        {
            Webhook = new NotificationWebhookOptions
            {
                Enabled = true,
                Secret = webhookSecret
            }
        });
        var service = new AlertNotificationService(
            dbFactory,
            watchService.Object,
            Mock.Of<ILogger<AlertNotificationService>>(),
            notificationOptions: options,
            httpClientFactory: httpClientFactory.Object);
        var alert = new ComplianceAlert
        {
            Id = Guid.NewGuid(),
            AlertId = "ALT-INTEGRATION",
            Type = AlertType.Drift,
            Severity = AlertSeverity.High,
            Status = AlertStatus.New,
            Title = "Integration alert",
            Description = "Webhook delivery regression",
            SubscriptionId = "sub-integration",
            AffectedResources = ["resource-1"],
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Act
        await service.SendNotificationAsync(alert);

        // Assert
        handler.RequestUri.Should().Be(webhookUrl);
        handler.Payload.Should().Contain(alert.AlertId);
        handler.Signature.Should().Be(ComputeSignature(handler.Payload!, webhookSecret));

        await using var resultDb = dbFactory.CreateDbContext();
        var notification = await resultDb.AlertNotifications
            .SingleAsync(item => item.AlertId == alert.Id && item.Channel == NotificationChannel.Webhook);
        notification.IsDelivered.Should().Be(expectedDelivered);
        notification.DeliveredAt.HasValue.Should().Be(expectedDelivered);
        notification.DeliveryError.Should().Be(expectedError);
    }

    private static TestDbContextFactory CreateDbFactory()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"AlertDeliveryIntegration_{Guid.NewGuid()}")
            .Options;
        return new TestDbContextFactory(options);
    }

    private static string ComputeSignature(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    }

    private sealed class TestDbContextFactory(DbContextOptions<AtoCopilotContext> options)
        : IDbContextFactory<AtoCopilotContext>
    {
        public AtoCopilotContext CreateDbContext() => new(options);
    }

    private sealed class RecordingHttpMessageHandler(HttpStatusCode responseStatus) : HttpMessageHandler
    {
        public string? RequestUri { get; private set; }
        public string? Payload { get; private set; }
        public string? Signature { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.ToString();
            Payload = await request.Content!.ReadAsStringAsync(cancellationToken);
            Signature = request.Headers.GetValues("X-Signature").Single();
            return new HttpResponseMessage(responseStatus);
        }
    }
}