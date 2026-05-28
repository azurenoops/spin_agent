namespace Ato.Copilot.Core.Models.Tenancy;

/// <summary>
/// Lifecycle event recorded against a <see cref="CspInheritedCapability"/>.
/// Feature 050 FR-004 / FR-005 / FR-014 / FR-015 / FR-016 — drives the
/// audit-trail rows surfaced by the capability detail drawer's History tab.
/// </summary>
/// <remarks>
/// Persisted as <c>nvarchar(32)</c> via <c>HasConversion&lt;string&gt;()</c>
/// in <c>AtoCopilotContext</c> to match the existing enum-serialization
/// convention on <see cref="CspInheritedCapabilityStatus"/> and
/// <see cref="MappedBy"/>; this keeps raw-SQL audit queries readable.
/// </remarks>
public enum CapabilityHistoryEventType
{
    /// <summary>Row first persisted (manual add or AI mapping output).</summary>
    Created = 0,

    /// <summary>Mutable fields changed (Name / Description / mapped controls).</summary>
    Edited = 1,

    /// <summary>Human review completed; status advanced to <c>Mapped</c>.</summary>
    Reviewed = 2,

    /// <summary>Reparented to a different <c>CspInheritedComponent</c>.</summary>
    Moved = 3,

    /// <summary>Soft-deleted; <c>Status</c> flipped to <c>Archived</c>.</summary>
    Archived = 4,

    /// <summary>Restored from <c>Archived</c>.</summary>
    Unarchived = 5,
}
