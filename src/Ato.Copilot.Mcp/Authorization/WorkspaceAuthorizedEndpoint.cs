namespace Ato.Copilot.Mcp.Authorization;

/// <summary>
/// Marks an HTTP handler which enforces its own persisted workspace-operation authorization.
/// The coarse legacy compliance-role middleware must not require an unrelated global role.
/// </summary>
public sealed class WorkspaceAuthorizedEndpoint;
