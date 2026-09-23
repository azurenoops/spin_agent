namespace Ato.Copilot.State.Abstractions;

/// <summary>
/// Request-scoped authorization boundary shared by all tool dispatch paths. A host binds
/// its policy for the duration of dispatch; local non-HTTP callers retain their own policy.
/// </summary>
public static class ToolExecutionAuthorization
{
    private static readonly AsyncLocal<Func<string, Dictionary<string, object?>, CancellationToken, Task>?> Policy = new();

    public static IDisposable Push(Func<string, Dictionary<string, object?>, CancellationToken, Task> authorize)
    {
        ArgumentNullException.ThrowIfNull(authorize);
        var previous = Policy.Value;
        Policy.Value = authorize;
        return new RestoreScope(previous);
    }

    public static Task DemandAsync(string tool, Dictionary<string, object?> arguments, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Policy.Value?.Invoke(tool, arguments, ct) ?? Task.CompletedTask;
    }

    private sealed class RestoreScope(
        Func<string, Dictionary<string, object?>, CancellationToken, Task>? previous) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            Policy.Value = previous;
            _disposed = true;
        }
    }
}
