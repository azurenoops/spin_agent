using Microsoft.Extensions.Configuration;

namespace Ato.Copilot.Core.Services.Ticketing;

/// <summary>Resolves administrator-provisioned credential references, never browser tokens.</summary>
public sealed class TicketingCredentialResolver(IConfiguration configuration)
{
    public string Resolve(string baseUrl, string reference)
    {
        ValidateEndpoint(baseUrl);
        if (string.IsNullOrWhiteSpace(reference) || reference.Length > 500)
            throw new InvalidOperationException("A configured credential reference is required.");
        return configuration[$"Ticketing:Credentials:{reference}"]
            ?? throw new InvalidOperationException("The credential reference is not provisioned on this server.");
    }

    public void ValidateEndpoint(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment) || !uri.IsDefaultPort
            || !configuration.GetSection("Ticketing:AllowedHosts").GetChildren()
                .Any(x => string.Equals(x.Value, uri.Host, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Ticket endpoint must be an administrator-allowlisted HTTPS origin.");
    }
}
