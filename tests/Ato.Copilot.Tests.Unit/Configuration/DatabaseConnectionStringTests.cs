using Ato.Copilot.Core.Configuration;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Configuration;

public class DatabaseConnectionStringTests
{
    [Fact]
    public void WithManagedIdentityClientId_ManagedIdentitySql_AppendsUserId()
    {
        // Arrange
        const string connectionString = "Server=server;Database=db;Authentication=Active Directory Managed Identity;";

        // Act
        var result = DatabaseConnectionString.WithManagedIdentityClientId(connectionString, "client-id");

        // Assert
        result.Should().Be(connectionString + "User Id=client-id;");
    }

    [Theory]
    [InlineData("Data Source=chat.db")]
    [InlineData("Server=server;Database=db;User Id=user;Password=password;")]
    [InlineData("Server=server;Authentication=Active Directory Managed Identity;User Id=existing;")]
    public void WithManagedIdentityClientId_NonApplicableConnectionString_IsUnchanged(
        string connectionString)
    {
        // Act
        var result = DatabaseConnectionString.WithManagedIdentityClientId(connectionString, "client-id");

        // Assert
        result.Should().Be(connectionString);
    }
}