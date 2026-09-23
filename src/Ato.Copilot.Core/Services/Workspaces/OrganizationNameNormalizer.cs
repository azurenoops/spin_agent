namespace Ato.Copilot.Core.Services.Workspaces;

internal static class OrganizationNameNormalizer
{
    internal static string Normalize(string value) => value.Trim().ToUpperInvariant();
}
