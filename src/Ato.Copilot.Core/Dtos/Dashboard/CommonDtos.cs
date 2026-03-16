namespace Ato.Copilot.Core.Dtos.Dashboard;

/// <summary>
/// Cursor-based paginated response envelope for all dashboard collection endpoints.
/// </summary>
/// <typeparam name="T">The type of items in the page.</typeparam>
public class PaginatedResponse<T>
{
    /// <summary>Items in the current page.</summary>
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>Cursor for the next page, or null if no more results.</summary>
    public string? NextCursor { get; init; }

    /// <summary>Total count of items matching the query.</summary>
    public int TotalCount { get; init; }
}

/// <summary>
/// Standard error response per Constitution Principle VII.
/// All error responses MUST include error, errorCode, and optionally details and suggestion.
/// </summary>
public class ErrorResponse
{
    /// <summary>Human-readable error message.</summary>
    public required string Error { get; init; }

    /// <summary>Machine-readable error code (e.g., SYSTEM_NOT_FOUND, VALIDATION_FAILED).</summary>
    public required string ErrorCode { get; init; }

    /// <summary>Additional context about the error.</summary>
    public string? Details { get; init; }

    /// <summary>Corrective guidance for the caller.</summary>
    public string? Suggestion { get; init; }
}

/// <summary>
/// Common pagination query parameters.
/// </summary>
public class PaginationQuery
{
    /// <summary>Cursor from a previous response to fetch the next page.</summary>
    public string? Cursor { get; init; }

    /// <summary>Number of items per page (1-100, default 50).</summary>
    public int? PageSize { get; init; }

    /// <summary>Clamps PageSize to the allowed range.</summary>
    public int EffectivePageSize => Math.Clamp(PageSize ?? 50, 1, 100);
}
