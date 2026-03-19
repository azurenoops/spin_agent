using System.Collections.Concurrent;

namespace Ato.Copilot.Core.Services;

/// <summary>
/// In-memory pessimistic lock manager for boundary definitions.
/// Prevents concurrent editing conflicts with auto-expiry after 5 minutes.
/// </summary>
public class BoundaryLockService
{
    private readonly ConcurrentDictionary<string, LockEntry> _locks = new();
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(5);

    public record LockEntry(string UserId, string DisplayName, DateTime AcquiredAt, DateTime ExpiresAt);

    /// <summary>
    /// Attempts to acquire a lock on a boundary. Returns (true, entry) if acquired,
    /// or (false, existing entry) if already locked by someone else.
    /// </summary>
    public (bool Acquired, LockEntry Entry) AcquireLock(string boundaryId, string userId, string displayName)
    {
        var now = DateTime.UtcNow;
        var newEntry = new LockEntry(userId, displayName, now, now.Add(LockDuration));

        var result = _locks.AddOrUpdate(boundaryId,
            _ => newEntry,
            (_, existing) =>
            {
                // If expired or same user, allow re-acquire
                if (existing.ExpiresAt <= now || existing.UserId == userId)
                    return newEntry;
                return existing;
            });

        if (result.UserId == userId && result.AcquiredAt == newEntry.AcquiredAt)
            return (true, result);

        // If expired entry was replaced
        if (result == newEntry)
            return (true, result);

        return (false, result);
    }

    /// <summary>
    /// Releases the lock for a boundary.
    /// </summary>
    public bool ReleaseLock(string boundaryId)
    {
        return _locks.TryRemove(boundaryId, out _);
    }

    /// <summary>
    /// Returns the current lock status for a boundary.
    /// </summary>
    public LockEntry? GetLockStatus(string boundaryId)
    {
        if (_locks.TryGetValue(boundaryId, out var entry))
        {
            if (entry.ExpiresAt <= DateTime.UtcNow)
            {
                _locks.TryRemove(boundaryId, out _);
                return null;
            }
            return entry;
        }
        return null;
    }
}
