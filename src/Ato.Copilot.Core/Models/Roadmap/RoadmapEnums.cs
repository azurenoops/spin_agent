namespace Ato.Copilot.Core.Models.Roadmap;

/// <summary>Lifecycle status of an implementation roadmap.</summary>
public enum RoadmapStatus
{
    /// <summary>Just generated, not yet reviewed.</summary>
    Draft = 0,
    /// <summary>Approved and in execution.</summary>
    Active = 1,
    /// <summary>All phases complete.</summary>
    Completed = 2,
    /// <summary>Superseded by a newer roadmap.</summary>
    Archived = 3
}

/// <summary>Lifecycle status of a roadmap phase.</summary>
public enum PhaseStatus
{
    /// <summary>Phase has not started.</summary>
    NotStarted = 0,
    /// <summary>Phase is actively being executed.</summary>
    InProgress = 1,
    /// <summary>All items in the phase are complete.</summary>
    Complete = 2
}

/// <summary>Lifecycle status of a roadmap item.</summary>
public enum ItemStatus
{
    /// <summary>Item has not started.</summary>
    NotStarted = 0,
    /// <summary>Item is actively being worked.</summary>
    InProgress = 1,
    /// <summary>Item implementation is complete.</summary>
    Complete = 2
}

/// <summary>Type of compliance gap identified for a control.</summary>
public enum GapType
{
    /// <summary>No capability mapping exists.</summary>
    Unmapped = 0,
    /// <summary>Capability exists but implementation is incomplete.</summary>
    PartiallyImplemented = 1,
    /// <summary>Control has not yet been assessed.</summary>
    NotAssessed = 2
}

/// <summary>Severity level of a roadmap item, mapped to CAT risk points.</summary>
public enum ItemSeverity
{
    /// <summary>CAT I — 10 risk points.</summary>
    Critical = 0,
    /// <summary>CAT II — 5 risk points.</summary>
    High = 1,
    /// <summary>CAT III — 1 risk point.</summary>
    Medium = 2
}
