using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Poam;

public class ConnectedRemediationStartupOrderTests
{
    [Theory]
    [InlineData("ConnectedRemediationSchemaAdditions")]
    [InlineData("TaskTicketingSchemaAdditions")]
    public void NewTenantTables_AreCreatedBeforeModelDrivenTenantRetrofit(string module)
    {
        // Arrange
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Ato.Copilot.sln")))
            directory = directory.Parent;
        directory.Should().NotBeNull("the integration test runs from the repository");
        var source = File.ReadAllText(Path.Combine(directory!.FullName, "src", "Ato.Copilot.Mcp", "Program.cs"));
        var creation = source.IndexOf($"EnsureSchemaAdditions.{module}", StringComparison.Ordinal);
        var retrofit = source.IndexOf("EnsureSchemaAdditions.TenantIdColumnAdditions", StringComparison.Ordinal);

        // Act
        var createdBeforeRetrofit = creation >= 0 && retrofit > creation;

        // Assert
        createdBeforeRetrofit.Should().BeTrue(
            "{0} creates model-discovered tenant tables which the retrofit alters and indexes", module);
    }
}
