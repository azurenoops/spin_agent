using System.ComponentModel.DataAnnotations;

namespace Ato.Copilot.Core.Configuration;

/// <summary>
/// Configuration options for the EF Core database connection.
/// Bound from the <c>Database</c> appsettings section via <c>IOptions&lt;DatabaseOptions&gt;</c>.
/// Drives provider selection (SQL Server vs SQLite), retry policy, command timeout,
/// and sensitive-data-logging behavior for <c>AtoCopilotContext</c> and
/// <c>ChatDbContext</c>.
/// </summary>
public class DatabaseOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Database";

    /// <summary>
    /// EF Core provider name. Accepted values: <c>"SqlServer"</c>, <c>"SQLite"</c>.
    /// Defaults to <c>"SQLite"</c> for dev parity.
    /// </summary>
    public string Provider { get; set; } = "SQLite";

    /// <summary>
    /// Enables EF Core's <c>EnableSensitiveDataLogging</c>.
    /// <b>Must</b> remain <c>false</c> in production — exposes parameter values in logs.
    /// </summary>
    public bool EnableSensitiveDataLogging { get; set; }

    /// <summary>
    /// Per-command timeout in seconds passed to
    /// <c>SqlServerDbContextOptionsBuilder.CommandTimeout</c> /
    /// <c>SqliteDbContextOptionsBuilder.CommandTimeout</c>.
    /// </summary>
    [Range(1, 600)]
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum retry attempts on transient SQL failures
    /// (SQL Server only; SQLite ignores this).
    /// Plumbed into <c>EnableRetryOnFailure(maxRetryCount: …)</c>.
    /// </summary>
    [Range(0, 20)]
    public int MaxRetryCount { get; set; } = 5;

    /// <summary>
    /// Maximum delay between retries in seconds
    /// (SQL Server only; SQLite ignores this).
    /// Plumbed into <c>EnableRetryOnFailure(maxRetryDelay: TimeSpan.FromSeconds(…))</c>.
    /// </summary>
    [Range(1, 300)]
    public int MaxRetryDelay { get; set; } = 30;
}
