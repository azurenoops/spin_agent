namespace Ato.Copilot.Core.Dtos.Dashboard;

/// <summary>
/// DTO for a single notification in API responses.
/// </summary>
public record NotificationDto
{
    public Guid Id { get; init; }
    public Guid AlertId { get; init; }
    public string Channel { get; init; } = string.Empty;
    public string? Subject { get; init; }
    public string? Body { get; init; }
    public bool IsRead { get; init; }
    public DateTimeOffset? ReadAt { get; init; }
    public DateTimeOffset SentAt { get; init; }
    public string? AlertTitle { get; init; }
    public string? AlertSeverity { get; init; }
}

/// <summary>
/// Summary counts of unread notifications for badge display.
/// </summary>
public record NotificationSummaryDto
{
    public int UnreadCount { get; init; }
    public int TotalCount { get; init; }
}

/// <summary>
/// Request to mark specific notifications as read.
/// </summary>
public record MarkNotificationsReadRequest(List<Guid> NotificationIds);

/// <summary>
/// User notification preferences for backend persistence.
/// </summary>
public record NotificationPreferencesDto
{
    public bool PoamOverdueAlerts { get; init; } = true;
    public bool AtoExpirationAlerts { get; init; } = true;
    public bool ComplianceDriftAlerts { get; init; } = true;
    public int AlertDaysBefore { get; init; } = 30;
}
