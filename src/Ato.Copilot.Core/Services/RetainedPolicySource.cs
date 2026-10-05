using System.Text.Json;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Services;

/// <summary>Shared read of the system policy workspace's retained source, without borrowing current library content.</summary>
public static class RetainedPolicySource
{
    public static PolicySourceDto? Read(ComponentSystemAssignment? assignment) =>
        assignment?.PolicySourceSnapshotJson is { } json ? JsonSerializer.Deserialize<PolicySourceDto>(json) : null;
}
