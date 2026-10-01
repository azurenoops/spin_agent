using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Core.Data.Interceptors;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Rls;

[Collection("RLS")]
public sealed class RequirementCoverageRlsTests(RlsIntegrationFixture fixture)
{
    [SkippableFact]
    public async Task AutomaticAssociation_UsesSeparateTenantContextsUnderRealSqlServerRls()
    {
        // Arrange
        Skip.IfNot(fixture.DockerAvailable, fixture.SkipReason);
        await using var connection = await fixture.OpenConnectionAsync();
        await using (var admin = new SqlCommand("EXEC sp_set_session_context N'IsCspAdmin', N'true';", connection))
            await admin.ExecuteNonQueryAsync();
        await using var db = new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlServer(connection).Options);
        var identifier = $"SYNTHETIC-{Guid.NewGuid():N}";
        db.ComplianceFrameworks.Add(new() { Identifier = identifier, Name = "Synthetic automatic source",
            CatalogUrl = "https://example.invalid/catalog", RequirementCatalogJson = """
            {"uuid":"synthetic","metadata":{"version":"1"},"controls":[{"id":"control","title":"Control",
              "props":[{"name":"label","value":"CONTROL"}],"parts":[{"id":"statement","name":"statement","prose":"Synthetic requirement"}]}]}
            """ });
        var baselines = new List<ControlBaseline>();
        foreach (var tenant in new[] { fixture.TenantA, fixture.TenantB })
        {
            var system = new RegisteredSystem { TenantId = tenant, Name = "Synthetic automatic RLS system", CreatedBy = "fixture" };
            var baseline = new ControlBaseline { TenantId = tenant, RegisteredSystemId = system.Id,
                SourceFrameworkIdentifier = identifier, BaselineLevel = "Synthetic", CreatedBy = "fixture", ControlIds = ["CONTROL"] };
            db.AddRange(system, baseline);
            baselines.Add(baseline);
        }
        await db.SaveChangesAsync();
        var accessor = new TenantContextAccessor();
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseSqlServer(fixture.ConnectionString, sql => sql.EnableRetryOnFailure())
            .AddInterceptors(new SqlServerSessionContextConnectionInterceptor(accessor, NullLogger<SqlServerSessionContextConnectionInterceptor>.Instance))
            .Options;
        var service = new AutomaticCatalogBindingService(new AutomaticFactory(options, accessor), accessor,
            NullLogger<AutomaticCatalogBindingService>.Instance);

        // Act
        await service.BackfillAsync(identifier);

        // Assert
        var ids = baselines.Select(x => x.Id).ToArray();
        var bindings = await db.BaselineCatalogBindings.AsNoTracking().Where(x => ids.Contains(x.ControlBaselineId)).ToListAsync();
        bindings.Should().HaveCount(2);
        bindings.Select(x => x.TenantId).Should().BeEquivalentTo(new[] { fixture.TenantA, fixture.TenantB });
        bindings.Should().OnlyContain(x => x.BoundBy == "system:catalog-association");
    }

    [SkippableTheory]
    [InlineData("BaselineCatalogBindings")]
    [InlineData("RequirementEnhancementProposals")]
    public async Task RequirementTables_FilterOtherTenantAndBlockForeignWrites(string table)
    {
        // Arrange
        Skip.IfNot(fixture.DockerAvailable, fixture.SkipReason);
        await using var connection = await fixture.OpenConnectionAsync();
        await using (var admin = new SqlCommand("EXEC sp_set_session_context N'IsCspAdmin', N'true';", connection))
            await admin.ExecuteNonQueryAsync();
        await using var db = new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseSqlServer(connection).Options);
        var baselineA = Seed(db, fixture.TenantA);
        var baselineB = Seed(db, fixture.TenantB);
        await db.SaveChangesAsync();
        await using (var scope = new SqlCommand(
            "EXEC sp_set_session_context N'IsCspAdmin', NULL; EXEC sp_set_session_context N'TenantId', @tenant;", connection))
        {
            scope.Parameters.AddWithValue("@tenant", fixture.TenantA.ToString());
            await scope.ExecuteNonQueryAsync();
        }
        var tenants = new List<Guid>();

        // Act
        await using (var select = new SqlCommand($"SELECT TenantId FROM dbo.[{table}]", connection))
        await using (var reader = await select.ExecuteReaderAsync())
            while (await reader.ReadAsync()) tenants.Add(reader.GetGuid(0));

        // Assert
        tenants.Should().Contain(fixture.TenantA);
        tenants.Should().NotContain(fixture.TenantB);

        // Arrange
        if (table == "BaselineCatalogBindings")
            db.BaselineCatalogBindings.Add(new() { TenantId = fixture.TenantB, ControlBaselineId = baselineB.Id });
        else
            db.RequirementEnhancementProposals.Add(new() { TenantId = fixture.TenantB, ControlBaselineId = baselineB.Id });

        // Act
        Func<Task> foreignWrite = () => db.SaveChangesAsync();

        // Assert
        var error = await foreignWrite.Should().ThrowAsync<DbUpdateException>();
        error.Which.InnerException.Should().BeOfType<SqlException>().Which.Number.Should().Be(33504);
        baselineA.TenantId.Should().Be(fixture.TenantA);
    }

    private static ControlBaseline Seed(AtoCopilotContext db, Guid tenant)
    {
        var system = new RegisteredSystem { TenantId = tenant, Name = "Synthetic requirement RLS system", CreatedBy = "fixture" };
        var baseline = new ControlBaseline { TenantId = tenant, RegisteredSystemId = system.Id,
            BaselineLevel = "Synthetic", CreatedBy = "fixture", ControlIds = ["DEMO"] };
        db.AddRange(system, baseline,
            new BaselineCatalogBinding { TenantId = tenant, ControlBaselineId = baseline.Id,
                FrameworkIdentifier = "SYNTHETIC", CatalogJson = "{}", ContentHash = new string('a', 64) },
            new RequirementEnhancementProposal { TenantId = tenant, ControlBaselineId = baseline.Id,
                ControlId = "DEMO-CHILD", ParentControlId = "DEMO", Rationale = "Synthetic RLS fixture",
                ActiveControlKey = Guid.NewGuid().ToString(), CreatedBy = "fixture" });
        return baseline;
    }

    private sealed class AutomaticFactory(DbContextOptions<AtoCopilotContext> options, TenantContextAccessor accessor)
        : IDbContextFactory<AtoCopilotContext>
    {
        public AtoCopilotContext CreateDbContext() => new(options, accessor);
    }
}
