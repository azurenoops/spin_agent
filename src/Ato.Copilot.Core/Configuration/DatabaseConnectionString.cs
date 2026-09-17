namespace Ato.Copilot.Core.Configuration;

public static class DatabaseConnectionString
{
    public static string WithManagedIdentityClientId(
        string connectionString,
        string? managedIdentityClientId)
    {
        if (string.IsNullOrWhiteSpace(managedIdentityClientId)
            || !connectionString.Contains(
                "Active Directory Managed Identity",
                StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains("User Id=", StringComparison.OrdinalIgnoreCase))
        {
            return connectionString;
        }

        return connectionString.TrimEnd(';')
            + $";User Id={managedIdentityClientId};";
    }
}