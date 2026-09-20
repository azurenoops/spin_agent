using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class OscalSspImportServiceTests : IDisposable
{
    private readonly ServiceProvider _provider;

    public OscalSspImportServiceTests()
    {
        var services = new ServiceCollection();
      var databaseRoot = new InMemoryDatabaseRoot();
        var databaseName = $"OscalSspImport_{Guid.NewGuid():N}";
        services.AddDbContext<AtoCopilotContext>(options =>
          options.UseInMemoryDatabase(databaseName, databaseRoot));
        _provider = services.BuildServiceProvider();
    }

    public void Dispose() => _provider.Dispose();

    [Fact]
    public async Task ImportAsync_DualStatements_PersistsCanonicalHalves()
    {
        // Arrange
        const string oscalJson = """
            {
              "system-security-plan": {
                "control-implementation": {
                  "implemented-requirements": [{
                    "control-id": "ac-2",
                    "statements": [
                      {
                        "statement-id": "AC-2_smt.policy",
                        "description": "Accounts are governed by the access control policy."
                      },
                      {
                        "statement-id": "AC-2_smt.technical",
                        "description": "Microsoft Entra ID enforces account lifecycle controls."
                      }
                    ]
                  }]
                }
              }
            }
            """;
        var service = new OscalSspImportService(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<OscalSspImportService>.Instance);

        // Act
        var result = await service.ImportAsync("system-1", oscalJson, ImportMode.Full);

        // Assert
        result.ControlsCreated.Should().Be(1);
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var implementation = await db.ControlImplementations.IgnoreQueryFilters().SingleAsync();
        implementation.PolicyNarrative.Should().Be("Accounts are governed by the access control policy.");
        implementation.TechnicalNarrative.Should().Be("Microsoft Entra ID enforces account lifecycle controls.");
        implementation.Narrative.Should().Be(implementation.TechnicalNarrative);
    }
}