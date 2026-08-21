using System.Security.Claims;

namespace Ato.Copilot.Mcp.Services;

/// <summary>
/// Resolves the current user's identity from the active <see cref="HttpContext"/>.
///
/// The CAC authentication middleware (<c>CacAuthenticationMiddleware</c>) writes the
/// principal onto <c>HttpContext.User</c> before any endpoint handler runs.  In
/// Development / SimulationMode the principal is synthesised from config with a
/// <c>ClaimTypes.NameIdentifier</c> set to the simulated user's UPN.
///
/// fix(DEF-002): injected into dashboard endpoint handlers to replace the
/// hardcoded <c>"dashboard-user"</c> audit actor literal.
/// </summary>
public sealed class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    public string CurrentUserId
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user is null) return "unknown";

            // Primary: NameIdentifier carries UPN / sub in both JWT and simulated paths.
            var nameId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrWhiteSpace(nameId)) return nameId;

            // Secondary: display name.
            var name = user.FindFirstValue(ClaimTypes.Name);
            if (!string.IsNullOrWhiteSpace(name)) return name;

            return "unknown";
        }
    }
}
