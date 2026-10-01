using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using System.Data.Common;

namespace Ato.Copilot.Tests.Integration.Data;

public sealed class RequirementCoverageSqlServerTests(BoundarySchemaSqlServerFixture fixture)
    : IClassFixture<BoundarySchemaSqlServerFixture>
{
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Mutations_WorkWithProductionSqlServerRetryStrategy(bool transientFailure)
    {
        // Arrange
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        await using var database = await fixture.CreateDatabaseAsync();
        var fault = new OneTimeProposalTimeout();
        await using var db = new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseSqlServer(database.Database.GetConnectionString(), options => options.EnableRetryOnFailure(2, TimeSpan.FromMilliseconds(10), null))
            .AddInterceptors(fault).Options);
        await db.Database.EnsureCreatedAsync();
        var tenantId = Guid.NewGuid();
        var author = Guid.NewGuid();
        var reviewer = Guid.NewGuid();
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(x => x.EffectiveTenantId).Returns(tenantId);
        tenant.SetupGet(x => x.PersonId).Returns(author);
        var system = new RegisteredSystem { TenantId = tenantId, Name = "Synthetic retry test", CreatedBy = "fixture" };
        var baseline = new ControlBaseline { TenantId = tenantId, RegisteredSystemId = system.Id,
            BaselineLevel = "Synthetic", CreatedBy = "fixture", ControlIds = ["PARENT"] };
        var framework = new ComplianceFramework { Identifier = "SYNTHETIC", Name = "Synthetic retry source",
            CatalogUrl = "https://example.invalid/catalog", RequirementCatalogJson = """
            {"uuid":"synthetic","metadata":{"version":"1"},"controls":[{"id":"parent","title":"Parent",
              "props":[{"name":"label","value":"PARENT"}],"controls":[{"id":"child","title":"Child",
              "props":[{"name":"label","value":"CHILD"}],"parts":[{"id":"child-statement","name":"statement","prose":"Synthetic requirement"}]}]}]}
            """ };
        db.AddRange(system, baseline, framework);
        await db.SaveChangesAsync();
        var access = new Mock<ISystemWorkspaceAccessService>();
        access.Setup(x => x.GetAccessAsync(tenantId, It.IsAny<Guid?>(), system.Id, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemWorkspaceAccessResponse(system.Id, [], new(true, false, true, true, true, true, false, false, false)));
        var service = new RequirementCoverageService(db, tenant.Object, access.Object, NullLogger<RequirementCoverageService>.Instance);

        // Act
        await service.BindCatalogAsync(system.Id, framework.Id, 0, "Verified source", "author", default);
        fault.Armed = transientFailure;
        var proposal = await service.ProposeAsync(system.Id, new("PARENT", "CHILD", 1, "Scope", null, "Draft"), "author", default);
        tenant.SetupGet(x => x.PersonId).Returns(reviewer);
        await service.ReturnProposalAsync(system.Id, proposal.Id, 1, "Clarify scope", "reviewer", default);
        tenant.SetupGet(x => x.PersonId).Returns(author);
        proposal = await service.ProposeAsync(system.Id, new("PARENT", "CHILD", 1, "Clarified scope", null, "Draft"), "author", default);
        tenant.SetupGet(x => x.PersonId).Returns(reviewer);
        await service.AcceptAsync(system.Id, proposal.Id, 1, "reviewer", default);
        var evidence = new EvidenceArtifact { TenantId = tenantId, RegisteredSystemId = system.Id,
            ContentHash = new string('a', 64), FileName = "synthetic.txt", UploadedBy = "fixture" };
        db.Add(evidence);
        await db.SaveChangesAsync();
        tenant.SetupGet(x => x.PersonId).Returns(author);
        await service.SaveMappingsAsync(system.Id, "CHILD",
            new(1, [new("child-statement", "Technical", "Synthetic response", [new(evidence.Id, evidence.ContentHash)])],
                new Dictionary<string, string>()), "author", default);
        tenant.SetupGet(x => x.PersonId).Returns(reviewer);
        await service.ReviewMappingsAsync(system.Id, "CHILD", 2, "reviewer", default);

        // Assert
        (await db.ControlBaselines.AsNoTracking().SingleAsync()).ControlIds.Should().Equal("PARENT", "CHILD");
        (await db.ControlImplementations.AsNoTracking().SingleAsync()).ApprovalStatus.Should().Be(SspSectionStatus.Draft);
        (await service.ReadAsync(system.Id, "CHILD", default)).Requirements.Single().Reviewed.Should().BeTrue();
        fault.Failures.Should().Be(transientFailure ? 1 : 0);
        (await db.RequirementEnhancementProposals.CountAsync()).Should().Be(2, "a retried proposal must not retain a duplicate tracked insert");
    }

    private sealed class OneTimeProposalTimeout : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public int Failures { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Armed && command.CommandText.Contains("INSERT INTO [RequirementEnhancementProposals]", StringComparison.Ordinal))
            {
                Armed = false;
                Failures++;
                throw new TimeoutException("Synthetic single-attempt transient database fault.");
            }
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
