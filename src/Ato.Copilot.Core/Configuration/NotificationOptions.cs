namespace Ato.Copilot.Core.Configuration;

/// <summary>
/// Top-level configuration options for outbound notification delivery channels.
/// Bound from the <c>Notifications</c> section of <c>appsettings.json</c>.
/// Phase 14 (UF-016 / #200).
/// </summary>
public class NotificationOptions
{
    /// <summary>Configuration section name in <c>appsettings.json</c>.</summary>
    public const string SectionName = "Notifications";

    /// <summary>SMTP email delivery settings.</summary>
    public NotificationEmailOptions Email { get; set; } = new();

    /// <summary>Microsoft Teams incoming-webhook settings.</summary>
    public NotificationTeamsOptions Teams { get; set; } = new();

    /// <summary>Slack incoming-webhook settings.</summary>
    public NotificationSlackOptions Slack { get; set; } = new();
}

/// <summary>
/// SMTP email delivery options for outbound notification messages.
/// </summary>
public class NotificationEmailOptions
{
    /// <summary>Enable or disable email delivery.</summary>
    public bool Enabled { get; set; }

    /// <summary>SMTP server hostname (e.g. smtp.office365.com).</summary>
    public string SmtpHost { get; set; } = "";

    /// <summary>SMTP server port (default: 587 for STARTTLS).</summary>
    public int SmtpPort { get; set; } = 587;

    /// <summary>Use SSL/TLS for the SMTP connection.</summary>
    public bool UseSsl { get; set; } = true;

    /// <summary>From address shown in delivered emails.</summary>
    public string FromAddress { get; set; } = "noreply@spinagent.gov";

    /// <summary>SMTP authentication username (leave empty for anonymous relay).</summary>
    public string Username { get; set; } = "";

    /// <summary>SMTP authentication password. Use a secret/vault reference in production.</summary>
    public string Password { get; set; } = "";
}

/// <summary>
/// Microsoft Teams incoming-webhook options for outbound notification messages.
/// </summary>
public class NotificationTeamsOptions
{
    /// <summary>Enable or disable Teams delivery.</summary>
    public bool Enabled { get; set; }

    /// <summary>Teams incoming-webhook URL (from Teams channel connector settings).</summary>
    public string WebhookUrl { get; set; } = "";
}

/// <summary>
/// Slack incoming-webhook options for outbound notification messages.
/// </summary>
public class NotificationSlackOptions
{
    /// <summary>Enable or disable Slack delivery.</summary>
    public bool Enabled { get; set; }

    /// <summary>Slack incoming-webhook URL (from Slack app configuration).</summary>
    public string WebhookUrl { get; set; } = "";
}
