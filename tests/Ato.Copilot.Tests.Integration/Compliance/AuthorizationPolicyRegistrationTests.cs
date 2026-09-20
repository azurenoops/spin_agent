using Ato.Copilot.Mcp.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Compliance;

public sealed class AuthorizationPolicyRegistrationTests
{
    [Fact]
    public async Task AddAtoCopilotMcpForTesting_RegistersEveryCanonicalPolicy()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAI:Enabled"] = "false",
                ["Database:Provider"] = "Sqlite",
                ["ConnectionStrings:DefaultConnection"] = "Data Source=:memory:",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAtoCopilotMcpForTesting(
            configuration,
            $"PolicyRegistration_{Guid.NewGuid():N}");
        await using var provider = services.BuildServiceProvider();
        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var policyNames = new[]
        {
            Policies.CspAdmin,
            Policies.SocAnalyst,
            Policies.ComplianceReader,
            Policies.ComplianceWriter,
            Policies.ComplianceAdministrator,
            Policies.AuthorizationDecisionIssuer,
        };

        // Act
        var policies = await Task.WhenAll(
            policyNames.Select(policyProvider.GetPolicyAsync));

        // Assert
        policies.Should().NotContainNulls();
    }
}