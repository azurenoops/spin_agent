using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Interfaces.Kanban;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Notification dispatch service using bounded Channel for async, fire-and-forget delivery.
/// Phase 14 (UF-016 / #200): Implements SMTP, Teams, and Slack delivery channels.
/// Registered as Singleton — maintains channel and HTTP clients for webhook delivery.
/// </summary>
public class NotificationService : INotificationService, IDisposable
{
    private readonly Channel<NotificationMessage> _channel;
    private readonly ILogger<NotificationService> _logger;
    private readonly IOptions<NotificationOptions> _options;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _dispatchTask;

    public NotificationService(
        ILogger<NotificationService> logger,
        IOptions<NotificationOptions> options)
    {
        _logger = logger;
        _options = options;
        _channel = Channel.CreateBounded<NotificationMessage>(new BoundedChannelOptions(500)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
        });

        _dispatchTask = Task.Run(DispatchLoopAsync);
    }

    /// <inheritdoc />
    public async Task EnqueueAsync(NotificationMessage message)
    {
        await _channel.Writer.WriteAsync(message);
        _logger.LogDebug("Notification enqueued: {EventType} for task {TaskNumber} to user {TargetUserId}",
            message.EventType, message.TaskNumber, message.TargetUserId);
    }

    private async Task DispatchLoopAsync()
    {
        try
        {
            await foreach (var message in _channel.Reader.ReadAllAsync(_cts.Token))
            {
                try
                {
                    _logger.LogInformation("Dispatching notification: {EventType} for {TaskNumber} to {TargetUserId}: {Title}",
                        message.EventType, message.TaskNumber, message.TargetUserId, message.Title);

                    var opts = _options.Value;

                    if (opts.Email.Enabled && !string.IsNullOrWhiteSpace(opts.Email.SmtpHost))
                        await SendEmailAsync(message, opts.Email);

                    if (opts.Teams.Enabled && !string.IsNullOrWhiteSpace(opts.Teams.WebhookUrl))
                        await SendTeamsCardAsync(message, opts.Teams.WebhookUrl);

                    if (opts.Slack.Enabled && !string.IsNullOrWhiteSpace(opts.Slack.WebhookUrl))
                        await SendSlackMessageAsync(message, opts.Slack.WebhookUrl);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to dispatch notification for task {TaskNumber}", message.TaskNumber);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
    }

    private async Task SendEmailAsync(NotificationMessage message, NotificationEmailOptions email)
    {
        using var client = new SmtpClient(email.SmtpHost, email.SmtpPort);
        client.EnableSsl = email.UseSsl;
        if (!string.IsNullOrEmpty(email.Username))
            client.Credentials = new NetworkCredential(email.Username, email.Password);

        using var mail = new MailMessage(email.FromAddress, message.TargetUserId)
        {
            Subject = message.Title,
            Body = $"{message.Details}\n\nTask: {message.TaskNumber}\nBoard: {message.BoardId}\nEvent: {message.EventType}",
        };

        await client.SendMailAsync(mail);
        _logger.LogInformation("Email sent to {TargetUserId} for {EventType}", message.TargetUserId, message.EventType);
    }

    private async Task SendTeamsCardAsync(NotificationMessage message, string webhookUrl)
    {
        var payload = JsonSerializer.Serialize(new
        {
            type = "message",
            attachments = new[]
            {
                new
                {
                    contentType = "application/vnd.microsoft.card.adaptive",
                    content = new
                    {
                        type = "AdaptiveCard",
                        version = "1.4",
                        body = new object[]
                        {
                            new { type = "TextBlock", text = message.Title, weight = "bolder", size = "medium" },
                            new { type = "TextBlock", text = message.Details, wrap = true },
                            new { type = "FactSet", facts = new object[]
                            {
                                new { title = "Task", value = message.TaskNumber },
                                new { title = "Event", value = message.EventType.ToString() },
                            }},
                        },
                    },
                },
            },
        });

        using var http = new HttpClient();
        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var resp = await http.PostAsync(webhookUrl, content);
        resp.EnsureSuccessStatusCode();
        _logger.LogInformation("Teams card sent for {EventType} task {TaskNumber}", message.EventType, message.TaskNumber);
    }

    private async Task SendSlackMessageAsync(NotificationMessage message, string webhookUrl)
    {
        var payload = JsonSerializer.Serialize(new
        {
            text = $"*{message.Title}*\n{message.Details}\nTask: {message.TaskNumber}",
        });

        using var http = new HttpClient();
        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var resp = await http.PostAsync(webhookUrl, content);
        resp.EnsureSuccessStatusCode();
        _logger.LogInformation("Slack message sent for {EventType} task {TaskNumber}", message.EventType, message.TaskNumber);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _channel.Writer.TryComplete();
        _cts.Cancel();
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}

