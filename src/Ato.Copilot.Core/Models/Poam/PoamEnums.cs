namespace Ato.Copilot.Core.Models.Poam;

/// <summary>
/// Event types for the POA&amp;M audit trail (<see cref="PoamHistoryEntry"/>).
/// </summary>
public enum PoamHistoryEventType
{
    Created = 0,
    StatusChanged = 1,
    SeverityChanged = 2,
    DueDateChanged = 3,
    PocChanged = 4,
    MilestoneUpdated = 5,
    ComponentLinked = 6,
    ComponentUnlinked = 7,
    TaskLinked = 8,
    TaskUnlinked = 9,
    CascadeApplied = 10,
    CommentAdded = 11,
    TicketSynced = 12,
    TicketSyncFailed = 13,
    DeviationLinked = 14,
    FindingLinked = 15,
    FieldEdited = 16
}

/// <summary>
/// Origin of a cascade change for circular-prevention tracking.
/// </summary>
public enum CascadeOrigin
{
    Direct = 0,
    FromTask = 1,
    FromPoam = 2,
    FromTicketing = 3
}

/// <summary>
/// Supported external ticketing system providers.
/// </summary>
public enum TicketingProvider
{
    Jira = 0,
    ServiceNow = 1
}

/// <summary>
/// Synchronization state between a POA&amp;M item and its external ticket.
/// </summary>
public enum TicketSyncStatus
{
    Synced = 0,
    Pending = 1,
    Conflict = 2,
    Error = 3
}
