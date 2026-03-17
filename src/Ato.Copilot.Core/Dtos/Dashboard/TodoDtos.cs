namespace Ato.Copilot.Core.Dtos.Dashboard;

/// <summary>
/// Todo list response for a system, derived from current RMF phase and compliance data.
/// </summary>
public class TodoListDto
{
    /// <summary>System identifier.</summary>
    public required string SystemId { get; init; }

    /// <summary>System name.</summary>
    public required string SystemName { get; init; }

    /// <summary>Current RMF phase name.</summary>
    public required string CurrentPhase { get; init; }

    /// <summary>Next RMF phase name, or null if at Monitor.</summary>
    public string? NextPhase { get; init; }

    /// <summary>Ordered list of actionable items.</summary>
    public required IReadOnlyList<TodoItemDto> Items { get; init; }
}

/// <summary>
/// A single actionable todo item.
/// </summary>
public class TodoItemDto
{
    /// <summary>Stable identifier for the item type.</summary>
    public required string Id { get; init; }

    /// <summary>Short display label.</summary>
    public required string Label { get; init; }

    /// <summary>Subtitle / context detail.</summary>
    public required string Detail { get; init; }

    /// <summary>Category: phase-action, finding, poam, narrative, authorization.</summary>
    public required string Category { get; init; }

    /// <summary>Natural language prompt for Teams / VS Code copilot.</summary>
    public string? Prompt { get; init; }

    /// <summary>Optional link path for the dashboard.</summary>
    public string? Link { get; init; }

    /// <summary>Deferred prerequisite ID (present when category = "deferred").</summary>
    public string? DeferredId { get; init; }
}
