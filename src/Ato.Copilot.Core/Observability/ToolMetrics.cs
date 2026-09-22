using System.Diagnostics.Metrics;

namespace Ato.Copilot.Core.Observability;

/// <summary>
/// Static metrics class providing structured telemetry instruments for tool invocations.
/// Uses <see cref="System.Diagnostics.Metrics.Meter"/> named "Ato.Copilot" for automatic
/// integration with OpenTelemetry exporters when configured.
/// Per FR-046: tracks latency, error rate, throughput, and active sessions.
/// </summary>
public static class ToolMetrics
{
    /// <summary>The meter name used for all Security Posture Intelligence Navigator metrics.</summary>
    public const string MeterName = "Ato.Copilot";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    /// <summary>
    /// Counter for total tool invocations. Tags: tool, agent, status.
    /// Increment on every tool call entry with status "started",
    /// then again with "completed" or "failed".
    /// </summary>
    public static readonly Counter<long> ToolInvocations =
        Meter.CreateCounter<long>(
            "ato.copilot.tool.invocations",
            unit: "{invocations}",
            description: "Total number of tool invocations");

    /// <summary>
    /// Histogram for tool execution duration in milliseconds. Tags: tool, agent.
    /// </summary>
    public static readonly Histogram<double> ToolDurationMs =
        Meter.CreateHistogram<double>(
            "ato.copilot.tool.duration",
            unit: "ms",
            description: "Tool execution duration in milliseconds");

    /// <summary>
    /// Counter for tool invocation errors. Tags: tool, agent, errorCode.
    /// </summary>
    public static readonly Counter<long> ToolErrors =
        Meter.CreateCounter<long>(
            "ato.copilot.tool.errors",
            unit: "{errors}",
            description: "Total number of tool invocation errors");

    /// <summary>
    /// UpDownCounter for currently active sessions (CAC/PIM).
    /// </summary>
    public static readonly UpDownCounter<long> ActiveSessions =
        Meter.CreateUpDownCounter<long>(
            "ato.copilot.sessions.active",
            unit: "{sessions}",
            description: "Number of currently active sessions");

    /// <summary>
    /// Records a successful tool invocation with duration.
    /// </summary>
    /// <param name="durationMs">Execution time in milliseconds.</param>
    /// <param name="toolName">The tool name.</param>
    /// <param name="agentName">The agent that owns the tool.</param>
    public static void RecordSuccess(double durationMs, string toolName, string agentName)
    {
        ToolInvocations.Add(1,
            new KeyValuePair<string, object?>("tool", toolName),
            new KeyValuePair<string, object?>("agent", agentName),
            new KeyValuePair<string, object?>("status", "completed"));

        ToolDurationMs.Record(durationMs,
            new KeyValuePair<string, object?>("tool", toolName),
            new KeyValuePair<string, object?>("agent", agentName));
    }

    /// <summary>
    /// Records a tool invocation start.
    /// </summary>
    /// <param name="toolName">The tool name.</param>
    /// <param name="agentName">The agent that owns the tool.</param>
    public static void RecordStart(string toolName, string agentName)
    {
        ToolInvocations.Add(1,
            new KeyValuePair<string, object?>("tool", toolName),
            new KeyValuePair<string, object?>("agent", agentName),
            new KeyValuePair<string, object?>("status", "started"));
    }

    /// <summary>
    /// Records a tool invocation error.
    /// </summary>
    /// <param name="toolName">The tool name.</param>
    /// <param name="agentName">The agent that owns the tool.</param>
    /// <param name="errorCode">An error classification code.</param>
    public static void RecordError(string toolName, string agentName, string errorCode)
    {
        ToolErrors.Add(1,
            new KeyValuePair<string, object?>("tool", toolName),
            new KeyValuePair<string, object?>("agent", agentName),
            new KeyValuePair<string, object?>("errorCode", errorCode));
    }
}
